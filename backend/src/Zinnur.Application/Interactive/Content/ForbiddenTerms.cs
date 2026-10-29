using System.Globalization;
using System.Text;

namespace Zinnur.Application.Interactive.Content;

/// <summary>Ruxsat etilgan topilma: kitob + birlik + atama (+ sabab).</summary>
public sealed record ForbiddenTermAllow(string Book, string Unit, string Term, string Reason);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TAQIQLANGAN ATAMALAR TEKSHIRUVI — HAR IMPORTDA (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasining qoidasi: o'quvchi ko'radigan hech narsada diniy atama
/// bo'lmaydi. Kontent kitobdan olinadi, shuning uchun har import uni qayta
/// tekshiradi.
///
/// ★ RO'YXAT: <c>frontend/src/shared/lib/sensitive-words.ts</c> dagi atamalar
///   (chat va javob ogohlantirishi) + `00_ATF_reja.md` 3-bo'limidagi kitob
///   atamalari. ⚠️ Ro'yxatlar ikki joyda — birini o'zgartirsangiz ikkinchisini
///   ham yangilang (TODO: bitta JSON manbaga ko'chirish).
/// ★ So'z chegarasi bilan: lotin — kichik harf, tutuq belgisiz; arabcha —
///   harakatsiz, to'liq so'z ("الحجر" — tosh — "الحج" deb topilmaydi).
/// ★ `ustozga` va ustoz qo'llanmasi alohida hisoblanadi: ular faqat xodimga
///   ko'rinadi (egasi qarori, 2026-10-29), topilma — ogohlantirish, xato emas.
/// </summary>
public static class ForbiddenTerms
{
    private static readonly string[] PrefixStems =
    [
        "alloh", "olloh", "xudo", "xudoy", "quron", "qoron", "kuran",
        "namoz", "paygambar", "rasulul", "masjid", "jannat", "dozax", "oxirat",
        "hadis", "zikr", "tavba", "istigfor", "zakot", "momin", "kofir", "shayton",
        "farishta", "bismillah", "bismilloh", "inshaalloh", "inshoalloh", "mashaalloh",
        "mashoalloh", "subhanalloh", "alhamdulillah", "astagfirulloh", "sahoba",
        "shariat", "oyat", "duo",

        // 00_ATF_reja.md, 3-bo'lim, 1-qoida
        "mahshar", "zamzam", "kavsar",
    ];

    private static readonly string[] ExactWords = ["imom", "haj", "umra", "iymon", "sura", "suralar"];

    private static readonly string[] ArabicPhrases =
    [
        "الله", "قرآن", "القرآن", "صلاة", "الصلاة", "مسجد", "المسجد", "الجنة",
        "النبي", "سورة", "آية", "ان شاء", "إن شاء", "سبحان", "بسم الله",

        // 00_ATF_reja.md, 3-bo'lim — kitobdagi lug'aviy so'zlar (Amaliyot-2
        // matnlarida egasi ATAYLAB qoldirgan; allow-ro'yxat bilan o'tadi).
        "صدقة", "الصدقة", "الجمعة", "لبيك", "زمزم", "الكوثر", "السلام عليكم",
        "استغفر", "الآخرة", "الحج", "طاهر", "نجس",
    ];

    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['ғ'] = "g", ['д'] = "d", ['е'] = "e",
        ['ё'] = "yo", ['ж'] = "j", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['қ'] = "q",
        ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['ў'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "x", ['ҳ'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['ъ'] = "", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary>Matndagi taqiqlangan atamalar (lotin — yozilgan shaklda, arabcha — ro'yxatdagi shaklda).</summary>
    public static IReadOnlyList<string> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var found = new List<string>();

        foreach (var raw in SplitLatin(text))
        {
            if (raw.Length < 3) continue;

            var word = NormalizeLatin(raw);
            if (ExactWords.Contains(word, StringComparer.Ordinal)
                || PrefixStems.Any(stem => word.StartsWith(stem, StringComparison.Ordinal)))
            {
                found.Add(raw);
            }
        }

        var arabic = " " + string.Join(' ', ArabicTokens(text)) + " ";
        foreach (var phrase in ArabicPhrases)
        {
            if (arabic.Contains(" " + phrase + " ", StringComparison.Ordinal))
                found.Add(phrase);
        }

        return found.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> SplitLatin(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if ((char.IsLetter(ch) && !IsArabic(ch)) || ch is '\'' or 'ʻ' or 'ʼ' or '‘' or '’' or '`')
            {
                sb.Append(ch);
                continue;
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0) yield return sb.ToString();
    }

    private static string NormalizeLatin(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var ch in word.ToLower(CultureInfo.InvariantCulture))
        {
            if (ch is '\'' or 'ʻ' or 'ʼ' or '‘' or '’' or '`') continue;
            if (Cyrillic.TryGetValue(ch, out var latin)) sb.Append(latin);
            else sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>Arabcha so'zlar — harakat va tatvilsiz.</summary>
    private static IEnumerable<string> ArabicTokens(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch is (>= 'ً' and <= 'ٟ') or 'ٰ' or 'ـ') continue;

            if (IsArabic(ch) && char.IsLetter(ch))
            {
                sb.Append(ch);
                continue;
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0) yield return sb.ToString();
    }

    private static bool IsArabic(char ch) => ch is >= '؀' and <= 'ۿ';
}
