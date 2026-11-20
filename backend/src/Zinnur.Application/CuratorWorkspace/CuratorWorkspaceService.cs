using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Messaging.Dtos;
using Zinnur.Application.Messaging.Services;
using Zinnur.Application.SelfStudy;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.CuratorWorkspace;

/// <summary>Ish stoli chap ro'yxatidagi bitta o'quvchi.</summary>
/// <param name="CanChat">Xodim shu o'quvchining suhbatdoshimi (yozishmaga YOZA oladimi).</param>
/// <param name="IsBackup">Mustaqil o'quvchining zaxira kuratori — yozishmani faqat o'qiydi.</param>
/// <param name="UnansweredQuestions">Oxirgi javobdan keyin dars sahifasidan yozilgan savollar.</param>
/// <param name="PendingSubmissions">Shu xodim baholashi kerak bo'lgan javoblar.</param>
/// <param name="WaitingSince">Eng uzoq kutayotgan narsa (javobsiz xabar yoki tekshirilmagan javob).</param>
public sealed record WorkspaceStudentDto(
    long StudentId,
    string FullName,
    bool IsSelfStudy,
    string? GroupName,
    bool CanChat,
    bool IsBackup,
    int UnreadCount,
    int UnansweredQuestions,
    int PendingSubmissions,
    AwaitingLessonDto? AwaitingDecision,
    DateTimeOffset? WaitingSince,
    DateTimeOffset? LastActivityAt,
    bool NeedsAction);

/// <summary>O'ng panel sarlavhasi.</summary>
/// <param name="PrimaryCuratorId">Zaxira kurator ko'rganda — yozishma qaysi juftlikdan o'qiladi.</param>
public sealed record WorkspaceStudentHeaderDto(
    long StudentId,
    string FullName,
    string? Phone,
    bool IsSelfStudy,
    string? GroupName,
    DateTimeOffset? SubscriptionEndsAt,
    bool SubscriptionActive,
    bool CanChat,
    bool IsBackup,
    long? PrimaryCuratorId,
    string? PrimaryCuratorName,
    int MasteredLessons,
    int TotalLessons,
    AwaitingLessonDto? AwaitingDecision);

/// <summary>Oqimdagi test natijasi.</summary>
public sealed record WorkspaceTestResultDto(
    long TestId,
    string Title,
    string? LessonName,
    decimal? Score,
    decimal? MaxScore,
    DateTimeOffset SubmittedAt);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// KURATOR ISH STOLI (2026-11-20, loyiha egasi): "kurator u yoqdan bu yoqqa
/// yugurib qolyapti". Endi bitta oyna: chapda — kim nimani kutyapti, o'ngda —
/// shu o'quvchining yozishmasi, dars savollari, javoblari, testlari va dars
/// qarori bir oqimda.
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ YANGI RUXSAT QOIDASI YO'Q — mavjud ikkitasining birlashmasi:
///   • ko'rish: <c>IAssignmentService.ListMyStudentIdsAsync</c> (StaffDuty.Access);
///   • yozishish: <c>ICuratorDirectory.StudentIdsAsync</c> (yozishma qoidasi).
///   Baholash va dars qarori o'z endpointlarida, o'z qoidasi bilan qoladi —
///   ish stoli ularni faqat bir joyga yig'adi.
///
/// ★ ZAXIRA KURATOR yozishmani faqat O'QIYDI (o'quvchi bitta odam bilan
///   gaplashadi), lekin baholaydi va dars qarorini beradi — avvalgidek.
/// </summary>
public interface ICuratorWorkspaceService
{
    Task<IReadOnlyList<WorkspaceStudentDto>> ListStudentsAsync(long staffId, CancellationToken ct = default);

    Task<WorkspaceStudentHeaderDto> GetStudentAsync(long studentId, long staffId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkspaceTestResultDto>> ListTestResultsAsync(
        long studentId, long staffId, CancellationToken ct = default);

    /// <summary>Zaxira kurator: asosiy kurator bilan yozishmani o'qish ("o'qildi" o'zgarmaydi).</summary>
    Task<MessagePageDto> ReadBackupThreadAsync(
        long studentId, long staffId, long? beforeId, int take, CancellationToken ct = default);
}

public sealed class CuratorWorkspaceService(
    IApplicationDbContext db,
    IAssignmentService assignments,
    ICuratorDirectory curators,
    IDirectMessageService messages,
    ILessonMasteryService mastery,
    TimeProvider clock) : ICuratorWorkspaceService
{
    /// <summary>Ro'yxat chegarasi — eng ko'p ish kutayotganlar birinchi, qolgani qidiruvda.</summary>
    private const int MaxStudents = 500;

    public async Task<IReadOnlyList<WorkspaceStudentDto>> ListStudentsAsync(
        long staffId, CancellationToken ct = default)
    {
        await EnsureStaffAsync(staffId, ct);
        var scope = await ScopeAsync(staffId, ct);
        if (scope.All.Count == 0) return [];

        var ids = scope.All.ToList();

        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.IsActive })
            .ToDictionaryAsync(u => u.Id, ct);

