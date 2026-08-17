using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [HttpGet("today")]
    [ProducesResponseType<IReadOnlyList<TeacherAvailabilityTodayDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TeacherAvailabilityTodayDto>>> Today(CancellationToken ct) =>
        Ok(await availability.GetTodayAsync(ct));
}
