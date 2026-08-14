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
/// ── IKKI QAMROV: GURUH VA O'QUV MARKAZ ──────────────────────────────────
///
/// ★ QAROR O'ZGARTIRILDI (2026-08-13, EGASINING KO'RSATMASI). Bu yerda
///   ilgari *"«umumiy reyting» endpointi ATAYLAB yo'q"* deb yozilgan edi.
///   Endi u BOR: <c>GET /api/v1/leaderboard/center</c>. Qarorning to'liq
///   asosi (bekor qilingan ikki e'tiroz va ularga berilgan javob)
///   <c>ILeaderboardService</c> izohida.
///
/// 🔴 "CENTER" — "HAMMA FOYDALANUVCHI" DEGANI EMAS. Jadval BITTA o'quv
///    markaz bilan chegaralanadi; chegarani <c>ILearningCenterScope</c>
///    beradi. Bugun markaz tushunchasi domenda yo'q va ikki to'plam bir
///    xil, lekin URL nomi ham, servis nomi ham KELAJAKDAGI ma'noni
///    aytadi — chunki mahsulot bir necha o'quv markazga sotiladi.
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
    /// BUTUN O'QUV MARKAZ bo'yicha jadval.
    ///
    /// Javob TO'LIQ EMAS: eng yaxshi <c>topCount</c> ta qator (bugun 100)
    /// + so'rovchining O'Z qatori. O'z qatori yuqori yuzlikka kirmasa
    /// <c>rows</c> ichida BO'LMAYDI, lekin <c>me</c> da HAQIQIY o'rin
    /// bilan keladi. Sabab <c>LeaderboardService.CenterTopRows</c> izohida.
    ///
    /// Ko'ra oladi: markazning har qanday FAOL foydalanuvchisi.
    /// </summary>
    /// <param name="period"><c>YYYY-MM</c>. Berilmasa — joriy oy.</param>
    [HttpGet("center")]
    [ProducesResponseType<CenterLeaderboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CenterLeaderboardDto>> CenterBoard(
        [FromQuery] string? period, CancellationToken ct) =>
        Ok(await leaderboard.GetCenterBoardAsync(CurrentUserId, period, ct));

    /// <summary>
    /// O'quvchining o'z o'rni — jadvalsiz, yengil ko'rinish (bosh sahifa kartochkasi).
    /// Guruh topilmasa <c>groupId</c> va <c>me</c> — <c>null</c>.
    /// </summary>
    /// <param name="scope">
    /// <c>Group</c> (standart) — o'z guruhi ichidagi o'rin;
    /// <c>Center</c> — butun o'quv markazdagi o'rin.
    ///
    /// ★ STANDART QIYMAT `Group` — ESKI MIJOZLAR UCHUN. Parametrsiz so'rov
    /// avvalgidek ishlaydi, ya'ni bosh sahifa kartochkasi o'zgarmaydi va
    /// hech kim qimmat markaz hisobini bexosdan chaqirib qo'ymaydi.
    /// </param>
    [HttpGet("me")]
    [ProducesResponseType<MyRankDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MyRankDto>> MyRank(
        [FromQuery] string? period,
        [FromQuery] LeaderboardScope scope = LeaderboardScope.Group,
        CancellationToken ct = default) =>
        Ok(await leaderboard.GetMyRankAsync(CurrentUserId, scope, period, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
