using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.LiveSessions.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>Jonli darslar: ro'yxat, boshlash/yakunlash va LiveKit tokeni.</summary>
[ApiController]
[Route("api/v1/live-sessions")]
[Authorize]
[Produces("application/json")]
public sealed class LiveSessionsController(ILiveSessionService sessions) : ControllerBase
{
    /// <summary>Foydalanuvchining yaqin darslari (roli bo'yicha filtrlanadi).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LiveSessionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LiveSessionDto>>> List(CancellationToken ct) =>
        Ok(await sessions.ListForUserAsync(CurrentUserId, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<LiveSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LiveSessionDto>> Get(long id, CancellationToken ct) =>
        Ok(await sessions.GetAsync(id, CurrentUserId, ct));

    /// <summary>Darsni boshlash (faqat host).</summary>
    [HttpPost("{id:long}/start")]
    [Authorize(Roles = "Teacher,Assistant,Academic,Admin")]
    [ProducesResponseType<LiveSessionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LiveSessionDto>> Start(long id, CancellationToken ct) =>
        Ok(await sessions.StartAsync(id, CurrentUserId, ct));

    /// <summary>Darsni yakunlash (faqat host). Davomat ham yakunlanadi.</summary>
    [HttpPost("{id:long}/end")]
    [Authorize(Roles = "Teacher,Assistant,Academic,Admin")]
    [ProducesResponseType<LiveSessionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LiveSessionDto>> End(long id, CancellationToken ct) =>
        Ok(await sessions.EndAsync(id, CurrentUserId, ct));

    /// <summary>
    /// LiveKit'ga ulanish uchun token.
    /// Ruxsat (a'zolik/host) va dars holati servis ichida tekshiriladi.
    /// </summary>
    [HttpPost("{id:long}/token")]
    [ProducesResponseType<LiveKitJoinDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LiveKitJoinDto>> CreateToken(long id, CancellationToken ct) =>
        Ok(await sessions.CreateJoinTokenAsync(id, CurrentUserId, ct));

    /// <summary>Chatning oxirgi xabarlari (sahifa ochilganda bir marta yuklanadi).</summary>
    [HttpGet("{id:long}/messages")]
    [ProducesResponseType<IReadOnlyList<ChatMessageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> Messages(
        long id, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await sessions.GetRecentMessagesAsync(id, CurrentUserId, take, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
