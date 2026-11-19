using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Messaging.Dtos;
using Zinnur.Application.Messaging.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.SelfStudy.Panel;

/// <summary>
/// Mustaqil paneli — o'quv bo'limi va administrator uchun bitta joyda:
/// o'quvchilar, kuratorlar, vazifalar va yozishmalar (2026-11-19).
///
/// ★ YANGI QOIDA YO'Q: biriktirish <see cref="ISelfStudyCuratorService.AssignAsync"/>
///   orqali (guruh o'quvchisi emas, obuna bor, zaxira ≠ asosiy, parallel
///   o'zgarishdan himoya — hammasi o'sha yerda). Panel faqat KO'RINISH va
///   ommaviy chaqiruv qo'shadi.
///
/// 🔐 YOZISHMALAR — FAQAT O'QISH (loyiha egasining qarori): har bir ochish
///   <see cref="DirectMessageAccessAudit"/> ga yoziladi, "o'qildi"
///   bayroqlariga tegilmaydi, yozish endpointi yo'q.
/// </summary>
public interface ISelfStudyPanelService
{
    Task<SelfStudyPanelSummaryDto> GetSummaryAsync(long actorId, CancellationToken ct = default);

    Task<PagedResult<SelfStudyStudentRowDto>> ListStudentsAsync(
        SelfStudyStudentQuery query, long actorId, CancellationToken ct = default);

