using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Messaging.Dtos;
using Zinnur.Application.SelfStudy.Panel;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// MUSTAQIL PANELI (2026-11-19) — o'quv bo'limi va administrator:
/// o'quvchilar, kuratorlar (ommaviy biriktirish bilan), vazifalar va
/// yozishmalar (FAQAT O'QISH, har ochish auditga yoziladi).
///
/// Bitta-bitta biriktirish/olib qo'yish — mavjud
/// <c>api/v1/self-study/curators/students/{id}</c> (qoidalar o'sha yerda).
/// </summary>
[ApiController]
[Authorize(Roles = ManageRoles)]
[Route("api/v1/self-study/panel")]
public sealed class SelfStudyPanelController(ISelfStudyPanelService panel) : ControllerBase
{
    private const string ManageRoles = "Academic,Admin";

    [HttpGet("summary")]
    [ProducesResponseType<SelfStudyPanelSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SelfStudyPanelSummaryDto>> Summary(CancellationToken ct) =>
        Ok(await panel.GetSummaryAsync(CurrentUserId, ct));

    [HttpGet("students")]
    [ProducesResponseType<PagedResult<SelfStudyStudentRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SelfStudyStudentRowDto>>> Students(
        [FromQuery] SelfStudyStudentQuery query, CancellationToken ct) =>
        Ok(await panel.ListStudentsAsync(query, CurrentUserId, ct));

    /// <summary>Bir nechta o'quvchini bitta kuratorga (ixtiyoriy zaxira bilan) biriktirish.</summary>
    [HttpPost("students/assign")]
    [ProducesResponseType<BulkAssignResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkAssignResultDto>> BulkAssign(
        [FromBody] BulkAssignSelfStudyCuratorRequest request, CancellationToken ct) =>
        Ok(await panel.BulkAssignAsync(request, CurrentUserId, ct));

    [HttpGet("curators")]
    [ProducesResponseType<IReadOnlyList<SelfStudyCuratorRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SelfStudyCuratorRowDto>>> Curators(CancellationToken ct) =>
        Ok(await panel.ListCuratorsAsync(CurrentUserId, ct));

    [HttpGet("submissions")]
    [ProducesResponseType<PagedResult<SelfStudySubmissionRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SelfStudySubmissionRowDto>>> Submissions(
        [FromQuery] SelfStudySubmissionQuery query, CancellationToken ct) =>
        Ok(await panel.ListSubmissionsAsync(query, CurrentUserId, ct));

    [HttpGet("chats")]
    [ProducesResponseType<PagedResult<SelfStudyChatRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SelfStudyChatRowDto>>> Chats(
        [FromQuery] SelfStudyChatQuery query, CancellationToken ct) =>
        Ok(await panel.ListChatsAsync(query, CurrentUserId, ct));

    /// <summary>Yozishmani o'qish (faqat o'qish; birinchi sahifa auditga yoziladi).</summary>
    [HttpGet("chats/{studentId:long}/{staffId:long}/messages")]
    [ProducesResponseType<MessagePageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessagePageDto>> ChatMessages(
        long studentId, long staffId, [FromQuery] long? beforeId, [FromQuery] int take, CancellationToken ct) =>
        Ok(await panel.ReadChatAsync(studentId, staffId, beforeId, take, CurrentUserId, ct));

    /// <summary>Yozishma biriktirmasi (rasm/ovoz/hujjat), `Range` bilan.</summary>
    [HttpGet("chats/attachments/{attachmentId:long}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChatAttachment(long attachmentId, CancellationToken ct)
    {
        var download = await panel.OpenChatAttachmentAsync(
            attachmentId, MediaResponse.RawRange(Request.Headers.Range), CurrentUserId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    /// <summary>Shu yozishmani kim, qachon ochgani.</summary>
    [HttpGet("chats/{studentId:long}/{staffId:long}/access-log")]
    [ProducesResponseType<IReadOnlyList<ChatAccessLogEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatAccessLogEntryDto>>> ChatAccessLog(
        long studentId, long staffId, CancellationToken ct) =>
        Ok(await panel.ListChatAccessAsync(studentId, staffId, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
