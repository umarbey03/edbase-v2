using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Courses.Services;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// ========================================================================
/// USTOZ TEKSHIRISHDA BIRIKTIRADIGAN FAYLLAR (R37)
/// ========================================================================
///
/// ── ★ NIMA UCHUN <c>POST /grade</c> MULTIPART'GA AYLANTIRILMADI ───────
///
/// Eng aniq ko'rinadigan yechim shu edi: <c>GradeSubmissionRequest</c> ni
/// <c>[FromForm]</c> ga o'tkazib, o'sha so'rovga fayllarni qo'shish.
/// RAD ETILDI, uchta sabab bilan:
///
///  1) 🔴 MAVJUD KLIENTLARNI BUZARDI. <c>POST /submissions/{id}/grade</c>
///     bugun JSON qabul qiladi va uni frontend ham, integratsion testlar
///     ham shunday chaqiradi. <c>Consumes("multipart/form-data")</c>
///     qo'shilishi bilan HAR BIR mavjud chaqiruv **415** olardi.
///
///  2) BAHO QO'YISH VA FAYL BIRIKTIRISH — IKKI XIL TEZLIKDAGI AMAL.
///     Ustoz 50 ta ishni ketma-ket baholaydi (har biri bir necha yuz
///     bayt), fayl esa faqat ba'zilarida va u 10 MB bo'lishi mumkin.
///     Bitta endpointda birlashtirilsa, ARZON amal QIMMAT amalning
///     so'rov chegaralarini (<c>RequestSizeLimit</c>, multipart
///     buferlash) meros qilib olardi.
///
///  3) QAYTA URINISH SEMANTIKASI BOSHQA. Yuklash yiqilsa, ustoz faylni
///     qayta yuboradi — lekin BAHO allaqachon qo'yilgan bo'lishi mumkin
///     va uni ikkinchi marta yozish bildirishnomani ham ikkinchi marta
///     yuborardi. Alohida endpointda "baho" va "fayl" mustaqil ravishda
///     qayta urinadi.
///
/// ── ★ NIMA UCHUN <c>SubmissionAttachmentReader</c> ISHLATILMAYDI ──────
///
/// U FAQAT <c>Image | Audio</c> ni o'tkazadi va bu ATAYLAB shunday: o'quvchi
/// yo'li o'sha tor ro'yxat bilan eski zaiflikni yopgan. Ustozning sharhi
/// esa ko'pincha PDF bo'ladi.
///
/// 🔴 O'QUVCHI YO'LI KENGAYTIRILMAYDI. <c>AllowedCategories</c> ga
/// <c>Document</c> qo'shish BIR QATOR bo'lardi, lekin u AYNI paytda
/// o'quvchining topshirish yo'lini ham kengaytirardi — ya'ni bitta talab
/// ("ustoz PDF yubora olsin") ikkinchi, umuman so'ralmagan o'zgarishni
/// (o'quvchi endi ixtiyoriy PDF yuklaydi) olib kelardi. Buning o'rniga
/// <c>AssignmentAttachmentService</c> allaqachon ishlatadigan yo'l
/// (<c>MediaSignatures</c> + <c>Image|Audio|Document</c>) qayta
/// ishlatiladi.
///
/// ── OMBOR: <c>IMediaStorage</c>, <c>ISubmissionStorage</c> EMAS ───────
///
/// Sabab bitta va hal qiluvchi: <c>ISubmissionStorage</c> da
/// <c>DeleteAsync</c> YO'Q (ataylab — o'quvchining javobi hech qachon
/// o'chirilmaydi). Ustoz esa noto'g'ri faylni biriktirib qo'yishi mumkin
/// va uni o'chira olishi SHART. Qo'shimcha foyda: <c>IMediaStorage</c>
/// oqim bilan yozadi va <c>Range</c> ni qo'llaydi.
/// </summary>
public interface ISubmissionFeedbackFileService
{
    /// <summary>
    /// Javob tekshiruviga fayl biriktiradi. Turi MAZMUNDAN aniqlanadi.
    ///
    /// RUXSAT: javobni BAHOLASH huquqi bilan AYNI
    /// (<c>IAssignmentService.EnsureCanGradeSubmissionAsync</c>).
    /// </summary>
    Task<SubmissionFeedbackFileDto> UploadAsync(
        long submissionId, LessonAssetUpload upload, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Faylni O'QISHGA ochadi (oqim, <c>Range</c> bilan).
    ///
    /// RUXSAT: javobni KO'RISH huquqi — ya'ni O'QUVCHI HAM OLADI. Bu R37
    /// talabining MOHIYATI: ustoz biriktirgan tuzatishni o'quvchi ko'rishi
    /// kerak, aks holda fayl faqat ustozning o'ziga ko'rinardi.
    /// </summary>
    Task<LessonAssetDownload> OpenAsync(
        long fileId, string? rangeHeader, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Faylni o'chiradi (bazadan, so'ng ombordan).
    ///
    /// RUXSAT: yuklash bilan AYNI (baholay oladigan xodim).
    /// ⚠️ O'QUVCHI O'CHIRA OLMAYDI — bu ustozning sharhi.
    /// </summary>
    Task DeleteAsync(long fileId, long actorId, CancellationToken ct = default);
}
