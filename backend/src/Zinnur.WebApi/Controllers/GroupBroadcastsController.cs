using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Broadcasts.Dtos;
using Zinnur.Application.Broadcasts.Services;
using Zinnur.Application.Common.Models;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// "Xabarlar" paneli (2026-08-16) — o'quv bo'limi/admin tanlangan guruhlarga
/// (shablon yoki qo'lda yozilgan) xabar yuboradi. Naqsh
/// <see cref="IGroupBroadcastService"/> izohida.
///
/// Controller YUPQA. Haqiqiy qoida servisda.
/// </summary>
[ApiController]
[Route("api/v1/broadcasts")]
[Authorize(Roles = ManageRoles)]
[Produces("application/json")]
public sealed class GroupBroadcastsController(IGroupBroadcastService broadcasts) : ControllerBase
{
    private const string ManageRoles = "Academic,Admin";

    /// <summary>Tarix — yangisidan eskisiga, sahifalangan.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<GroupBroadcastDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<GroupBroadcastDto>>> List(
        [FromQuery] GroupBroadcastListQuery query, CancellationToken ct) =>
        Ok(await broadcasts.ListAsync(query, CurrentUserId, ct));

    /// <summary>
    /// Yuboradi. 400 — bo'sh matn/guruh ro'yxati yoki kanal tanlanmagan;
    /// 404 — guruh yoki shablon topilmadi; 409 — tanlangan guruhlardan
    /// biri arxivlangan.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<GroupBroadcastDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupBroadcastDto>> Send(
        [FromBody] SendGroupBroadcastRequest request, CancellationToken ct)
    {
        var created = await broadcasts.SendAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(List), null, created);
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
