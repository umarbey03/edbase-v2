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
/// ★ FOYDALANUVCHI TAHRIRLAY VA O'CHIRA OLMAYDI (qaror kuchida):
/// eski ilovada chat xabari uchun tahrirlash yoki o'chirish endpointi
/// UMUMAN bo'lmagan (<c>student_router</c> / <c>teacher_router</c> da
/// faqat GET va POST bor). Ya'ni foydalanuvchilar buni kutmaydi, va
/// chatning o'zi ustoz-o'quvchi muloqotining YOZMA IZI: "savolimni
/// o'chirdim" degan imkoniyat nizoli holatda dalilni yo'qotardi.
/// Bu qoida O'ZGARMADI: bugun ham na o'quvchi, na xodim bitta xabarni
/// tanlab o'chira olmaydi va tahrirlash endpointi yo'q.
///
/// ★ 🔴 LEKIN MUDDATLI (retention) O'CHIRISH BOR — QAROR QAYTA KO'RILDI.
///
/// Avvalgi izohda "o'chirish umuman yo'q, kerak bo'lsa keyin qo'shiladi"
/// deb yozilgan edi. Egasi TALAB QILDI: belgilangan muddatdan (standart
/// 3 oy) eski guruh yozishmalari Telegram'dagi kabi DOIMIY o'chirilib
/// borishi kerak. Shu sababli <c>ChatRetentionJob</c> qo'shildi va u
/// kesimdan eski qatorlarni QATTIQ (hard) o'chiradi.
///
/// ★ NIMA UCHUN "YOZMA IZ" ARGUMENTI BUNI RAD ETMAYDI: u argument
/// TANLAB o'chirishga qarshi edi — ya'ni "o'z savolimni o'chirib
/// tashlayman" degan imkoniyatga. Muddatli tozalash tanlamaydi: u
/// hammaga, hamma guruhga va faqat VAQT bo'yicha tegishli, ya'ni
/// tomonlardan biri dalilni yo'qota olmaydi. Nizo esa amalda YAQIN
/// o'tmish ustida bo'ladi; 3 oydan eski yozishmani saqlash narxi
/// (eng katta jadval, cheksiz o'sish) undan yuqori.
///
/// ⚠️ TIKLASH YO'LI YO'Q (loyihada soft-delete yo'q) — batafsili va
/// zaxiradan tiklash tartibi <c>ChatRetentionJob</c> izohida. Aynan
/// shuning uchun tozalash STANDART HOLDA O'CHIQ va uni administrator
/// paneldan ongli ravishda yoqadi.
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

    /// <summary>
    /// Xabar matni.
    ///
    /// ⚠️ R16b DAN KEYIN BO'SH SATR BO'LISHI MUMKIN — lekin FAQAT
    /// biriktirmasi bor xabarda (izohsiz surat, Telegram'dagi kabi).
    /// Ustun NOT NULL bo'lib qoladi; sabab va butun qaror
    /// <see cref="MessageText.NormalizeOptional"/> izohida.
    /// </summary>
    public required string Body { get; set; }

    public DateTimeOffset SentAt { get; set; }

    /// <summary>
    /// Biriktirilgan fayllar (rasm / ovoz / hujjat) — R16b.
    ///
    /// ★ Xabar bilan BITTA tranzaksiyada tug'iladi, ya'ni "egasiz
    /// biriktirma" holati yo'q (sabab <see cref="GroupChatAttachment"/> da).
    /// </summary>
    public ICollection<GroupChatAttachment> Attachments { get; set; } =
        new List<GroupChatAttachment>();

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Yangi MATNLI xabar: matn tozalanadi, bo'sh rad etiladi, uzuni
    /// surrogat juftlikni BUZMASDAN qirqiladi (<see cref="MessageText"/>).
    ///
    /// ★ YANGI NORMALIZATSIYA YOZILMADI — mavjud <see cref="MessageText"/>
    /// qayta ishlatiladi. Sabab o'sha sinf izohida: 500/2000-belgisi emojiga
    /// to'g'ri kelgan xabar yolg'iz surrogat qoldirib, Postgres'da
    /// <c>U+FFFD</c> ga aylanardi. Qoida ikki nusxa bo'lsa himoya bittasida
    /// unutilardi.
    ///
    /// 🔴 BO'SH MATN AVVALGIDEK RAD ETILADI. Biriktirmali xabar uchun
    /// ALOHIDA fabrika bor (<see cref="CreateWithAttachments"/>) — shu
    /// tufayli "bo'sh matnga ruxsat" yumshatilishi FAQAT o'sha yo'lda
    /// amal qiladi va bu yo'lga sizib o'ta olmaydi.
    /// </summary>
    /// <param name="now">Joriy vaqt — ARGUMENT (Domain soatni bilmaydi).</param>
    public static GroupChatMessage Create(
        long groupId,
        GroupChatChannel channel,
        long senderId,
        string? senderName,
        UserRole senderRole,
        string? body,
        DateTimeOffset now) =>
        Build(
            groupId, channel, senderId, senderName, senderRole,
            MessageText.Normalize(body, MaxBodyLength), now);

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// BIRIKTIRMALI XABAR (R16b) — MATN IXTIYORIY
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 SHU YERDA VA FAQAT SHU YERDA bo'sh matnga ruxsat beriladi, va u
    /// SHARTSIZ emas: <paramref name="attachmentCount"/> kamida 1 bo'lishi
    /// kerak. Ya'ni Domain invarianti "xabarda MAZMUN bo'lishi shart"
    /// bo'lib qoladi, faqat mazmun endi matn YOKI fayl bo'lishi mumkin.
    /// To'liq asoslash: <see cref="MessageText.NormalizeOptional"/> izohi.
    ///
    /// ⚠️ BIRIKTIRMALARNING O'ZI BU YERDA QO'SHILMAYDI — faqat SONI
    /// tekshiriladi. Sabab: biriktirma yaratish uchun ombor kaliti kerak,
    /// u esa faqat fayl R2 ga yozilgandan KEYIN ma'lum bo'ladi. Domain esa
    /// omborni bilmaydi. Shuning uchun use-case avval fayllarni saqlaydi,
    /// sonini shu metodga aytadi va qatorlarni <c>Attachments</c> ga
    /// qo'shadi — hammasi bitta <c>SaveChanges</c> ichida.
    /// </summary>
    public static GroupChatMessage CreateWithAttachments(
        long groupId,
        GroupChatChannel channel,
        long senderId,
        string? senderName,
        UserRole senderRole,
        string? body,
        int attachmentCount,
        DateTimeOffset now)
    {
        if (attachmentCount <= 0)
        {
            throw new DomainException(
                "Biriktirmasiz xabarda matn bo'lishi shart.");
        }

        if (attachmentCount > GroupChatAttachment.MaxPerMessage)
        {
            throw new DomainException(
                $"Bitta xabarga ko'pi bilan {GroupChatAttachment.MaxPerMessage} ta fayl "
                + "biriktiriladi.");
        }

        return Build(
            groupId, channel, senderId, senderName, senderRole,
            MessageText.NormalizeOptional(body, MaxBodyLength), now);
    }

    /// <summary>
    /// Ikkala fabrikaning UMUMIY o'zagi: matndan TASHQARI barcha
    /// tekshiruvlar. Nusxalansa, bir kuni ulardan birida "kanal ma'lummi"
    /// yoki "muallif bormi" tekshiruvi tushib qolardi.
    /// </summary>
    private static GroupChatMessage Build(
        long groupId,
        GroupChatChannel channel,
        long senderId,
        string? senderName,
        UserRole senderRole,
        string body,
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
            Body = body,
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
