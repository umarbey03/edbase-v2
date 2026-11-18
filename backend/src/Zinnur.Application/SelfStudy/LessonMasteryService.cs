using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Subscriptions;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.SelfStudy;

/// <summary>
/// ========================================================================
/// «YAXSHI O'ZLASHTIRDI» — QAT'IY QOIDA (Mustaqil, 2026-11-18)
/// ========================================================================
///
/// Mustaqil o'quvchida N-dars faqat kurator (N−1)-darsni «o'zlashtirdi»
/// deb belgilagandan keyin ochiladi. Qoidaning O'ZI <c>LessonGate</c> da;
/// bu servis qarorni yozadi, kim yoza olishini tekshiradi va o'quvchiga
/// xabar beradi.
///
/// KIM NIMA QILA OLADI:
///   • asosiy va zaxira kurator — taxtani ko'radi, qaror chiqaradi;
///   • administrator — qaror chiqaradi VA darsni istisno sifatida ochadi
///     (sabab MAJBURIY, jurnalga yoziladi);
///   • o'quv bo'limi — faqat ko'radi (loyiha egasi: istisno faqat admin).
/// </summary>
public interface ILessonMasteryService
{
    Task<StudentLessonBoardDto> GetBoardAsync(long studentId, long actorId, CancellationToken ct = default);

    Task<StudentLessonBoardDto> DecideAsync(
        long studentId, long lessonId, DecideMasteryRequest request, long actorId, CancellationToken ct = default);

    Task<StudentLessonBoardDto> AdminUnlockAsync(
        long studentId, long lessonId, AdminLessonUnlockRequest request, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<MasteryEventDto>> ListEventsAsync(long studentId, long actorId, CancellationToken ct = default);

    /// <summary>O'quvchi: o'z darslari bo'yicha kurator qarorlari.</summary>
    Task<IReadOnlyList<MyLessonMasteryDto>> ListMineAsync(long studentId, CancellationToken ct = default);
}

public sealed class LessonMasteryService(
    IApplicationDbContext db,
    IGatingService gating,
    ISubscriptionAccess subscriptions,
    INotificationOutbox outbox,
    INotificationNotifier notifier,
    TimeProvider clock) : ILessonMasteryService
{
    /// <summary>Istisno sababining eng qisqa uzunligi — "ok" kabi bo'sh sabab o'tmasin.</summary>
    public const int MinReasonLength = 5;

    private enum Access
    {
        Read,
        Decide,
        Admin,
    }

    // ================================================================ o'qish

    public async Task<StudentLessonBoardDto> GetBoardAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        var (_, access) = await AuthorizeAsync(studentId, actorId, needWrite: false, ct);
        return await BuildBoardAsync(studentId, access, ct);
    }

    public async Task<IReadOnlyList<MasteryEventDto>> ListEventsAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        await AuthorizeAsync(studentId, actorId, needWrite: false, ct);

