namespace Zinnur.Domain.Progress;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL O'QUVCHINING KUNLIK SERIYASI (2026-11-21, loyiha egasi tasdiqlagan)
/// ════════════════════════════════════════════════════════════════════
///
/// Guruh o'quvchisining seriyasi jonli darsga qatnashishdan hisoblanadi
/// (<see cref="PointRules.StreakBonus"/>). Mustaqil o'quvchida jonli dars yo'q —
/// uning seriyasi KUNLAR bo'yicha: kun ichida video ko'rsa, test/vazifa
/// topshirsa yoki darslik birligini tugatsa — o'sha kun "faol".
///
/// ★ MUZLATISH: haftasiga (dushanba–yakshanba) BITTA o'tkazib yuborilgan kun
///   seriyani kuydirmaydi (kasallik, mehmon — odatiy hayot). Ikki kun ketma-ket
///   o'tkazilsa yoki shu haftada muzlatish ishlatilgan bo'lsa — seriya 0.
///   O'tkazilgan kun seriyaga QO'SHILMAYDI, faqat uni uzmaydi.
///
/// ★ BONUS: 3 → +5, 7 → +15, 14 → +25, 30 → +50, keyin har 30 kunda +50.
///   Kichik raqamlar ataylab: test 10 balldan oshmaydi, seriya darsdan
///   ustun turmasligi kerak.
///
/// Sof funksiya — vaqt, baza yo'q; kunlar markaz vaqt zonasida beriladi.
/// </summary>
public static class DailyStreak
{
    public const int MonthlyEvery = 30;
    public const int MonthlyBonus = 50;

    private static readonly (int Day, int Bonus)[] Milestones = [(3, 5), (7, 15), (14, 25), (30, 50)];

    /// <summary>N-kunlik seriyadagi bonus (bo'lmasa <c>null</c>).</summary>
    public static int? BonusFor(int streak)
    {
        if (streak <= 0) return null;
        foreach (var (day, bonus) in Milestones)
            if (streak == day) return bonus;
        return streak > MonthlyEvery && streak % MonthlyEvery == 0 ? MonthlyBonus : null;
    }

    /// <summary>Keyingi bonus: necha kunlik seriyada va qancha ball.</summary>
    public static (int Day, int Bonus) NextBonus(int streak)
    {
        foreach (var (day, bonus) in Milestones)
            if (streak < day) return (day, bonus);
        return (((streak / MonthlyEvery) + 1) * MonthlyEvery, MonthlyBonus);
    }

    /// <summary>
    /// Faol kunlardan seriyani hisoblaydi. <paramref name="today"/> — markaz vaqtidagi bugun.
    /// </summary>
    public static StreakResult Compute(IEnumerable<DateOnly> activeDays, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(activeDays);
        var days = activeDays.Where(d => d <= today).Distinct().Order().ToList();

        var bonuses = new List<(DateOnly Day, int Streak, int Bonus)>();
        var frozenWeeks = new HashSet<DateOnly>();
        var frozenDays = new List<DateOnly>();
        var streak = 0;
        var best = 0;
        DateOnly? last = null;

        foreach (var day in days)
        {
            if (last is { } prev)
            {
                var gap = day.DayNumber - prev.DayNumber;
                if (gap == 2 && frozenWeeks.Add(WeekOf(prev.AddDays(1))))
                {
                    frozenDays.Add(prev.AddDays(1));
                }
                else if (gap != 1)
                {
                    streak = 0;
                }
            }

            streak++;
            best = Math.Max(best, streak);
            if (BonusFor(streak) is { } bonus) bonuses.Add((day, streak, bonus));
            last = day;
        }

        // Bugungi holat: seriya hali tirikmi?
        var current = 0;
        var doneToday = false;
        var freezeUsedNow = false;
        if (last is { } lastDay)
        {
            var since = today.DayNumber - lastDay.DayNumber;
            doneToday = since == 0;
            if (since <= 1)
            {
                current = streak;
            }
            else if (since == 2 && !frozenWeeks.Contains(WeekOf(today.AddDays(-1))))
            {
                // Kecha o'tkazildi — muzlatish bilan seriya hali tirik (bugun o'qisa davom etadi).
                current = streak;
                freezeUsedNow = true;
            }
        }

        var freezeAvailable = !frozenWeeks.Contains(WeekOf(today))
            && !(freezeUsedNow && WeekOf(today.AddDays(-1)) == WeekOf(today));

        return new StreakResult(
            current,
            best,
            doneToday,
            freezeAvailable,
            bonuses,
            [.. frozenDays, .. freezeUsedNow ? new[] { today.AddDays(-1) } : []]);
    }

    /// <summary>Haftalik maqsad (2026-11-21): haftada shuncha faol kun.</summary>
    public const int WeeklyGoalDays = 4;

    /// <summary>Haftalik maqsad bajarilganda beriladigan ball.</summary>
    public const int WeeklyGoalBonus = 10;

    /// <summary>
    /// Maqsad bajarilgan haftalar: hafta boshi (dushanba) va maqsadga yetgan kun
    /// (<see cref="WeeklyGoalDays"/>-faol kun). Bonus shu kunga yoziladi.
    /// </summary>
    public static IReadOnlyList<(DateOnly Week, DateOnly ReachedOn)> GoalWeeks(IEnumerable<DateOnly> activeDays)
    {
        ArgumentNullException.ThrowIfNull(activeDays);
        return [.. activeDays.Distinct().Order()
            .GroupBy(WeekOf)
            .Where(g => g.Count() >= WeeklyGoalDays)
            .Select(g => (g.Key, g.ElementAt(WeeklyGoalDays - 1)))];
    }

    /// <summary>Hafta boshi (dushanba).</summary>
    public static DateOnly WeekOf(DateOnly day) =>
        day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}

/// <param name="Current">Hozirgi seriya (uzilgan bo'lsa 0).</param>
/// <param name="Best">Eng uzun seriya.</param>
/// <param name="DoneToday">Bugun allaqachon o'qidi.</param>
/// <param name="FreezeAvailable">Shu hafta muzlatish hali ishlatilmagan.</param>
/// <param name="Bonuses">Har bir bonus kuni (tarix bo'yicha, ball daftari uchun).</param>
/// <param name="FrozenDays">Muzlatish bilan "qutqarilgan" kunlar.</param>
public sealed record StreakResult(
    int Current,
    int Best,
    bool DoneToday,
    bool FreezeAvailable,
    IReadOnlyList<(DateOnly Day, int Streak, int Bonus)> Bonuses,
    IReadOnlyList<DateOnly> FrozenDays);
