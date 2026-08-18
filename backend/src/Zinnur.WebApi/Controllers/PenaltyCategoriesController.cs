using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Penalties.Dtos;
using Zinnur.Application.Penalties.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// JARIMA TARIFLARI KATALOGI (2026-08-18) — "Sozlamalar" sahifasining
/// "Jarimalar" bo'limi.
///
/// ★ RUXSAT: o'quv bo'limi va admin — ham ko'radi, ham boshqaradi.
/// Sabab <see cref="IPenaltyCategoryService"/> izohida. Qoida servis
/// qatlamida ham takrorlanadi — atribut chetlab o'tilsa ham himoya
/// qoladi (loyihadagi AYNI naqsh).
///
/// Controller YUPQA. Haqiqiy qoida <see cref="IPenaltyCategoryService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/penalty-categories")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class PenaltyCategoriesController(IPenaltyCategoryService categories) : ControllerBase
{
    /// <param name="activeOnly">Jarima kiritish oynasi uchun <c>true</c>.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PenaltyCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PenaltyCategoryDto>>> List(
        [FromQuery] bool activeOnly = false, CancellationToken ct = default) =>
        Ok(await categories.ListAsync(activeOnly, CurrentUserId, ct));

    [HttpPost]
    [ProducesResponseType<PenaltyCategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PenaltyCategoryDto>> Create(
        [FromBody] SavePenaltyCategoryRequest request, CancellationToken ct) =>
        Ok(await categories.CreateAsync(request, CurrentUserId, ct));

    [HttpPut("{id:long}")]
    [ProducesResponseType<PenaltyCategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PenaltyCategoryDto>> Update(
        long id, [FromBody] SavePenaltyCategoryRequest request, CancellationToken ct) =>
        Ok(await categories.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>Ishlatilgan tarif o'chirilmaydi — ARXIVLANADI.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, CurrentUserId, ct);

        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
