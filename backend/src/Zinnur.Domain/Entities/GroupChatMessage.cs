using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// GURUH CHATI — DOIMIY XABAR
/// ========================================================================
///
/// Har guruhning DOIMIY chati: o'quvchilar savol beradi, ustoz/kurator
/// javob beradi — dars vaqtidan TASHQARIDA ham. Eski ilovada bu
/// <c>chat_messages</c> jadvali edi va kundalik ishlatilardi.
///
/// ★ LOYIHADAGI UCHINCHI "CHAT" — ATAYLAB UCHINCHI JADVAL. Farqi:
///
///   <see cref="ChatMessage"/>      — JONLI DARS xonasidagi oqim
///                                    (<c>SessionId</c> ga bog'langan).
///                                    Dars tugashi bilan o'lik tarixga
///                                    aylanadi; 200 kishi bir vaqtda
///                                    yozgani uchun fon navbati orqali
///                                    paketlab yoziladi va yo'qolishi
///                                    MAQBUL.
///
///   <see cref="DirectMessage"/>    — IKKI kishilik shaxsiy yozishma
///                                    (kurator ↔ o'quvchi). Suhbat kaliti
///                                    <c>(StudentId, StaffId)</c>.
///
///   <see cref="GroupChatMessage"/> — GURUHNING doimiy ommaviy chati.
///                                    Suhbat kaliti <c>(GroupId, Channel)</c>.
///                                    Oylab yashaydi, o'qilmaganlar sanog'i
///                                    bor va XABAR YO'QOLMASLIGI SHART —
///                                    o'quvchining savoli. Shuning uchun
///                                    navbat YO'Q: avval saqlanadi, keyin
///                                    tarqatiladi (commit-then-send).
///
/// Uchalasini bitta jadvalga qo'shish "sessiya id'si bo'sh xabar" yoki
/// "guruh id'si bo'sh xabar" degan yarim holatlar yaratardi va har
/// so'rovda <c>WHERE ... IS NULL</c> filtri kerak bo'lardi — bitta joyda
/// unutilsa o'quvchining shaxsiy savoli butun guruhga ko'rinib qolardi.
///
/// ★ TAHRIRLASH VA O'CHIRISH YO'Q (qaror, sabab bilan):
/// eski ilovada chat xabari uchun tahrirlash yoki o'chirish endpointi
/// UMUMAN bo'lmagan (<c>student_router</c> / <c>teacher_router</c> da
/// faqat GET va POST bor). Ya'ni foydalanuvchilar buni kutmaydi, va
/// chatning o'zi ustoz-o'quvchi muloqotining YOZMA IZI: "savolimni
/// o'chirdim" degan imkoniyat nizoli holatda dalilni yo'qotardi.
/// Kerak bo'lsa keyin qo'shiladi — teskarisi (mavjud imkoniyatni olib
/// tashlash) ancha og'ir.
/// </summary>
public class GroupChatMessage : BaseEntity
{
    /// <summary>
    /// Maksimal uzunlik. Jonli dars chatidan (500) UZUNROQ va
    /// <see cref="DirectMessage"/> bilan bir xil (2000): bu yerda ham
    /// o'quvchi vazifa bo'yicha batafsil savol yozadi, bir qatorli
    /// replika emas.
    /// </summary>
    public const int MaxBodyLength = 2000;

    /// <summary>Yuboruvchi ismi ko'chirmasining chegarasi (EF konfiguratsiyasi bilan bir xil).</summary>
    public const int MaxSenderNameLength = 200;

    public long GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>
    /// Qaysi oqimga yozilgan. Ruxsat SHU MAYDONGA bog'liq —
    /// izohi <see cref="GroupChatChannel"/> da.
    /// </summary>
    public GroupChatChannel Channel { get; set; }

    public long SenderId { get; set; }

    /// <summary>
    /// Yuboruvchi ismi xabar bilan BIRGA saqlanadi (denormalizatsiya) —
    /// <see cref="ChatMessage.SenderName"/> dagi bilan bir xil sabab:
    /// 50 ta xabarli sahifani o'qishda <c>Users</c> ga JOIN kerak emas.
    ///
    /// Qo'shimcha foyda: xodim almashsa ham eski xabar KIM yozgani
    /// o'zgarmaydi. Eski ilovada ism har o'qishda JOIN bilan olinardi.
    /// </summary>
    public required string SenderName { get; set; }

    /// <summary>
    /// Yuboruvchining YOZGAN PAYTDAGI roli — Telegram uslubidagi yorliq uchun
    /// (eski ilovadagi <c>_sender_role</c> / <c>_chat_role</c>).
    ///
    /// ★ NIMA UCHUN SAQLANADI, HISOBLANMAYDI: eski ilova yorliqni har
    /// o'qishda JORIY guruh biriktiruvidan hisoblardi. Ustoz almashtirilsa
    /// uning eski xabarlari birdan "o'quvchi" yorlig'i bilan ko'rinardi —
    /// tarix o'zgarib ketardi. Bu yerda yorliq xabar bilan qotib qoladi.
    /// </summary>
    public UserRole SenderRole { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset SentAt { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Yangi xabar yaratadi: matn tozalanadi, bo'sh rad etiladi, uzuni
    /// surrogat juftlikni BUZMASDAN qirqiladi (<see cref="MessageText"/>).
    ///
    /// ★ YANGI NORMALIZATSIYA YOZILMADI — mavjud <see cref="MessageText"/>
    /// qayta ishlatiladi. Sabab o'sha sinf izohida: 500/2000-belgisi emojiga
    /// to'g'ri kelgan xabar yolg'iz surrogat qoldirib, Postgres'da
    /// <c>U+FFFD</c> ga aylanardi. Qoida ikki nusxa bo'lsa himoya bittasida
    /// unutilardi.
    /// </summary>
    /// <param name="now">Joriy vaqt — ARGUMENT (Domain soatni bilmaydi).</param>
    public static GroupChatMessage Create(
        long groupId,
        GroupChatChannel channel,
        long senderId,
        string? senderName,
        UserRole senderRole,
        string? body,
        DateTimeOffset now)
    {
        if (groupId <= 0)
            throw new DomainException("Xabar guruhga bog'langan bo'lishi kerak.");

        if (senderId <= 0)
            throw new DomainException("Xabar muallifi ko'rsatilishi kerak.");

        if (!Enum.IsDefined(channel))
            throw new DomainException("Noma'lum chat kanali.");

        return new GroupChatMessage
        {
            GroupId = groupId,
            Channel = channel,
            SenderId = senderId,
            SenderName = TrimName(senderName),
            SenderRole = senderRole,
            Body = MessageText.Normalize(body, MaxBodyLength),
            SentAt = now,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Ismni ustun chegarasiga sig'diradi.
    ///
    /// Kesish <see cref="MessageText"/> orqali EMAS: u bo'sh matnni rad
    /// etadi, ism esa bo'sh bo'lishi mumkin (eski ma'lumotda uchraydi) va
    /// bu butun xabarni yo'qotishga arzimaydi.
    /// </summary>
    private static string TrimName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();

        if (name.Length == 0) return "Noma'lum";

        if (name.Length <= MaxSenderNameLength) return name;

        // Surrogat juftlik himoyasi bu yerda ham kerak: ism ham emoji
        // saqlashi mumkin (foydalanuvchilar profilga qo'yadi).
        var cut = MaxSenderNameLength;
        if (char.IsHighSurrogate(name[cut - 1])) cut--;

        return name[..cut];
    }
}
