using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.TeacherAvailability.Dtos;
using Zinnur.Application.TeacherAvailability.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// USTOZ KUNLIK TASDIQLASH + O'RINBOSAR (2026-08-17) — o'quv bo'limi
/// paneli uchun BUGUNGI holat. Suhbat mantig'i (savol/javob, o'rinbosar
/// qidirish) BUTUNLAY Telegram bot orqali; bu controller faqat KUZATUV
/// (polling, real-vaqt push hozircha YO'Q — sabab loyihaning "Bu versiyada
/// YO'Q" bo'limida).
///
/// Controller YUPQA. Haqiqiy qoida <see cref="ITeacherAvailabilityService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/teacher-availability")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class TeacherAvailabilityController(ITeacherAvailabilityService availability) : ControllerBase
{
    /// <summary>
    /// Yozuvlar ro'yxati — filtr, qidiruv, saralash va sahifalash bilan.
    /// Sana oralig'i berilmasa BARCHA kunlar qaytadi.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TeacherAvailabilityRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TeacherAvailabilityRowDto>>> List(
        [FromQuery] TeacherAvailabilityListQuery query, CancellationToken ct) =>
        Ok(await availability.ListAsync(query, ct));

    /// <summary>
    /// AYNI filtrga mos butun to'plam bo'yicha yig'ma ko'rsatkichlar
    /// (sahifalashga bog'liq EMAS — sabab DTO izohida).
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType<TeacherAvailabilitySummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TeacherAvailabilitySummaryDto>> Summary(
        [FromQuery] TeacherAvailabilityListQuery query, CancellationToken ct) =>
        Ok(await availability.GetSummaryAsync(query, ct));

    /// <summary>Bitta yozuvning to'liq tafsiloti — taklif tarixi bilan.</summary>
    [HttpGet("{checkinId:long}")]
    [ProducesResponseType<TeacherAvailabilityDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeacherAvailabilityDetailDto>> Detail(
        long checkinId, CancellationToken ct) =>
        Ok(await availability.GetDetailAsync(checkinId, ct));
}
