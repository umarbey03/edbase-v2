using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Payroll.Dtos;
using Zinnur.Application.Payroll.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ========================================================================
/// OYLIK HISOBLASH — ustoz/kurator haqi. FAQAT ADMIN
/// ========================================================================
///
/// Sinf darajasidagi <c>[Authorize(Roles = "Admin")]</c> — bu darvoza,
/// haqiqiy tekshiruv (token eski bo'lsa ham) <c>PayrollGuard</c> ichida.
///
/// ★ 2026-09-04 — <c>rates</c> yo'llari <c>rules</c> ga ALMASHDI: eski
/// "stavka" bitta qatorda beshta pul ustunini olib yurardi, yangi "qoida"
/// esa turi bilan belgilanadi (soatbay, bosqichli, tushumdan foiz...).
/// Sabab <c>PayrollRule</c> sinf izohida.
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class PayrollController(
    IPayrollService payroll,
    IPayrollRuleService rules) : ControllerBase
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

    // ---------------------------------------------------------------- qoidalar

    [HttpGet("rules")]
    [ProducesResponseType<IReadOnlyList<PayrollRuleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PayrollRuleDto>>> ListRules(CancellationToken ct) =>
        Ok(await rules.ListAsync(CurrentUserId, ct));

    [HttpPost("rules")]
    [ProducesResponseType<PayrollRuleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayrollRuleDto>> CreateRule(
        [FromBody] PayrollRuleRequest request, CancellationToken ct)
    {
        var created = await rules.CreateAsync(request, CurrentUserId, ct);

        return CreatedAtAction(nameof(ListRules), new { }, created);
    }

    /// <summary>★ <c>PUT</c> — TO'LIQ ALMASHTIRISH (izoh: <see cref="PayrollRuleRequest"/>).</summary>
    [HttpPut("rules/{id:long}")]
    [ProducesResponseType<PayrollRuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayrollRuleDto>> UpdateRule(
        long id, [FromBody] PayrollRuleRequest request, CancellationToken ct) =>
        Ok(await rules.UpdateAsync(id, request, CurrentUserId, ct));

    [HttpDelete("rules/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRule(long id, CancellationToken ct)
    {
        await rules.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- guruh tayinlash

    /// <summary>Oylik rejimi ODDIYDAN farq qiladigan guruhlar (izoh: <c>GroupPayrollMode</c>).</summary>
    [HttpGet("group-assignments")]
    [ProducesResponseType<IReadOnlyList<GroupPayrollAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GroupPayrollAssignmentDto>>> ListGroupAssignments(
        CancellationToken ct) =>
        Ok(await rules.ListGroupAssignmentsAsync(CurrentUserId, ct));

    [HttpPut("group-assignments/{groupId:long}")]
    [ProducesResponseType<GroupPayrollAssignmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupPayrollAssignmentDto>> SetGroupAssignment(
        long groupId, [FromBody] SetGroupPayrollAssignmentRequest request, CancellationToken ct) =>
        Ok(await rules.SetGroupAssignmentAsync(groupId, request, CurrentUserId, ct));

    // ---------------------------------------------------------------- koeffitsient

    /// <summary>
    /// Oylik o'quvchi bonusi koeffitsientini o'rnatadi. <c>100</c> yuborilsa
    /// yozuv o'chiriladi va javob <c>204</c> bo'ladi (izoh:
    /// <see cref="SetPayrollStudentCoefficientRequest"/>).
    /// </summary>
    [HttpPut("coefficients")]
    [ProducesResponseType<PayrollStudentCoefficientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollStudentCoefficientDto>> SetCoefficient(
        [FromBody] SetPayrollStudentCoefficientRequest request, CancellationToken ct)
    {
        var result = await rules.SetStudentCoefficientAsync(request, CurrentUserId, ct);

        return result is null ? NoContent() : Ok(result);
    }

    // ---------------------------------------------------------------- tuzatish

    [HttpPost("adjustments")]
    [ProducesResponseType<PayrollAdjustmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollAdjustmentDto>> CreateAdjustment(
        [FromBody] CreatePayrollAdjustmentRequest request, CancellationToken ct)
    {
        var created = await payroll.CreateAdjustmentAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetDetail), new { userId = created.UserId }, created);
    }

    [HttpDelete("adjustments/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAdjustment(long id, CancellationToken ct)
    {
        await payroll.DeleteAdjustmentAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- tasdiqlash/to'lov

    /// <summary>Davrni tasdiqlaydi — jami summa suratga olinadi (`PayrollService.ApproveAsync`).</summary>
    [HttpPost("approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve([FromBody] PayrollPeriodActionRequest request, CancellationToken ct)
    {
        await payroll.ApproveAsync(request, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Tasdiqlangan davrni "to'landi" deb belgilaydi.</summary>
    [HttpPost("mark-paid")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkPaid([FromBody] PayrollPeriodActionRequest request, CancellationToken ct)
    {
        await payroll.MarkPaidAsync(request, CurrentUserId, ct);
        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
