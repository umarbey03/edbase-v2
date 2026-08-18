using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARSGA KELMAGAN O'QUVCHIGA YUBORILGAN XABAR (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi: *"xabarlar qismida darsga kirmagan o'quvchilar uchun
/// yuborilgan xabarlar turishi kerak va u alohida tab bo'lishi kerak"*.
///
/// ★ NEGA MAVJUD <see cref="GroupBroadcast"/> YARAMAYDI: u GURUHGA
/// yuboriladi va bitta qator butun guruhni ifodalaydi
/// (<c>TargetGroupNames</c> — nomlar ro'yxati, <c>TelegramRecipientCount</c>
/// — shunchaki son). Ya'ni "Doniyorga xabar bordimi?" degan savolga
/// javob berib bo'lmasdi. Kelmaganlar bilan ishlashda esa savol AYNAN
/// shunday: kurator bitta o'quvchi bo'yicha ish yuritadi.
///
/// Shuning uchun bu yerda HAR OLUVCHIGA ALOHIDA QATOR.
///
/// ★ MATN SURATGA OLINADI: shablon keyin tahrirlansa ham, yuborilgan
/// xabar o'zgarmaydi. Bahsda "unga nima yozgan edingiz?" degan savolga
/// javob aynan shu ustunda.
///
/// ★ DARS HAM SAQLANADI: "qaysi dars uchun" — takroriy yuborishni
/// aniqlash va tarixni o'qish uchun. Dars o'chirilmaydi (moliya va
/// davomat unga tayanadi), shuning uchun havola xavfsiz.
/// </summary>
public class AbsenceNotice : BaseEntity
{
    public const int MaxBodyLength = 2000;

