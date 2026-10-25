namespace Zinnur.Domain.Progress;

/// <summary>
/// BALL QOIDALARI (2026-10-23, loyiha egasi tasdiqlagan).
///
/// Asos — eski platformaning ball tizimi (real o'quvchilarda sinalgan):
/// kelish +10, kelmaslik −5 (uzrli −3), ketma-ket 4 darsga +10, 8 darsga
/// +25, dars bahosi ×2, uy vazifasi ×3. v2'da qo'shimcha: test (10 gacha)
/// va dars videosi (+3). To'lov ATAYLAB ballga kirmaydi — pul masalasi
/// motivatsiyadan yuqori turmasligi kerak.
/// </summary>
public static class PointRules
{
    public const int Present = 10;
    public const int Absent = -5;
    public const int Excused = -3;
    public const int VideoWatched = 3;

    public const int StreakSmallEvery = 4;
    public const int StreakSmallBonus = 10;
    public const int StreakBigEvery = 8;
    public const int StreakBigBonus = 25;

    public const int LessonGradeMultiplier = 2;
    public const int AssignmentMultiplier = 3;
    public const int TestMax = 10;

    /// <summary>Ketma-ket N-darsdagi bonus (bo'lmasa <c>null</c>): 4 → 10, 8 → 25, 12 → 10, 16 → 25...</summary>
    public static int? StreakBonus(int streak) =>
        streak <= 0 ? null
        : streak % StreakBigEvery == 0 ? StreakBigBonus
        : streak % StreakSmallEvery == 0 ? StreakSmallBonus
        : null;

    /// <summary>Keyingi bonusgacha qolgan darslar soni (1..4).</summary>
    public static int LessonsToNextBonus(int streak) =>
        StreakSmallEvery - (Math.Max(0, streak) % StreakSmallEvery);

    /// <summary>Har qanday shkaladagi bahoni 0..5 butun songa keltiradi.</summary>
    public static int ToFive(decimal score, decimal maxScore)
    {
        if (maxScore <= 0) return 0;
        var five = Math.Round(score / maxScore * 5m, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(five, 0m, 5m);
    }

    public static int LessonGrade(decimal score, decimal maxScore) =>
        ToFive(score, maxScore) * LessonGradeMultiplier;

    public static int Assignment(decimal score, decimal maxScore) =>
        ToFive(score, maxScore) * AssignmentMultiplier;

    public static int Test(decimal score, decimal maxScore)
    {
        if (maxScore <= 0) return 0;
        var points = Math.Round(score / maxScore * TestMax, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(points, 0m, TestMax);
    }

    /// <summary>Oylik jami — manfiyga tushmaydi (eski tizim qoidasi).</summary>
    public static int MonthTotal(IEnumerable<int> deltas)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        return Math.Max(0, deltas.Sum());
    }
}

/// <summary>Ball jadvalidagi bitta o'quvchi.</summary>
public sealed record PointsStanding(long StudentId, string StudentName, int Points);

/// <summary>Tartiblangan qator; <see cref="Rank"/> = 0 — ball yo'q, o'rin berilmaydi.</summary>
public sealed record RankedPoints(int Rank, PointsStanding Standing);

/// <summary>
/// BALL REYTINGI TARTIBI.
///
/// ★ TENG BALL — TENG O'RIN (1, 1, 3), sport uslubida.
/// ★ TENGLAR ICHIDA TARTIB ALIFBO EMAS — KUNDA BIR MARTA ARALASHADI.
///   Eski tizimda teng ballilar ism bo'yicha tartiblanardi va "A" harfli
///   familiyalar doim tepada turardi; o'quvchilar familiyasini o'zgartirmoqchi
///   bo'lgan (2026-10-20). Aralashtirish DETERMINISTIK (o'quvchi Id'si va
///   sana) — bir kun ichida har serverda va har keshda bir xil tartib.
/// ★ 0 BALL — O'RIN YO'Q (<see cref="RankedPoints.Rank"/> = 0, ekranda "—").
/// </summary>
public static class PointsRanking
{
    public static IReadOnlyList<RankedPoints> Rank(IEnumerable<PointsStanding> standings, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(standings);

        var seed = (ulong)day.DayNumber;
        var ordered = standings
            .OrderByDescending(s => s.Points)
            .ThenBy(s => Mix((ulong)s.StudentId, seed))
            .ThenBy(s => s.StudentId)
            .ToList();

        var result = new List<RankedPoints>(ordered.Count);
        var rank = 0;
        int? previous = null;

        for (var i = 0; i < ordered.Count; i++)
        {
            var standing = ordered[i];
            if (previous != standing.Points) rank = i + 1;
            result.Add(new RankedPoints(standing.Points > 0 ? rank : 0, standing));
            previous = standing.Points;
        }

        return result;
    }

    /// <summary>SplitMix64 — tez, deterministik aralashtirish (<c>HashCode</c> jarayonga bog'liq).</summary>
    private static ulong Mix(ulong value, ulong seed)
    {
        var z = value + (seed * 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
