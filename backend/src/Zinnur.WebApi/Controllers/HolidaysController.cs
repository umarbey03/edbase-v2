using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Scheduling.Dtos;
using Zinnur.Application.Scheduling.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// BAYRAM KALENDARI (2026-08-16) — umumiy sanalar, BARCHA guruhlarning
/// o'sha kundagi darsini bekor qiladi va jadvalni avtomatik oldinga suradi.
///
/// Controller YUPQA. Haqiqiy qoida <see cref="IHolidayService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/holidays")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class HolidaysController(IHolidayService holidays) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HolidayDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HolidayDto>>> List(CancellationToken ct) =>
        Ok(await holidays.ListAsync(CurrentUserId, ct));

    /// <summary>
    /// Yangi bayram e'lon qiladi. Javobda ta'sirlangan guruh/dars soni
    /// qaytadi — xodim "nechta guruhga tegdi"ni darhol ko'radi.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<HolidayImpactDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HolidayImpactDto>> Create(
        [FromBody] CreateHolidayRequest request, CancellationToken ct)
    {
        var created = await holidays.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(List), null, created);
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await holidays.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