        return await db.LessonMasteryEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId)
            .OrderByDescending(e => e.At)
            .ThenByDescending(e => e.Id)
            .Take(200)
            .Select(e => new MasteryEventDto(
                e.Id,
                e.ModuleLessonId,
                db.ModuleLessons.Where(l => l.Id == e.ModuleLessonId).Select(l => l.Name).FirstOrDefault() ?? "",
                e.Kind,
                e.Note,
                db.Users.Where(u => u.Id == e.ActorId).Select(u => u.FullName).FirstOrDefault() ?? "",
                e.At))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MyLessonMasteryDto>> ListMineAsync(
        long studentId, CancellationToken ct = default) =>
        await db.LessonMasteries.AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .OrderBy(m => m.ModuleLessonId)
            .Select(m => new MyLessonMasteryDto(m.ModuleLessonId, m.Status, m.Note, m.DecidedAt))
            .ToListAsync(ct);

    // ================================================================ qaror

    public async Task<StudentLessonBoardDto> DecideAsync(
        long studentId, long lessonId, DecideMasteryRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Status))
            throw Invalid("status", "Qaror turi noma'lum.");

        var (actor, access) = await AuthorizeAsync(studentId, actorId, needWrite: true, ct);

        var gate = await gating.GetLessonGateAsync(studentId, lessonId, ct);
        if (gate.LockReason == LessonLockReason.NotInCourse)
            throw new NotFoundException(nameof(ModuleLesson), lessonId);
        if (!gate.RequiresMastery)
            throw new ConflictException("Bu qoida faqat Mustaqil o'quvchilarga tegishli.");
        if (!gate.Unlocked)
        {
            throw new ConflictException(gate.LockReason == LessonLockReason.SubscriptionExpired
                ? "O'quvchining obunasi tugagan — qaror obuna uzaytirilgach chiqariladi."
                : "Bu dars o'quvchiga hali ochilmagan — avval oldingi darsga qaror chiqaring.");
        }

        if (request.Status == MasteryStatus.Mastered)
        {
            var missing = MissingWork(gate);
            if (missing.Count > 0)
            {
                throw new ConflictException(
                    "«O'zlashtirdi» deb bo'lmaydi — o'quvchi hali bajarmagan: "
                    + string.Join(", ", missing) + ".");
            }
        }

        var now = clock.GetUtcNow();
        var mastery = await db.LessonMasteries.AsTracking()
            .FirstOrDefaultAsync(m => m.StudentId == studentId && m.ModuleLessonId == lessonId, ct);

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        // Aynan shu qaror qayta yuborildi — jurnal va xabar takrorlanmaydi.
        if (mastery is not null && mastery.Status == request.Status && mastery.Note == note)
            return await BuildBoardAsync(studentId, access, ct);

        try
        {
            if (mastery is null)
            {
                mastery = LessonMastery.Create(studentId, lessonId, request.Status, note, actor.Id, now);
                db.LessonMasteries.Add(mastery);
            }
            else
            {
                mastery.Decide(request.Status, note, actor.Id, now);
            }
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            throw Invalid("note", ex.Message);
        }

        db.LessonMasteryEvents.Add(LessonMasteryEvent.Record(
            studentId,
            lessonId,
            request.Status == MasteryStatus.Mastered ? MasteryEventKind.Mastered : MasteryEventKind.NeedsWork,
            note,
            actor.Id,
            now));

        await db.SaveChangesAsync(ct);
        await gating.InvalidateAsync(studentId, ct);

        await NotifyStudentAsync(studentId, lessonId, mastery, ct);

        return await BuildBoardAsync(studentId, access, ct);
    }

    public async Task<StudentLessonBoardDto> AdminUnlockAsync(
        long studentId, long lessonId, AdminLessonUnlockRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (actor, access) = await AuthorizeAsync(studentId, actorId, needWrite: true, ct);

        if (access != Access.Admin)
            throw new ForbiddenException("Darsni istisno sifatida faqat administrator ochadi.");

        var reason = request.Reason?.Trim();
        if (request.Unlocked && (reason is null || reason.Length < MinReasonLength))
        {
            throw Invalid(
                "reason",
                string.Create(CultureInfo.InvariantCulture,
                    $"Sababni yozing (kamida {MinReasonLength} belgi) — u jurnalda saqlanadi."));
        }
        if (reason is { Length: > LessonMasteryEvent.MaxNoteLength })
            throw Invalid("reason", $"Sabab {LessonMasteryEvent.MaxNoteLength} belgidan oshmasin.");

        var gate = await gating.GetLessonGateAsync(studentId, lessonId, ct);
        if (gate.LockReason == LessonLockReason.NotInCourse)
            throw new NotFoundException(nameof(ModuleLesson), lessonId);
        if (!gate.RequiresMastery)
            throw new ConflictException("Bu qoida faqat Mustaqil o'quvchilarga tegishli.");

        // ★ Jurnal yozuvi AYNI SaveChanges'da ketadi: `SetOverrideAsync`
        //   o'zi saqlaydi va kuzatilayotgan hamma narsa birga yoziladi.
        db.LessonMasteryEvents.Add(LessonMasteryEvent.Record(
            studentId,
            lessonId,
            request.Unlocked ? MasteryEventKind.AdminUnlocked : MasteryEventKind.AdminUnlockRevoked,
            reason,
            actor.Id,
            clock.GetUtcNow()));

        await gating.SetOverrideAsync(studentId, lessonId, request.Unlocked, reason, actor.Id, ct);

        return await BuildBoardAsync(studentId, access, ct);
    }

    // ================================================================ taxta

    private async Task<StudentLessonBoardDto> BuildBoardAsync(long studentId, Access access, CancellationToken ct)
    {
        var studentName = await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId)
            .Select(u => u.FullName)
            .FirstAsync(ct);

        var gate = await gating.GetCourseGateAsync(studentId, ct);
        var active = await subscriptions.HasActiveAsync(studentId, ct);
        var canDecide = access is Access.Decide or Access.Admin;
        var canOverride = access == Access.Admin;

        if (gate.CourseId is not { } courseId)
            return new StudentLessonBoardDto(studentId, studentName, null, active, canDecide, canOverride, []);

        var courseName = await db.Courses.AsNoTracking()
            .Where(c => c.Id == courseId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        var meta = await db.ModuleLessons.AsNoTracking()
            .Where(l => l.Module!.CourseId == courseId)
            .Select(l => new { l.Id, l.Name, ModuleName = l.Module!.Name })
            .ToDictionaryAsync(l => l.Id, ct);

        var lessonIds = meta.Keys.ToList();

        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.ModuleLessonId != null && lessonIds.Contains(a.ModuleLessonId.Value))
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                LessonId = a.ModuleLessonId!.Value,
                a.Id,
                a.Title,
                a.MaxScore,
                Submission = db.Submissions
                    .Where(s => s.AssignmentId == a.Id && s.StudentId == studentId)
                    .Select(s => new { s.Id, s.Status, s.Score, s.SubmittedAt, s.AttemptNumber })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var tests = await db.TestAttempts.AsNoTracking()
            .Where(t => t.StudentId == studentId
                     && t.Status == AttemptStatus.Submitted
                     && t.Test!.ModuleLessonId != null
                     && lessonIds.Contains(t.Test.ModuleLessonId.Value))
            .Select(t => new { LessonId = t.Test!.ModuleLessonId!.Value, t.Score, t.MaxScore })
            .ToListAsync(ct);

        var masteries = await db.LessonMasteries.AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .Select(m => new
            {
                m.ModuleLessonId,
                m.Note,
                m.DecidedAt,
                DecidedBy = db.Users.Where(u => u.Id == m.DecidedById).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToDictionaryAsync(m => m.ModuleLessonId, ct);

        var overrides = await db.LessonProgress.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.UnlockedOverride)
            .Select(p => new { p.ModuleLessonId, p.OverrideReason })
            .ToDictionaryAsync(p => p.ModuleLessonId, p => p.OverrideReason, ct);

        var rows = new List<LessonBoardRowDto>(gate.Lessons.Count);
        foreach (var lesson in gate.Lessons)
        {
            if (!meta.TryGetValue(lesson.LessonId, out var info)) continue;

            var assignment = assignments.Find(a => a.LessonId == lesson.LessonId);
            var best = tests
                .Where(t => t.LessonId == lesson.LessonId && t.Score is not null && t.MaxScore is > 0)
                .Select(t => (decimal?)Math.Round(t.Score!.Value / t.MaxScore!.Value * 100m, 1))
                .DefaultIfEmpty(null)
                .Max();
            masteries.TryGetValue(lesson.LessonId, out var decision);
            overrides.TryGetValue(lesson.LessonId, out var overrideReason);

            rows.Add(new LessonBoardRowDto(
                lesson.LessonId,
                lesson.Index,
                info.ModuleName,
                info.Name,
                lesson.Unlocked,
                lesson.LockReason,
                MissingWork(lesson).Count == 0,
                lesson.HasVideo,
                lesson.VideoWatched,
                lesson.HasAssignment,
                assignment is null
                    ? null
                    : new BoardAssignmentDto(
                        assignment.Id,
                        assignment.Title,
                        assignment.MaxScore,
                        assignment.Submission?.Id,
                        assignment.Submission?.Status,
                        assignment.Submission?.Score,
                        assignment.Submission?.SubmittedAt,
                        assignment.Submission?.AttemptNumber),
                lesson.HasTest,
                lesson.TestTaken,
                best,
                lesson.Mastery,
                decision?.Note,
                decision?.DecidedBy,
                decision?.DecidedAt,
                lesson.UnlockedOverride,
                overrideReason));
        }

        return new StudentLessonBoardDto(studentId, studentName, courseName, active, canDecide, canOverride, rows);
    }

    private static List<string> MissingWork(LessonGateDto gate)
    {
        var missing = new List<string>(3);
        if (gate.HasVideo && !gate.VideoWatched) missing.Add("videoni ko'rmagan");
        if (gate.HasAssignment && !gate.AssignmentSubmitted) missing.Add("vazifani topshirmagan");
        if (gate.HasTest && !gate.TestTaken) missing.Add("testni yechmagan");
        return missing;
    }

    // ================================================================ xabar

    private async Task NotifyStudentAsync(
        long studentId, long lessonId, LessonMastery mastery, CancellationToken ct)
    {
        var mastered = mastery.Status == MasteryStatus.Mastered;

        // Keyingi dars — invalidatsiyadan KEYINGI daraxtdan: «ochildi» faqat
        // haqiqatan ochilgan bo'lsa aytiladi.
        string? nextName = null;
        if (mastered)
        {
            var course = await gating.GetCourseGateAsync(studentId, ct);
            var index = course.Lessons.ToList().FindIndex(l => l.LessonId == lessonId);
            if (index >= 0 && index + 1 < course.Lessons.Count && course.Lessons[index + 1].Unlocked)
            {
                var nextId = course.Lessons[index + 1].LessonId;
                nextName = await db.ModuleLessons.AsNoTracking()
                    .Where(l => l.Id == nextId).Select(l => l.Name).FirstOrDefaultAsync(ct);
            }
        }

        var lessonName = await db.ModuleLessons.AsNoTracking()
            .Where(l => l.Id == lessonId).Select(l => l.Name).FirstOrDefaultAsync(ct);
        var telegramId = await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId).Select(u => u.TelegramId).FirstOrDefaultAsync(ct);

        var now = clock.GetUtcNow();
        var row = Notification.Create(
            studentId,
            NotificationKind.LessonMasteryDecided,
            NotificationTemplates.LessonMasteryTitle(mastered),
            NotificationTemplates.LessonMasteryBody(lessonName, nextName, mastered, mastery.Note),
            lessonId,
            now);
        db.Notifications.Add(row);

        if (telegramId is { } chatId)
        {
            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = studentId,
                    RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.LessonMasteryDecided,
                    Body = TelegramTemplates.LessonMasteryText(lessonName, nextName, mastered, mastery.Note),
                    // Har qaror — yangi hodisa (vaqt bilan), qayta yuborish esa
                    // yuqorida qaytariladi, ya'ni takror xabar ketmaydi.
                    IdempotencyKey = string.Create(
                        CultureInfo.InvariantCulture,
                        $"lesson_mastery:{studentId}:{lessonId}:{mastery.DecidedAt.UtcTicks}"),
                },
                ct);
        }

        await db.SaveChangesAsync(ct);

        // Realtime — kommitdan KEYIN (AssignmentService.GradeAsync dagi AYNI qoida).
        await notifier.NotificationCreatedAsync(studentId, NotificationFeed.ToDto(row), ct);
    }

    // ================================================================ ruxsat

    private async Task<(User Actor, Access Access)> AuthorizeAsync(
        long studentId, long actorId, bool needWrite, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);
        if (!actor.IsActive || actor.Role == UserRole.Student)
            throw new ForbiddenException("Bu bo'lim faqat xodimlar uchun.");

        var student = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);
        if (student.Role != UserRole.Student)
            throw new NotFoundException(nameof(User), studentId);

        var inGroup = await db.GroupMembers.AsNoTracking()
            .AnyAsync(m => m.StudentId == studentId && m.Status == MemberStatus.Active, ct);
        if (inGroup)
            throw new ConflictException("O'quvchi guruhda — «o'zlashtirdi» qoidasi faqat Mustaqil uchun.");

        Access access;
        if (actor.Role == UserRole.Admin)
        {
            access = Access.Admin;
        }
        else
        {
            var isCurator = await db.SelfStudyCuratorAssignments.AsNoTracking()
                .Where(SelfStudyCuratorAssignment.ActiveFor(actor.Id))
                .AnyAsync(a => a.StudentId == studentId, ct);

            if (isCurator)
                access = Access.Decide;
            else if (actor.Role == UserRole.Academic)
                access = Access.Read;
            else
                throw new ForbiddenException("Bu o'quvchi sizga biriktirilmagan.");
        }

        if (needWrite && access == Access.Read)
            throw new ForbiddenException("Qarorni o'quvchining kuratori yoki administrator chiqaradi.");

        return (actor, access);
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
