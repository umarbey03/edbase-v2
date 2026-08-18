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

    /* ═══════════════════════════════════════════════════════════════════
       O'QUVCHI KESIMI (2026-08-18) — o'quv bo'limi so'rovi bo'yicha.
       Yuqoridagilar HODISALARNI sanaydi, quyidagilar O'QUVCHILARNI.
       ═══════════════════════════════════════════════════════════════════ */

    /// <summary>
    /// Yo'qotilgan o'quvchilar: qaytgan / muzlatishda / butunlay ketgan
    /// + qayta jalb qilish ulushi.
    /// </summary>
    [HttpGet("students")]
    [ProducesResponseType<AttritionStudentSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AttritionStudentSummaryDto>> Students(
        [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.GetStudentSummaryAsync(query, CurrentUserId, ct));

    /// <summary>To'kilib, keyin qayta faol bo'lganlar ro'yxati.</summary>
    [HttpGet("returned")]
    [ProducesResponseType<IReadOnlyList<AttritionReturnedDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AttritionReturnedDto>>> Returned(
        [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.GetReturnedAsync(query, CurrentUserId, ct));

    /// <summary>To'kilish sabablari foiz bilan.</summary>
    [HttpGet("reasons")]
    [ProducesResponseType<AttritionReasonsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AttritionReasonsDto>> Reasons(
        [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.GetReasonsAsync(query, CurrentUserId, ct));

    /// <summary>
    /// Bitta guruhning tafsiloti: ustoz, boshlangan sana, kursda qayerga
    /// kelgani va shu guruhdagi to'kilish yig'masi. O'quvchilar ro'yxati
    /// <c>GET /attrition?groupId=X</c> orqali olinadi.
    /// </summary>
    [HttpGet("group/{groupId:long}")]
    [ProducesResponseType<GroupAttritionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupAttritionDetailDto>> GroupDetail(
        long groupId, [FromQuery] AttritionListQuery query, CancellationToken ct) =>
        Ok(await attrition.GetGroupDetailAsync(groupId, query, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
