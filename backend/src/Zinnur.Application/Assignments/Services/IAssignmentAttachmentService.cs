using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Courses.Services;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// Uy vazifasi SHARTIGA biriktirilgan fayllar (rasm / audio / hujjat).
///
/// ★ NIMA UCHUN `IAssignmentService` GA QO'SHILMADI: u allaqachon katta
/// (vazifa CRUD, topshirish, baholash, qayta topshirish) va OMBORNI faqat
/// javob fayllari uchun biladi. Shart biriktirmalari esa BOSHQA ruxsat
/// qoidasiga bo'ysunadi (shartni faqat vazifani TAHRIRLAY oladigan xodim
/// o'zgartiradi, KO'RISHNI esa o'quvchi ham qiladi) va boshqa omborni
/// (`IMediaStorage`) ishlatadi.
/// </summary>
public interface IAssignmentAttachmentService
{
    /// <summary>Shartga yangi fayl biriktiradi. Turi MAZMUNDAN aniqlanadi.</summary>
    Task<AssignmentAttachmentDto> UploadAsync(
        long assignmentId, LessonAssetUpload upload, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Faylni O'QISHGA ochadi (oqim, `Range` bilan).
    ///
    /// RUXSAT: xodim — mavjud vazifa ko'rish qoidasi bo'yicha; o'quvchi —
    /// vazifa unga KO'RINADIGAN bo'lsa (kurs vazifasida gating ham).
    /// </summary>
    Task<LessonAssetDownload> OpenAsync(
        long attachmentId, string? rangeHeader, long actorId, CancellationToken ct = default);

    /// <summary>Biriktirmani o'chiradi (bazadan, so'ng ombordan).</summary>
    Task DeleteAsync(long attachmentId, long actorId, CancellationToken ct = default);
}
