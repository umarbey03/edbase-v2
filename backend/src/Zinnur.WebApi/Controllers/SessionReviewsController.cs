using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.SessionReviews.Dtos;
using Zinnur.Application.SessionReviews.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// DARS SIFATI TAHLILI (talab R29 va R30).
///
/// ★ MANZIL DARSGA BOG'LANGAN (<c>/live-sessions/{id}/review</c>), YOZUVGA
/// EMAS — bu entity qarorining to'g'ridan-to'g'ri natijasi
/// (<c>SessionReview</c> izohi). Yozuvga bog'langan manzil bo'lsa, ustozning
/// "Darslarim" jadvali (unda yozuv Id'si UMUMAN yo'q) avval dars -> yozuv
/// izlashga majbur bo'lardi va yozuvi chiqmagan darsda tahlilga yo'l
/// qolmasdi.
///
/// Controller YUPQA: <c>[Authorize(Roles=…)]</c> — faqat DARVOZA. Haqiqiy
/// qoida ("faqat O'Z darsim", "ustoz tahrirlay olmaydi", "o'quvchi hech
/// qachon") <see cref="ISessionReviewService"/> ICHIDA.
///
/// 🔴 O'QUVCHI UCHUN ATRIBUT VA SERVIS — IKKI MUSTAQIL QATLAM VA IKKALASI
/// HAM KERAK. Atribut HTTP yo'lini yopadi, servis esa hub, fon vazifasi
/// yoki kelajakdagi boshqa chaqiruvchi uchun ham ishlaydi. Bittasiga
/// tayanish — o'quvchi ustozi haqidagi ichki bahoni o'qib qolishi degani.
/// </summary>
[ApiController]
[Route("api/v1/live-sessions/{sessionId:long}/review")]
[Authorize]
[Produces("application/json")]
public sealed class SessionReviewsController(ISessionReviewService reviews) : ControllerBase
{
    /// <summary>Tahlilni yozadigan rollar (R29).</summary>
    private const string WriteRoles = "Academic,Admin";

    /// <summary>
    /// Tahlilni o'qiydigan rollar: yuqoridagilar + darsning O'Z ustozi
    /// yoki kuratori (R30). "O'Z darsi" sharti atribut bilan ifodalanmaydi
    /// — u servisda.
    /// </summary>
    private const string ReadRoles = "Teacher,Assistant,Academic,Admin";

    /// <summary>
    /// Darsning sifat tahlili.
    ///
    /// ⚠️ TAHLIL YO'Q BO'LSA <c>200</c> VA JSON <c>null</c> — 404 EMAS.
    /// "Hali yozilmagan" — normal va eng ko'p uchraydigan holat; 404 bo'lsa
    /// klient uni "dars topilmadi" bilan ajrata olmasdi va modal har
    /// ochilishida qizil ogohlantirish ko'rsatardi.
    /// </summary>
    /// <response code="200">Tahlil yoki <c>null</c>.</response>
    /// <response code="403">O'quvchi, yoki begona guruhning darsi.</response>
    [HttpGet]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<SessionReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long sessionId, CancellationToken ct)
    {
        var review = await reviews.GetAsync(sessionId, CurrentUserId, ct);

        /*
          🔴 `Ok(null)` EMAS, `JsonResult` — VA BU MAJBURIY.

          ASP.NET Core'da `HttpNoContentOutputFormatter` qiymati `null`
          bo'lgan javobni JIMGINA `204 No Content` ga aylantiradi. Ya'ni
          yuqoridagi `[ProducesResponseType(200)]` va XML izohda yozilgan
          shartnoma bajarilmasdi: OpenAPI 200 deb e'lon qilar, server esa
          204 qaytarardi.

          ★ Buni `SessionReviewEndpointsTests.MissingReview_Returns200With
          Null_NotAnError` ushladi. 204 ning O'ZI xato emas (u ham 404 emas,
          ya'ni yuqoridagi asosiy talab buzilmasdi), lekin hujjat bilan kod
          ZID bo'lib qolardi — va shu shartnomadan klient generatsiya
          qiladigan har qanday vosita noto'g'ri tur olardi.

          `JsonResult` no-content formatteridan chetlab o'tadi va tanaga
          haqiqiy JSON `null` yozadi.
        */
        return new JsonResult(review);
    }

    /// <summary>
    /// Tahlilni yozadi yoki yangilaydi (UPSERT, faqat o'quv bo'limi).
    ///
    /// ★ NIMA UCHUN <c>PUT</c> VA UPSERT: bitta darsda BITTA tahlil bo'ladi
    /// (unikal indeks). <c>POST</c>/<c>PUT</c> ajratilsa klient yozishdan
    /// oldin "bormi?" deb so'rashga majbur bo'lardi va ikki so'rov orasida
    /// hamkasbi yozib qo'ysa 409 olardi.
    /// </summary>
    /// <response code="200">Saqlangan tahlil.</response>
    /// <response code="403">Ustoz yoki o'quvchi — tahlilni ular yozmaydi.</response>
    /// <response code="409">Matn bo'sh yoki chegaradan uzun (domain qoidasi).</response>
    [HttpPut]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<SessionReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SessionReviewDto>> Save(
        long sessionId, [FromBody] SaveSessionReviewRequest request, CancellationToken ct) =>
        Ok(await reviews.SaveAsync(sessionId, request, CurrentUserId, ct));

    /// <summary>
    /// Tahlilni o'chiradi (faqat o'quv bo'limi).
    /// IDEMPOTENT: tahlil bo'lmasa ham <c>204</c>.
    /// </summary>
    [HttpDelete]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long sessionId, CancellationToken ct)
    {
        await reviews.DeleteAsync(sessionId, CurrentUserId, ct);
        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
