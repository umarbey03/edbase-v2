using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Courses.Dtos;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;
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
public sealed class LessonAssetsController(
    ILessonAssetService assets, IMediaAccessTicketService tickets) : ControllerBase
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
    /// ═══════════════════════════════════════════════════════════════════
    /// ★★ KIMLIGINI ANIQLASHNING IKKI YO'LI
    ///
    ///   1) `Authorization: Bearer …` — odatiy yo'l (`fetch`, rasm
    ///      ko'rish, xodim vositalari, testlar).
    ///   2) `?ticket=…` — QISQA MUDDATLI CHIPTA
    ///      (<see cref="IMediaAccessTicketService"/>). U FAQAT shuning
    ///      uchun bor: brauzerning `&lt;video src&gt;` elementi
    ///      `Authorization` sarlavhasini YUBORA OLMAYDI va uni
    ///      majburlashning yo'li yo'q.
    ///
    /// Shuning uchun endpoint `[AllowAnonymous]`. 🔴 BU "OCHIQ" DEGANI
    /// EMAS: quyidagi <see cref="ResolveActorId"/> ikkala yo'ldan HECH
    /// BIRI kimligini aniqlay olmasa **401** beradi, aniqlagan taqdirda
    /// esa qaror baribir `LessonAssetService.EnsureCanReadAsync` da
    /// (to'lov bloki + gating, HAR so'rovda) qabul qilinadi.
    ///
    /// ⚠️ CHIPTA `assetId` GA BOG'LANGAN: 5-fayl chiptasi 6-faylda
    /// ishlamaydi (imzo ichida Id bor).
    /// ═══════════════════════════════════════════════════════════════════
    ///
    /// ⚠️ PLEYER UCHUN SHARTNOMA: chipta ~15 daqiqada o'ladi. Uzun darsda
    /// keyingi `Range` so'rovi **401** oladi va brauzer buni `error`
    /// hodisasi qilib beradi — pleyer YANGI chipta olib, `currentTime` ni
    /// tiklashi kerak (`RecordingPlayerModal` dagi AYNI naqsh).
    /// </summary>
    [HttpGet("assets/{assetId:long}")]
    [AllowAnonymous]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(
        long assetId, [FromQuery] string? ticket, CancellationToken ct)
    {
        if (ResolveActorId(assetId, ticket) is not { } actorId)
            return Unauthorized();

        var download = await assets.OpenAsync(
            assetId, MediaResponse.RawRange(Request.Headers.Range), actorId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    /// <summary>
    /// O'YNATISH CHIPTASI: `&lt;video src&gt;` ga qo'yish uchun qisqa
    /// muddatli belgi.
    ///
    /// 🔴 CHIPTA BERISHDAN OLDIN RUXSAT TO'LIQ TEKSHIRILADI (to'lov bloki
    /// va gating). Ya'ni qulflangan darsda yoki qarz bilan o'quvchi
    /// chiptani UMUMAN olmaydi va sababni **403** ning matnida DARHOL
    /// ko'radi — videoni bosib, keyin "buzuq fayl" bilan yuzlashmaydi.
    ///
    /// ⚠️ Javob `expiresAt` bilan keladi va u SHARTNOMANING BIR QISMI:
    /// pleyer shu vaqtdan oldin yangisini so'rashi kerak
    /// (`RecordingsController.Link` bilan AYNI naqsh).
    /// </summary>
    /// <response code="200">Chipta va uning muddati.</response>
    /// <response code="403">Gating yopiq yoki to'lov qarzi (`Video` qamrovi).</response>
    [HttpGet("assets/{assetId:long}/ticket")]
    [ProducesResponseType<MediaAccessTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaAccessTicket>> Ticket(long assetId, CancellationToken ct)
    {
        var ticket = await assets.CreateTicketAsync(assetId, CurrentUserId, ct);

        // 🔴 KESHLANMASIN: ichida imzo bor va u FOYDALANUVCHIGA xos.
        //    Oraliq proksi (yoki umumiy kompyuterdagi brauzer) saqlab
        //    qolsa, keyingi foydalanuvchi o'zganing chiptasini olardi.
        //    `RecordingsController.Link` da AYNI sabab, AYNI qator.
        Response.Headers.CacheControl = "no-store";

        return Ok(ticket);
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
    /// ★ METOD `POST` — loyihadagi qolgan UCHTA reorder endpointi bilan AYNI
    /// (`POST /courses/reorder`, `POST /courses/{id}/modules/reorder`,
    /// `POST /courses/{id}/modules/{id}/lessons/reorder`).
    ///
    /// ⚠️ AVVAL `PUT` edi (topshiriq shakli shunday bergan) — integratsiyada
    /// `POST` ga o'tkazildi: to'rtta bir xil amal ikki xil metodda bo'lishi
    /// frontend uchun yashirin tuzoq. `PUT` bu yerda semantik jihatdan ham
    /// noto'g'ri edi — u "resursni to'liq almashtirish" degan ma'noni beradi
    /// (`DAVOM_ETTIRISH.md` 6-bo'lim, 1-tuzoq), bu esa RESURS emas, AMAL.
    /// Frontend hali yozilmagani uchun buzilgan klient yo'q.
    /// </summary>
    [HttpPost("{lessonId:long}/assets/reorder")]
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

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 KIMLIGINI ANIQLASH — SESSIYA YOKI CHIPTA (BOSHQA YO'L YO'Q)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Bu metod FAQAT "kim?" degan savolga javob beradi. "Ruxsatmi?"
    /// degan savol bu yerda UMUMAN so'ralmaydi — u
    /// `LessonAssetService.EnsureCanReadAsync` ning ishi va u HAR bayt
    /// so'rovida bajariladi. Ikkalasini bir joyga qo'shish o'sha
    /// tekshiruvni chetlab o'tadigan ikkinchi yo'l yasardi.
    ///
    /// ★ SESSIYA USTUN: `Authorization` sarlavhasi kelgan bo'lsa chipta
    ///   umuman o'qilmaydi. Sabab — sarlavha JWT quvurida to'liq
    ///   tekshirilgan (imzo, muddat, `ver`/TokenVersion), chipta esa
    ///   `ver` ni bilmaydi. Kuchliroq dalil ustun turishi kerak.
    ///
    /// 🔴 `null` QAYTSA — 401. "Havolani bilish" hech nima bermaydi.
    /// </summary>
    private long? ResolveActorId(long assetId, string? ticket)
    {
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } subject
            && long.TryParse(subject, CultureInfo.InvariantCulture, out var sessionUserId))
        {
            return sessionUserId;
        }

        return tickets.TryResolveUserId(ticket, assetId);
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