    Task<BulkAssignResultDto> BulkAssignAsync(
        BulkAssignSelfStudyCuratorRequest request, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<SelfStudyCuratorRowDto>> ListCuratorsAsync(long actorId, CancellationToken ct = default);

    Task<PagedResult<SelfStudySubmissionRowDto>> ListSubmissionsAsync(
        SelfStudySubmissionQuery query, long actorId, CancellationToken ct = default);

    Task<PagedResult<SelfStudyChatRowDto>> ListChatsAsync(
        SelfStudyChatQuery query, long actorId, CancellationToken ct = default);

    /// <summary>Yozishmani o'qish. Birinchi sahifa (<paramref name="beforeId"/> = null) auditga yoziladi.</summary>
    Task<MessagePageDto> ReadChatAsync(
        long studentId, long staffId, long? beforeId, int take, long actorId, CancellationToken ct = default);

    /// <summary>Yozishma biriktirmasi. Har ochish auditga yoziladi.</summary>
    Task<Courses.Services.LessonAssetDownload> OpenChatAttachmentAsync(
        long attachmentId, string? rangeHeader, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<ChatAccessLogEntryDto>> ListChatAccessAsync(
        long studentId, long staffId, long actorId, CancellationToken ct = default);
}

public sealed class SelfStudyPanelService(
    IApplicationDbContext db,
    ISelfStudyCuratorService curators,
    IDirectMessageService messages,
    TimeProvider clock) : ISelfStudyPanelService
{
    private const int MaxPageSize = 100;
    private const int MaxBulk = 200;
    private const int MinSearchLength = 3;
    private const int PreviewLength = 120;

    /// <summary>Kurator bo'la oladigan rollar — <c>SelfStudyCuratorService</c> bilan AYNI.</summary>
    private static readonly UserRole[] CuratorRoles = [UserRole.Assistant, UserRole.Teacher];

    // ================================================================ umumiy

    public async Task<SelfStudyPanelSummaryDto> GetSummaryAsync(long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);
        var now = clock.GetUtcNow();

        var students = SelfStudyStudents();

        var total = await students.CountAsync(ct);
        var active = await students.CountAsync(u => db.SubscriptionPeriods.Any(p =>
            p.StudentId == u.Id && p.CancelledAt == null && p.StartsAt <= now && now < p.EndsAt), ct);
        var unassigned = await students.CountAsync(u =>
            db.SubscriptionPeriods.Any(p =>
                p.StudentId == u.Id && p.CancelledAt == null && p.StartsAt <= now && now < p.EndsAt)
            && !db.SelfStudyCuratorAssignments.Any(a => a.StudentId == u.Id && a.EndedAt == null), ct);
        var pending = await db.Submissions.AsNoTracking()
            .CountAsync(s => s.Status == SubmissionStatus.Submitted && students.Any(u => u.Id == s.StudentId), ct);

        var pairs = await ChatPairsAsync(students, curatorId: null, ct);

        return new SelfStudyPanelSummaryDto(total, active, unassigned, pending, pairs.Count(p => p.WaitingSince is not null));
    }

    // ================================================================ o'quvchilar

    public async Task<PagedResult<SelfStudyStudentRowDto>> ListStudentsAsync(
        SelfStudyStudentQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureManagerAsync(actorId, ct);
        var (page, pageSize) = Paging(query.Page, query.PageSize);
        var now = clock.GetUtcNow();

        var rows = ApplySearch(SelfStudyStudents(), query.Search);

        rows = query.State switch
        {
            SelfStudySubscriptionState.Active => rows.Where(u => db.SubscriptionPeriods.Any(p =>
                p.StudentId == u.Id && p.CancelledAt == null && p.StartsAt <= now && now < p.EndsAt)),
            SelfStudySubscriptionState.Upcoming => rows.Where(u =>
                !db.SubscriptionPeriods.Any(p =>
                    p.StudentId == u.Id && p.CancelledAt == null && p.StartsAt <= now && now < p.EndsAt)
                && db.SubscriptionPeriods.Any(p => p.StudentId == u.Id && p.CancelledAt == null && p.StartsAt > now)),
            SelfStudySubscriptionState.Expired => rows.Where(u =>
                !db.SubscriptionPeriods.Any(p => p.StudentId == u.Id && p.CancelledAt == null && p.EndsAt > now)),
            _ => rows,
        };

        if (query.UnassignedOnly)
            rows = rows.Where(u => !db.SelfStudyCuratorAssignments.Any(a => a.StudentId == u.Id && a.EndedAt == null));

        if (query.CuratorId is { } curatorId)
        {
            rows = rows.Where(u => db.SelfStudyCuratorAssignments.Any(a =>
                a.StudentId == u.Id && a.EndedAt == null
                && (a.CuratorId == curatorId || a.BackupCuratorId == curatorId)));
        }

        var total = await rows.CountAsync(ct);

        var pageRows = await rows
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new { u.Id, u.FullName, u.Phone, u.IsActive })
            .ToListAsync(ct);

        var ids = pageRows.ConvertAll(r => r.Id);
        var details = await StudentDetailsAsync(ids, now, ct);

        var items = pageRows.ConvertAll(r =>
        {
            var d = details[r.Id];
            return new SelfStudyStudentRowDto(
                r.Id, r.FullName, r.Phone, r.IsActive,
                d.PlanName, d.State, d.EndsAt,
                d.CuratorId, d.CuratorName, d.BackupId, d.BackupName, d.AssignedAt,
                d.Mastered, d.TotalLessons, d.Pending, d.LastMessageAt, d.WaitingSince);
        });

        return new PagedResult<SelfStudyStudentRowDto>(items, page, pageSize, total);
    }

