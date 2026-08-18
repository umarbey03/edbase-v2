using System.Globalization;

namespace Zinnur.Application.Absentees;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// XABAR MATNIDAGI O'RIN EGALLOVCHILAR (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NEGA KERAK: kelmaganlarga yuboriladigan xabar SHAXSIY bo'lishi
/// kerak — "Assalomu alaykum" bilan boshlangan bir xil matn 40 kishiga
/// ketsa, u e'longa o'xshab qoladi va o'qilmaydi. Ism, guruh va dars
/// sanasi qo'yilgan xabar esa aynan o'sha o'quvchiga qaratilgan bo'ladi.
///
/// ★ NEGA UMUMIY SHABLON DVIGATELI EMAS: loyihada shablonlar
/// (<c>MessageTemplate</c>) shunchaki MATN — o'rin egallovchi tushunchasi
/// umuman yo'q. To'liq dvigatel qurish (shartlar, sikllar, filtrlar)
/// hozircha hech qayerda kerak emas. Bu yerda esa ATIGI besh kalit bor
/// va ular QAT'IY ro'yxat — noma'lum kalit matnda o'zgarishsiz qoladi,
/// ya'ni xato yozilgan `{ism}` o'quvchiga bo'sh joy bo'lib ketmaydi.
/// </summary>
public static class AbsenceNoticePlaceholders
{
    /// <summary>UI'da ko'rsatiladigan ro'yxat — matn maydoni ostidagi maslahat.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "{ism}",
        "{guruh}",
        "{sana}",
        "{vaqt}",
        "{ustoz}",
    ];

    /// <summary>
    /// Matndagi kalitlarni haqiqiy qiymatlar bilan almashtiradi.
    /// </summary>
    /// <param name="teacherName">Ustoz tayinlanmagan bo'lsa bo'sh qoldiriladi.</param>
    public static string Apply(
        string body,
        string studentName,
        string groupName,
        DateTimeOffset sessionStart,
        TimeZoneInfo zone,
        string? teacherName)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(sessionStart, zone);

        return (body ?? string.Empty)
            .Replace("{ism}", studentName, StringComparison.Ordinal)
            .Replace("{guruh}", groupName, StringComparison.Ordinal)
            .Replace("{sana}", local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{vaqt}", local.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{ustoz}", teacherName ?? string.Empty, StringComparison.Ordinal);
    }
}
