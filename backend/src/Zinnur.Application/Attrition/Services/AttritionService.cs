using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Attrition.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Attrition.Services;

/// <inheritdoc cref="IAttritionService"/>
public sealed class AttritionService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone) : IAttritionService
{
    /// <inheritdoc />
    public async Task<PagedResult<AttritionRowDto>> ListAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = Filter(query);

        var total = await rows.CountAsync(ct);

        var items = await Sort(rows, query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.OccurredAt,
                e.StudentId,
                StudentName = e.Student!.FullName,
                e.GroupId,
                GroupName = e.Group!.Name,
                e.TeacherId,

                // Ustoz va nishon guruh nomi — KORRELYATSIYALANGAN ichki
                // so'rov bilan: ikkalasi ham navigatsiyasiz FK (surat
                // qiymatlari), ya'ni `Include` ishlamaydi. Loyihadagi
                // `UserProfileService` va `GroupService` dagi AYNI naqsh.
                TeacherName = e.TeacherId == null
                    ? null
                    : db.Users.Where(u => u.Id == e.TeacherId).Select(u => u.FullName).FirstOrDefault(),
                e.Kind,
                e.Reason,
                e.MovedToGroupId,
                MovedToGroupName = e.MovedToGroupId == null
                    ? null
                    : db.Groups.Where(g => g.Id == e.MovedToGroupId).Select(g => g.Name).FirstOrDefault(),
                ActorName = e.Actor!.FullName,
                e.LessonsCompleted,
            })
            .ToListAsync(ct);

        // `Kind.ToString()` va `IsTrial` XOTIRADA: ularni so'rov ichida
        // yozish SQL'ga tarjima qilishga majburlardi (loyihadagi
        // `LessonGradeSummaryService` izohidagi AYNI sabab).
        var mapped = items.ConvertAll(x => new AttritionRowDto(
            x.Id,
            x.OccurredAt,
            x.StudentId,
            x.StudentName,
            x.GroupId,
            x.GroupName,
            x.TeacherId,
            x.TeacherName,
            x.Kind.ToString(),
            x.Reason,
            x.MovedToGroupId,
            x.MovedToGroupName,
            x.ActorName,
            x.LessonsCompleted,
            x.LessonsCompleted < GroupMembershipEvent.TrialLessonCount));

        return new PagedResult<AttritionRowDto>(mapped, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<AttritionSummaryDto> GetSummaryAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var rows = Filter(query);

        // Bitta so'rovda: tur bo'yicha sanoq + sinov/aktiv bo'linishi +
        // o'rtacha dars soni. Uchta alohida so'rov o'rniga yassi qatorlar
        // olinadi va xotirada yig'iladi (`AssignmentService.GetGroupsOverviewAsync`
        // dagi naqsh) — hodisa jurnalida qator kam va bu arzon.
        var flat = await rows
            .Select(e => new { e.Kind, e.LessonsCompleted })
            .ToListAsync(ct);

        int CountOf(MembershipEventKind kind) => flat.Count(x => x.Kind == kind);

        // ★ "YO'QOTISH" faqat CHIQARISH va KO'CHIRISH: muzlatish vaqtinchalik
        //   (o'quvchi qaytishi mumkin) va qaytish/qo'shilish umuman yo'qotish
        //   emas. Aks holda "to'kilish" raqami sun'iy ravishda oshib ketardi.
        var losses = flat
            .Where(x => x.Kind is MembershipEventKind.Stopped or MembershipEventKind.Moved)
            .ToList();

        var stoppedOnly = flat.Where(x => x.Kind == MembershipEventKind.Stopped).ToList();

        return new AttritionSummaryDto(
            Total: flat.Count,
            Stopped: CountOf(MembershipEventKind.Stopped),
            Paused: CountOf(MembershipEventKind.Paused),
            Moved: CountOf(MembershipEventKind.Moved),
            TrialLosses: losses.Count(x => x.LessonsCompleted < GroupMembershipEvent.TrialLessonCount),
            ActiveLosses: losses.Count(x => x.LessonsCompleted >= GroupMembershipEvent.TrialLessonCount),

            // O'rtacha FAQAT chiqarilganlar bo'yicha: ko'chirish "yo'qotish"
            // bo'lsa ham, o'quvchi markazda QOLADI va uni bu o'rtachaga
            // qo'shish "qancha dars keyin yo'qotamiz" ma'nosini buzardi.
            AverageLessonsBeforeLeaving: stoppedOnly.Count == 0
                ? 0
                : Math.Round(stoppedOnly.Average(x => x.LessonsCompleted), 1));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttritionByTeacherDto>> GetByTeacherAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var flat = await Filter(query)
            .Select(e => new { e.TeacherId, e.Kind, e.LessonsCompleted })
            .ToListAsync(ct);

        if (flat.Count == 0) return [];

        var teacherIds = flat.Where(x => x.TeacherId is not null)
            .Select(x => x.TeacherId!.Value)
            .Distinct()
            .ToList();

        var names = teacherIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking()
                .Where(u => teacherIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return flat
            .GroupBy(x => x.TeacherId)
            .Select(g => new AttritionByTeacherDto(
                g.Key,
                g.Key is { } id && names.TryGetValue(id, out var name) ? name : "Ustoz tayinlanmagan",
                g.Count(x => x.Kind == MembershipEventKind.Stopped),
                g.Count(x => x.Kind == MembershipEventKind.Paused),
                g.Count(x => x.Kind == MembershipEventKind.Moved),
                g.Count(x => x.Kind is MembershipEventKind.Stopped or MembershipEventKind.Moved
                          && x.LessonsCompleted < GroupMembershipEvent.TrialLessonCount)))
            // ★ ENG KO'P CHIQARILGANI BIRINCHI — panel ochilganda diqqat
            //   talab qiladigan ustoz tepada turadi.
            .OrderByDescending(x => x.Stopped)
            .ThenByDescending(x => x.Moved)
            .ThenBy(x => x.TeacherName, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttritionByGroupDto>> GetByGroupAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var flat = await Filter(query)
            .Select(e => new
            {
                e.GroupId,
                GroupName = e.Group!.Name,
                TeacherName = e.TeacherId == null
                    ? null
                    : db.Users.Where(u => u.Id == e.TeacherId).Select(u => u.FullName).FirstOrDefault(),
                e.Kind,
                e.LessonsCompleted,
            })
            .ToListAsync(ct);

        if (flat.Count == 0) return [];

        var groupIds = flat.Select(x => x.GroupId).Distinct().ToList();

        var activeCounts = await db.GroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId) && m.Status == MemberStatus.Active)
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        return flat
            .GroupBy(x => x.GroupId)
            .Select(g =>
            {
                var first = g.First();

                return new AttritionByGroupDto(
                    g.Key,
                    first.GroupName,
                    first.TeacherName,
                    g.Count(x => x.Kind == MembershipEventKind.Stopped),
                    g.Count(x => x.Kind is MembershipEventKind.Stopped or MembershipEventKind.Moved
                              && x.LessonsCompleted < GroupMembershipEvent.TrialLessonCount),
                    activeCounts.TryGetValue(g.Key, out var active) ? active : 0);
            })
            .OrderByDescending(x => x.Stopped)
            .ThenBy(x => x.GroupName, StringComparer.Ordinal)
            .ToList();
    }

    // ---------------------------------------------------------------- filtr / saralash

    /// <summary>
    /// Ro'yxat va BARCHA yig'malar uchun AYNI filtr — takrorlansa yig'ma
    /// raqamlari ro'yxatga mos kelmay qolardi.
    /// </summary>
    private IQueryable<GroupMembershipEvent> Filter(AttritionListQuery query)
    {
        var rows = db.GroupMembershipEvents.AsNoTracking();

        // ★ `OccurredAt` — `DateTimeOffset` (UTC), filtr esa MAHALLIY sana.
        //   Shuning uchun UTC chegaralariga o'giriladi: chap chegara KIRADI,
        //   o'ng chegara `to + 1 kun` (KIRMAYDI). `23:59:59` yozilsa o'sha
        //   oxirgi soniyadagi hodisa YO'QOLARDI — loyihadagi
        //   `PaymentSummaryService.ResolveWindow` bilan AYNI qoida.
        var zone = timeZone.TimeZone;

        if (query.From is { } from)
        {
            var fromUtc = LocalWallClock.StartOfDayUtc(from, zone);
            rows = rows.Where(e => e.OccurredAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = LocalWallClock.StartOfDayUtc(to.AddDays(1), zone);
            rows = rows.Where(e => e.OccurredAt < toUtc);
        }

        if (query.From is { } start && query.To is { } end && start > end)
            throw Invalid("from", "Davr boshi oxiridan keyin bo'lmasligi kerak.");

        if (query.Kind is { } kind) rows = rows.Where(e => e.Kind == kind);
        if (query.GroupId is { } groupId) rows = rows.Where(e => e.GroupId == groupId);
        if (query.TeacherId is { } teacherId) rows = rows.Where(e => e.TeacherId == teacherId);

        if (query.Trial is { } trial)
        {
            rows = trial
                ? rows.Where(e => e.LessonsCompleted < GroupMembershipEvent.TrialLessonCount)
                : rows.Where(e => e.LessonsCompleted >= GroupMembershipEvent.TrialLessonCount);
        }

        var term = NormalizeSearch(query.Search);

        if (term is not null)
        {
#pragma warning disable CA1304, CA1311
            rows = rows.Where(e =>
                EF.Functions.Like(e.Student!.FullName.ToLower(), term)
                || EF.Functions.Like(e.Group!.Name.ToLower(), term)
                || (e.Reason != null && EF.Functions.Like(e.Reason.ToLower(), term)));
#pragma warning restore CA1304, CA1311
        }

        return rows;
    }

    /// <summary>Saralash — OQ RO'YXAT, har variantda `Id` bilan aniqlashtirilgan.</summary>
    private static IQueryable<GroupMembershipEvent> Sort(
        IQueryable<GroupMembershipEvent> rows, AttritionListQuery query) =>
        (query.Sort, query.Desc) switch
        {
            (AttritionSort.Student, false) => rows.OrderBy(e => e.Student!.FullName).ThenBy(e => e.Id),
            (AttritionSort.Student, true) => rows.OrderByDescending(e => e.Student!.FullName).ThenBy(e => e.Id),

            (AttritionSort.Group, false) => rows.OrderBy(e => e.Group!.Name).ThenBy(e => e.Id),
            (AttritionSort.Group, true) => rows.OrderByDescending(e => e.Group!.Name).ThenBy(e => e.Id),

            (AttritionSort.Lessons, false) => rows.OrderBy(e => e.LessonsCompleted).ThenBy(e => e.Id),
            (AttritionSort.Lessons, true) => rows.OrderByDescending(e => e.LessonsCompleted).ThenBy(e => e.Id),

            (_, false) => rows.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id),
            (_, true) => rows.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id),
        };

    // ---------------------------------------------------------------- yordamchi

    /// <summary>
    /// Hisobotni faqat o'quv bo'limi va admin ko'radi.
    ///
    /// ★ RUXSAT SERVISDA, controller atributiga QO'SHIMCHA: loyihada
    /// tekshiruv har doim servis qatlamida ham bor (`GroupService.
    /// EnsureCanManage` bilan AYNI qoida) — controller atributi chetlab
    /// o'tilsa ham (masalan boshqa servis chaqirsa) ma'lumot sizmaydi.
    /// </summary>
    private async Task EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct);

        if (role is null)
            throw new NotFoundException(nameof(User), actorId);

        if (role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "To'kilishlar hisobotini faqat o'quv bo'limi xodimi yoki administrator ko'radi.");
        }
    }

    private static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        return "%" + EscapeLike(trimmed.ToLowerInvariant()) + "%";
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private const int MaxPageSize = 100;
}
