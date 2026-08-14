using Zinnur.Application.Courses.Dtos;
using Zinnur.Application.Media;

namespace Zinnur.Application.Courses.Services;

/// <summary>
/// Dars mediasi (video qismlari / imtihon rasmlari) use-case'lari.
///
/// Har metod <c>actorId</c> oladi: ruxsat qoidasi SERVIS ichida tekshiriladi,
/// controller atributi faqat DARVOZA (`ICourseService` bilan AYNI naqsh).
/// Sabab: servis fon vazifasidan yoki SignalR hub'idan ham chaqirilishi
/// mumkin — o'sha yerda `[Authorize]` atributi umuman ishlamaydi.
/// </summary>
public interface ILessonAssetService
{
    /// <summary>
    /// Yangi media yuklaydi. Turi (`Video`/`Image`) DARS TURIDAN aniqlanadi,
    /// klientdan qabul qilinmaydi.
    /// </summary>
    /// <exception cref="Common.Exceptions.ForbiddenException">
    /// Yozish huquqi yo'q (faqat <c>Academic</c>/<c>Admin</c>).
    /// </exception>
    /// <exception cref="Common.Exceptions.ValidationException">
    /// Fayl turi qo'llanmaydi (magic bytes) yoki metama'lumot noto'g'ri -> 400.
    /// </exception>
    /// <exception cref="Common.Exceptions.PayloadTooLargeException">
    /// Fayl sozlamadagi chegaradan katta -> 413.
    /// </exception>
    Task<LessonAssetDto> UploadAsync(
        long lessonId, LessonAssetUpload upload, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Faylni O'QISHGA ochadi (oqim). <paramref name="rangeHeader"/> berilsa
    /// QISMAN javob tayyorlanadi.
    ///
    /// 🔴 RUXSAT HAR SO'ROVDA tekshiriladi: xodim — har doim; o'quvchi —
    /// GATING va TO'LOV BLOKI (`PaymentBlockScope.Video`) dan keyin.
    /// </summary>
    Task<LessonAssetDownload> OpenAsync(
        long assetId, string? rangeHeader, long actorId, CancellationToken ct = default);

    /// <summary>
    /// O'YNATISH CHIPTASINI beradi — `&lt;video src&gt;` uchun.
    ///
    /// 🔴 RUXSAT AYNAN <see cref="OpenAsync"/> DAGIDEK tekshiriladi
    /// (to'lov bloki + gating). Bu ATAYLAB: qulflangan darsning chiptasi
    /// UMUMAN berilmasin — o'quvchi 403 ni videoni bosgandan keyin emas,
    /// DARHOL va tushunarli xabar bilan ko'rsin.
    ///
    /// ⚠️ Chipta ruxsat BERMAYDI, u faqat "kim" ekanini aytadi. Har bayt
    /// so'rovida ruxsat QAYTADAN tekshiriladi — batafsil:
    /// <see cref="IMediaAccessTicketService"/>.
    /// </summary>
    Task<MediaAccessTicket> CreateTicketAsync(
        long assetId, long actorId, CancellationToken ct = default);

    /// <summary>Faylni o'chiradi (bazadan, so'ng ombordan).</summary>
    Task DeleteAsync(long assetId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Tartibni o'zgartiradi. 🔴 TO'LIQ ro'yxat kutiladi — yetishmasa 400
    /// (`DAVOM_ETTIRISH.md` 6-bo'lim, 7-tuzoq).
    /// </summary>
    Task<IReadOnlyList<PositionDto>> ReorderAsync(
        long lessonId, ReorderRequest request, long actorId, CancellationToken ct = default);
}

/// <summary>
/// Yuklashga kelgan fayl — HTTP'dan MUSTAQIL ko'rinish (Application qatlami
/// <c>IFormFile</c> ni ko'rmaydi: u ASP.NET turi).
/// </summary>
/// <param name="ClientFileName">Faqat log va xato xabari uchun.</param>
/// <param name="ClientContentType">
/// Klient AYTGAN tur. 🔴 QARORGA ASOS BO'LMAYDI — uni istalgan klient
/// xohlagan qiymatga yozib yuboradi. Faqat xato xabarida ishlatiladi.
/// </param>
/// <param name="Content">
/// IZLANADIGAN (seekable) oqim, pozitsiyasi 0 da. Sabab
/// <see cref="MediaUpload.Content"/> izohida.
/// </param>
/// <param name="Length">Fayl hajmi — chegara tekshiruvi BUNDAN boshlanadi.</param>
/// <param name="Title">Ko'rinadigan nom ("1-qism").</param>
/// <param name="DurationSec">Klient o'lchagan davomiylik (ixtiyoriy).</param>
public sealed record LessonAssetUpload(
    string? ClientFileName,
    string? ClientContentType,
    Stream Content,
    long Length,
    string? Title = null,
    int? DurationSec = null,
    int? Width = null,
    int? Height = null);

/// <summary>
/// O'qishga ochilgan media va javob uchun TAYYOR sarlavha qiymatlari.
///
/// ★ NIMA UCHUN HOLAT KODI HAM SHU YERDA: "206 yoki 200?" degan savolga
/// javob ombor javobiga bog'liq (`StoredMedia.IsPartial`). Controller uni
/// O'ZI taxmin qilsa, bir kuni ombor `Range` ni bajarmagan holatda ham 206
/// qaytarib qo'yardi — brauzer o'shanda videoni buzuq deb hisoblardi.
/// </summary>
/// <param name="Content">Ombordan ochilgan oqim (chaqiruvchi yopadi).</param>
/// <param name="ContentType">BAZADAN olingan MIME turi (ombor sarlavhasidan ustun).</param>
/// <param name="FileName">
/// Taklif qilinadigan fayl nomi. 🔴 OBYEKT KALITI EMAS — unda ichki
/// tuzilma bor.
/// </param>
/// <param name="TotalLength">Faylning TO'LIQ hajmi (`Content-Range` uchun).</param>
/// <param name="Range">Qaytarilayotgan oraliq; to'liq javobda <c>null</c>.</param>
public sealed record LessonAssetDownload(
    StoredMedia Content,
    string ContentType,
    string FileName,
    long TotalLength,
    MediaByteRange? Range);
