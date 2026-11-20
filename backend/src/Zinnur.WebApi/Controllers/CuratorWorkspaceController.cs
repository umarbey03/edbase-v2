using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.CuratorWorkspace;
using Zinnur.Application.Messaging.Dtos;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// KURATOR ISH STOLI (2026-11-20): kurator va ustoz bitta oynada ishlaydi.
///
/// Yozish (xabar, baho, dars qarori) MAVJUD endpointlar orqali — o'z ruxsat
/// qoidalari bilan: <c>api/v1/messages/...</c>, <c>api/v1/submissions/{id}/grade</c>,
/// <c>api/v1/self-study/students/{id}/lessons/{lessonId}/mastery</c>. Bu yerda
/// faqat bir joyga yig'ilgan O'QISH.
/// </summary>
[ApiController]
[Authorize(Roles = StaffRoles)]
[Route("api/v1/curator/workspace")]
public sealed class CuratorWorkspaceController(
    ICuratorWorkspaceService workspace,
    IAssignmentService assignments) : ControllerBase
{
    private const string StaffRoles = "Teacher,Assistant";

    /// <summary>Mening o'quvchilarim — ish kutayotganlar tepada.</summary>
    [HttpGet("students")]
    [ProducesResponseType<IReadOnlyList<WorkspaceStudentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceStudentDto>>> Students(CancellationToken ct) =>
        Ok(await workspace.ListStudentsAsync(CurrentUserId, ct));

    [HttpGet("students/{studentId:long}")]
    [ProducesResponseType<WorkspaceStudentHeaderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WorkspaceStudentHeaderDto>> Student(long studentId, CancellationToken ct) =>
        Ok(await workspace.GetStudentAsync(studentId, CurrentUserId, ct));

    /// <summary>O'quvchining barcha javoblari — baholash oynasi uchun to'liq shaklda.</summary>
    [HttpGet("students/{studentId:long}/submissions")]
    [ProducesResponseType<IReadOnlyList<StaffStudentSubmissionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<StaffStudentSubmissionDto>>> Submissions(
        long studentId, CancellationToken ct) =>
        Ok(await assignments.ListStudentSubmissionsAsync(studentId, CurrentUserId, ct));

    [HttpGet("students/{studentId:long}/tests")]
    [ProducesResponseType<IReadOnlyList<WorkspaceTestResultDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceTestResultDto>>> Tests(long studentId, CancellationToken ct) =>
        Ok(await workspace.ListTestResultsAsync(studentId, CurrentUserId, ct));

    /// <summary>Zaxira kurator: asosiy kurator bilan yozishma — faqat o'qish.</summary>
    [HttpGet("students/{studentId:long}/backup-thread")]
    [ProducesResponseType<MessagePageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MessagePageDto>> BackupThread(
        long studentId, [FromQuery] long? beforeId, [FromQuery] int take, CancellationToken ct) =>
        Ok(await workspace.ReadBackupThreadAsync(studentId, CurrentUserId, beforeId, take, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
