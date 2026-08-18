using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Attrition.Dtos;
using Zinnur.Application.Attrition.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// TO'KILISH SABABLARI KATALOGI (2026-08-18) — "Sozlamalar" sahifasining
/// "To'kilish sabablari" bo'limi.
///
/// Sabab ro'yxatdan tanlanadi, chunki erkin matn bo'yicha foiz hisoblab
/// bo'lmaydi (batafsil <c>AttritionReason</c> izohida).
///
/// Controller YUPQA. Haqiqiy qoida <see cref="IAttritionReasonService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/attrition-reasons")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class AttritionReasonsController(IAttritionReasonService reasons) : ControllerBase
{
    /// <param name="activeOnly">Chiqarish/muzlatish oynasi uchun <c>true</c>.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AttritionReasonDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AttritionReasonDto>>> List(
        [FromQuery] bool activeOnly = false, CancellationToken ct = default) =>
        Ok(await reasons.ListAsync(activeOnly, CurrentUserId, ct));

    [HttpPost]
    [ProducesResponseType<AttritionReasonDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AttritionReasonDto>> Create(
        [FromBody] SaveAttritionReasonRequest request, CancellationToken ct) =>
        Ok(await reasons.CreateAsync(request, CurrentUserId, ct));

    [HttpPut("{id:long}")]
    [ProducesResponseType<AttritionReasonDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AttritionReasonDto>> Update(
        long id, [FromBody] SaveAttritionReasonRequest request, CancellationToken ct) =>
        Ok(await reasons.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>Ishlatilgan sabab o'chirilmaydi — ARXIVLANADI.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await reasons.DeleteAsync(id, CurrentUserId, ct);

        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
