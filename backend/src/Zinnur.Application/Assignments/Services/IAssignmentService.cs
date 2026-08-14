using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Common.Models;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// Uy vazifalari: yaratish/tahrirlash (xodim), topshirish (o'quvchi),
/// baholash va qayta topshirishga ruxsat berish (ustoz/kurator).
/// </summary>
public interface IAssignmentService
{
    Task<PagedResult<AssignmentDto>> ListAsync(
        AssignmentListQuery query, long actorId, CancellationToken ct = default);

    Task<AssignmentDto> GetAsync(long id, long actorId, CancellationToken ct = default);

    /// <summary>O'quvchining KO'RA OLADIGAN vazifalari + o'z javobi holati.</summary>
    Task<IReadOnlyList<StudentAssignmentDto>> ListMineAsync(
        long studentId, CancellationToken ct = default);

    Task<AssignmentDto> CreateAsync(
        CreateAssignmentRequest request, long actorId, CancellationToken ct = default);

    Task<AssignmentDto> UpdateAsync(
        long id, UpdateAssignmentRequest request, long actorId, CancellationToken ct = default);

    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Javob topshirish (matn va/yoki fayllar).
    ///
    /// Fayllar OQIM DAVOMIDA tekshiriladi
    /// (<see cref="SubmissionAttachmentReader"/>) va obyekt omboriga yoziladi.
    /// Ombor sozlanmagan bo'lsa 503 — lokal diskka YOZILMAYDI.
    /// </summary>
    Task<StudentSubmissionDto> SubmitAsync(
        long assignmentId,
        string? text,
        IReadOnlyList<IncomingFile> files,
        long studentId,
        CancellationToken ct = default);

    Task<IReadOnlyList<SubmissionDto>> ListSubmissionsAsync(
        long assignmentId, long actorId, CancellationToken ct = default);

    Task<SubmissionDto> GradeAsync(
        long submissionId, GradeSubmissionRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Qayta topshirishga ruxsat beradi (bir marta — Domain o'zi yopadi).</summary>
    Task<SubmissionDto> ReopenAsync(
        long submissionId, ReopenSubmissionRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Ilova qilingan faylni O'QISHGA ochadi (ruxsat tekshirilgach).
    ///
    /// Fayl ID'si bo'yicha — obyekt kaliti bo'yicha EMAS: kalitni
    /// chaqiruvchidan qabul qilish begona obyektni so'rash imkonini berardi.
    /// </summary>
    /// <exception cref="Common.Exceptions.ForbiddenException">
    /// Fayl boshqa o'quvchiniki (yoki xodimning guruhida bo'lmagan o'quvchiniki).
    /// </exception>
    /// <exception cref="Common.Exceptions.ServiceUnavailableException">
    /// Ombor sozlanmagan yoki javob bermayapti.
    /// </exception>
    Task<SubmissionFileDownload> OpenFileAsync(
        long fileId, long actorId, CancellationToken ct = default);

    // ================================================================= WAVE 1
    //
    // ★ RUXSAT QOIDASINI QAYTA ISHLATISH UCHUN IKKI DARVOZA.
    //
    // NIMA UCHUN OSHKOR QILINDI: vazifa SHARTINING biriktirmalari
    // (`IAssignmentAttachmentService`) AYNI qoidaga bo'ysunishi kerak —
    // "kim vazifani ko'radi" va "kim uni tahrirlaydi". Qoida esa juda
    // nozik: kurs vazifasini HAR ustoz ko'radi (baholash uchun), lekin
    // faqat o'quv bo'limi tahrirlaydi; guruh vazifasini esa o'sha
    // guruhning ustozi/kuratori ham tahrirlaydi; o'quvchi faqat o'ziga
    // TEGISHLI vazifani ko'radi va kurs vazifasida gating ham qatnashadi.
    //
    // Bu qoidani ikkinchi servisda qayta yozish — kafolatlangan xato:
    // nusxalardan biri "kurs vazifasi" holatini o'tkazib yuborardi va
    // ustoz butun platforma vazifalarining shartini tahrirlay olardi.

    /// <summary>
    /// Vazifani KO'RISH huquqini tekshiradi (rolga va nishoniga qarab).
    /// O'quvchi uchun "menga tegishlimi" tekshiruvi ham shu yerda.
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Vazifa yo'q.</exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task EnsureCanReadAssignmentAsync(
        long assignmentId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Vazifani TAHRIRLASH huquqini tekshiradi — o'qishdan qat'iyroq.
    /// O'quvchi hech qachon o'tmaydi.
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Vazifa yo'q.</exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task EnsureCanWriteAssignmentAsync(
        long assignmentId, long actorId, CancellationToken ct = default);

    // ================================================================= R37
    //
    // ★ JAVOB DARAJASIDAGI IKKI DARVOZA — TEKSHIRUV FAYLLARI UCHUN.
    //
    // Yuqoridagi ikkitasi VAZIFA haqida ("kim shu vazifani ko'radi"),
    // bulari esa BITTA JAVOB haqida ("kim shu o'quvchining ishini
    // ko'radi", "kim uni baholaydi"). Farq muhim: kurs vazifasini HAR
    // ustoz ko'radi, lekin begona guruhning JAVOBINI ko'rmaydi.
    //
    // 🔴 NIMA UCHUN QAYTA YOZILMADI: ikkala qoida ham allaqachon
    // `AssignmentService` ICHIDA bor (`EnsureCanReadStudentWorkAsync` va
    // `LoadSubmissionForStaffAsync`) va aynan ular fayl o'qish yo'lini
    // qo'riqlaydi. Ularni ikkinchi servisda takrorlash eski tizimning
    // X-6 kamchiligini (begona bolaning ishini ko'rish) qaytarib
    // keltirishning eng ehtimolli yo'li bo'lardi.

    /// <summary>
    /// Javobni (o'quvchining ISHINI) KO'RISH huquqi.
    ///
    /// O'TADI: javobning EGASI; o'sha o'quvchiga mas'ul ustoz/kurator;
    /// o'quv bo'limi va admin. Boshqa o'quvchi — SHARTSIZ 403.
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Javob yo'q.</exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task EnsureCanReadSubmissionAsync(
        long submissionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Javobni BAHOLASH (va tekshiruv fayli biriktirish) huquqi —
    /// o'qishdan qat'iyroq: o'quvchi HECH QACHON o'tmaydi.
    ///
    /// ★ AYNAN <c>GradeAsync</c> ishlatadigan yo'l bilan bir xil manba
    /// (<c>LoadSubmissionForStaffAsync</c>) — ya'ni "baho qo'ya oladigan
    /// odam" va "tekshiruv fayli qo'ya oladigan odam" DOIM bir xil
    /// bo'lib qoladi.
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Javob yo'q.</exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task EnsureCanGradeSubmissionAsync(
        long submissionId, long actorId, CancellationToken ct = default);
}

/// <summary>
/// Klientga uzatishga tayyor fayl.
///
/// <c>Content</c> — TARMOQ OQIMI, ya'ni chaqiruvchi uni javob tugagach
/// yopishi SHART (WebApi buni `RegisterForDisposeAsync` bilan qiladi).
/// Baytlar API xotirasida to'planmaydi.
/// </summary>
/// <param name="Content">Ombordan ochilgan oqim (egalik chaqiruvchida).</param>
/// <param name="ContentType">Yuklashda MAZMUNDAN aniqlangan MIME turi.</param>
/// <param name="FileName">Foydalanuvchiga ko'rinadigan nom (obyekt kaliti EMAS).</param>
public sealed record SubmissionFileDownload(
    StoredFile Content,
    string ContentType,
    string FileName);
