using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Progress.Services;

/// <summary>Ball daftarini manba jadvallari bilan tenglashtiradi.</summary>
public interface IPointsSyncService
{
    /// <returns>Qo'shilgan, o'zgargan va o'chirilgan yozuvlar soni.</returns>
    Task<int> SyncAsync(CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// BALL SINXRONLASH (2026-10-23)
/// ════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN HAR YOZUV JOYIDA EMAS, DAVRIY TENGLASHTIRISH: ball besh
///   xil manbadan keladi (davomat, dars bahosi, vazifa, test, video) va
///   ularning har biri bir nechta joyda o'zgaradi (davomat — dars yakuni,
///   qo'lda tuzatish, uzrli deb belgilash; baho — qo'yish, o'zgartirish,
///   o'chirish). Har joyga "ball ham yoz" qo'shish birorta yo'lni
///   unutishga olib kelardi. Bu yerda esa oxirgi <see cref="Window"/>
///   ichidagi manbalardan "bo'lishi kerak bo'lgan" yozuvlar yasaladi va
///   daftar ularga TENGLASHTIRILADI: yo'q bo'lsa qo'shiladi, farq qilsa
///   yangilanadi, manbasi yo'qolgan bo'lsa o'chiriladi. Eski platforma
///   ham xuddi shu naqshda (2 daqiqalik tick) ishlagan.
///
/// ★ KELMAGANLIK: darsga umuman kirmagan o'quvchi uchun davomat qatori
///   yaratilmaydi, shuning uchun "kutilgan" o'quvchilar dars vaqtidagi
///   guruh a'zoligidan topiladi. PAUZADAGI a'zo kutilmaydi — u ball
///   yo'qotmaydi va ketma-ketligi ham uzilmaydi.
///
/// ★ KETMA-KETLIK tarixi <see cref="StreakHistory"/> bo'yicha hisoblanadi
///   (bonus oynadan oldingi darslarga ham bog'liq), bonus esa faqat
///   oyna ichidagi darslarga yoziladi.
/// </summary>
public sealed class PointsSyncService(
    IApplicationDbContext db,
    ISettingsResolver settings,
    INotificationOutbox outbox,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock,
    ILogger<PointsSyncService> logger) : IPointsSyncService
{
    /// <summary>Tenglashtiriladigan oyna: o'tgan oyning oxirgi kunlari ham tuzatilsin.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(40);

    /// <summary>Ketma-ketlik uchun tarix (haftada 2-3 dars → ~50 dars).</summary>
    public static readonly TimeSpan StreakHistory = TimeSpan.FromDays(120);

    /// <summary>Faqat shu muddat ichida tugagan darslar uchun Telegram xabari.</summary>
    public static readonly TimeSpan MessageWindow = TimeSpan.FromHours(24);

    private const int QuietFromHour = 22;
    private const int QuietUntilHour = 8;

    private static readonly SettingDefinition EnabledSetting = Definition(SettingsRegistry.Keys.PointsEnabled);
    private static readonly SettingDefinition MessageSetting =
        Definition(SettingsRegistry.Keys.PointsLessonMessageEnabled);

    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var resolved = await settings.ResolveManyAsync([EnabledSetting, MessageSetting], ct).ConfigureAwait(false);
        if (!(SettingValueParser.TryReadBool(resolved[0].Value, out var enabled) && enabled))
            return 0;
        var messagesOn = SettingValueParser.TryReadBool(resolved[1].Value, out var flag) && flag;

        var now = clock.GetUtcNow();
        var from = now - Window;

        var desired = new Dictionary<(long StudentId, string Ref), DesiredPoint>();
        var lessons = await AddAttendanceAsync(desired, from, now, ct).ConfigureAwait(false);
        await AddLessonGradesAsync(desired, from, ct).ConfigureAwait(false);
        await AddAssignmentsAsync(desired, from, ct).ConfigureAwait(false);
        await AddTestsAsync(desired, from, ct).ConfigureAwait(false);
        await AddVideosAsync(desired, from, ct).ConfigureAwait(false);
        await AddInteractiveUnitsAsync(desired, from, ct).ConfigureAwait(false);
        await AddInteractiveTestsAsync(desired, from, ct).ConfigureAwait(false);

        var existing = await db.StudentPoints
            .Where(p => p.OccurredAt >= from)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Manba vaqti oynadan chiqib ketgan yozuv (masalan, eski vazifa qayta
        // tekshirildi) — unikal kalit to'qnashmasin, uni ham yangilaymiz.
        var known = existing.Select(p => (p.StudentId, p.Ref)).ToHashSet();
        var missingStudents = desired.Keys.Where(k => !known.Contains(k)).Select(k => k.StudentId).Distinct().ToList();
        var missingRefs = desired.Keys.Where(k => !known.Contains(k)).Select(k => k.Ref).Distinct().ToList();
        if (missingRefs.Count > 0)
        {
            var outside = await db.StudentPoints
                .Where(p => p.OccurredAt < from && missingStudents.Contains(p.StudentId) && missingRefs.Contains(p.Ref))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            existing.AddRange(outside.Where(p => desired.ContainsKey((p.StudentId, p.Ref))));
        }

        var byKey = existing.ToDictionary(p => (p.StudentId, p.Ref));
        var inserted = new List<StudentPoint>();
        var changes = 0;

        foreach (var (key, want) in desired)
        {
            if (byKey.TryGetValue(key, out var row))
            {
                if (row.Delta == want.Delta && row.Kind == want.Kind && row.OccurredAt == want.OccurredAt) continue;
                row.Delta = want.Delta;
                row.Kind = want.Kind;
                row.OccurredAt = want.OccurredAt;
                row.SessionId = want.SessionId;
                row.UpdatedAt = now;
                changes++;
                continue;
            }

            var created = new StudentPoint
            {
                StudentId = key.StudentId,
                Ref = key.Ref,
                Kind = want.Kind,
                Delta = want.Delta,
                OccurredAt = want.OccurredAt,
                SessionId = want.SessionId,
                CreatedAt = now,
            };
            db.StudentPoints.Add(created);
            inserted.Add(created);
            changes++;
        }

        foreach (var row in existing)
        {
            if (row.OccurredAt < from || desired.ContainsKey((row.StudentId, row.Ref))) continue;
            db.StudentPoints.Remove(row);
            changes++;
        }

        if (changes == 0) return 0;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        PointsLog.Synced(logger, inserted.Count, changes - inserted.Count);

        if (messagesOn)
            await EnqueueLessonMessagesAsync(inserted, desired, lessons, now, ct).ConfigureAwait(false);

        return changes;
    }

    // ================================================================ davomat

    /// <summary>
    /// Davomat va ketma-ketlik bonusi. Javob — oynadagi har bir (dars, o'quvchi)
    /// uchun ketma-ketlik qiymati (Telegram xabari uchun).
    /// </summary>
    private async Task<Dictionary<(long SessionId, long StudentId), LessonOutcome>> AddAttendanceAsync(
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired,
        DateTimeOffset from, DateTimeOffset now, CancellationToken ct)
    {
        var outcomes = new Dictionary<(long SessionId, long StudentId), LessonOutcome>();
        var historyFrom = now - StreakHistory;

        var sessions = await db.LiveSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Ended
                     && s.ScheduledStart >= historyFrom
                     && s.ScheduledStart <= now)
            .Select(s => new SessionRow(s.Id, s.GroupId, s.ScheduledStart, s.ScheduledEnd))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (sessions.Count == 0) return outcomes;

        List<long> groupIds = [.. sessions.Select(s => s.GroupId).Distinct()];
        List<long> sessionIds = [.. sessions.Select(s => s.Id)];

        var members = await db.GroupMembers.AsNoTracking()
            .Where(m => m.Student!.Role == UserRole.Student
                     && (groupIds.Contains(m.GroupId)
                         || (m.Group!.CuratorGroupId != null && groupIds.Contains(m.Group.CuratorGroupId.Value))))
            .Select(m => new MemberSpan(m.StudentId, m.GroupId, m.Group!.CuratorGroupId, m.Status, m.JoinedAt, m.LeftAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var attendance = await db.Attendances.AsNoTracking()
            .Where(a => sessionIds.Contains(a.SessionId))
            .Select(a => new AttendanceRow(a.SessionId, a.StudentId, a.Status, a.IsExcused))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var bySession = attendance.ToLookup(a => a.SessionId);
        var membersByGroup = members
            .SelectMany(m => m.CuratorGroupId is { } curator
                ? new[] { (GroupId: m.GroupId, Member: m), (GroupId: curator, Member: m) }
                : [(GroupId: m.GroupId, Member: m)])
            .ToLookup(x => x.GroupId, x => x.Member);

        // O'quvchi -> xronologik (dars, keldimi, uzrlimi)
        var timeline = new Dictionary<long, List<(SessionRow Session, bool Attended, bool Excused)>>();

        foreach (var session in sessions)
        {
            var rows = bySession[session.Id].ToDictionary(a => a.StudentId);
            var expected = membersByGroup[session.GroupId]
                .Where(m => m.ExpectedAt(session.ScheduledStart))
                .Select(m => m.StudentId)
                .ToHashSet();

            // Kirgan, lekin a'zo bo'lmagan o'quvchi (masalan, keyinroq chiqib ketgan) ham hisoblanadi.
            foreach (var row in rows.Values.Where(r => r.Status != AttendanceStatus.Absent))
                expected.Add(row.StudentId);

            foreach (var studentId in expected)
            {
                rows.TryGetValue(studentId, out var row);
                var attended = row is not null && row.Status != AttendanceStatus.Absent;
                var excused = !attended && row is { IsExcused: true };

                if (!timeline.TryGetValue(studentId, out var list))
                    timeline[studentId] = list = [];
                list.Add((session, attended, excused));
            }
        }

        foreach (var (studentId, list) in timeline)
        {
            var streak = 0;
            foreach (var (session, attended, excused) in list.OrderBy(x => x.Session.ScheduledStart).ThenBy(x => x.Session.Id))
            {
                streak = attended ? streak + 1 : 0;
                if (session.ScheduledStart < from) continue;

                var (kind, delta) = attended ? (PointKind.Present, PointRules.Present)
                    : excused ? (PointKind.Excused, PointRules.Excused)
                    : (PointKind.Absent, PointRules.Absent);

                desired[(studentId, Ref("att", session.Id))] =
                    new DesiredPoint(kind, delta, session.ScheduledStart, session.Id);

                var bonus = attended ? PointRules.StreakBonus(streak) : null;
                if (bonus is { } value)
                {
                    desired[(studentId, Ref("streak", session.Id))] =
                        new DesiredPoint(PointKind.StreakBonus, value, session.ScheduledStart, session.Id);
                }

                outcomes[(session.Id, studentId)] = new LessonOutcome(session, attended, streak, bonus);
            }
        }

        return outcomes;
    }

    // ================================================================ boshqa manbalar

    private async Task AddLessonGradesAsync(
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired, DateTimeOffset from, CancellationToken ct)
    {
        var grades = await db.LessonGrades.AsNoTracking()
            .Where(g => g.Session!.ScheduledStart >= from && g.Session.Status != SessionStatus.Cancelled)
            .Select(g => new { g.StudentId, g.SessionId, g.Score, g.MaxScore, g.Session!.ScheduledStart })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var g in grades)
        {
            var delta = PointRules.LessonGrade(g.Score, g.MaxScore ?? LessonGrade.DefaultMaxScore);
            if (delta == 0) continue;
            desired[(g.StudentId, Ref("grade", g.SessionId))] =
                new DesiredPoint(PointKind.LessonGrade, delta, g.ScheduledStart, g.SessionId);
        }
    }

    private async Task AddAssignmentsAsync(
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired, DateTimeOffset from, CancellationToken ct)
    {
        var graded = await db.Submissions.AsNoTracking()
            .Where(s => s.Status == SubmissionStatus.Graded && s.Score != null && s.GradedAt >= from)
            .Select(s => new
            {
                s.StudentId,
                s.AssignmentId,
                s.AttemptNumber,
                Score = s.Score!.Value,
                s.Assignment!.MaxScore,
                GradedAt = s.GradedAt!.Value,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Bir nechta urinish bo'lsa — oxirgisi hisoblanadi.
        foreach (var s in graded.GroupBy(x => (x.StudentId, x.AssignmentId)).Select(g => g.MaxBy(x => x.AttemptNumber)!))
        {
            var delta = PointRules.Assignment(s.Score, s.MaxScore);
            if (delta == 0) continue;
            desired[(s.StudentId, Ref("asg", s.AssignmentId))] =
                new DesiredPoint(PointKind.Assignment, delta, s.GradedAt, null);
        }
    }

    private async Task AddTestsAsync(
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired, DateTimeOffset from, CancellationToken ct)
    {
        var attempts = await db.TestAttempts.AsNoTracking()
            .Where(a => a.Status == AttemptStatus.Submitted
                     && a.SubmittedAt >= from
                     && a.MaxScore != null
                     && a.MaxScore > 0)
            .Select(a => new
            {
                a.StudentId,
                a.TestId,
                Score = a.Score ?? 0,
                MaxScore = a.MaxScore!.Value,
                SubmittedAt = a.SubmittedAt!.Value,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var a in attempts)
        {
            var delta = PointRules.Test(a.Score, a.MaxScore);
            if (delta == 0) continue;
            desired[(a.StudentId, Ref("test", a.TestId))] =
                new DesiredPoint(PointKind.Test, delta, a.SubmittedAt, null);
        }
    }

    private async Task AddVideosAsync(
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired, DateTimeOffset from, CancellationToken ct)
    {
        var watched = await db.LessonProgress.AsNoTracking()
            .Where(p => p.VideoWatchedAt != null && p.VideoWatchedAt >= from)
            .Select(p => new { p.StudentId, p.ModuleLessonId, WatchedAt = p.VideoWatchedAt!.Value })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var w in watched)
        {
            desired[(w.StudentId, Ref("video", w.ModuleLessonId))] =
                new DesiredPoint(PointKind.VideoWatched, PointRules.VideoWatched, w.WatchedAt, null);
        }
    }

    // ================================================================ interaktiv darslik

    /// <summary>
    /// Darslik birligi yakunlandi (2026-10-29, egasi qarori 3): barcha
    /// mashqlarning BIRINCHI urinishi foizidan 0–5 ball, birlikka bir marta
    /// (<c>Ref = iunit:{unitId}</c>). Mashqsiz birlik ball bermaydi.
    /// </summary>
    private async Task AddInteractiveUnitsAsync(
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired, DateTimeOffset from, CancellationToken ct)
    {
        var completed = await db.InteractiveUnitProgress.AsNoTracking()
            .Where(p => p.CompletedAt != null && p.CompletedAt >= from)
            .Select(p => new { p.StudentId, p.UnitId, p.ExerciseRight, p.ExerciseTotal, CompletedAt = p.CompletedAt!.Value })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var p in completed)
        {
            var delta = PointRules.ToFive(p.ExerciseRight, p.ExerciseTotal);
            if (delta == 0) continue;
            desired[(p.StudentId, Ref("iunit", p.UnitId))] =
                new DesiredPoint(PointKind.InteractiveUnit, delta, p.CompletedAt, null);
        }
    }

    /// <summary>
    /// Darslik mini-testi (2026-10-29): faqat BIRINCHI topshiriq, mavjud test
    /// shkalasi bilan 0–10 (<c>Ref = itest:{unitId}</c>).
    /// </summary>
    private async Task AddInteractiveTestsAsync(
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired, DateTimeOffset from, CancellationToken ct)
    {
        var attempts = await db.InteractiveTestAttempts.AsNoTracking()
            .Where(a => a.IsFirst && a.SubmittedAt != null && a.SubmittedAt >= from && a.MaxScore != null && a.MaxScore > 0)
            .Select(a => new { a.StudentId, a.UnitId, Score = a.Score ?? 0, MaxScore = a.MaxScore!.Value, SubmittedAt = a.SubmittedAt!.Value })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var a in attempts)
        {
            var delta = PointRules.Test(a.Score, a.MaxScore);
            if (delta == 0) continue;
            desired[(a.StudentId, Ref("itest", a.UnitId))] =
                new DesiredPoint(PointKind.InteractiveTest, delta, a.SubmittedAt, null);
        }
    }

    // ================================================================ Telegram

    /// <summary>
    /// Yangi davomat yozuvi tushgan har bir (dars, o'quvchi) uchun bitta xabar.
    /// Faqat oxirgi <see cref="MessageWindow"/> ichida tugagan darslar —
    /// birinchi ishga tushishda oynadagi eski darslar uchun xabar yog'ilmasin.
    /// </summary>
    private async Task EnqueueLessonMessagesAsync(
        List<StudentPoint> inserted,
        Dictionary<(long StudentId, string Ref), DesiredPoint> desired,
        Dictionary<(long SessionId, long StudentId), LessonOutcome> outcomes,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var fresh = inserted
            .Where(p => p.Kind is PointKind.Present or PointKind.Absent or PointKind.Excused
                     && p.SessionId is { } id
                     && outcomes.TryGetValue((id, p.StudentId), out var o)
                     && o.Session.ScheduledEnd >= now - MessageWindow)
            .ToList();

        if (fresh.Count == 0) return;

        List<long> studentIds = [.. fresh.Select(p => p.StudentId).Distinct()];
        List<long> sessionIds = [.. fresh.Select(p => p.SessionId!.Value).Distinct()];

        var students = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id) && u.IsActive && u.TelegramId != null)
            .Select(u => new { u.Id, u.FullName, TelegramId = u.TelegramId!.Value })
            .ToDictionaryAsync(u => u.Id, ct)
            .ConfigureAwait(false);

        if (students.Count == 0) return;

        var groupNames = await db.LiveSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Group!.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct)
            .ConfigureAwait(false);

