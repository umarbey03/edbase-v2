using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.SelfStudy;
using Zinnur.Application.SelfStudy.Nudges;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Mustaqil o'quvchining kuratori (2026-11-18): o'quv bo'limi biriktiradi,
/// kurator o'z o'quvchilarini ko'radi, o'quvchi o'z kuratorini biladi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/self-study/curators")]
public sealed class SelfStudyCuratorsController(
    ISelfStudyCuratorService curators,
    ILessonMasteryService mastery,
    ISelfStudyNudgeService nudges) : ControllerBase
{
    private const string ManageRoles = "Academic,Admin";
    private const string StaffRoles = "Teacher,Assistant,Academic,Admin";

    /// <summary>Kurator bo'la oladigan xodimlar va har birining hozirgi yuki.</summary>
    [HttpGet("candidates")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<CuratorCandidateDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratorCandidateDto>>> Candidates(CancellationToken ct) =>
        Ok(await curators.ListCandidatesAsync(CurrentUserId, ct));

    /// <summary>Faol obunasi bor, lekin kuratori yo'q o'quvchilar.</summary>
    [HttpGet("unassigned")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<UnassignedSelfStudyStudentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UnassignedSelfStudyStudentDto>>> Unassigned(CancellationToken ct) =>
        Ok(await curators.ListUnassignedAsync(CurrentUserId, ct));

    /// <summary>O'quvchining joriy kuratori va tarixi.</summary>
    [HttpGet("students/{studentId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<StudentCuratorInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentCuratorInfoDto>> ForStudent(long studentId, CancellationToken ct) =>
        Ok(await curators.GetForStudentAsync(studentId, CurrentUserId, ct));

    /// <summary>Kurator biriktirish yoki almashtirish.</summary>
    [HttpPut("students/{studentId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<StudentCuratorInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StudentCuratorInfoDto>> Assign(
        long studentId, [FromBody] AssignSelfStudyCuratorRequest request, CancellationToken ct) =>
        Ok(await curators.AssignAsync(studentId, request, CurrentUserId, ct));

    /// <summary>Kuratorni olib qo'yish.</summary>
    [HttpDelete("students/{studentId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<StudentCuratorInfoDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StudentCuratorInfoDto>> Unassign(long studentId, CancellationToken ct) =>
        Ok(await curators.UnassignAsync(studentId, CurrentUserId, ct));

    /// <summary>Kurator: menga biriktirilgan Mustaqil o'quvchilar.</summary>
    [HttpGet("my-students")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<IReadOnlyList<MySelfStudyStudentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MySelfStudyStudentDto>>> MyStudents(CancellationToken ct) =>
        Ok(await curators.ListMyStudentsAsync(CurrentUserId, ct));

    /// <summary>O'quvchi: mening kuratorim (yo'q bo'lsa 204).</summary>
    /// <summary>
    /// Faolsizlik eslatmalari hisoboti (2026-11-25): har bosqich va matn
    /// varianti bo'yicha nechta yuborildi va nechtasi 48 soat ichida qaytdi.
    /// </summary>
    [HttpGet("~/api/v1/self-study/nudges/stats")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<NudgeStatsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NudgeStatsDto>> NudgeStats([FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(await nudges.GetStatsAsync(days, CurrentUserId, ct));

    [HttpGet("mine")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType<MyCuratorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<MyCuratorDto>> Mine(CancellationToken ct)
    {
        var curator = await curators.GetMyCuratorAsync(CurrentUserId, ct);
        return curator is null ? NoContent() : Ok(curator);
    }

    /// <summary>O'quvchi: darslarim bo'yicha kurator qarorlari (izoh bilan).</summary>
    [HttpGet("~/api/v1/self-study/me/mastery")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType<IReadOnlyList<MyLessonMasteryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MyLessonMasteryDto>>> MyMastery(CancellationToken ct) =>
        Ok(await mastery.ListMineAsync(CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
