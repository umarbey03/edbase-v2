using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Subscriptions;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Mustaqil o'qish obunasi (2026-10-23). Boshqaruv — faqat o'quv bo'limi va
/// administrator (atribut DARVOZA, qoida servisda ham tekshiriladi);
/// <c>me</c> — o'quvchining o'zi.
/// </summary>
[ApiController]
[Route("api/v1/subscriptions")]
[Authorize]
[Produces("application/json")]
public sealed class SubscriptionsController(ISubscriptionService subscriptions) : ControllerBase
{
    private const string ManageRoles = "Academic,Admin";

    [HttpGet("me")]
    [ProducesResponseType<MySubscriptionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MySubscriptionDto>> Mine(CancellationToken ct) =>
        Ok(await subscriptions.GetMineAsync(CurrentUserId, ct));

    [HttpGet("plans")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<SubscriptionPlanDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> Plans(CancellationToken ct) =>
        Ok(await subscriptions.ListPlansAsync(CurrentUserId, ct));

    [HttpPost("plans")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<SubscriptionPlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SubscriptionPlanDto>> CreatePlan(
        [FromBody] SaveSubscriptionPlanRequest request, CancellationToken ct) =>
        Ok(await subscriptions.CreatePlanAsync(request, CurrentUserId, ct));

    [HttpPut("plans/{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<SubscriptionPlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionPlanDto>> UpdatePlan(
        long id, [FromBody] SaveSubscriptionPlanRequest request, CancellationToken ct) =>
        Ok(await subscriptions.UpdatePlanAsync(id, request, CurrentUserId, ct));

    [HttpGet("students/{studentId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<SubscriptionPeriodDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPeriodDto>>> ForStudent(
        long studentId, CancellationToken ct) =>
        Ok(await subscriptions.ListForStudentAsync(studentId, CurrentUserId, ct));

    /// <summary>
    /// Guruhga qo'shishdan oldin: obunada necha kun va qancha pul qolgan —
    /// xodim dialogi "balansga o'tkazib qo'shilsinmi?" deb so'raydi (Q-2).
    /// Hech narsa o'zgartirmaydi; o'tkazish guruhga qo'shish bilan birga bo'ladi.
    /// </summary>
    [HttpGet("students/{studentId:long}/group-conversion")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupConversionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GroupConversionDto>> GroupConversion(
        long studentId, CancellationToken ct) =>
        Ok(await subscriptions.PreviewGroupConversionAsync(studentId, CurrentUserId, ct));

    /// <summary>Obuna berish yoki uzaytirish (to'lov qo'lda qabul qilingan).</summary>
    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<SubscriptionPeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionPeriodDto>> Grant(
        [FromBody] GrantSubscriptionRequest request, CancellationToken ct) =>
        Ok(await subscriptions.GrantAsync(request, CurrentUserId, ct));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<SubscriptionPeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionPeriodDto>> Cancel(
        long id, [FromBody] CancelSubscriptionRequest request, CancellationToken ct) =>
        Ok(await subscriptions.CancelAsync(id, request, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
