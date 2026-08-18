using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Penalties.Dtos;
using Zinnur.Application.Penalties.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// USTOZ/KURATOR JARIMALARI (2026-08-18).
///
/// ★ IKKI XIL RUXSAT: ko'rish va qo'lda kiritish — o'quv bo'limi va
/// admin; TASDIQLASH/BEKOR QILISH esa FAQAT admin (u pulga aylanadi).
/// Qoida servis qatlamida ham takrorlanadi — atribut chetlab o'tilsa
/// ham himoya qoladi (loyihadagi AYNI naqsh).
///
/// Controller YUPQA. Haqiqiy qoida <see cref="IPenaltyService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/penalties")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class PenaltiesController(IPenaltyService penalties) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<PenaltyRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PenaltyRowDto>>> List(
        [FromQuery] PenaltyListQuery query, CancellationToken ct) =>
        Ok(await penalties.ListAsync(query, CurrentUserId, ct));

    [HttpGet("summary")]
    [ProducesResponseType<PenaltySummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PenaltySummaryDto>> Summary(
        [FromQuery] PenaltyListQuery query, CancellationToken ct) =>
        Ok(await penalties.GetSummaryAsync(query, CurrentUserId, ct));

    /// <summary>Xodim kesimi — "kimda ko'p jarima".</summary>
    [HttpGet("by-user")]
    [ProducesResponseType<IReadOnlyList<PenaltyByUserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PenaltyByUserDto>>> ByUser(
        [FromQuery] PenaltyListQuery query, CancellationToken ct) =>
        Ok(await penalties.GetByUserAsync(query, CurrentUserId, ct));

    /// <summary>Oylik hisobot — xodim va tur kesimida, sahifalanmagan.</summary>
    [HttpGet("report")]
    [ProducesResponseType<PenaltyReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PenaltyReportDto>> Report(
        [FromQuery] string period, CancellationToken ct) =>
        Ok(await penalties.GetReportAsync(period, CurrentUserId, ct));

    [HttpPost]
    [ProducesResponseType<PenaltyRowDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PenaltyRowDto>> Create(
        [FromBody] CreateManualPenaltyRequest request, CancellationToken ct) =>
        Ok(await penalties.CreateManualAsync(request, CurrentUserId, ct));

    /// <summary>Tasdiqlash — oylikka manfiy tuzatma yaratiladi (FAQAT admin).</summary>
    [HttpPost("{id:long}/approve")]
    [ProducesResponseType<PenaltyRowDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PenaltyRowDto>> Approve(long id, CancellationToken ct) =>
        Ok(await penalties.ApproveAsync(id, CurrentUserId, ct));

    /// <summary>Bekor qilish (uzrli sabab yoki xato yozuv) — FAQAT admin.</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType<PenaltyRowDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PenaltyRowDto>> Cancel(
        long id, [FromBody] CancelPenaltyRequest? request, CancellationToken ct) =>
        Ok(await penalties.CancelAsync(id, request ?? new CancelPenaltyRequest(), CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
