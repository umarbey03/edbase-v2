using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// UY VAZIFASI SHARTINING BIRIKTIRMASI (rasm / audio / hujjat)
/// ========================================================================
///
/// ★ NIMA UCHUN KERAK BO'LDI: `Assignment.ImageKey` — BITTA rasm. Talab esa
/// "shart matn, rasm yoki audio bo'lishi mumkin va BIR NECHTA bo'lishi
/// mumkin" (arab tili talaffuzi uchun audio namuna + qo'lyozma varaq rasmi
/// birga). Bitta ustunni bir nechta qiymat uchun ishlatishning yo'li yo'q.
///
/// ★ `Assignment.ImageKey` O'CHIRILMADI: mavjud vazifalarning rasmi
/// yo'qolmasin. Migratsiya uni shu jadvalga KO'CHIRADI (backfill) va DTO'da
/// `imageKey` "deprecated" deb belgilanadi — yangi UI faqat
/// <c>attachments</c> bilan ishlaydi.
///
/// ★ JAVOB TOMONI ALOHIDA: o'quvchining javob fayli — <see cref="SubmissionFile"/>.
/// Ikkalasi bir jadvalga qo'shilmaydi: ular BOSHQA-BOSHQA ruxsat qoidasiga
/// bo'ysunadi (shart hamma ko'radi, javobni faqat egasi va uning ustozi) va
/// bir jadvalda bo'lsa bitta `WHERE` ni unutish begona bolaning ishini
/// oshkor qilardi.
/// </summary>
public class AssignmentAttachment : BaseEntity
{
    public long AssignmentId { get; set; }

    public Assignment? Assignment { get; set; }

    /// <summary>Fayl turi — MAZMUNDAN aniqlanadi, klient aytganidan emas.</summary>
    public AttachmentKind Kind { get; set; }

    /// <summary>Shart ichidagi tartib (0 dan, ZICH).</summary>
    public int Position { get; set; }

    /// <summary>
    /// 🔴 OMBOR KALITI — UI'GA CHIQMAYDI (16-tuzoq). Sabab
    /// <see cref="LessonAsset.ObjectKey"/> da batafsil.
    /// </summary>
    public required string ObjectKey { get; set; }

    /// <summary>MAZMUNDAN aniqlangan MIME turi.</summary>
    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Audio davomiyligi (bo'lsa) — pleyer uzunlikni oldindan ko'rsatsin.</summary>
    public int? DurationSec { get; set; }

    public long? CreatedById { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ObjectKey))
            throw new DomainException("Ombor kaliti bo'sh bo'lishi mumkin emas.");

        if (string.IsNullOrWhiteSpace(ContentType))
            throw new DomainException("Fayl turi (MIME) aniqlanmagan.");

        if (SizeBytes <= 0)
            throw new DomainException("Fayl hajmi noldan katta bo'lishi kerak.");

        if (DurationSec is { } duration && duration is < 0 or > LessonAsset.MaxDurationSec)
            throw new DomainException("Davomiylik qiymati haqiqatga to'g'ri kelmaydi.");
    }
}