        var tz = timeZone.TimeZone;
        var local = TimeZoneInfo.ConvertTime(now, tz);
        var monthStartLocal = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var monthStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, tz));

        var monthTotals = (await db.StudentPoints.AsNoTracking()
                .Where(p => studentIds.Contains(p.StudentId) && p.OccurredAt >= monthStartUtc)
                .GroupBy(p => p.StudentId)
                .Select(g => new { StudentId = g.Key, Sum = g.Sum(p => p.Delta) })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToDictionary(x => x.StudentId, x => Math.Max(0, x.Sum));

        var sendAfter = QuietHoursEnd(now, tz);

        foreach (var point in fresh)
        {
            if (!students.TryGetValue(point.StudentId, out var student)) continue;
            var sessionId = point.SessionId!.Value;
            var outcome = outcomes[(sessionId, point.StudentId)];

            var lessonPoints = desired
                .Where(d => d.Key.StudentId == point.StudentId && d.Value.SessionId == sessionId)
                .Sum(d => d.Value.Delta);

            var body = TelegramTemplates.PointsLessonText(
                student.FullName,
                groupNames.GetValueOrDefault(sessionId) ?? string.Empty,
                outcome.Attended,
                lessonPoints,
                outcome.Streak,
                outcome.Bonus,
                local.Month,
                monthTotals.GetValueOrDefault(point.StudentId));

            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = student.Id,
                    RecipientAddress = student.TelegramId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.PointsLesson,
                    Body = body,
                    IdempotencyKey = $"points_lesson:{sessionId}:{student.Id}",
                    SendAfter = sendAfter,
                },
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>22:00–08:00 oralig'ida — keyingi 08:00 (markaz vaqti); aks holda <c>null</c>.</summary>
    public static DateTimeOffset? QuietHoursEnd(DateTimeOffset now, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var local = TimeZoneInfo.ConvertTime(now, tz);
        if (local.Hour is >= QuietUntilHour and < QuietFromHour) return null;

        var day = local.Hour >= QuietFromHour ? local.Date.AddDays(1) : local.Date;
        var wake = DateTime.SpecifyKind(day.AddHours(QuietUntilHour), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wake, tz));
    }

    // ================================================================ yordamchi

    private static string Ref(string prefix, long id) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}:{id}");

    private static SettingDefinition Definition(string key) =>
        SettingsRegistry.TryGet(key, out var definition)
            ? definition
            : throw new InvalidOperationException($"Sozlama registrda yo'q: {key}");

    private sealed record DesiredPoint(PointKind Kind, int Delta, DateTimeOffset OccurredAt, long? SessionId);

    private sealed record SessionRow(long Id, long GroupId, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd);

    private sealed record AttendanceRow(long SessionId, long StudentId, AttendanceStatus Status, bool IsExcused);

    private sealed record LessonOutcome(SessionRow Session, bool Attended, int Streak, int? Bonus);

    private sealed record MemberSpan(
        long StudentId, long GroupId, long? CuratorGroupId, MemberStatus Status,
        DateTimeOffset JoinedAt, DateTimeOffset? LeftAt)
    {
        /// <summary>
        /// Dars paytida kutilgan a'zomi: o'sha paytgacha qo'shilgan va hali
        /// faol yoki darsdan KEYIN chiqib ketgan. Pauzadagi a'zo kutilmaydi.
        /// </summary>
        public bool ExpectedAt(DateTimeOffset start) =>
            JoinedAt <= start
            && (Status == MemberStatus.Active
                || (Status is MemberStatus.Stopped or MemberStatus.Moved && LeftAt is { } left && left > start));
    }
}

internal static partial class PointsLog
{
    [LoggerMessage(EventId = 7301, Level = LogLevel.Information,
        Message = "Ball daftari yangilandi: {Inserted} yangi, {Changed} o'zgargan/o'chirilgan")]
    public static partial void Synced(ILogger logger, int inserted, int changed);
}
