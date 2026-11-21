using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Arab alifbosining 28 harfi — profil harflar jadvali (2026-11-21) tartibi.
/// Jadval darsdagi tartibda emas, ALIFBO tartibida chiziladi: o'quvchi
/// qaysi harflar hali qolganini bir qarashda ko'radi.
/// </summary>
public static class ArabicAlphabet
{
    public const int MaxLettersPerLesson = 6;

    /// <summary>Alifbo tartibi.</summary>
    public static readonly IReadOnlyList<string> Letters =
    [
        "ا", "ب", "ت", "ث", "ج", "ح", "خ", "د", "ذ", "ر", "ز", "س", "ش", "ص",
        "ض", "ط", "ظ", "ع", "غ", "ف", "ق", "ك", "ل", "م", "ن", "ه", "و", "ي",
    ];

    private static readonly HashSet<string> Known = [.. Letters];

    /// <summary>
    /// Kiritilgan matndan harflar satrini yasaydi: bo'shliq, vergul va
    /// takrorlar olib tashlanadi, tartib saqlanadi. Bo'sh — <c>null</c>.
    /// </summary>
    /// <exception cref="DomainException">Alifboda yo'q belgi yoki juda ko'p harf.</exception>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var result = new List<string>();
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch) || ch is ',' or '،' or '-' or '/') continue;
            var letter = ch.ToString();
            if (!Known.Contains(letter))
                throw new DomainException($"«{letter}» — arab alifbosining 28 harfidan biri emas.");
            if (!result.Contains(letter)) result.Add(letter);
        }

        if (result.Count > MaxLettersPerLesson)
            throw new DomainException($"Bitta darsga ko'pi bilan {MaxLettersPerLesson} ta harf.");

        return result.Count == 0 ? null : string.Concat(result);
    }

    /// <summary>Saqlangan satrni harflarga ajratadi.</summary>
    public static IEnumerable<string> Split(string? letters) =>
        string.IsNullOrEmpty(letters) ? [] : letters.Select(c => c.ToString());
}