    public async Task<BulkAssignResultDto> BulkAssignAsync(
        BulkAssignSelfStudyCuratorRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureManagerAsync(actorId, ct);

        var ids = (request.StudentIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            throw Invalid("studentIds", "Kamida bitta o'quvchini tanlang.");
        if (ids.Count > MaxBulk)
            throw Invalid("studentIds", $"Bir martada ko'pi bilan {MaxBulk} ta o'quvchi.");

        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var assigned = 0;
        var failed = new List<BulkAssignFailureDto>();
        var assign = new AssignSelfStudyCuratorRequest(request.CuratorId, request.BackupCuratorId);

        // Har o'quvchi ALOHIDA tranzaksiyada (AssignAsync ichida): bittasining
        // xatosi (masalan guruhga o'tgan) qolganlarini to'xtatmaydi va natija
        // aniq "kim biriktirildi, kim yo'q, nega" bo'lib qaytadi.
        foreach (var studentId in ids)
        {
            try
            {
                await curators.AssignAsync(studentId, assign, actorId, ct);
                assigned++;
            }
            catch (ValidationException ex)
            {
                failed.Add(new(studentId, names.GetValueOrDefault(studentId), FirstMessage(ex)));
            }
            catch (ConflictException ex)
            {
                failed.Add(new(studentId, names.GetValueOrDefault(studentId), ex.Message));
            }
            catch (NotFoundException)
            {
                failed.Add(new(studentId, null, "O'quvchi topilmadi."));
            }
        }

        return new BulkAssignResultDto(assigned, failed);
    }

    // ================================================================ kuratorlar

    public async Task<IReadOnlyList<SelfStudyCuratorRowDto>> ListCuratorsAsync(
        long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var staff = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && CuratorRoles.Contains(u.Role))
            .Select(u => new { u.Id, u.FullName, u.Role })
            .ToListAsync(ct);

        var active = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.EndedAt == null)
            .Select(a => new { a.StudentId, a.CuratorId, a.BackupCuratorId })
            .ToListAsync(ct);

        var primaryStudentIds = active.Select(a => a.StudentId).ToList();
        var pendingByStudent = await db.Submissions.AsNoTracking()
            .Where(s => s.Status == SubmissionStatus.Submitted && primaryStudentIds.Contains(s.StudentId))
            .GroupBy(s => s.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.StudentId, g => g.Count, ct);

        var students = SelfStudyStudents();
        var pairs = await ChatPairsAsync(students, curatorId: null, ct);

        var lastReply = await db.DirectMessages.AsNoTracking()
            .Where(m => m.SenderId == m.StaffId && students.Any(u => u.Id == m.StudentId))
            .GroupBy(m => m.StaffId)
            .Select(g => new { StaffId = g.Key, At = g.Max(m => m.SentAt) })
            .ToDictionaryAsync(g => g.StaffId, g => g.At, ct);

