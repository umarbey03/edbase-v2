using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Enrollment.Dtos;
using Zinnur.Application.Enrollment.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// KURSGA ARIZALAR (2026-08-28) — landing sahifadagi forma
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 <see cref="Submit"/> — ILOVADAGI YAGONA ANONIM YOZISH ENDPOINTI.
///    Shuning uchun uning atrofidagi himoya ATAYLAB qalin:
///
///      • <see cref="PublicFormRateLimitPolicy"/> — IP bo'yicha, qo'pol filtr;
///      • RAQAM bo'yicha kvota — use-case ichida, Redis'da atomar
///        (`EnrollmentApplicationService`), IP'ga bog'liq emas;
///      • javob HECH NARSA qaytarmaydi — hisob sanashga yo'l yo'q;
///      • yozuv `Users` jadvaliga UMUMAN tegmaydi va kirish huquqi
///        bermaydi.
///
/// ★ QOLGAN AMALLAR `Academic`/`Admin` bilan yopilgan: arizada telefon
///   raqami bor, ya'ni u R27 (kontakt ma'lumoti) doirasiga kiradi va
///   ustozga ochilmaydi.
///
/// Controller YUPQA. Haqiqiy qoida
/// <see cref="IEnrollmentApplicationService"/> ICHIDA.
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
[ApiController]
[Route("api/v1/applications")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class ApplicationsController(IEnrollmentApplicationService applications) : ControllerBase
{
    /// <summary>
    /// Ochiq formalar uchun rate-limit siyosatining nomi.
    ///
    /// ★ NOM SHU YERDA e'lon qilinadi, `Program.cs` esa siyosatni AYNAN
    /// shu const bilan ro'yxatdan o'tkazadi (`AuthController` dagi AYNI
    /// naqsh va AYNI sabab: nom ikki joyda oddiy satr bo'lsa, atributni
    /// unutish JIMGINA "cheklovsiz endpoint" berardi).
    /// </summary>
    public const string PublicFormRateLimitPolicy = "public-form";

    /// <summary>
    /// Ariza qoldirish — ANONIM.
    ///
    /// Tana: <c>{ "fullName": "...", "phone": "+998...", "course": null, "note": null }</c>.
    ///
    /// 🔴 JAVOB TANASI YO'Q (202) — server arizaning taqdiri haqida hech
    /// narsa aytmaydi. "Bu raqam allaqachon o'quvchi" yoki "bunday ariza
    /// bor" degan javob formani hisob sanash vositasiga aylantirardi.
    ///
    /// <c>400</c> — ism yoki raqam yaroqsiz (bu foydalanuvchining O'ZIGA
    /// ham ko'rinib turibdi, ya'ni hech nima oshkor qilmaydi) ·
    /// <c>429</c> — kvota.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(PublicFormRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(
        [FromBody] CreateEnrollmentApplicationRequest request, CancellationToken ct)
    {
        await applications.SubmitAsync(request, ct);

        // 202, 201 EMAS: 201 `Location` sarlavhasini talab qiladi, ya'ni
        // anonim foydalanuvchiga o'z arizasining manzilini berardi — u esa
        // faqat xodimga ochiq.
        return Accepted();
    }

    /// <summary>Arizalar ro'yxati (filtr + sahifalash).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<EnrollmentApplicationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EnrollmentApplicationDto>>> List(
        [FromQuery] EnrollmentApplicationListParams query, CancellationToken ct) =>
        Ok(await applications.ListAsync(query, ct));

    /// <summary>
    /// Holatni va operator izohini yangilash.
    ///
    /// ★ ARIZA O'CHIRILMAYDI (`DELETE` endpointi ATAYLAB YO'Q): "nechta
    /// ariza keldi, nechtasi o'quvchiga aylandi" — markazning asosiy
    /// o'lchovi va o'chirilgan qator uni jimgina buzardi.
    /// </summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<EnrollmentApplicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnrollmentApplicationDto>> Update(
        long id, [FromBody] UpdateEnrollmentApplicationRequest request, CancellationToken ct) =>
        Ok(await applications.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>
    /// Amalni bajargan xodim — TOKENDAN.
    ///
    /// 🔴 So'rov tanasidagi identifikatorga HECH QACHON ishonilmaydi —
    /// eski tizimning X-1 zaifligi aynan shunday paydo bo'lgan.
    /// </summary>
    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
