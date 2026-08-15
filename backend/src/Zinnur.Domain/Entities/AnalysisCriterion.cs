using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARS TAHLILI MEZONI (R29/R30 kengaytmasi — mezon asosidagi ballash)
/// ════════════════════════════════════════════════════════════════════════
///
/// O'quv bo'limi (yoki Admin) shu katalogni sozlaydi — "Metodika", "Vaqt
/// boshqaruvi" kabi mezonlar va ularning maksimal balli. Har bir
/// <see cref="SessionReview"/> shu katalogdan tanlab ball qo'yadi.
///
/// ── NIMA UCHUN O'CHIRISH TARIXNI BUZMAYDI ──────────────────────────────
///
/// <see cref="SessionReviewScore"/> mezonning NOMI va MAKSIMAL BALINI
/// yozish vaqtida O'ZIDA nusxalaydi (snapshot) — <c>LessonGrade.MaxScore</c>
/// dagi AYNI mulohaza: mezon keyin o'chirilsa yoki tahrirlansa ham,
/// ALLAQACHON yozilgan tahlilning ma'nosi o'zgarmasligi kerak ("5/5"
/// keyinchalik "5/10" ga aylanib qolmasin). Shu sababli bu yerda
/// "ishlatilganmi" degan tekshiruv yo'q — o'chirish har doim xavfsiz.
/// </summary>
public class AnalysisCriterion : BaseEntity
{
    /// <summary>Nom uzunligi chegarasi ("Metodika", "O'quvchilar bilan muloqot" kabi qisqa nomlar).</summary>
    public const int MaxNameLength = 200;

    /// <summary>Maksimal ballning quyi chegarasi.</summary>
    public const decimal MinMaxScore = 1m;

    /// <summary>
    /// Maksimal ballning yuqori chegarasi. 100 — foizga to'g'ridan-to'g'ri
    /// mos keladigan eng katta "aqlli" shkala; undan kattasi amalda
    /// ishlatilmaydi va xato terilgan qiymatdan (masalan minglab) himoya beradi.
    /// </summary>
    public const decimal MaxMaxScore = 100m;

    public required string Name { get; set; }

    /// <summary>Shu mezon bo'yicha qo'yish mumkin bo'lgan eng yuqori ball.</summary>
    public decimal MaxScore { get; set; }

    /// <summary>Ko'rsatish tartibi (kichikdan kattaga). Teng bo'lsa — Id bo'yicha.</summary>
    public int SortOrder { get; set; }

    public static AnalysisCriterion Create(
        string? name, decimal maxScore, int sortOrder, DateTimeOffset now) =>
        new()
        {
            Name = RequireName(name),
            MaxScore = RequireMaxScore(maxScore),
            SortOrder = sortOrder,
            CreatedAt = now,
        };

    public void Edit(string? name, decimal maxScore, int sortOrder, DateTimeOffset now)
    {
        Name = RequireName(name);
        MaxScore = RequireMaxScore(maxScore);
        SortOrder = sortOrder;
        UpdatedAt = now;
    }

    private static string RequireName(string? name)
    {
        var value = name?.Trim();

        if (string.IsNullOrEmpty(value))
            throw new DomainException("Mezon nomi bo'sh bo'lishi mumkin emas.");

        if (value.Length > MaxNameLength)
            throw new DomainException($"Mezon nomi {MaxNameLength} belgidan oshmasin.");

        return value;
    }

    private static decimal RequireMaxScore(decimal maxScore)
    {
        if (maxScore < MinMaxScore || maxScore > MaxMaxScore)
        {
            throw new DomainException(
                $"Maksimal ball {MinMaxScore} dan {MaxMaxScore} gacha bo'lishi kerak.");
        }

        return maxScore;
    }
}