        return staff
            .Select(s => new SelfStudyCuratorRowDto(
                s.Id,
                s.FullName,
                s.Role.ToString(),
                active.Count(a => a.CuratorId == s.Id),
                active.Count(a => a.BackupCuratorId == s.Id),
                active.Where(a => a.CuratorId == s.Id).Sum(a => pendingByStudent.GetValueOrDefault(a.StudentId)),
                pairs.Count(p => p.StaffId == s.Id && p.WaitingSince is not null),
                lastReply.TryGetValue(s.Id, out var at) ? at : null))
            // Avval haqiqiy kuratorlar (o'quvchisi borlar), ichida — ko'p ish kutayotgani yuqorida.
            .OrderByDescending(r => r.PrimaryStudents + r.BackupStudents > 0)
            .ThenBy(r => r.Role == nameof(UserRole.Assistant) ? 0 : 1)
            .ThenByDescending(r => r.PendingSubmissions + r.WaitingChats)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
    }

    // ================================================================ vazifalar

    public async Task<PagedResult<SelfStudySubmissionRowDto>> ListSubmissionsAsync(
        SelfStudySubmissionQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureManagerAsync(actorId, ct);
        var (page, pageSize) = Paging(query.Page, query.PageSize);

        var students = ApplySearch(SelfStudyStudents(), query.Search);

        if (query.CuratorId is { } curatorId)
        {
            students = students.Where(u => db.SelfStudyCuratorAssignments.Any(a =>
                a.StudentId == u.Id && a.EndedAt == null
                && (a.CuratorId == curatorId || a.BackupCuratorId == curatorId)));
        }

        var rows = db.Submissions.AsNoTracking().Where(s => students.Any(u => u.Id == s.StudentId));

        if (query.Status is { } status)
            rows = rows.Where(s => s.Status == status);

        var total = await rows.CountAsync(ct);

        // Tekshiruv kutayotganlar tepada va ENG UZOQ kutgani birinchi;
        // keyin tekshirilganlar — eng yangisi birinchi.
        var items = await rows
            .OrderBy(s => s.Status)
            .ThenBy(s => s.Status == SubmissionStatus.Submitted ? (DateTimeOffset?)s.SubmittedAt : null)
            .ThenByDescending(s => s.GradedAt)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SelfStudySubmissionRowDto(
                s.Id,
                s.AssignmentId,
                s.Assignment!.Title,
                s.Assignment.ModuleLesson == null ? null : s.Assignment.ModuleLesson.Name,
                s.StudentId,
                s.Student!.FullName,
                db.SelfStudyCuratorAssignments
                    .Where(a => a.StudentId == s.StudentId && a.EndedAt == null)
                    .Select(a => a.Curator!.FullName)
                    .FirstOrDefault(),
                s.Status,
                s.Score,
                s.Assignment.MaxScore,
                s.SubmittedAt,
                s.GradedAt,
                db.Users.Where(u => u.Id == s.GradedById).Select(u => u.FullName).FirstOrDefault(),
                s.IsLate,
                s.AttemptNumber))
            .ToListAsync(ct);

        return new PagedResult<SelfStudySubmissionRowDto>(items, page, pageSize, total);
    }

    // ================================================================ yozishmalar

    public async Task<PagedResult<SelfStudyChatRowDto>> ListChatsAsync(
        SelfStudyChatQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureManagerAsync(actorId, ct);
        var (page, pageSize) = Paging(query.Page, query.PageSize);

        var students = ApplySearch(SelfStudyStudents(), query.Search);
        var pairs = await ChatPairsAsync(students, query.CuratorId, ct);

        IEnumerable<ChatPair> filtered = pairs;
        if (query.WaitingOnly)
            filtered = filtered.Where(p => p.WaitingSince is not null);

        // Javob kutayotganlar tepada (eng uzoq kutgani birinchi), keyin — eng yangi yozishma.
        var ordered = filtered
            .OrderBy(p => p.WaitingSince is null)
            .ThenBy(p => p.WaitingSince ?? DateTimeOffset.MaxValue)
            .ThenByDescending(p => p.LastMessageAt)
            .ToList();

        var pageRows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var userIds = pageRows.SelectMany(p => new[] { p.StudentId, p.StaffId }).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var pageStudentIds = pageRows.Select(p => p.StudentId).Distinct().ToList();
        var currentCurator = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.EndedAt == null && pageStudentIds.Contains(a.StudentId))
            .ToDictionaryAsync(a => a.StudentId, a => a.CuratorId, ct);

        var lastIds = pageRows.Select(p => p.LastMessageId).ToList();
        var last = await db.DirectMessages.AsNoTracking()
            .Include(m => m.Attachments)
            .Where(m => lastIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var items = pageRows.ConvertAll(p =>
        {
            var message = last[p.LastMessageId];
            return new SelfStudyChatRowDto(
                p.StudentId,
                names.GetValueOrDefault(p.StudentId) ?? "?",
                p.StaffId,
                names.GetValueOrDefault(p.StaffId) ?? "?",
                currentCurator.TryGetValue(p.StudentId, out var c) && c == p.StaffId,
                p.MessageCount,
                p.LastMessageAt,
                Preview(message),
                message.SenderId == p.StudentId,
                p.UnreadByStaff,
                p.WaitingSince);
        });

        return new PagedResult<SelfStudyChatRowDto>(items, page, pageSize, ordered.Count);
    }

    public async Task<MessagePageDto> ReadChatAsync(
        long studentId, long staffId, long? beforeId, int take, long actorId, CancellationToken ct = default)
    {
        var actor = await EnsureManagerAsync(actorId, ct);
        await EnsureSelfStudyPairAsync(studentId, staffId, ct);

        // 🔐 AUDIT — O'QISHDAN OLDIN: yozilmasa yozishma ham ochilmaydi.
        //    Faqat BIRINCHI sahifa (eskiroqlarni varaqlash — o'sha ochilishning davomi).
        if (beforeId is null)
        {
            db.DirectMessageAccessAudits.Add(
                DirectMessageAccessAudit.Thread(actor.Id, studentId, staffId, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }

        return await messages.ReadThreadForOversightAsync(studentId, staffId, beforeId, take, ct);
    }

    public async Task<Courses.Services.LessonAssetDownload> OpenChatAttachmentAsync(
        long attachmentId, string? rangeHeader, long actorId, CancellationToken ct = default)
    {
        var actor = await EnsureManagerAsync(actorId, ct);
        var (studentId, staffId) = await messages.GetAttachmentPairAsync(attachmentId, ct);
        await EnsureSelfStudyPairAsync(studentId, staffId, ct);

        // `Range` so'rovlari (ovozni oldinga surish) bitta faylga bir necha
        // marta keladi — har biri audit qatori bo'lmasin: faqat to'liq yoki
        // boshidan so'ralgan ochilish yoziladi.
        if (string.IsNullOrEmpty(rangeHeader) || rangeHeader.StartsWith("bytes=0-", StringComparison.Ordinal))
        {
            db.DirectMessageAccessAudits.Add(DirectMessageAccessAudit.Attachment(
                actor.Id, studentId, staffId, attachmentId, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }

        var opened = await messages.OpenAttachmentForOversightAsync(attachmentId, rangeHeader, ct);
        return opened.Download;
    }

    public async Task<IReadOnlyList<ChatAccessLogEntryDto>> ListChatAccessAsync(
        long studentId, long staffId, long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);
        await EnsureSelfStudyPairAsync(studentId, staffId, ct);

        return await db.DirectMessageAccessAudits.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.StaffId == staffId)
            .OrderByDescending(a => a.At)
            .Take(50)
            .Select(a => new ChatAccessLogEntryDto(
                db.Users.Where(u => u.Id == a.ViewerId).Select(u => u.FullName).FirstOrDefault() ?? "?",
                a.Kind,
                a.At))
            .ToListAsync(ct);
    }

    // ================================================================ yordamchilar

    /// <summary>
    /// ★ "MUSTAQIL O'QUVCHI" TA'RIFI — panelning YAGONA manbasi: o'quvchi,
    /// bekor qilinmagan obunasi bor va faol guruh a'zosi emas.
    /// </summary>
    private IQueryable<User> SelfStudyStudents() =>
        db.Users.AsNoTracking().Where(u =>
            u.Role == UserRole.Student
            && db.SubscriptionPeriods.Any(p => p.StudentId == u.Id && p.CancelledAt == null)
            && !db.GroupMembers.Any(m => m.StudentId == u.Id && m.Status == MemberStatus.Active));

    /// <summary>
    /// Yozishma nazoratga ochiqmi: o'quvchi tomoni Mustaqil o'quvchi bo'lishi
    /// SHART. Aks holda panel orqali guruh o'quvchisining yozishmasini ham
    /// o'qish yo'li ochilardi — qaror faqat Mustaqil uchun qabul qilingan.
    /// </summary>
    private async Task EnsureSelfStudyPairAsync(long studentId, long staffId, CancellationToken ct)
    {
        var allowed = await SelfStudyStudents().AnyAsync(u => u.Id == studentId, ct);
        if (!allowed)
            throw new NotFoundException("SelfStudyChat", studentId);

        var exists = await db.DirectMessages.AsNoTracking()
            .AnyAsync(m => m.StudentId == studentId && m.StaffId == staffId, ct);
        if (!exists)
            throw new NotFoundException("SelfStudyChat", staffId);
    }

    /// <summary>
    /// Yozishma juftliklari statistikasi. Mustaqil o'quvchilar soni kichik
    /// (har biriga bitta-ikkita juftlik), shuning uchun "javob kutyaptimi"
    /// hisobi xotirada — SQL'da oyna funksiyalarisiz aniq va o'qilishi oson.
    /// </summary>
    private async Task<List<ChatPair>> ChatPairsAsync(
        IQueryable<User> students, long? curatorId, CancellationToken ct)
    {
        var scope = db.DirectMessages.AsNoTracking().Where(m => students.Any(u => u.Id == m.StudentId));
        if (curatorId is { } staffId)
            scope = scope.Where(m => m.StaffId == staffId);

        var stats = await scope
            .GroupBy(m => new { m.StudentId, m.StaffId })
            .Select(g => new
            {
                g.Key.StudentId,
                g.Key.StaffId,
                Count = g.Count(),
                LastId = g.Max(m => m.Id),
                LastAt = g.Max(m => m.SentAt),
                LastStaffReplyId = g.Where(m => m.SenderId == m.StaffId).Max(m => (long?)m.Id),
                Unread = g.Count(m => m.SenderId == m.StudentId && !m.ReadByStaff),
            })
            .ToListAsync(ct);

        if (stats.Count == 0) return [];

        // Javobsiz o'quvchi xabarlarining ENG ESKISI (oxirgi xodim javobidan keyin).
        var waiting = await scope
            .Where(m => m.SenderId == m.StudentId)
            .Where(m => !scope.Any(r =>
                r.StudentId == m.StudentId && r.StaffId == m.StaffId
                && r.SenderId == r.StaffId && r.Id > m.Id))
            .GroupBy(m => new { m.StudentId, m.StaffId })
            .Select(g => new { g.Key.StudentId, g.Key.StaffId, Since = g.Min(m => m.SentAt) })
            .ToListAsync(ct);

        var waitingByPair = waiting.ToDictionary(w => (w.StudentId, w.StaffId), w => w.Since);

        return stats.ConvertAll(s => new ChatPair(
            s.StudentId,
            s.StaffId,
            s.Count,
            s.LastId,
            s.LastAt,
            s.Unread,
            waitingByPair.TryGetValue((s.StudentId, s.StaffId), out var since) ? since : null));
    }

    private async Task<Dictionary<long, StudentDetail>> StudentDetailsAsync(
        List<long> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        var periods = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => ids.Contains(p.StudentId) && p.CancelledAt == null)
            .Select(p => new { p.StudentId, p.StartsAt, p.EndsAt, p.CourseId, PlanName = p.Plan == null ? null : p.Plan.Name })
            .ToListAsync(ct);

        var assignments = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.StudentId) && a.EndedAt == null)
            .Select(a => new
            {
                a.StudentId,
                a.CuratorId,
                CuratorName = a.Curator!.FullName,
                a.BackupCuratorId,
                BackupName = a.BackupCurator == null ? null : a.BackupCurator.FullName,
                a.AssignedAt,
            })
            .ToDictionaryAsync(a => a.StudentId, ct);

        var mastered = await db.LessonMasteries.AsNoTracking()
            .Where(m => ids.Contains(m.StudentId) && m.Status == MasteryStatus.Mastered)
            .GroupBy(m => m.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.StudentId, g => g.Count, ct);

        var pending = await db.Submissions.AsNoTracking()
            .Where(s => ids.Contains(s.StudentId) && s.Status == SubmissionStatus.Submitted)
            .GroupBy(s => s.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.StudentId, g => g.Count, ct);

        var courseIds = periods.Select(p => p.CourseId).Distinct().ToList();
        var lessonsByCourse = await db.ModuleLessons.AsNoTracking()
            .Where(l => courseIds.Contains(l.Module!.CourseId))
            .GroupBy(l => l.Module!.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CourseId, g => g.Count, ct);

        var students = db.Users.AsNoTracking().Where(u => ids.Contains(u.Id));
        var pairs = await ChatPairsAsync(students, curatorId: null, ct);

        var result = new Dictionary<long, StudentDetail>();
        foreach (var id in ids)
        {
            var own = periods.Where(p => p.StudentId == id).ToList();
            var current = own.Where(p => p.StartsAt <= now && now < p.EndsAt).MaxBy(p => p.EndsAt);
            var latest = current ?? own.MaxBy(p => p.EndsAt);

            var state = current is not null ? SelfStudySubscriptionState.Active
                : own.Any(p => p.StartsAt > now) ? SelfStudySubscriptionState.Upcoming
                : SelfStudySubscriptionState.Expired;

            assignments.TryGetValue(id, out var a);
            var ownPairs = pairs.Where(p => p.StudentId == id).ToList();

            result[id] = new StudentDetail(
                latest?.PlanName,
                state,
                own.Count == 0 ? null : own.Max(p => p.EndsAt),
                a?.CuratorId,
                a?.CuratorName,
                a?.BackupCuratorId,
                a?.BackupName,
                a?.AssignedAt,
                mastered.GetValueOrDefault(id),
                latest is null ? 0 : lessonsByCourse.GetValueOrDefault(latest.CourseId),
                pending.GetValueOrDefault(id),
                ownPairs.Count == 0 ? null : ownPairs.Max(p => p.LastMessageAt),
                ownPairs.Where(p => p.WaitingSince is not null).Select(p => p.WaitingSince).Min());
        }

        return result;
    }

    /// <summary>Foydalanuvchilar qidiruvi bilan AYNI qoida (<c>UserService.ApplySearch</c>).</summary>
    private static IQueryable<User> ApplySearch(IQueryable<User> rows, string? search)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return rows;

        if (trimmed.Length < MinSearchLength)
            throw Invalid("search", $"Qidiruv uchun kamida {MinSearchLength} belgi kiriting.");

        var term = "%" + Escape(trimmed.ToLowerInvariant()) + "%";
        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

