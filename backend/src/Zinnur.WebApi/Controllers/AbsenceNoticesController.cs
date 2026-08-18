using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Absentees.Dtos;
using Zinnur.Application.Absentees.Services;
using Zinnur.Application.Common.Models;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// KELMAGANLARGA XABAR (2026-08-18) — "Xabarlar" panelidagi alohida tab.
///
/// ★ MAVJUD `broadcasts` DAN FARQI: u GURUHGA yuboradi va bitta qator
/// butun guruhni ifodalaydi. Bu yerda esa HAR OLUVCHIGA alohida yozuv —
/// "Doniyorga xabar bordimi?" degan savolga javob berish uchun.
///
/// ★ RUXSAT NOMUTANOSIB: yuborish — o'quv bo'limi va admin (bu markaz
/// nomidan ketadigan rasmiy xabar); tarixni esa o'quvchidan boshqa
/// hamma ko'radi, chunki qo'ng'iroqlarni kurator qiladi. Qoida servis
/// qatlamida ham takrorlanadi.
///
/// Controller YUPQA.
/// </summary>
[ApiController]
[Route("api/v1/absence-notices")]
[Authorize]
[Produces("application/json")]
public sealed class AbsenceNoticesController(IAbsenceNoticeService notices) : ControllerBase
{
    /// <summary>Yuborilgan xabarlar tarixi.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AbsenceNoticeRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AbsenceNoticeRowDto>>> List(
        [FromQuery] AbsenceNoticeListQuery query, CancellationToken ct) =>
        Ok(await notices.ListAsync(query, CurrentUserId, ct));

    [HttpGet("summary")]
    [ProducesResponseType<AbsenceNoticeSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AbsenceNoticeSummaryDto>> Summary(
        [FromQuery] AbsenceNoticeListQuery query, CancellationToken ct) =>
        Ok(await notices.GetSummaryAsync(query, CurrentUserId, ct));

    /// <summary>
    /// Berilgan darslar bo'yicha ALLAQACHON xabar olganlar — kelmaganlar
    /// ro'yxatida "yuborilgan" belgisini chizish uchun.
    /// </summary>
    /// <param name="sessionIds">
    /// Vergul bilan ajratilgan dars ID'lari. Massiv o'rniga satr —
    /// frontenddagi <c>http</c> mijozi so'rov parametrida massivni
    /// qo'llab-quvvatlamaydi va uni kengaytirish butun loyihaga ta'sir
    /// qilardi.
    /// </param>
    [HttpGet("sent")]
    [ProducesResponseType<IReadOnlyList<AbsenceNoticeTarget>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AbsenceNoticeTarget>>> Sent(
        [FromQuery] string? sessionIds, CancellationToken ct)
    {
        var ids = (sessionIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        return Ok(await notices.GetSentTargetsAsync(ids, CurrentUserId, ct));
    }

    /// <summary>Xabar yuborish — FAQAT o'quv bo'limi va admin.</summary>
    [HttpPost]
    [ProducesResponseType<SendAbsenceNoticeResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SendAbsenceNoticeResultDto>> Send(
        [FromBody] SendAbsenceNoticeRequest request, CancellationToken ct) =>
        Ok(await notices.SendAsync(request, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
