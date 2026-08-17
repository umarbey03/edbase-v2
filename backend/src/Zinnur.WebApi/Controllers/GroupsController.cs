using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Groups.Dtos;
using Zinnur.Application.Groups.Services;
using Zinnur.Application.Scheduling.Dtos;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Guruhlar, a'zolik va dars jadvali (o'quv bo'limi / admin paneli + ustoz).
///
/// Controller YUPQA: hech qanday biznes qoidasi yo'q, faqat
/// "so'rov -> servis -> javob".
///
/// RUXSAT IKKI QATLAMLI:
///  1) Sinf darajasidagi <c>[Authorize(Roles = "Teacher,Assistant,Academic,Admin")]</c>
///     — umumiy DARVOZA. O'quvchi bu kontrollerga umuman kirmaydi.
///  2) O'zgartiruvchi endpointlarda qo'shimcha
///     <c>[Authorize(Roles = "Academic,Admin")]</c>. ASP.NET atributlarni
///     "VA" bilan qo'shadi, ya'ni natijada faqat kesishma qoladi
///     (Academic + Admin) — ustoz o'zgartira olmaydi.
///
/// Haqiqiy qoida (kim nimani KO'RADI, kim nimani O'ZGARTIRADI) esa
/// <see cref="IGroupService"/> ICHIDA — atribut faqat darvoza, chunki servis
/// fon vazifasidan ham chaqirilishi mumkin.
///
/// USTOZ/KURATOR "MENING GURUHLARIM": alohida <c>/groups/mine</c> endpointi
/// ATAYLAB YO'Q. <c>GET /api/v1/groups</c> ning o'zi ustoz va kurator uchun
/// natijani avtomatik ravishda o'z guruhlariga cheklaydi — bitta endpoint,
/// bitta filtr mantiqi (ikkitasi bo'lsa vaqt o'tib ajralib ketardi).
/// </summary>
[ApiController]
[Route("api/v1/groups")]
[Authorize(Roles = "Teacher,Assistant,Academic,Admin")]
[Produces("application/json")]
public sealed class GroupsController(IGroupService groups) : ControllerBase
{
    // ================================================================= o'qish

