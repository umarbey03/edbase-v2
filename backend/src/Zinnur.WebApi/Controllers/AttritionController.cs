using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Attrition.Dtos;
using Zinnur.Application.Attrition.Services;
using Zinnur.Application.Common.Models;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// TO'KILISHLAR HISOBOTI (2026-08-17) — o'quvchi qachon, qaysi guruhdan,
/// qaysi ustozdan va nima sababdan ketgani/muzlatilgani/ko'chirilgani.
///
/// Manba — o'chmaydigan <c>GroupMembershipEvent</c> jurnali; uni
/// <c>GroupService</c> a'zolik o'zgarishi bilan bitta tranzaksiyada yozadi.
///
/// Controller YUPQA. Haqiqiy qoida <see cref="IAttritionService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/attrition")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class AttritionController(IAttritionService attrition) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AttritionRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AttritionRowDto>>> List(
        [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.ListAsync(query, CurrentUserId, ct));

    [HttpGet("summary")]
    [ProducesResponseType<AttritionSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AttritionSummaryDto>> Summary(
        [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.GetSummaryAsync(query, CurrentUserId, ct));

    /// <summary>Ustoz kesimi — "kimning guruhida ko'p to'kiladi".</summary>
    [HttpGet("by-teacher")]
    [ProducesResponseType<IReadOnlyList<AttritionByTeacherDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AttritionByTeacherDto>>> ByTeacher(
        [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.GetByTeacherAsync(query, CurrentUserId, ct));

    [HttpGet("by-group")]
    [ProducesResponseType<IReadOnlyList<AttritionByGroupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AttritionByGroupDto>>> ByGroup(
        [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.GetByGroupAsync(query, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
