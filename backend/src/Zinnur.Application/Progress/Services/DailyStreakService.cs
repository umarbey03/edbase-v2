using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Progress.Services;

/// <summary>O'quvchiga ko'rsatiladigan kunlik seriya (Mustaqil, 2026-11-21).</summary>
/// <param name="Enabled">Seriya shu o'quvchiga tegishlimi (faqat Mustaqil).</param>
/// <param name="Days">Oxirgi 14 kun: eng eskisidan bugungacha.</param>
public sealed record MyStreakDto(
    bool Enabled,
    int Current,
    int Best,
    bool DoneToday,
    bool FreezeAvailable,
    int NextBonusDay,
    int NextBonusPoints,
    IReadOnlyList<StreakDayDto> Days,
    int WeekActiveDays = 0,
    int WeekGoalDays = DailyStreak.WeeklyGoalDays,
    int WeekGoalBonus = DailyStreak.WeeklyGoalBonus);

/// <param name="State">Active, Frozen, Missed yoki Today (bugun, hali o'qilmagan).</param>
public sealed record StreakDayDto(DateOnly Date, string State);

public interface IDailyStreakService
{
    /// <summary>Mustaqil o'quvchilar (bekor qilinmagan obuna davri bor).</summary>
    Task<List<long>> SelfStudyStudentIdsAsync(CancellationToken ct = default);

    /// <summary>O'quvchi → faol kunlar (markaz vaqtida), <paramref name="from"/> dan beri.</summary>
    Task<Dictionary<long, List<DateOnly>>> ActiveDaysAsync(
        IReadOnlyCollection<long> studentIds, DateTimeOffset from, CancellationToken ct = default);

    /// <summary>Markaz vaqtidagi bugun.</summary>
    DateOnly Today();

    DateOnly LocalDate(DateTimeOffset at);

    Task<MyStreakDto> GetMineAsync(long studentId, CancellationToken ct = default);
}

/// <summary>
/// Kunlik seriyaning MA'LUMOT tomoni: qaysi kunlar "faol". Qoida —
/// <see cref="DailyStreak"/> (sof funksiya). Faol kun manbalari: dars videosi,
/// topshirilgan test, topshirilgan vazifa (baholanishini kutmaydi — o'quvchi
/// o'z ishini qildi), darslik birligi va mini-testi.
/// </summary>
public sealed class DailyStreakService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IDailyStreakService
{
    /// <summary>Seriya tarixi — bundan uzun uzluksiz seriya juda kam; bonuslar oynada yoziladi.</summary>
    public static readonly TimeSpan History = TimeSpan.FromDays(400);

    private const int ShownDays = 14;

    public DateOnly Today() => LocalDate(clock.GetUtcNow());

    public DateOnly LocalDate(DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, timeZone.TimeZone).DateTime);

    public Task<List<long>> SelfStudyStudentIdsAsync(CancellationToken ct = default) =>
        db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.CancelledAt == null && p.Student!.Role == UserRole.Student && p.Student.IsActive)
            .Select(p => p.StudentId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<Dictionary<long, List<DateOnly>>> ActiveDaysAsync(
        IReadOnlyCollection<long> studentIds, DateTimeOffset from, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(studentIds);
        var result = new Dictionary<long, HashSet<DateOnly>>();
        if (studentIds.Count == 0) return [];

        List<long> ids = [.. studentIds];

        void Add(long studentId, DateTimeOffset at)
        {
            if (!result.TryGetValue(studentId, out var set)) result[studentId] = set = [];
            set.Add(LocalDate(at));
        }

        foreach (var x in await db.LessonProgress.AsNoTracking()
                     .Where(p => ids.Contains(p.StudentId) && p.VideoWatchedAt != null && p.VideoWatchedAt >= from)
                     .Select(p => new { p.StudentId, At = p.VideoWatchedAt!.Value })
                     .ToListAsync(ct).ConfigureAwait(false))
            Add(x.StudentId, x.At);

        foreach (var x in await db.TestAttempts.AsNoTracking()
                     .Where(a => ids.Contains(a.StudentId) && a.Status == AttemptStatus.Submitted
                              && a.SubmittedAt != null && a.SubmittedAt >= from)
                     .Select(a => new { a.StudentId, At = a.SubmittedAt!.Value })
                     .ToListAsync(ct).ConfigureAwait(false))
            Add(x.StudentId, x.At);

        foreach (var x in await db.Submissions.AsNoTracking()
                     .Where(s => ids.Contains(s.StudentId) && s.SubmittedAt >= from)
                     .Select(s => new { s.StudentId, At = s.SubmittedAt })
                     .ToListAsync(ct).ConfigureAwait(false))
            Add(x.StudentId, x.At);

        foreach (var x in await db.InteractiveUnitProgress.AsNoTracking()
                     .Where(p => ids.Contains(p.StudentId) && p.CompletedAt != null && p.CompletedAt >= from)
                     .Select(p => new { p.StudentId, At = p.CompletedAt!.Value })
                     .ToListAsync(ct).ConfigureAwait(false))
            Add(x.StudentId, x.At);

        foreach (var x in await db.InteractiveTestAttempts.AsNoTracking()
                     .Where(a => ids.Contains(a.StudentId) && a.SubmittedAt != null && a.SubmittedAt >= from)
                     .Select(a => new { a.StudentId, At = a.SubmittedAt!.Value })
                     .ToListAsync(ct).ConfigureAwait(false))
            Add(x.StudentId, x.At);

        return result.ToDictionary(kv => kv.Key, kv => kv.Value.Order().ToList());
    }

    public async Task<MyStreakDto> GetMineAsync(long studentId, CancellationToken ct = default)
    {
        var isSelfStudy = await db.SubscriptionPeriods.AsNoTracking()
            .AnyAsync(p => p.StudentId == studentId && p.CancelledAt == null, ct)
            .ConfigureAwait(false);

        var today = Today();
        if (!isSelfStudy)
            return new MyStreakDto(false, 0, 0, false, false, 0, 0, []);

        var days = await ActiveDaysAsync([studentId], clock.GetUtcNow() - History, ct).ConfigureAwait(false);
        var active = days.GetValueOrDefault(studentId) ?? [];
        var result = DailyStreak.Compute(active, today);
        var (nextDay, nextBonus) = DailyStreak.NextBonus(result.Current);

        var activeSet = active.ToHashSet();
        var frozen = result.FrozenDays.ToHashSet();
        var shown = Enumerable.Range(0, ShownDays)
            .Select(i => today.AddDays(i - ShownDays + 1))
            .Select(d => new StreakDayDto(
                d,
                activeSet.Contains(d) ? "Active"
                : d == today ? "Today"
                : frozen.Contains(d) ? "Frozen"
                : "Missed"))
            .ToList();

        var week = DailyStreak.WeekOf(today);
        var weekActive = active.Count(d => d >= week && d <= today);

        return new MyStreakDto(
            true, result.Current, result.Best, result.DoneToday, result.FreezeAvailable, nextDay, nextBonus, shown,
            weekActive);
    }
}
