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
