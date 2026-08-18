using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Absentees.Dtos;
using Zinnur.Application.Absentees.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// DARSGA KIRMAGANLAR XARITASI (2026-08-18) — kunlik, guruh kesimida.
///
/// Loyiha egasi: *"bir kun avval darsga kirmagan o'quvchilarni bittada
/// ko'ra olishimiz uchun"*.
///
/// ★ RUXSAT: o'quvchidan BOSHQA hamma (qo'ng'iroqlarni amalda kurator
/// va ustoz qiladi). Qoida servis qatlamida ham takrorlanadi.
///
/// Controller YUPQA. Haqiqiy qoida <see cref="IAbsenteeService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/absentees")]
[Authorize]
[Produces("application/json")]
public sealed class AbsenteesController(IAbsenteeService absentees) : ControllerBase
{
    /// <summary>Sana berilmasa — KECHA.</summary>
    [HttpGet]
    [ProducesResponseType<AbsenteeReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AbsenteeReportDto>> Get(
        [FromQuery] AbsenteeQuery query, CancellationToken ct) =>
        Ok(await absentees.GetAsync(query, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