#pragma warning disable CA1304, CA1311
        if (digits.Length < MinSearchLength)
            return rows.Where(u => EF.Functions.Like(u.FullName.ToLower(), term));

        var phoneTerm = "%" + digits + "%";
        return rows.Where(u =>
            EF.Functions.Like(u.FullName.ToLower(), term) ||
            (u.PhoneNormalized != null && EF.Functions.Like(u.PhoneNormalized, phoneTerm)));
#pragma warning restore CA1304, CA1311
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static string Preview(DirectMessage message)
    {
        if (message.Body.Length > 0)
            return message.Body.Length <= PreviewLength ? message.Body : message.Body[..PreviewLength];

        var count = message.Attachments.Count;
        return count == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"📎 {count} ta fayl");
    }

    private static (int Page, int PageSize) Paging(int page, int pageSize) =>
        (Math.Max(1, page), pageSize <= 0 ? 20 : Math.Min(pageSize, MaxPageSize));

    private static string FirstMessage(ValidationException ex) =>
        ex.Errors.Values.SelectMany(v => v).FirstOrDefault() ?? ex.Message;

    private async Task<User> EnsureManagerAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive || actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Mustaqil paneli faqat o'quv bo'limi va administrator uchun.");

        return actor;
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private sealed record ChatPair(
        long StudentId,
        long StaffId,
        int MessageCount,
        long LastMessageId,
        DateTimeOffset LastMessageAt,
        int UnreadByStaff,
        DateTimeOffset? WaitingSince);

    private sealed record StudentDetail(
        string? PlanName,
        SelfStudySubscriptionState State,
        DateTimeOffset? EndsAt,
        long? CuratorId,
        string? CuratorName,
        long? BackupId,
        string? BackupName,
        DateTimeOffset? AssignedAt,
        int Mastered,
        int TotalLessons,
        int Pending,
        DateTimeOffset? LastMessageAt,
        DateTimeOffset? WaitingSince);
}