    /// <summary>
    /// Ro'yxat: nom bo'yicha qidiruv, tur/faollik filtri, sahifalash.
    /// Ustoz va kurator uchun FAQAT o'z guruhlari qaytadi.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<GroupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<GroupDto>>> List(
        [FromQuery] GroupListQuery query, CancellationToken ct) =>
        Ok(await groups.ListAsync(query, CurrentUserId, ct));

    /// <summary>Guruh kartochkasi: kurs/ustoz/kurator nomlari va a'zolar soni bilan.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<GroupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupDto>> Get(long id, CancellationToken ct) =>
        Ok(await groups.GetAsync(id, CurrentUserId, ct));

    // ================================================================= yozish

    /// <summary>Guruh yaratadi VA butun kurs jadvalini generatsiya qiladi.</summary>
    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CreateGroupResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateGroupResponse>> Create(
        [FromBody] CreateGroupRequest request, CancellationToken ct)
    {
        var created = await groups.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Group.Id }, created);
    }

    /// <summary>
    /// Guruhni tahrirlaydi. Javobdagi <c>schedule</c> maydoni jadvalga AYNAN
    /// nima qilinganini aytadi (qayta tuzildi / o'rnida yangilandi / tegilmadi).
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UpdateGroupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateGroupResponse>> Update(
        long id, [FromBody] UpdateGroupRequest request, CancellationToken ct) =>
        Ok(await groups.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>Arxivlash. Jadvalga TEGMAYDI — guruh keyin tiklanishi mumkin.</summary>
    [HttpPost("{id:long}/archive")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GroupDto>> Archive(long id, CancellationToken ct) =>
        Ok(await groups.SetActiveAsync(id, isActive: false, CurrentUserId, ct));

    [HttpPost("{id:long}/restore")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GroupDto>> Restore(long id, CancellationToken ct) =>
        Ok(await groups.SetActiveAsync(id, isActive: true, CurrentUserId, ct));

    // ================================================================= a'zolik

    /// <summary>
    /// Guruh o'quvchilari. KURATOR guruhida ular bog'langan ustoz
    /// guruhlaridan yig'iladi (<c>sourceGroupId</c> qaysi guruhdan kelganini
    /// ko'rsatadi).
    /// </summary>
    [HttpGet("{id:long}/members")]
    [ProducesResponseType<IReadOnlyList<GroupMemberDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GroupMemberDto>>> Members(
        long id, CancellationToken ct) =>
        Ok(await groups.ListMembersAsync(id, CurrentUserId, ct));

    [HttpPost("{id:long}/members")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupMemberDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupMemberDto>> AddMember(
        long id, [FromBody] AddMemberRequest request, CancellationToken ct)
    {
        var member = await groups.AddMemberAsync(id, request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Members), new { id }, member);
    }

    /// <summary>Pauza. <c>pausedUntil</c> ixtiyoriy (bo'lmasa muddatsiz).</summary>
    [HttpPost("{id:long}/members/{studentId:long}/pause")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupMemberDto>> PauseMember(
        long id, long studentId, [FromBody] PauseMemberRequest? request, CancellationToken ct) =>
        Ok(await groups.PauseMemberAsync(
            id, studentId, request ?? new PauseMemberRequest(), CurrentUserId, ct));

    [HttpPost("{id:long}/members/{studentId:long}/resume")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupMemberDto>> ResumeMember(
        long id, long studentId, CancellationToken ct) =>
        Ok(await groups.ResumeMemberAsync(id, studentId, CurrentUserId, ct));

    /// <summary>
    /// YUMSHOQ chiqarish: yozuv o'chirilmaydi, holati <c>Stopped</c> bo'ladi —
    /// davomat va to'lov tarixi a'zolikka ishora qiladi.
    ///
    /// ★ `POST`, `DELETE` EMAS (2026-08-17): amal endi MAJBURIY sabab
    /// (so'rov tanasi) talab qiladi. `DELETE` bilan tana yuborish
    /// rasman mumkin bo'lsa ham, ko'p klient va proksi uni tashlab
    /// yuboradi — shuning uchun `pause`/`resume`/`move` bilan AYNI
    /// `POST` naqshiga o'tkazildi.
    /// </summary>
    [HttpPost("{id:long}/members/{studentId:long}/remove")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GroupMemberDto>> RemoveMember(
        long id, long studentId, [FromBody] RemoveMemberRequest request, CancellationToken ct) =>
        Ok(await groups.RemoveMemberAsync(id, studentId, request, CurrentUserId, ct));

    /// <summary>Boshqa guruhga ko'chirish — ATOMIK (bitta tranzaksiya).</summary>
    [HttpPost("{id:long}/members/{studentId:long}/move")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<MoveMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MoveMemberResponse>> MoveMember(
        long id, long studentId, [FromBody] MoveMemberRequest request, CancellationToken ct) =>
        Ok(await groups.MoveMemberAsync(id, studentId, request, CurrentUserId, ct));

    // ================================================================= jadval

    /// <summary>
    /// Generatsiya qilingan dars jadvali.
    /// </summary>
    /// <param name="from">Oraliq boshi (UTC, ixtiyoriy).</param>
    /// <param name="to">Oraliq oxiri (UTC, ixtiyoriy).</param>
    [HttpGet("{id:long}/schedule")]
    [ProducesResponseType<IReadOnlyList<ScheduledSessionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ScheduledSessionDto>>> Schedule(
        long id,
        [FromQuery(Name = "from")] DateTimeOffset? from,
        [FromQuery(Name = "to")] DateTimeOffset? to,
        CancellationToken ct) =>
        Ok(await groups.GetScheduleAsync(id, from, to, CurrentUserId, ct));

    /// <summary>
    /// Jadvalni ATAYLAB qayta tuzadi. Faqat kelajakdagi rejalashtirilgan
    /// darslar almashtiriladi — o'tgan, jonli, yakunlangan va bekor qilingan
    /// darslar saqlanadi.
    /// </summary>
    [HttpPost("{id:long}/schedule/regenerate")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<ScheduleChangeSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScheduleChangeSummary>> RegenerateSchedule(
        long id, CancellationToken ct) =>
        Ok(await groups.RegenerateScheduleAsync(id, CurrentUserId, ct));

    // ================================================================= kurator

    /// <summary>Bu guruh bog'lanishi mumkin bo'lgan kurator guruhlari.</summary>
    [HttpGet("{id:long}/curator-candidates")]
    [ProducesResponseType<IReadOnlyList<CuratorCandidateDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratorCandidateDto>>> CuratorCandidates(
        long id, CancellationToken ct) =>
        Ok(await groups.ListCuratorCandidatesAsync(id, CurrentUserId, ct));

    // ---------------------------------------------------------------- ichki

    /// <summary>Guruhni O'ZGARTIRA oladigan rollar (sinf darvozasi bilan kesishadi).</summary>
    private const string ManageRoles = "Academic,Admin";

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
