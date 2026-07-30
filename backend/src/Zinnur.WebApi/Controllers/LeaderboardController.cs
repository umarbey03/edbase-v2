using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Progress.Dtos;
using Zinnur.Application.Progress.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Oylik reyting. Ball uch mezondan (davomat, vazifa, test) o'rtacha
/// hisoblanadi — batafsil <c>LeaderboardScore</c> izohida.
///
/// ★ "UMUMIY REYTING" ENDPOINTI ATAYLAB YO'Q: o'quvchi faqat O'Z guruhini
/// ko'radi (sabab <c>ILeaderboardService</c> izohida).
/// </summary>
[ApiController]
[Route("api/v1/leaderboard")]
[Authorize]
[Produces("application/json")]
public sealed class LeaderboardController(ILeaderboardService leaderboard) : ControllerBase
{
    /// <summary>
    /// Guruhning oylik reyting jadvali.
    /// Ko'ra oladi: guruhning faol a'zosi, ustozi/kuratori, o'quv bo'limi, admin.
    /// </summary>
    /// <param name="groupId">Guruh.</param>
    /// <param name="period"><c>YYYY-MM</c>. Berilmasa — joriy oy.</param>
    [HttpGet("groups/{groupId:long}")]
    [ProducesResponseType<GroupLeaderboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupLeaderboardDto>> GroupBoard(
        long groupId, [FromQuery] string? period, CancellationToken ct) =>
        Ok(await leaderboard.GetGroupBoardAsync(groupId, CurrentUserId, period, ct));

    /// <summary>
    /// O'quvchining o'z o'rni — jadvalsiz, yengil ko'rinish (bosh sahifa kartochkasi).
    /// Guruh topilmasa <c>groupId</c> va <c>me</c> — <c>null</c>.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType<MyRankDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MyRankDto>> MyRank(
        [FromQuery] string? period, CancellationToken ct) =>
        Ok(await leaderboard.GetMyRankAsync(CurrentUserId, period, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
