using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Services;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Progress.Services;

/// <param name="Code">Barqaror kalit (frontend ikonka/rang tanlaydi).</param>
/// <param name="Progress">Hozirgi qiymat (masalan, 3 ta dars).</param>
/// <param name="Target">Nishon uchun kerak (masalan, 5 ta dars).</param>
/// <param name="EarnedAt">Qachon olingani — ma'lum bo'lsa (yangi nishon belgisi uchun).</param>
public sealed record BadgeDto(
    string Code, string Title, string Description, string Emoji,
    bool Earned, int Progress, int Target, DateTimeOffset? EarnedAt);

public sealed record MyBadgesDto(int Earned, int Total, IReadOnlyList<BadgeDto> Badges);

public interface IBadgeService
{
    Task<MyBadgesDto> GetMineAsync(long studentId, CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// NISHONLAR (2026-11-21, gamifikatsiya 3-bosqich)
/// ════════════════════════════════════════════════════════════════════
///
/// ★ SAQLANMAYDI — har safar manba ma'lumotdan HISOBLANADI (dars qarori,
///   test, vazifa, seriya, hamjamiyat). Sabab: ball daftari bilan AYNI
///   mulohaza — "nishon ber" ni har bir yo'lga qo'shish birortasini
///   unutishga olib kelardi; hisoblangan nishon esa doim to'g'ri.
/// ★ NOMLAR NEYTRAL (loyiha qoidasi): o'qish yutug'i, boshqa hech narsa.
/// </summary>
public sealed class BadgeService(
    IApplicationDbContext db,
    IGatingService gating,
    ILetterProgressService letters,
    IDailyStreakService streaks,
    TimeProvider clock) : IBadgeService
{
    private sealed record Def(string Code, string Title, string Description, string Emoji, int Target);

    private static readonly Def FirstLesson = new("first_lesson", "Birinchi qadam", "Birinchi darsni o'zlashtiring", "🌱", 1);
    private static readonly Def Lessons5 = new("lessons_5", "Beshlik", "5 ta darsni o'zlashtiring", "📗", 5);
    private static readonly Def Lessons10 = new("lessons_10", "O'nlik", "10 ta darsni o'zlashtiring", "📚", 10);
    private static readonly Def Module = new("module_done", "Modul tugadi", "Bitta modulning barcha darslarini o'zlashtiring", "🏁", 1);
    private static readonly Def Alphabet = new("alphabet", "Alifbo ustasi", "28 harfning hammasini o'zlashtiring", "🔤", 28);
    private static readonly Def Streak7 = new("streak_7", "Bir hafta", "7 kun ketma-ket o'qing", "🔥", 7);
    private static readonly Def Streak30 = new("streak_30", "Bir oy", "30 kun ketma-ket o'qing", "☄️", 30);
    private static readonly Def PerfectTest = new("perfect_test", "A'lo test", "Testni 100% bilan topshiring", "🎯", 1);
    private static readonly Def Assignments10 = new("assignments_10", "Mehnatkash", "10 ta vazifangiz baholansin", "✍️", 10);
    private static readonly Def Community = new("community", "Hamjamiyat a'zosi", "Hamjamiyatda birinchi xabaringizni yozing", "🤝", 1);

    public async Task<MyBadgesDto> GetMineAsync(long studentId, CancellationToken ct = default)
    {
        var result = new List<BadgeDto>();

        // ---- darslar (o'zlashtirilgan: Mustaqil — kurator qarori, guruh — bajarilgan)
        var gate = await gating.GetCourseGateAsync(studentId, ct).ConfigureAwait(false);
        var doneIds = gate.Lessons
            .Where(l => l.RequiresMastery ? l.Mastery == MasteryStatus.Mastered : l.Completed)
            .Select(l => l.LessonId)
            .ToHashSet();

        var masteryDates = await db.LessonMasteries.AsNoTracking()
            .Where(m => m.StudentId == studentId && m.Status == MasteryStatus.Mastered)
            .Select(m => m.DecidedAt)
            .OrderBy(d => d)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        DateTimeOffset? NthMastery(int n) => masteryDates.Count >= n ? masteryDates[n - 1] : null;

        result.Add(Count(FirstLesson, doneIds.Count, NthMastery(1)));
        result.Add(Count(Lessons5, doneIds.Count, NthMastery(5)));
        result.Add(Count(Lessons10, doneIds.Count, NthMastery(10)));

        // ---- modul
        List<long> lessonIds = [.. gate.Lessons.Select(l => l.LessonId)];
        var modules = lessonIds.Count == 0
            ? []
            : await db.ModuleLessons.AsNoTracking()
                .Where(l => lessonIds.Contains(l.Id))
                .Select(l => new { l.Id, l.ModuleId })
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var modulesDone = modules.GroupBy(m => m.ModuleId).Count(g => g.All(m => doneIds.Contains(m.Id)));
        result.Add(Count(Module, modulesDone, null));

        // ---- harflar
        var letterBoard = await letters.GetMineAsync(studentId, ct).ConfigureAwait(false);
        if (letterBoard.Letters.Any(l => l.LessonId is not null))
            result.Add(Count(Alphabet, letterBoard.Mastered, null));

        // ---- seriya (faqat Mustaqil)
        var isSelfStudy = await db.SubscriptionPeriods.AsNoTracking()
            .AnyAsync(p => p.StudentId == studentId && p.CancelledAt == null, ct)
            .ConfigureAwait(false);
        if (isSelfStudy)
        {
            var days = await streaks.ActiveDaysAsync([studentId], clock.GetUtcNow() - DailyStreakService.History, ct)
                .ConfigureAwait(false);
            var streak = DailyStreak.Compute(days.GetValueOrDefault(studentId) ?? [], streaks.Today());
            result.Add(Count(Streak7, streak.Best, null));
            result.Add(Count(Streak30, streak.Best, null));
        }

        // ---- a'lo test
        var perfectAt = await db.TestAttempts.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Status == AttemptStatus.Submitted
                     && a.MaxScore != null && a.MaxScore > 0 && a.Score != null && a.Score >= a.MaxScore)
            .OrderBy(a => a.SubmittedAt)
            .Select(a => a.SubmittedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        result.Add(Count(PerfectTest, perfectAt is null ? 0 : 1, perfectAt));

        // ---- vazifalar
        var graded = await db.Submissions.AsNoTracking()
            .Where(s => s.StudentId == studentId && s.Status == SubmissionStatus.Graded && s.GradedAt != null)
            .GroupBy(s => s.AssignmentId)
            .Select(g => g.Min(s => s.GradedAt!.Value))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        graded.Sort();
        result.Add(Count(Assignments10, graded.Count, graded.Count >= 10 ? graded[9] : null));

        // ---- hamjamiyat (faqat Mustaqil)
        if (isSelfStudy)
        {
            var firstPost = await db.CommunityMessages.AsNoTracking()
                .Where(m => m.AuthorId == studentId)
                .OrderBy(m => m.Id)
                .Select(m => (DateTimeOffset?)m.CreatedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            result.Add(Count(Community, firstPost is null ? 0 : 1, firstPost));
        }

        return new MyBadgesDto(result.Count(b => b.Earned), result.Count, result);
    }

    private static BadgeDto Count(Def def, int progress, DateTimeOffset? earnedAt)
    {
        var earned = progress >= def.Target;
        return new BadgeDto(
            def.Code, def.Title, def.Description, def.Emoji,
            earned, Math.Min(progress, def.Target), def.Target, earned ? earnedAt : null);
    }
}