    /// <summary>Telegram navbatidagi yozuv kaliti — yetkazilish holatini o'qish uchun.</summary>
    public const int MaxOutboxKeyLength = 128;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public long GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>Qaysi dars uchun yuborilgan.</summary>
    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }

    /// <summary>
    /// Darsning rejadagi vaqti — SURAT.
    ///
    /// ★ NEGA NUSXA: ro'yxat "qaysi kunlar uchun" bo'yicha filtrlanadi va
    /// saralanadi. Har safar darsga JOIN qilinsa, eng ko'p ishlatiladigan
    /// so'rov bekorga qimmatlashardi.
    /// </summary>
    public DateTimeOffset SessionStart { get; set; }

    /// <summary>Yuborilgan matn (o'rin egallovchilar allaqachon almashtirilgan).</summary>
    public string Body { get; set; } = string.Empty;

    public long SentById { get; set; }

    public User? SentBy { get; set; }

    public DateTimeOffset SentAt { get; set; }

    /// <summary>
    /// Telegram navbatiga qo'yilganmi.
    ///
    /// ⚠️ <c>false</c> — o'quvchida Telegram ulanmagan, ya'ni xabar
    /// FAQAT ilova ichida ko'rinadi. Bu holat yashirilmaydi: kurator
    /// bunday o'quvchiga qo'ng'iroq qilishi kerakligini bilishi shart.
    /// </summary>
    public bool ToTelegram { get; set; }

    /// <summary>
    /// Navbatdagi yozuvning idempotentlik kaliti.
    ///
    /// Yetkazilish holati (yuborildi / xato / urinishlar soni) AYNI shu
    /// kalit orqali o'qiladi — navbat jadvalining o'zi Application
    /// qatlamiga ochilmaydi.
    /// </summary>
    public string? OutboxKey { get; set; }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// O'QUVCHI TELEGRAMDA YOZGAN SABAB (2026-08-18)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Loyiha egasi: *"sababini yuborilgan xabarning o'zida so'rab olish
    /// kerak va shu yerning o'ziga sababini yozib yuborsin, va u
    /// platformada ko'rinsin"*.
    ///
    /// ★ NEGA BU ENG MUHIM MAYDON: u kuratorning ISH RO'YXATINI ikkiga
    /// bo'ladi. Sababini yozib yuborganlar bilan bog'lanish SHART EMAS —
    /// sabab allaqachon ma'lum. Javob bermaganlar esa qo'ng'iroq qilib,
    /// sababini aniqlab, darsga qaytarishga urinish kerak bo'lgan
    /// ro'yxat. Bu maydonsiz kurator hammasini birma-bir qo'ng'iroq
    /// qilardi va vaqtining yarmi bekorga ketardi.
    /// </summary>
    public string? ReplyText { get; set; }

    /// <summary>Javob qachon kelgani. <c>null</c> — hali javob yo'q.</summary>
    public DateTimeOffset? RepliedAt { get; set; }

    /// <summary>Javob kelganmi — qo'ng'iroq ro'yxatini ajratishning yagona mezoni.</summary>
    public bool HasReply => RepliedAt is not null;

    /// <summary>
    /// O'quvchi yozgan sababni yozib qo'yadi.
    ///
    /// ★ FAQAT BIR MARTA: birinchi javob saqlanadi va keyingi xabarlar
    /// uni O'ZGARTIRMAYDI. Aks holda o'quvchining keyingi tasodifiy
    /// xabari ("rahmat", "salom") aniq yozilgan sababni o'chirib
    /// yuborardi.
    /// </summary>
    /// <returns><c>false</c> — javob allaqachon bor yoki matn bo'sh.</returns>
    public bool Reply(string? text, DateTimeOffset now)
    {
        if (HasReply) return false;

        var trimmed = (text ?? string.Empty).Trim();

        if (trimmed.Length == 0) return false;

        ReplyText = trimmed.Length > MaxReplyLength ? trimmed[..MaxReplyLength] : trimmed;
        RepliedAt = now;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Javob matni uchun chegara.</summary>
    public const int MaxReplyLength = 500;

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// QO'NG'IROQ IZI (2026-08-18)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Loyiha egasi: ro'yxatda *"kim bog'langani — agar tel qilingan
    /// bo'lsa yoki TG xabar yuborilgan bo'lsa"* ko'rinishi kerak.
    ///
    /// ★ TELEGRAM XABARINI KIM YUBORGANI <see cref="SentById"/> DA, bu
    /// esa QO'NG'IROQNI kim qilgani. Ikkalasi har xil odam bo'lishi
    /// odatiy: xabarni o'quv bo'limi yuboradi, qo'ng'iroqni kurator
    /// qiladi. Bitta maydonga siqilsa, "men qo'ng'iroq qildim" degan
    /// fakt yo'qolardi.
    /// </summary>
    public long? CalledById { get; set; }

    public User? CalledBy { get; set; }

    public DateTimeOffset? CalledAt { get; set; }

    /// <summary>Qo'ng'iroqda aniqlangan sabab yoki qisqa izoh.</summary>
    public string? CallNote { get; set; }

    /// <summary>Qo'ng'iroq qilinganmi.</summary>
    public bool WasCalled => CalledAt is not null;

    /// <summary>
    /// Qo'ng'iroq izini yozadi.
    ///
    /// ★ TAKROR RUXSAT ETILADI (javobdan FARQLI): birinchi qo'ng'iroqda
    /// o'quvchi ko'tarmasligi mumkin va kurator qayta uringanini yozib
    /// qo'yishi kerak. Oxirgi urinish saqlanadi.
    /// </summary>
    public void MarkCalled(long actorId, string? note, DateTimeOffset now)
    {
        if (actorId <= 0) throw new DomainException("Qo'ng'iroq qilgan xodim ko'rsatilmagan.");

        var trimmed = (note ?? string.Empty).Trim();

        CalledById = actorId;
        CalledAt = now;
        CallNote = trimmed.Length == 0
            ? null
            : trimmed.Length > MaxReplyLength ? trimmed[..MaxReplyLength] : trimmed;

        UpdatedAt = now;
    }

    /// <summary>
    /// Yozuvni yaratadi.
    ///
    /// ★ FABRIKA METOD: matnni tozalash va chegaralash BITTA joyda —
    /// yuborish oqimi kelajakda ikkinchi joydan chaqirilsa (masalan fon
    /// vazifasidan) qoida takrorlanmaydi.
    /// </summary>
    public static AbsenceNotice Create(
        long studentId,
        long groupId,
        long sessionId,
        DateTimeOffset sessionStart,
        string? body,
        long sentById,
        bool toTelegram,
        DateTimeOffset now)
    {
        if (studentId <= 0) throw new DomainException("O'quvchi ko'rsatilmagan.");
        if (sessionId <= 0) throw new DomainException("Dars ko'rsatilmagan.");
        if (sentById <= 0) throw new DomainException("Yuboruvchi ko'rsatilmagan.");

        var trimmed = (body ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new DomainException("Xabar matnini kiriting.");

        return new AbsenceNotice
        {
            StudentId = studentId,
            GroupId = groupId,
            SessionId = sessionId,
            SessionStart = sessionStart,
            Body = trimmed.Length > MaxBodyLength ? trimmed[..MaxBodyLength] : trimmed,
            SentById = sentById,
            ToTelegram = toTelegram,
            SentAt = now,
            CreatedAt = now,
        };
    }
}
