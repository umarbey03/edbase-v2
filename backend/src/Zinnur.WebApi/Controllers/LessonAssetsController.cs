using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Courses.Dtos;
using Zinnur.Application.Courses.Services;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ========================================================================
/// DARS MEDIASI: video qismlari (odatiy dars) / rasmlar (imtihon)
/// ========================================================================
///
/// Controller YUPQA: <c>[Authorize(Roles=...)]</c> — faqat DARVOZA.
/// Haqiqiy qoida (gating, to'lov bloki, dars turiga moslik)
/// <see cref="ILessonAssetService"/> ICHIDA.
///
/// ⚠️ YO'L KURS DARAXTIDAN MUSTAQIL (`/lessons/...`, `/courses/{id}/...`
/// EMAS). Sabab: fayl o'qish so'rovi `assetId` dan boshqa hech nimani
/// bilmasligi kerak — kurs va modul ID'sini yo'lga qo'shish klientni ular
/// bilan bog'lab qo'yardi va `&lt;video src&gt;` uchun keraksiz uzun manzil
/// yasardi. Dars ierarxiyasi baribir bazadan tekshiriladi.
/// </summary>
[ApiController]
[Route("api/v1/lessons")]
[Authorize]
[Produces("application/json")]
public sealed class LessonAssetsController(ILessonAssetService assets) : ControllerBase
{
    /// <summary>
    /// Yangi media yuklaydi (`multipart/form-data`, maydon nomi — `file`).
    ///
    /// ⚠️ `kind` SO'RALMAYDI: u DARS TURIDAN kelib chiqadi
    /// (`Normal` -> video, `Exam` -> rasm). Klientdan qabul qilinsa,
    /// invariantni buzadigan yozuv yasash imkoni paydo bo'lardi.
    ///
    /// ═══════════════════════════════════════════════════════════════════
    /// ★★ SO'ROV HAJMI IKKI QATLAMDA
    ///
    ///  1) SHU YERDAGI ATRIBUTLAR — QAT'IY yuqori chegara
    ///     (<see cref="MaxUploadBytes"/>). Bu KONSTANTA bo'lishi SHART:
    ///     atribut kompilyatsiya paytida hisoblanadi, sozlamani esa faqat
    ///     ish paytida bilamiz.
    ///
    ///  2) SERVIS ICHIDA — HAQIQIY chegara, `AppSetting` registridan
    ///     (`lesson.video_max_mb` / `lesson.image_max_mb`). Oshsa **413**.
    ///
    /// 🔴 NIMA UCHUN QAT'IY CHEGARA HAM KERAK: ASP.NET `multipart` faylni
    /// MODEL BOG'LASHDA, ya'ni bizning kodimizdan OLDIN vaqtinchalik
    /// diskka buferlaydi. Ya'ni "chegarasiz" atribut bilan istalgan
    /// xodim serverning diskini to'ldirib qo'yishi mumkin edi. Qat'iy
    /// chegara sozlamaning ENG KATTA ruxsat etilgan qiymatiga
    /// (`lesson.video_max_mb` ning `Maximum` i) teng qilib olingan —
    /// ya'ni eng yomon holatdagi disk sarfi administrator tanlashi
    /// mumkin bo'lgan qiymatdan oshmaydi.
    /// ═══════════════════════════════════════════════════════════════════
    /// </summary>
    [HttpPost("{lessonId:long}/assets")]
    [Authorize(Roles = ManageRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType<LessonAssetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LessonAssetDto>> Upload(
        long lessonId,
        IFormFile file,
        [FromForm] string? title,
        [FromForm] int? durationSec,
        [FromForm] int? width,
        [FromForm] int? height,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw MediaResponse.MissingFile();

        // Oqim `finally` da yopiladi: aks holda katta yuklashda vaqtinchalik
        // fayl deskriptori so'rov tugagach ham ushlanib turardi.
        await using var stream = file.OpenReadStream();

        var created = await assets.UploadAsync(
            lessonId,
            new LessonAssetUpload(
                file.FileName,

                // Klient AYTGAN tur faqat XATO XABARI uchun uzatiladi —
                // haqiqiy tur MAZMUNDAN aniqlanadi.
                file.ContentType,
                stream,
                file.Length,
                title,
                durationSec,
                width,
                height),
            CurrentUserId,
            ct);

        // `CreatedAtAction` ISHLATILMAYDI: `Get` faylning O'ZINI (baytlarni)
        // qaytaradi, ya'ni `Location` sarlavhasi "yaratilgan resursning
        // ko'rinishi" degan ma'noni bermaydi — u yerda JSON kutilardi.
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// Faylni OQIM bilan beradi.
    ///
    /// ═══════════════════════════════════════════════════════════════════
    /// ★★ `Range` QO'LLAB-QUVVATLANADI — VIDEO UCHUN HAYOTIY
    ///
    /// `Range` bo'lmasa brauzer videoning oxiriga o'ta olmaydi (seek
    /// ishlamaydi) va HAR ko'rishda faylni BOSHIDAN oqizadi. ~1 GB dars
    /// videosi uchun bu funksiyani foydasiz qilardi.
    ///
    ///   `Range` yo'q      -> 200 + `Accept-Ranges: bytes` + `Content-Length`
    ///   `Range` bor       -> 206 + `Content-Range: bytes N-M/TOTAL`
    ///   oraliq tashqarida -> 416 + `Content-Range: bytes * /TOTAL`
    ///
    /// Oraliq OMBORGA uzatiladi (S3 `Range` so'rovi) — ya'ni izlash ombor
    /// tomonida bo'ladi va API xotirasiga faqat so'ralgan bo'lak tushadi.
    /// ═══════════════════════════════════════════════════════════════════
    ///
    /// 🔴 RUXSAT HAR SO'ROVDA: xodim — har doim; o'quvchi — TO'LOV BLOKI
    /// (`Video` qamrovi) va GATING dan keyin. UI'da yashirish YETARLI EMAS:
    /// `assetId` ketma-ket va uni taxmin qilish oson.
    ///
    /// ⚠️ FRONTEND UCHUN: brauzer `&lt;video src&gt;` bilan `Authorization`
    /// sarlavhasini YUBORMAYDI. Bu endpointga token bilan murojaat qilish
    /// kerak (`fetch` + `MediaSource`, yoki qisqa muddatli oraliq).
    /// Batafsil: hisobotdagi shartnoma bo'limi.
    /// </summary>
    [HttpGet("assets/{assetId:long}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(long assetId, CancellationToken ct)
    {
        var download = await assets.OpenAsync(
            assetId, MediaResponse.RawRange(Request.Headers.Range), CurrentUserId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    /// <summary>
    /// Faylni o'chiradi (bazadan, so'ng ombordan).
    ///
    /// ⚠️ TASDIQ SO'RASH — FRONTEND ishi: bu amal QAYTARIB BO'LMAYDI
    /// (ombordagi obyekt ham o'chadi).
    /// </summary>
    [HttpDelete("assets/{assetId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long assetId, CancellationToken ct)
    {
        await assets.DeleteAsync(assetId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>
    /// Tartibni o'zgartiradi.
    ///
    /// 🔴 TO'LIQ ro'yxat kutiladi (darsning BARCHA fayl Id'lari) — server
    /// uni 0,1,2... qilib zich qayta raqamlaydi. Yetishmasa yoki begona Id
    /// bo'lsa **400** va HECH NARSA yozilmaydi (`DAVOM_ETTIRISH.md`
    /// 6-bo'lim, 7-tuzoq).
    ///
    /// ⚠️ METOD `PUT` (kurs/modul/dars tartibi esa `POST .../reorder`).
    /// Farq ONGLI EMAS, u talab shaklidan keldi — hisobotda qayd etilgan.
    /// </summary>
    [HttpPut("{lessonId:long}/assets/reorder")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<PositionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PositionDto>>> Reorder(
        long lessonId, [FromBody] ReorderRequest request, CancellationToken ct) =>
        Ok(await assets.ReorderAsync(lessonId, request, CurrentUserId, ct));

    // ---------------------------------------------------------------- ichki

    /// <summary>Dars kontentini O'ZGARTIRA oladigan rollar (`CoursesController` bilan AYNI).</summary>
    private const string ManageRoles = "Academic,Admin";

    /// <summary>
    /// So'rov tanasining QAT'IY yuqori chegarasi (bayt).
    ///
    /// ★ QIYMAT `lesson.video_max_mb` sozlamasining `Maximum` i bilan MOS
    ///   (2048 MB) + sarlavhalar va multipart chegaralari uchun 1 MB zaxira.
    ///   Ikkisi bir-biridan chetga chiqmasligi uchun registrdagi chegara
    ///   izohida ham shu bog'liqlik yozilgan.
    ///
    /// ⚠️ Bu chegara HIMOYA, biznes qoidasi EMAS. Foydalanuvchi ko'radigan
    ///    chegara — sozlamadagi qiymat (standart 1024 MB).
    /// </summary>
    private const long MaxUploadBytes = (2048L * 1024 * 1024) + (1024 * 1024);

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
