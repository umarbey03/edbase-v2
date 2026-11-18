using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.SelfStudy;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// «Yaxshi o'zlashtirdi» qoidasi (Mustaqil, 2026-11-18): kuratorning
/// o'quvchi bo'yicha darslar taxtasi, qarorlar va administrator istisnosi.
/// Kim nima qila olishini servis tekshiradi (<see cref="ILessonMasteryService"/>).
/// </summary>
[ApiController]
[Authorize(Roles = StaffRoles)]
[Route("api/v1/self-study/students/{studentId:long}")]
public sealed class LessonMasteryController(ILessonMasteryService mastery) : ControllerBase
{
    private const string StaffRoles = "Teacher,Assistant,Academic,Admin";

    /// <summary>O'quvchining darslari: ochiqmi, nima bajarilgan, kurator qarori.</summary>
    [HttpGet("lessons")]
    [ProducesResponseType<StudentLessonBoardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StudentLessonBoardDto>> Board(long studentId, CancellationToken ct) =>
        Ok(await mastery.GetBoardAsync(studentId, CurrentUserId, ct));

    /// <summary>Kurator qarori: «o'zlashtirdi» yoki «qayta ishlasin» (izoh bilan).</summary>
    [HttpPut("lessons/{lessonId:long}/mastery")]
    [ProducesResponseType<StudentLessonBoardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StudentLessonBoardDto>> Decide(
        long studentId, long lessonId, [FromBody] DecideMasteryRequest request, CancellationToken ct) =>
        Ok(await mastery.DecideAsync(studentId, lessonId, request, CurrentUserId, ct));

    /// <summary>Administrator istisnosi: darsni sabab bilan ochish yoki bekor qilish.</summary>
    [HttpPut("lessons/{lessonId:long}/unlock")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<StudentLessonBoardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StudentLessonBoardDto>> Unlock(
        long studentId, long lessonId, [FromBody] AdminLessonUnlockRequest request, CancellationToken ct) =>
        Ok(await mastery.AdminUnlockAsync(studentId, lessonId, request, CurrentUserId, ct));

    /// <summary>O'zlashtirish jurnali (yangisi birinchi).</summary>
    [HttpGet("mastery-events")]
    [ProducesResponseType<IReadOnlyList<MasteryEventDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MasteryEventDto>>> Events(long studentId, CancellationToken ct) =>
        Ok(await mastery.ListEventsAsync(studentId, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
