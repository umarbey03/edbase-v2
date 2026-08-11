using Zinnur.Application.Media;

namespace Zinnur.UnitTests.Media;

/// <summary>
/// ========================================================================
/// `Range: bytes=…` — SOF QOIDA TESTLARI
/// ========================================================================
///
/// ★ NIMA UCHUN BU ALOHIDA TESTLANADI: `Range` mantiqi noto'g'ri bo'lsa
/// nosozlik "video oxiriga o'tmaydi" yoki "pleyer faylni buzuq deb
/// hisoblaydi" ko'rinishida chiqadi — ya'ni sababi HTTP javobida
/// ko'rinmaydi va uni faqat brauzerda, qo'lda payqash mumkin. Sof funksiya
/// darajasida esa har holat aniq va arzon tekshiriladi.
///
/// HTTP shartnomasi (jonli isbot) `LessonAssetRangeTests` da.
/// </summary>
public sealed class RangeHeaderTests
{
    private const long Total = 1000;

    // ================================================================= TO'LIQ JAVOB

    /// <summary>Sarlavha yo'q — to'liq javob (200).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_ReturnsNone(string? value)
    {
        RangeHeader.TryParse(value, Total, out _).Should().Be(RangeParseOutcome.None);
    }

    /// <summary>
    /// Tushunarsiz yoki qo'llanmaydigan shakl — TO'LIQ javob, XATO EMAS.
    ///
    /// ★ HTTP standarti aynan shunday talab qiladi: server tushunmagan
    /// `Range` ni E'TIBORSIZ qoldirishi kerak. 400 qaytarish eski yoki
    /// g'ayrioddiy klientlarda videoni butunlay ishlamas qilardi.
    /// </summary>
    [Theory]
    [InlineData("items=0-10")]       // birlik `bytes` emas
    [InlineData("bytes=abc-def")]    // raqam emas
    [InlineData("bytes=")]           // bo'sh
    [InlineData("bytes=10")]         // chiziqcha yo'q
    [InlineData("bytes=200-100")]    // teskari oraliq
    [InlineData("bytes=-0")]         // oxirgi 0 bayt — ma'nosiz
    [InlineData("bytes=+5-10")]      // ishorali son QABUL QILINMAYDI
    [InlineData("bytes=0-10,20-30")] // KO'P oraliq — ataylab qo'llanmaydi
    public void Unsupported_ReturnsNone(string value)
    {
        RangeHeader.TryParse(value, Total, out _).Should().Be(RangeParseOutcome.None);
    }

    /// <summary>
    /// Bo'sh fayl — har qanday oraliq ma'nosiz, lekin 416 ham noto'g'ri
    /// (`bytes * /0` ni hech qanday pleyer kutmaydi).
    /// </summary>
    [Fact]
    public void EmptyFile_ReturnsNone()
    {
        RangeHeader.TryParse("bytes=0-10", totalLength: 0, out _)
            .Should().Be(RangeParseOutcome.None);
    }

    // ================================================================= ANIQ ORALIQ

    /// <summary>★★ ASOSIY HOLAT: `bytes=100-199` -> AYNAN 100 bayt.</summary>
    [Fact]
    public void ExactRange_IsParsedInclusive()
    {
        RangeHeader.TryParse("bytes=100-199", Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.From.Should().Be(100);
        range.To.Should().Be(199);

        // 🔴 UZUNLIK — INKLYUZIV: (199 - 100) + 1 = 100.
        // Bu yerda bitta birlik xato (off-by-one) qilinsa, pleyer har
        // bo'lakda bir bayt yo'qotardi va video jimgina buzilardi.
        range.Length.Should().Be(100);
    }

    /// <summary>Katta-kichik harf va bo'shliqlar e'tiborga olinmaydi.</summary>
    [Theory]
    [InlineData("BYTES=100-199")]
    [InlineData("  bytes= 100 - 199 ")]
    public void Range_IsCaseAndSpaceInsensitive(string value)
    {
        RangeHeader.TryParse(value, Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.From.Should().Be(100);
        range.To.Should().Be(199);
    }

    /// <summary>`bytes=N-` — oxirigacha.</summary>
    [Fact]
    public void OpenEndedRange_ExtendsToLastByte()
    {
        RangeHeader.TryParse("bytes=900-", Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.From.Should().Be(900);
        range.To.Should().Be(999);
        range.Length.Should().Be(100);
    }

    /// <summary>
    /// `bytes=-N` — OXIRGI N bayt. MP4 pleyerlari `moov` atomini aynan
    /// shunday, fayl OXIRIDAN o'qiydi.
    /// </summary>
    [Fact]
    public void SuffixRange_ReadsFromEnd()
    {
        RangeHeader.TryParse("bytes=-200", Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.From.Should().Be(800);
        range.To.Should().Be(999);
        range.Length.Should().Be(200);
    }

    /// <summary>
    /// Oraliq fayldan UZUN bo'lsa QISQARTIRILADI, 416 EMAS — standart
    /// shunday, va pleyerlar ko'pincha ataylab ortiqcha so'raydi
    /// ("qolgan hammasini ber").
    /// </summary>
    [Fact]
    public void RangeBeyondEnd_IsClamped()
    {
        RangeHeader.TryParse("bytes=990-5000", Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.From.Should().Be(990);
        range.To.Should().Be(999);
        range.Length.Should().Be(10);
    }

    /// <summary>Suffiks fayldan uzun bo'lsa — BUTUN fayl.</summary>
    [Fact]
    public void SuffixLongerThanFile_ReturnsWholeFile()
    {
        RangeHeader.TryParse("bytes=-5000", Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.From.Should().Be(0);
        range.To.Should().Be(999);
        range.Length.Should().Be(Total);
    }

    /// <summary>Butun faylni so'rash ham QISMAN javob (206) — bu to'g'ri.</summary>
    [Fact]
    public void FullRange_IsStillPartial()
    {
        RangeHeader.TryParse("bytes=0-999", Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.Length.Should().Be(Total);
    }

    // ================================================================= 416

    /// <summary>
    /// ★ YAGONA 416 HOLATI: boshlanish fayldan TASHQARIDA. Qolgan hamma
    /// tushunarsizlikda to'liq javob beriladi.
    /// </summary>
    [Theory]
    [InlineData("bytes=1000-1010")]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=99999-")]
    public void StartBeyondEnd_IsUnsatisfiable(string value)
    {
        RangeHeader.TryParse(value, Total, out _)
            .Should().Be(RangeParseOutcome.Unsatisfiable);
    }

    /// <summary>Oxirgi bayt HALI fayl ichida (999) — 416 EMAS.</summary>
    [Fact]
    public void LastByte_IsSatisfiable()
    {
        RangeHeader.TryParse("bytes=999-999", Total, out var range)
            .Should().Be(RangeParseOutcome.Satisfiable);

        range.Length.Should().Be(1);
    }
}
