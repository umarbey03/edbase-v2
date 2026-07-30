using Zinnur.Application.Common.Models;
using Zinnur.Application.Groups.Dtos;
using Zinnur.Application.Scheduling.Dtos;

namespace Zinnur.Application.Groups.Services;

/// <summary>
/// Guruhlarni boshqarish (o'quv bo'limi / admin paneli) + guruh jadvali.
///
/// HAR BIR metod <paramref name="actorId"/> ni oladi — ruxsat tekshiruvi
/// SHU YERDA, controller'da emas (<c>UsersController</c> bilan bir xil
/// yondashuv). Controller faqat <c>[Authorize(Roles=...)]</c> darvozasini
/// ushlaydi.
///
/// KO'RISH QOIDASI:
///   • <c>Admin</c>, <c>Academic</c> — barcha guruhlar;
///   • <c>Teacher</c>, <c>Assistant</c> — FAQAT o'z guruhlari
///     (ustoz/kurator sifatida biriktirilgan) va o'z kurator guruhiga
///     bog'langan ustoz guruhlari.
/// TAHRIRLASH QOIDASI: faqat <c>Admin</c> va <c>Academic</c>.
/// </summary>
public interface IGroupService
{
    /// <summary>
    /// Sahifalangan ro'yxat. Ustoz/kurator uchun natija AVTOMATIK
    /// o'z guruhlariga cheklanadi — alohida "mening guruhlarim" endpointi
    /// kerak emas (bitta so'rov, bitta filtr mantiqi).
    /// </summary>
    Task<PagedResult<GroupDto>> ListAsync(
        GroupListQuery query, long actorId, CancellationToken ct = default);

    Task<GroupDto> GetAsync(long id, long actorId, CancellationToken ct = default);

    /// <summary>Guruh yaratadi VA butun kurs jadvalini generatsiya qiladi (bitta tranzaksiya).</summary>
    Task<CreateGroupResponse> CreateAsync(
        CreateGroupRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Guruhni tahrirlaydi. Jadval FAQAT jadval qoidasi o'zgarganda qayta
    /// tuziladi; ustoz/nom o'zgarsa darslar O'RNIDA tahrirlanadi.
    /// Javobda aynan nima bo'lgani qaytadi.
    /// </summary>
    Task<UpdateGroupResponse> UpdateAsync(
        long id, UpdateGroupRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Arxivlash / tiklash. Jadvalga TEGMAYDI.</summary>
    Task<GroupDto> SetActiveAsync(
        long id, bool isActive, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- a'zolik

    /// <summary>
    /// Guruh o'quvchilari. KURATOR guruhida a'zolar bevosita yo'q —
    /// ular <c>CuratorGroupId</c> havolasi orqali bog'langan ustoz
    /// guruhlaridan hisoblanadi.
    /// </summary>
    Task<IReadOnlyList<GroupMemberDto>> ListMembersAsync(
        long id, long actorId, CancellationToken ct = default);

    Task<GroupMemberDto> AddMemberAsync(
        long id, AddMemberRequest request, long actorId, CancellationToken ct = default);

    Task<GroupMemberDto> PauseMemberAsync(
        long id, long studentId, PauseMemberRequest request, long actorId, CancellationToken ct = default);

    Task<GroupMemberDto> ResumeMemberAsync(
        long id, long studentId, long actorId, CancellationToken ct = default);

    /// <summary>YUMSHOQ chiqarish: yozuv o'chirilmaydi, holati <c>Stopped</c> bo'ladi.</summary>
    Task<GroupMemberDto> RemoveMemberAsync(
        long id, long studentId, long actorId, CancellationToken ct = default);

    /// <summary>Boshqa guruhga ko'chirish — ATOMIK (bitta tranzaksiya).</summary>
    Task<MoveMemberResponse> MoveMemberAsync(
        long id, long studentId, MoveMemberRequest request, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- jadval

    Task<IReadOnlyList<ScheduledSessionDto>> GetScheduleAsync(
        long id, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, long actorId,
        CancellationToken ct = default);

    /// <summary>
    /// Jadvalni ATAYLAB qayta tuzadi (guruh maydonlari o'zgarmaganda ham).
    /// Saqlash qoidasi <c>UpdateAsync</c> dagi bilan bir xil.
    /// </summary>
    Task<ScheduleChangeSummary> RegenerateScheduleAsync(
        long id, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- kurator

    /// <summary>Bu ustoz guruhi bog'lanishi mumkin bo'lgan kurator guruhlari.</summary>
    Task<IReadOnlyList<CuratorCandidateDto>> ListCuratorCandidatesAsync(
        long id, long actorId, CancellationToken ct = default);
}
