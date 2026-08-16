using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Payroll.Dtos;
using Zinnur.Application.Payroll.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ========================================================================
/// OYLIK HISOBLASH (BOSQICH 4): ustoz/kurator haqi — FAQAT ADMIN
/// ========================================================================
///
/// Sinf darajasidagi <c>[Authorize(Roles = "Admin")]</c> — bu darvoza,
/// haqiqiy tekshiruv (token eski bo'lsa ham) <c>PayrollService</c> ichida
/// (izoh: shu sinf izohida).
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class PayrollController(IPayrollService payroll) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<PayrollSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollSummaryDto>> GetSummary(
        CancellationToken ct, [FromQuery] string? period = null) =>
        Ok(await payroll.GetSummaryAsync(period, CurrentUserId, ct));

    [HttpGet("{userId:long}/detail")]
    [ProducesResponseType<PayrollDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayrollDetailDto>> GetDetail(
        long userId, CancellationToken ct, [FromQuery] string? period = null) =>
        Ok(await payroll.GetDetailAsync(userId, period, CurrentUserId, ct));

    [HttpGet("rates")]
    [ProducesResponseType<IReadOnlyList<TeacherRateDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TeacherRateDto>>> ListRates(CancellationToken ct) =>
        Ok(await payroll.ListRatesAsync(CurrentUserId, ct));

    [HttpPost("rates")]
    [ProducesResponseType<TeacherRateDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TeacherRateDto>> CreateRate(
        [FromBody] CreateTeacherRateRequest request, CancellationToken ct)
    {
        var created = await payroll.CreateRateAsync(request, CurrentUserId, ct);

        return CreatedAtAction(nameof(ListRates), new { }, created);
    }

    /// <summary>★ <c>PUT</c> — TO'LIQ ALMASHTIRISH (izoh: <see cref="UpdateTeacherRateRequest"/>).</summary>
    [HttpPut("rates/{id:long}")]
    [ProducesResponseType<TeacherRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeacherRateDto>> UpdateRate(
        long id, [FromBody] UpdateTeacherRateRequest request, CancellationToken ct) =>
        Ok(await payroll.UpdateRateAsync(id, request, CurrentUserId, ct));

    [HttpDelete("rates/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRate(long id, CancellationToken ct)
    {
        await payroll.DeleteRateAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
