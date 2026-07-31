using Zinnur.Application.Notifications;

namespace Zinnur.UnitTests.Notifications;

/// <summary>
/// Telegram HTML uchun matn tayyorlash.
///
/// NIMA UCHUN MUHIM: <c>parse_mode=HTML</c> bilan yuborilgan xabarda
/// tekshirilmagan <c>&lt;</c> belgisi bo'lsa, Telegram butun so'rovni rad
/// etadi (<c>400: can't parse entities</c>) va xabar UMUMAN yetib bormaydi.
/// Bu jimgina xato: navbatda "yiqildi" deb turadi, o'quvchi esa hech narsa
/// olmaydi.
/// </summary>
public class NotificationTextTests
{
    [Theory]
    [InlineData("<", "&lt;")]
    [InlineData(">", "&gt;")]
    [InlineData("&", "&amp;")]
    [InlineData("a<b>c", "a&lt;b&gt;c")]
    public void Escape_ReplacesHtmlSpecialCharacters(string raw, string expected) =>
        NotificationText.Escape(raw).Should().Be(expected);

    /// <summary>
    /// ★ AMPERSAND IKKI MARTA ekranlanmasin: <c>&amp;lt;</c> ni yana
    /// ekranlash <c>&amp;amp;lt;</c> berardi va foydalanuvchi ekranida
    /// <c>&amp;lt;</c> so'zma-so'z ko'rinardi.
    /// </summary>
    [Fact]
    public void Escape_AppliedOnce_ProducesSingleEntity() =>
        NotificationText.Escape("Ona & Bola").Should().Be("Ona &amp; Bola");

    /// <summary>
    /// O'zbekcha va arabcha matn O'ZGARMAYDI. Bu `WebUtility.HtmlEncode`
    /// dan voz kechish sababi: u har ASCII'dan tashqari harfni
    /// <c>&amp;#1234;</c> ga aylantirib, 4096 belgilik chegarani bir necha
    /// barobar tezroq tugatardi.
    /// </summary>
    [Fact]
    public void Escape_KeepsNonLatinTextAsIs() =>
        NotificationText.Escape("Salom, Abdulloh — alif bo'limi ٱ")
            .Should().Be("Salom, Abdulloh — alif bo'limi ٱ");

    [Fact]
    public void Escape_WithNull_ReturnsEmpty() =>
        NotificationText.Escape(null).Should().BeEmpty();

    // ------------------------------------------------------------ parametr

    [Fact]
    public void Parameter_TrimsAndEscapes() =>
        NotificationText.Parameter("  <b>Ali</b>  ").Should().Be("&lt;b&gt;Ali&lt;/b&gt;");

    /// <summary>Uzun qiymat qirqiladi — chegara PARAMETRGA qo'llanadi, tayyor matnga emas.</summary>
    [Fact]
    public void Parameter_TruncatesBeforeEscaping()
    {
        var result = NotificationText.Parameter(new string('a', 500), maxLength: 10);

        result.Should().Be(new string('a', 10));
    }

    /// <summary>
    /// ★ EMOJI IKKIGA BO'LINMASIN. Surrogat juftlikning o'rtasidan kesilgan
    /// satr Postgres'ga yozilganda buziladi — ya'ni xato faqat chegarasi
    /// emojiga to'g'ri kelgan xabarda, testda emas, produksiyada chiqardi.
    /// </summary>
    [Fact]
    public void Parameter_DoesNotSplitSurrogatePair()
    {
        // "😀" — ikkita UTF-16 kod birligi. 3 belgiga qirqsak, juftlik
        // o'rtasiga tushadi va faqat "a" + to'liq bo'lmagan juftlik qolardi.
        var result = NotificationText.Parameter("a😀b", maxLength: 2);

        result.Should().Be("a");
        result.Should().NotContainAny("\ud83d", "\ude00");
    }

    [Fact]
    public void Parameter_WithNull_ReturnsEmpty() =>
        NotificationText.Parameter(null).Should().BeEmpty();

    /// <summary>Chegara Telegram hujjatidagi qiymat bilan bir xil bo'lib qolsin.</summary>
    [Fact]
    public void MaxBodyLength_MatchesTelegramLimit() =>
        NotificationText.MaxBodyLength.Should().Be(4096);
}
