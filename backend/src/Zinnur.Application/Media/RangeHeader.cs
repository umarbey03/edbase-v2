using System.Globalization;

namespace Zinnur.Application.Media;

/// <summary>`Range` sarlavhasini o'qish natijasi.</summary>
public enum RangeParseOutcome
{
    /// <summary>
    /// Sarlavha yo'q, tushunarsiz yoki bir nechta oraliq so'ralgan —
    /// TO'LIQ javob (`200`) beriladi.
    /// </summary>
    None = 0,

    /// <summary>Oraliq to'g'ri va fayl ichida — qisman javob (`206`).</summary>
    Satisfiable = 1,

    /// <summary>
    /// Oraliq fayl chegarasidan TASHQARIDA — `416` va
    /// `Content-Range: bytes */&lt;hajm&gt;`.
    /// </summary>
    Unsatisfiable = 2,
}

/// <summary>
/// ========================================================================
/// `Range: bytes=…` SARLAVHASINI O'QISH — SOF FUNKSIYA
/// ========================================================================
///
/// ★ NIMA UCHUN QO'LDA YOZILDI, `File(..., enableRangeProcessing: true)`
/// EMAS: MVC'ning tayyor mexanizmi IZLANADIGAN (seekable) oqimni talab
/// qiladi. Bizdagi oqim esa OMBORDAN kelayotgan TARMOQ oqimi — u
/// izlanmaydi, ya'ni MVC butun faylni avval xotiraga/diskka tushirishga
/// majbur bo'lardi. 1 GB video uchun bu yo'l umuman yaramaydi. Shuning
/// uchun oraliq SHU YERDA hisoblanadi va OMBORGA uzatiladi — izlash
/// S3/MinIO tomonida bo'ladi.
///
/// ★★ NIMA UCHUN `Range` UMUMAN MAJBURIY: usiz brauzer videoning oxiriga
/// o'ta olmaydi (seek ishlamaydi) va har ko'rishda faylni BOSHIDAN oqizadi.
/// ~1 GB dars videosi uchun bu funksiyani foydasiz qiladi.
///
/// ── QO'LLANADIGAN SHAKLLAR ─────────────────────────────────────────────
///     bytes=100-199   -> aniq oraliq
///     bytes=100-      -> 100-baytdan oxirigacha
///     bytes=-200      -> OXIRGI 200 bayt (moslashuvchan pleyerlar shunday
///                        so'rab, MP4 `moov` atomini oxiridan o'qiydi)
///
/// ── ATAYLAB QO'LLANMAYDIGAN ────────────────────────────────────────────
/// KO'P ORALIQ (`bytes=0-1,5-6`) — javob `multipart/byteranges` bo'lishi
/// kerak edi. HTTP standarti serverga bunday sarlavhani E'TIBORSIZ
/// qoldirishga ruxsat beradi, brauzerlar esa video uchun ko'p oraliq
/// so'ramaydi. Yarim ishlaydigan `multipart` javob esa pleyerni jimgina
/// buzardi — shuning uchun bu holatda TO'LIQ fayl beriladi.
/// </summary>
public static class RangeHeader
{
    /// <summary>Faqat `bytes` birligi tushunarli.</summary>
    private const string Unit = "bytes=";

    /// <summary>
    /// Sarlavhani o'qiydi va oraliqni FAYL HAJMI bo'yicha normallashtiradi.
    /// </summary>
    /// <param name="value">`Range` sarlavhasining xom qiymati (bo'lmasa <c>null</c>).</param>
    /// <param name="totalLength">Faylning to'liq hajmi (bazadan).</param>
    /// <param name="range">
    /// Natija <see cref="RangeParseOutcome.Satisfiable"/> bo'lganda —
    /// IKKI CHEGARASI HAM ANIQ oraliq.
    /// </param>
    public static RangeParseOutcome TryParse(
        string? value, long totalLength, out MediaByteRange range)
    {
        range = null!;

        if (string.IsNullOrWhiteSpace(value)) return RangeParseOutcome.None;

        // Bo'sh fayl uchun har qanday oraliq ma'nosiz. `416` ham noto'g'ri
        // bo'lardi (`Content-Range: bytes */0` — hech qanday pleyer bunday
        // javobni kutmaydi), shuning uchun to'liq (bo'sh) javob beriladi.
        if (totalLength <= 0) return RangeParseOutcome.None;

        var text = value.Trim();

        if (!text.StartsWith(Unit, StringComparison.OrdinalIgnoreCase))
            return RangeParseOutcome.None;

        var spec = text[Unit.Length..].Trim();

        // KO'P ORALIQ — ataylab qo'llanmaydi (izoh: sinf sarlavhasida).
        if (spec.Contains(',', StringComparison.Ordinal)) return RangeParseOutcome.None;

        var dash = spec.IndexOf('-', StringComparison.Ordinal);

        if (dash < 0) return RangeParseOutcome.None;

        var fromText = spec[..dash].Trim();
        var toText = spec[(dash + 1)..].Trim();

        // ---- `bytes=-N` : OXIRGI N bayt ----
        if (fromText.Length == 0)
        {
            if (!TryNumber(toText, out var suffixLength) || suffixLength <= 0)
                return RangeParseOutcome.None;

            // Fayldan uzun so'ralsa — butun fayl (`416` EMAS): standart
            // aynan shunday talab qiladi.
            var start = Math.Max(0, totalLength - suffixLength);

            range = new MediaByteRange(start, totalLength - 1);
            return RangeParseOutcome.Satisfiable;
        }

        if (!TryNumber(fromText, out var from)) return RangeParseOutcome.None;

        // ★ BOSHLANISH FAYLDAN TASHQARIDA -> 416. Bu YAGONA holat: qolgan
        //   hamma tushunarsizlikda to'liq javob beriladi.
        if (from >= totalLength) return RangeParseOutcome.Unsatisfiable;

        // ---- `bytes=N-` : oxirigacha ----
        if (toText.Length == 0)
        {
            range = new MediaByteRange(from, totalLength - 1);
            return RangeParseOutcome.Satisfiable;
        }

        // ---- `bytes=N-M` ----
        if (!TryNumber(toText, out var to)) return RangeParseOutcome.None;

        if (to < from) return RangeParseOutcome.None;

        // Oxiri fayldan uzun bo'lsa QISQARTIRILADI (`416` emas) — standart
        // shunday, va pleyerlar ko'pincha ataylab ortiqcha so'raydi.
        range = new MediaByteRange(from, Math.Min(to, totalLength - 1));

        return RangeParseOutcome.Satisfiable;
    }

    /// <summary>
    /// Manfiy bo'lmagan butun son. `long.TryParse` o'zi `+`/`-` va
    /// bo'shliqni qabul qilardi — bu yerda faqat RAQAM ruxsat etiladi.
    /// </summary>
    private static bool TryNumber(string text, out long value)
    {
        value = 0;

        if (text.Length == 0) return false;

        foreach (var symbol in text)
        {
            if (symbol is < '0' or > '9') return false;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
