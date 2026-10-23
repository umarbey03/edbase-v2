using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.LiveSessions.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Jonli dars ichidagi kichik guruhlar (breakout rooms).
///
/// Ruxsat servis ichida: holatni darsga kira oladigan har kim o'qiydi
/// (o'quvchi faqat o'z guruhini ko'radi), boshqaruvni faqat darsni olib
/// borayotgan ustoz qiladi, "ustozni chaqirish" — faqat o'quvchi.
/// </summary>
[ApiController]
[Route("api/v1/live-sessions/{id:long}/breakouts")]
[Authorize]
[Produces("application/json")]
public sealed class BreakoutsController(IBreakoutService breakouts) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BreakoutStateDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BreakoutStateDto>> Get(long id, CancellationToken ct) =>
        Ok(await breakouts.GetAsync(id, CurrentUserId, ct));

    /// <summary>Darsdagi o'quvchilarni 2..4 kishilik guruhlarga tasodifiy bo'ladi.</summary>
    [HttpPost]
    [ProducesResponseType<BreakoutStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BreakoutStateDto>> Start(
        long id, [FromBody] StartBreakoutsRequest request, CancellationToken ct) =>
        Ok(await breakouts.StartAsync(id, request, CurrentUserId, ct));

    /// <summary>O'quvchini boshqa guruhga (<c>0</c> — asosiy xonaga) o'tkazadi.</summary>
    [HttpPut("students/{studentId:long}")]
    [ProducesResponseType<BreakoutStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BreakoutStateDto>> Move(
        long id, long studentId, [FromBody] MoveBreakoutStudentRequest request, CancellationToken ct) =>
        Ok(await breakouts.MoveAsync(id, studentId, request, CurrentUserId, ct));

    /// <summary>Guruhlarni yopadi — hamma asosiy xonaga qaytadi.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Close(long id, CancellationToken ct)
    {
        await breakouts.CloseAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    [HttpPost("help")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Help(
        long id, [FromBody] BreakoutHelpRequest request, CancellationToken ct)
    {
        await breakouts.SetHelpAsync(id, request.Raised, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Guruh xonasiga LiveKit tokeni.</summary>
    [HttpPost("token")]
    [ProducesResponseType<LiveKitJoinDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LiveKitJoinDto>> Token(
        long id, [FromBody] BreakoutTokenRequest request, CancellationToken ct) =>
        Ok(await breakouts.CreateTokenAsync(id, request, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
