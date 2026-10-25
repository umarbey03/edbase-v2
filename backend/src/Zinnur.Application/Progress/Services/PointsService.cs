using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Progress.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Finance;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Progress.Services;

public interface IPointsService
{
    /// <summary>O'quvchining oylik ball xulosasi ("Mening ballim").</summary>
    Task<MyPointsDto> GetMineAsync(long studentId, string? period, CancellationToken ct = default);
}

/// <summary>
/// "Mening ballim" (2026-10-23). Reytingdagi o'rin
/// <see cref="ILeaderboardService"/> dan (<see cref="LeaderboardMetric.Points"/>)
/// olinadi — o'rin bitta joyda hisoblanadi, kartochka va jadval bir-biridan
/// farq qilmaydi. Ketma-ketlik — davomat xulosasidagi AYNI qiymat
/// (bosh sahifadagi "Ketma-ket N dars" bilan bir xil).
///
/// ★ TUR BO'YICHA TAFSILOT VA MINUS BALLAR FAQAT O'ZIGA: reyting jadvalida
///   boshqalarga faqat jami ko'rinadi (ochiq joyda faqat ijobiy ma'lumot).
/// </summary>
public sealed class PointsService(
    IApplicationDbContext db,
    ILeaderboardService leaderboard,
    IAttendanceSummaryService attendance,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IPointsService
{
    private const int RecentCount = 30;

    public async Task<MyPointsDto> GetMineAsync(long studentId, string? period, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);
        if (!user.IsActive) throw new ForbiddenException("Profilingiz faol emas.");

        var resolved = ResolvePeriod(period);
        var (startUtc, endUtc) = resolved.UtcRange(timeZone.TimeZone);

        var rows = await db.StudentPoints.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.OccurredAt >= startUtc && p.OccurredAt < endUtc)
            .Select(p => new { p.Kind, p.Delta, p.OccurredAt, p.Ref, p.SessionId })
            .ToListAsync(ct);

        var breakdown = rows
            .GroupBy(r => r.Kind)
            .OrderBy(g => g.Key)
            .Select(g => new PointsBreakdownDto(g.Key, g.Count(), g.Sum(r => r.Delta)))
            .ToList();

        var recentRows = rows.OrderByDescending(r => r.OccurredAt).ThenBy(r => r.Ref, StringComparer.Ordinal)
            .Take(RecentCount).ToList();
        var titles = await TitlesAsync(recentRows.Select(r => (r.Kind, r.Ref, r.SessionId)).ToList(), ct);
        var recent = recentRows
            .Select(r => new PointEventDto(r.Kind, r.Delta, r.OccurredAt, titles.GetValueOrDefault(r.Ref)))
            .ToList();

        var rank = await leaderboard.GetMyRankAsync(
            studentId, LeaderboardScope.Group, resolved.ToString(), LeaderboardMetric.Points, ct);

        var summary = await attendance.GetMySummaryAsync(studentId, null, null, null, ct);
        var toNext = PointRules.LessonsToNextBonus(summary.Streak);

        return new MyPointsDto(
            resolved.ToString(),
            PointRules.MonthTotal(rows.Select(r => r.Delta)),
            rank.Me is { Rank: > 0 } me ? me.Rank : null,
            rank.GroupName,
            summary.Streak,
            toNext,
            PointRules.StreakBonus(summary.Streak + toNext) ?? PointRules.StreakSmallBonus,
            breakdown,
            recent);
    }

    /// <summary>
    /// Yozuv nomlari: dars — guruh nomi (va mavzu), vazifa/test — sarlavha,
    /// video — dars nomi. Kalitdan (<c>asg:{id}</c>, ...) Id olinadi.
    /// </summary>
    private async Task<Dictionary<string, string>> TitlesAsync(
        List<(PointKind Kind, string Ref, long? SessionId)> items, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (items.Count == 0) return result;

        List<long> sessionIds = [.. items.Where(i => i.SessionId != null).Select(i => i.SessionId!.Value).Distinct()];
        List<long> assignmentIds = [.. IdsOf(items, PointKind.Assignment)];
        List<long> testIds = [.. IdsOf(items, PointKind.Test)];
        List<long> lessonIds = [.. IdsOf(items, PointKind.VideoWatched)];

        var sessions = sessionIds.Count == 0 ? new Dictionary<long, string>() : await db.LiveSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Title, Group = s.Group!.Name })
            .ToDictionaryAsync(s => s.Id, s => string.IsNullOrWhiteSpace(s.Title) ? s.Group : $"{s.Group} · {s.Title}", ct);
        var assignments = assignmentIds.Count == 0 ? new Dictionary<long, string>() : await db.Assignments.AsNoTracking()
            .Where(a => assignmentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Title, ct);
        var tests = testIds.Count == 0 ? new Dictionary<long, string>() : await db.Tests.AsNoTracking()
            .Where(t => testIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        var lessons = lessonIds.Count == 0 ? new Dictionary<long, string>() : await db.ModuleLessons.AsNoTracking()
            .Where(l => lessonIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        foreach (var (kind, reference, sessionId) in items)
        {
            string? title = kind switch
            {
                PointKind.Assignment => assignments.GetValueOrDefault(IdOf(reference)),
                PointKind.Test => tests.GetValueOrDefault(IdOf(reference)),
                PointKind.VideoWatched => lessons.GetValueOrDefault(IdOf(reference)),
                _ => sessionId is { } id ? sessions.GetValueOrDefault(id) : null,
            };
            if (title is not null) result[reference] = title;
        }

        return result;
    }

    private static IEnumerable<long> IdsOf(List<(PointKind Kind, string Ref, long? SessionId)> items, PointKind kind) =>
        items.Where(i => i.Kind == kind).Select(i => IdOf(i.Ref)).Where(id => id > 0).Distinct();

    private static long IdOf(string reference)
    {
        var colon = reference.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 && long.TryParse(reference.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : 0;
    }

    private BillingPeriod ResolvePeriod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone.TimeZone).DateTime);
            return BillingPeriod.FromDate(today);
        }

        try
        {
            return BillingPeriod.Parse(value);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["period"] = [ex.Message] });
        }
    }
}
