using System.Text;

namespace Zinnur.Application.Interactive.Grading;

/// <summary>
/// Arabcha javobni solishtirish shakliga keltirish (2026-10-29).
///
/// ★ BIRLIK RENDERERIDAGI <c>norm()</c> NING AYNAN NUSXASI
///   (`ATF-*/birlik_NN/interaktiv.html`):
///   <code>
///   s.replace(/[ً-ٰٟـ]/g, "")      // harakat, tanvin, tatvil
///    .replace(/[أإآٱ]/g, "ا") // أ إ آ ٱ → ا
///    .replace(/[؟?.!،,«»:؛\-—]/g, " ")
///    .replace(/\s+/g, " ").trim()
///   </code>
///   Kontent muallifi kalitlarni shu qoida bilan tekshirgan — server boshqacha
///   normallashtirsa, kitobda "to'g'ri" bo'lgan javob platformada "xato" bo'lardi.
///
/// 🔴 Faqat SERVERDA ishlatiladi: klientda kalit ham, bu funksiya ham yo'q.
/// </summary>
public static class ArabicNorm
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastWasSpace = true; // boshidagi bo'shliq tashlanadi (trim)

        foreach (var ch in value)
        {
            if (IsMark(ch)) continue;

            var mapped = ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                _ => ch,
            };

            if (IsPunctuation(mapped) || char.IsWhiteSpace(mapped))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            sb.Append(mapped);
            lastWasSpace = false;
        }

        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;

        return sb.ToString();
    }

    /// <summary>Ikki javob normallashtirilgandan keyin tengmi.</summary>
    public static bool Same(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static bool IsMark(char ch) =>
        ch is (>= 'ً' and <= 'ٟ') or 'ٰ' or 'ـ';

    private static bool IsPunctuation(char ch) =>
        ch is '؟' or '?' or '.' or '!' or '،' or ',' or '«' or '»' or ':' or '؛' or '-' or '—';
}
