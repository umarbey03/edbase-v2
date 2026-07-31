using Zinnur.Application.Settings;

namespace Zinnur.UnitTests.Settings;

/// <summary>
/// ========================================================================
/// QIYMAT TEKSHIRUVI VA KANONIK SHAKLGA KELTIRISH
/// ========================================================================
///
/// ★ NIMA UCHUN ALOHIDA SINALADI: bu qoidalar HTTP 400 javobining sababini
/// belgilaydi va bazaga nima tushishini hal qiladi. Ular faqat endpoint
/// orqali sinalsa, har chegara uchun to'liq integratsiya testi kerak
/// bo'lardi — natijada ko'pchiligi umuman yozilmasdi.
/// </summary>
public class SettingValueParserTests
{
    private static readonly SettingDefinition Threshold =
        Get(SettingsRegistry.Keys.BlockThreshold);

    private static readonly SettingDefinition Scope =
        Get(SettingsRegistry.Keys.BlockScope);

    private static readonly SettingDefinition Url = new()
    {
        Key = "test.url",
        Group = SettingGroup.General,
        DisplayName = "Sinov manzili",
        Description = "Faqat testda ishlatiladi.",
        Kind = SettingValueKind.Text,
        Format = SettingFormat.Url,
        Source = SettingSource.Database,
    };

    private static readonly SettingDefinition Secret = new()
    {
        Key = "test.secret",
        Group = SettingGroup.Security,
        DisplayName = "Sinov siri",
        Description = "Faqat testda ishlatiladi.",
        Kind = SettingValueKind.Secret,
        Source = SettingSource.Database,
    };

    private static readonly SettingDefinition Toggle = new()
    {
        Key = "test.toggle",
        Group = SettingGroup.General,
        DisplayName = "Sinov kaliti",
        Description = "Faqat testda ishlatiladi.",
        Kind = SettingValueKind.Toggle,
        Source = SettingSource.Database,
    };

    // ================================================================= son / pul

    /// <summary>Salbiy chegara — moliya uchun ma'nosiz va bloklashni buzardi.</summary>
    [Fact]
    public void NegativeThreshold_IsRejected()
    {
        SettingValueParser.TryNormalize(Threshold, "-1", out _, out var error)
            .Should().BeFalse();

        error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Nol qo'shib yuborish (5 400 000 o'rniga 54 000 000) hech kimni
    /// bloklamaydigan holatga olib kelardi va buni hech kim sezmasdi.
    /// </summary>
    [Fact]
    public void AbsurdlyLargeThreshold_IsRejected() =>
        SettingValueParser.TryNormalize(Threshold, "999999999", out _, out _)
            .Should().BeFalse();

    [Fact]
    public void NonNumericThreshold_IsRejected() =>
        SettingValueParser.TryNormalize(Threshold, "olti yuz ming", out _, out _)
            .Should().BeFalse();

    /// <summary>Baza ustuni `numeric(18,2)` — uchinchi kasr raqami jimgina yo'qolardi.</summary>
    [Fact]
    public void ThirdDecimalDigit_IsRejected() =>
        SettingValueParser.TryNormalize(Threshold, "600000.123", out _, out _)
            .Should().BeFalse();

    [Fact]
    public void ValidThreshold_IsAccepted()
    {
        SettingValueParser.TryNormalize(Threshold, " 600000 ", out var normalized, out _)
            .Should().BeTrue();

        // Bo'shliqlar kesiladi — nusxa-joylashtirishdagi eng ko'p uchraydigan
        // "ko'rinmas" xato bazaga tushmasin.
        normalized.Should().Be("600000");
    }

    // ================================================================= tanlov

    [Fact]
    public void UnknownChoice_IsRejectedWithAllowedList()
    {
        SettingValueParser.TryNormalize(Scope, "Hammasi", out _, out var error)
            .Should().BeFalse();

        error.Should().Contain("Platform");
    }

    /// <summary>
    /// Eski tizimdan ko'chirilgan `"video"` qabul qilinadi, LEKIN bazaga
    /// kanonik `"Video"` yoziladi — aks holda jadvalda ikki xil yozuv
    /// yonma-yon turardi.
    /// </summary>
    [Fact]
    public void LegacyLowercaseChoice_IsNormalized()
    {
        SettingValueParser.TryNormalize(Scope, "video", out var normalized, out _)
            .Should().BeTrue();

        normalized.Should().Be("Video");
    }

    // ================================================================= manzil

    [Theory]
    [InlineData("localhost:7880")]
    [InlineData("/api/v1")]
    [InlineData("shunchaki matn")]
    public void BrokenUrl_IsRejected(string value) =>
        SettingValueParser.TryNormalize(Url, value, out _, out _).Should().BeFalse();

    [Theory]
    [InlineData("https://livekit.zinnur.uz")]
    [InlineData("wss://livekit.zinnur.uz")]
    [InlineData("http://livekit:7880")]
    public void ValidUrl_IsAccepted(string value) =>
        SettingValueParser.TryNormalize(Url, value, out _, out _).Should().BeTrue();

    // ================================================================= mantiqiy

    [Fact]
    public void Toggle_NormalizesToLowercase()
    {
        SettingValueParser.TryNormalize(Toggle, "True", out var normalized, out _)
            .Should().BeTrue();

        normalized.Should().Be("true");
    }

    [Fact]
    public void Toggle_RejectsAnythingElse() =>
        SettingValueParser.TryNormalize(Toggle, "ha", out _, out _).Should().BeFalse();

    // ================================================================= sir

    /// <summary>
    /// Bo'sh sir RAD ETILADI: maskalangan maydonni "tegmayman" deb bo'sh
    /// qoldirib saqlash mavjud sirni JIMGINA o'chirib yuborardi va
    /// integratsiya keyinroq, sababsiz ishdan chiqardi.
    /// </summary>
    [Fact]
    public void EmptySecret_IsRejected() =>
        SettingValueParser.TryNormalize(Secret, "   ", out _, out _).Should().BeFalse();

    [Fact]
    public void TooLongValue_IsRejected() =>
        SettingValueParser.TryNormalize(Secret, new string('a', 5000), out _, out _)
            .Should().BeFalse();

    // ================================================================= o'qish

    /// <summary>
    /// Buzuq saqlangan qiymat ilovani YIQITMAYDI — o'qish `false` qaytaradi
    /// va chaqiruvchi standartga qaytadi.
    /// </summary>
    [Fact]
    public void CorruptStoredValue_IsReportedNotThrown()
    {
        SettingValueParser.TryReadDecimal(Threshold, "buzuq", out _).Should().BeFalse();
        SettingValueParser.TryReadDecimal(Threshold, "-5", out _).Should().BeFalse();
        SettingValueParser.TryReadBool("ha", out _).Should().BeFalse();
    }

    private static SettingDefinition Get(string key)
    {
        SettingsRegistry.TryGet(key, out var definition).Should().BeTrue();
        return definition;
    }
}
