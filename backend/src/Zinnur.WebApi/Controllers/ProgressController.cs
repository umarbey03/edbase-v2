using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Progress.Dtos;
using Zinnur.Application.Progress.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>O'quvchining o'z progressi (davomat xulosasi).</summary>
[ApiController]
[Route("api/v1/progress")]
[Authorize]
[Produces("application/json")]
public sealed class ProgressController(IAttendanceSummaryService attendance) : ControllerBase
{
    /// <summary>
    /// Davomat xulosasi: qatnashgan / qoldirgan / jami va foiz
    /// (ustoz va kurator darslari alohida) + ketma-ket qatnashish seriyasi.
    ///
    /// ★ FAQAT O'ZINIKI — servis boshqa o'quvchining Id'sini qabul qilmaydi.
    /// </summary>
    /// <param name="groupId">Berilmasa — barcha faol guruhlar birga.</param>
    /// <param name="from">Mahalliy sana (<c>YYYY-MM-DD</c>), KIRADI.</param>
    /// <param name="to">Mahalliy sana, KIRADI.</param>
    [HttpGet("attendance")]
    [ProducesResponseType<AttendanceSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AttendanceSummaryDto>> Attendance(
        [FromQuery] long? groupId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct) =>
        Ok(await attendance.GetMySummaryAsync(CurrentUserId, groupId, from, to, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
