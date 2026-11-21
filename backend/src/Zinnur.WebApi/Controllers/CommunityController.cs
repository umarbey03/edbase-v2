using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Community;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Hamjamiyat — Mustaqil o'quvchilarning umumiy chati (2026-11-21).
/// Kim kirishi va moderatsiya qoidalari — <see cref="CommunityService"/>.
/// </summary>
[ApiController]
[Route("api/v1/community")]
[Authorize]
[Produces("application/json")]
public sealed class CommunityController(ICommunityService community) : ControllerBase
{
    /// <summary>Kirish holati: yoqilganmi, yoza oladimi, moderatormi, o'qilmaganlar.</summary>
    [HttpGet("access")]
    [ProducesResponseType<CommunityAccessDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CommunityAccessDto>> Access(CancellationToken ct) =>
        Ok(await community.GetAccessAsync(CurrentUserId, ct));

    /// <summary>
    /// Xabarlar: <paramref name="beforeId"/> — eskiroqlar (yuqoriga skroll),
    /// <paramref name="afterId"/> — yangilari (so'rov bilan yangilash).
    /// </summary>
    [HttpGet("messages")]
    [ProducesResponseType<CommunityPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CommunityPageDto>> Messages(
        [FromQuery] long? beforeId, [FromQuery] long? afterId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await community.ListAsync(CurrentUserId, beforeId, afterId, take, ct));

    [HttpPost("messages")]
    [ProducesResponseType<CommunityMessageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CommunityMessageDto>> Send(
        [FromBody] SendCommunityMessageRequest request, CancellationToken ct) =>
        Ok(await community.SendAsync(CurrentUserId, request, ct));

    /// <summary>O'z xabarini — muallif, istalganini — moderator o'chiradi.</summary>
    [HttpDelete("messages/{messageId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(long messageId, CancellationToken ct)
    {
        await community.DeleteAsync(CurrentUserId, messageId, ct);
        return NoContent();
    }

    [HttpPost("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead([FromQuery] long lastId, CancellationToken ct)
    {
        await community.MarkReadAsync(CurrentUserId, lastId, ct);
        return NoContent();
    }

    /// <summary>Moderator: o'quvchini vaqtincha yozishdan to'xtatadi.</summary>
    [HttpPut("mutes/{studentId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mute(long studentId, [FromBody] MuteCommunityMemberRequest request, CancellationToken ct)
    {
        await community.MuteAsync(CurrentUserId, studentId, request, ct);
        return NoContent();
    }

    [HttpDelete("mutes/{studentId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unmute(long studentId, CancellationToken ct)
    {
        await community.UnmuteAsync(CurrentUserId, studentId, ct);
        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0", CultureInfo.InvariantCulture);
}