        var groupNames = await GroupNamesAsync(ids, ct);
        var selfStudy = await SelfStudyIdsAsync(ids, ct);

        // Yozishma ko'rsatkichlari — faqat SHU xodimning juftliklari.
        var pairs = await db.DirectMessages.AsNoTracking()
            .Where(m => m.StaffId == staffId && ids.Contains(m.StudentId))
            .GroupBy(m => m.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                LastAt = g.Max(m => m.SentAt),
                LastReplyId = g.Where(m => m.SenderId == m.StaffId).Max(m => (long?)m.Id),
                Unread = g.Count(m => m.SenderId == m.StudentId && !m.ReadByStaff),
            })
            .ToDictionaryAsync(p => p.StudentId, ct);

        var waiting = await db.DirectMessages.AsNoTracking()
            .Where(m => m.StaffId == staffId && ids.Contains(m.StudentId) && m.SenderId == m.StudentId)
            .Where(m => !db.DirectMessages.Any(r =>
                r.StudentId == m.StudentId && r.StaffId == m.StaffId && r.SenderId == r.StaffId && r.Id > m.Id))
            .GroupBy(m => m.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Since = g.Min(m => m.SentAt),
                Questions = g.Count(m => m.ModuleLessonId != null),
            })
            .ToDictionaryAsync(w => w.StudentId, ct);

        var pending = await assignments.SummarizePendingForStaffAsync(staffId, ids, ct);

        var lastSubmission = await db.Submissions.AsNoTracking()
            .Where(s => ids.Contains(s.StudentId))
            .GroupBy(s => s.StudentId)
            .Select(g => new { StudentId = g.Key, At = g.Max(s => s.SubmittedAt) })
            .ToDictionaryAsync(s => s.StudentId, s => s.At, ct);

        // Dars qarori — faqat shu xodim kurator (asosiy/zaxira) bo'lgan Mustaqil o'quvchilar.
        var awaiting = new Dictionary<long, AwaitingLessonDto>();
        foreach (var studentId in scope.CuratedSelfStudy)
        {
            if (await mastery.FindAwaitingDecisionAsync(studentId, ct) is { } lesson)
                awaiting[studentId] = lesson;
        }

        var rows = new List<WorkspaceStudentDto>(ids.Count);
        foreach (var id in ids)
        {
            if (!names.TryGetValue(id, out var user) || !user.IsActive) continue;

            pairs.TryGetValue(id, out var pair);
            waiting.TryGetValue(id, out var wait);
            pending.TryGetValue(id, out var pend);
            awaiting.TryGetValue(id, out var decision);
            lastSubmission.TryGetValue(id, out var submittedAt);

            var canChat = scope.Chat.Contains(id);
            var unread = canChat ? pair?.Unread ?? 0 : 0;
            var questions = canChat ? wait?.Questions ?? 0 : 0;
            var waitingSince = Earliest(canChat ? wait?.Since : null, pend?.OldestSubmittedAt);

            var lastActivity = Latest(pair?.LastAt, submittedAt == default ? null : submittedAt);

            rows.Add(new WorkspaceStudentDto(
                id,
                user.FullName,
                selfStudy.Contains(id),
                groupNames.GetValueOrDefault(id),
                canChat,
                scope.Backup.Contains(id),
                unread,
                questions,
                pend?.Count ?? 0,
                decision,
                waitingSince,
                lastActivity,
                NeedsAction: unread > 0 || questions > 0 || (canChat && wait is not null)
                             || (pend?.Count ?? 0) > 0 || decision is not null));
        }

        // Ish kutayotganlar tepada (eng uzoq kutgani birinchi), keyin — oxirgi faollik.
        return rows
            .OrderByDescending(r => r.NeedsAction)
            .ThenBy(r => r.WaitingSince ?? DateTimeOffset.MaxValue)
            .ThenByDescending(r => r.LastActivityAt ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .Take(MaxStudents)
            .ToList();
    }

    public async Task<WorkspaceStudentHeaderDto> GetStudentAsync(
        long studentId, long staffId, CancellationToken ct = default)
    {
        await EnsureStaffAsync(staffId, ct);
        var scope = await EnsureInScopeAsync(studentId, staffId, ct);
        var now = clock.GetUtcNow();

        var student = await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId)
            .Select(u => new { u.FullName, u.Phone })
            .FirstAsync(ct);

        var groupNames = await GroupNamesAsync([studentId], ct);
        var isSelfStudy = (await SelfStudyIdsAsync([studentId], ct)).Contains(studentId);

        var periods = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.CancelledAt == null)
            .Select(p => new { p.StartsAt, p.EndsAt, p.CourseId })
            .ToListAsync(ct);

        var curator = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.EndedAt == null)
            .Select(a => new { a.CuratorId, Name = a.Curator!.FullName })
            .FirstOrDefaultAsync(ct);

        var mastered = await db.LessonMasteries.AsNoTracking()
            .CountAsync(m => m.StudentId == studentId && m.Status == MasteryStatus.Mastered, ct);

        var courseId = periods.OrderByDescending(p => p.EndsAt).Select(p => (long?)p.CourseId).FirstOrDefault();
        var total = courseId is { } cid
            ? await db.ModuleLessons.AsNoTracking().CountAsync(l => l.Module!.CourseId == cid, ct)
            : 0;

        var awaiting = isSelfStudy && scope.CuratedSelfStudy.Contains(studentId)
            ? await mastery.FindAwaitingDecisionAsync(studentId, ct)
            : null;

        return new WorkspaceStudentHeaderDto(
            studentId,
            student.FullName,
            student.Phone,
            isSelfStudy,
            groupNames.GetValueOrDefault(studentId),
            periods.Count == 0 ? null : periods.Max(p => p.EndsAt),
            periods.Any(p => p.StartsAt <= now && now < p.EndsAt),
            scope.Chat.Contains(studentId),
            scope.Backup.Contains(studentId),
            curator?.CuratorId,
            curator?.Name,
            mastered,
            total,
            awaiting);
    }

    public async Task<IReadOnlyList<WorkspaceTestResultDto>> ListTestResultsAsync(
        long studentId, long staffId, CancellationToken ct = default)
    {
        await EnsureStaffAsync(staffId, ct);
        await EnsureInScopeAsync(studentId, staffId, ct);

        return await db.TestAttempts.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Status == AttemptStatus.Submitted && a.SubmittedAt != null)
            .OrderBy(a => a.SubmittedAt)
            .Select(a => new WorkspaceTestResultDto(
                a.TestId,
                a.Test!.Title,
                a.Test.ModuleLesson == null ? null : a.Test.ModuleLesson.Name,
                a.Score,
                a.MaxScore,
                a.SubmittedAt!.Value))
            .ToListAsync(ct);
    }

    public async Task<MessagePageDto> ReadBackupThreadAsync(
        long studentId, long staffId, long? beforeId, int take, CancellationToken ct = default)
    {
        await EnsureStaffAsync(staffId, ct);
        var scope = await EnsureInScopeAsync(studentId, staffId, ct);

        if (!scope.Backup.Contains(studentId))
            throw new ForbiddenException("Bu yozishma faqat zaxira kurator uchun o'qish rejimida ochiladi.");

        var primaryId = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.EndedAt == null)
            .Select(a => (long?)a.CuratorId)
            .FirstOrDefaultAsync(ct);

        if (primaryId is null)
            throw new NotFoundException("SelfStudyCuratorAssignment", studentId);

        return await messages.ReadThreadForOversightAsync(studentId, primaryId.Value, beforeId, take, ct);
    }

    // ================================================================ yordamchilar

    private sealed record Scope(
        HashSet<long> All,
        HashSet<long> Chat,
        HashSet<long> Backup,
        HashSet<long> CuratedSelfStudy);

    private async Task<Scope> ScopeAsync(long staffId, CancellationToken ct)
    {
        var access = await assignments.ListMyStudentIdsAsync(staffId, ct);
        var chat = (await curators.StudentIdsAsync(staffId, ct)).ToHashSet();

        var curated = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(SelfStudyCuratorAssignment.ActiveFor(staffId))
            .Select(a => new { a.StudentId, IsBackup = a.CuratorId != staffId })
            .ToListAsync(ct);

        var all = access.ToHashSet();
        all.UnionWith(chat);
        all.UnionWith(curated.Select(c => c.StudentId));

        return new Scope(
            all,
            chat,
            curated.Where(c => c.IsBackup).Select(c => c.StudentId).ToHashSet(),
            curated.Select(c => c.StudentId).ToHashSet());
    }

    private async Task<Scope> EnsureInScopeAsync(long studentId, long staffId, CancellationToken ct)
    {
        var scope = await ScopeAsync(staffId, ct);
        if (!scope.All.Contains(studentId))
            throw new ForbiddenException("Bu o'quvchi sizga biriktirilmagan.");
        return scope;
    }

    private async Task<Dictionary<long, string>> GroupNamesAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var rows = await db.GroupMembers.AsNoTracking()
            .Where(m => ids.Contains(m.StudentId) && m.Status == MemberStatus.Active)
            .Select(m => new { m.StudentId, m.Group!.Name })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>Mustaqil o'quvchi: bekor qilinmagan obuna bor va faol guruhda emas (panel bilan AYNI ta'rif).</summary>
    private async Task<HashSet<long>> SelfStudyIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        (await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id)
                     && db.SubscriptionPeriods.Any(p => p.StudentId == u.Id && p.CancelledAt == null)
                     && !db.GroupMembers.Any(m => m.StudentId == u.Id && m.Status == MemberStatus.Active))
            .Select(u => u.Id)
            .ToListAsync(ct))
        .ToHashSet();

    private async Task EnsureStaffAsync(long staffId, CancellationToken ct)
    {
        var staff = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == staffId, ct)
            ?? throw new NotFoundException(nameof(User), staffId);

        if (!staff.IsActive || staff.Role is not (UserRole.Teacher or UserRole.Assistant))
            throw new ForbiddenException("Ish stoli kurator va ustozlar uchun.");
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;
}
