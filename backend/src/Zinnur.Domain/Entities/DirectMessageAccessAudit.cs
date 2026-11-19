using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// SHAXSIY YOZISHMANI BEGONA XODIM O'QIGANINING IZI (2026-11-19)
/// ========================================================================
///
/// Kurator ↔ o'quvchi yozishmasi ilgari FAQAT o'sha ikki kishiga ochiq edi
/// (<c>DirectMessageService.ResolvePairAsync</c>: "admin avtomatik kirmaydi").
/// Loyiha egasi Mustaqil paneli uchun qaror qildi: o'quv bo'limi va
/// administrator kurator ishini nazorat qilish uchun yozishmani FAQAT
/// O'QIY oladi. Bu maxfiylikka tegadigan imkoniyat, shuning uchun HAR bir
/// ochish shu jadvalga yoziladi: "kim, qachon, kimning yozishmasini (yoki
/// qaysi faylini) ko'rdi" savoliga doim javob bo'lsin.
///
/// Naqsh <see cref="TelegramUnlinkAudit"/> bilan AYNI: hodisa bitta, turi
/// aniq, ustunlar tiplangan; FAQAT QO'SHILADI va O'QILADI — yangilanmaydi,
/// o'chirilmaydi. Yozish o'qishning O'ZIDAN OLDIN bajariladi: audit
/// yozilmasa (baza xatosi) yozishma ham ochilmaydi.
/// </summary>
public class DirectMessageAccessAudit : BaseEntity
{
    /// <summary>Yozishmani ochgan xodim (o'quv bo'limi yoki administrator).</summary>
    public long ViewerId { get; set; }

    /// <summary>Yozishma juftligining o'quvchi tomoni.</summary>
    public long StudentId { get; set; }

    /// <summary>Yozishma juftligining xodim (kurator) tomoni.</summary>
    public long StaffId { get; set; }

    /// <summary>Nima ochildi: yozishma sahifasi yoki biriktirilgan fayl.</summary>
    public DirectMessageAccessKind Kind { get; set; }

    /// <summary>Fayl ochilganda — qaysi biriktirma; yozishma sahifasida <c>null</c>.</summary>
    public long? AttachmentId { get; set; }

    /// <summary>Ochilgan vaqt (UTC).</summary>
    public DateTimeOffset At { get; set; }

    public static DirectMessageAccessAudit Thread(
        long viewerId, long studentId, long staffId, DateTimeOffset at) =>
        new()
        {
            ViewerId = viewerId,
            StudentId = studentId,
            StaffId = staffId,
            Kind = DirectMessageAccessKind.Thread,
            At = at,
        };

    public static DirectMessageAccessAudit Attachment(
        long viewerId, long studentId, long staffId, long attachmentId, DateTimeOffset at) =>
        new()
        {
            ViewerId = viewerId,
            StudentId = studentId,
            StaffId = staffId,
            Kind = DirectMessageAccessKind.Attachment,
            AttachmentId = attachmentId,
            At = at,
        };
}
