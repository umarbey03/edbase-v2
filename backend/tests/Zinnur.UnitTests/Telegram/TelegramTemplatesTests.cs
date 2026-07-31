using Zinnur.Application.Telegram;

namespace Zinnur.UnitTests.Telegram;

/// <summary>
/// Bot javoblarining matni va tugma tanlovi.
///
/// ★ ESCAPE MUHIM: matn Telegram'ga <c>parse_mode=HTML</c> bilan ketadi.
/// Ismda <c>&lt;</c> bo'lsa va u ekranlanmasa, Telegram BUTUN xabarni
/// <c>400 can't parse entities</c> bilan rad etadi — ya'ni o'quvchi
/// bog'lanish haqidagi javobni UMUMAN olmaydi.
/// </summary>
public class TelegramTemplatesTests
{
    // ------------------------------------------------------------------ tugmalar

    /// <summary>
    /// Telefon SO'RALADIGAN har holatda «Raqamni ulashish» tugmasi
    /// bo'lishi shart — busiz foydalanuvchi raqamni umuman yubora olmaydi
    /// (qo'lda kiritish yo'li ATAYLAB yo'q).
    /// </summary>
    [Theory]
    [InlineData(TelegramTemplates.StartUnlinked)]
    [InlineData(TelegramTemplates.ContactUnknown)]
    [InlineData(TelegramTemplates.ContactMismatch)]
    public void MarkupFor_WhenPhoneIsNeeded_RequestsContact(string templateKey)
    {
        TelegramTemplates.MarkupFor(templateKey).Should().Be(TelegramMarkup.RequestContact);
    }

    [Theory]
    [InlineData(TelegramTemplates.StartLinked)]
    [InlineData(TelegramTemplates.ContactLinked)]
    public void MarkupFor_WhenLinked_OffersApp(string templateKey)
    {
        TelegramTemplates.MarkupFor(templateKey).Should().Be(TelegramMarkup.OpenApp);
    }

    /// <summary>
    /// Boshqa modullarning xabarlari (eslatma, yozuv tayyor) tugmasiz
    /// ketadi — yuboruvchi ularga hech narsa qo'shmasligi kerak.
    /// </summary>
    [Theory]
    [InlineData("lesson_reminder")]
    [InlineData("recording_ready")]
    [InlineData("")]
    [InlineData(null)]
    public void MarkupFor_WithUnknownTemplate_IsNone(string? templateKey)
    {
        TelegramTemplates.MarkupFor(templateKey).Should().Be(TelegramMarkup.None);
    }

    // ------------------------------------------------------------------ escape

    [Fact]
    public void StartLinkedText_EscapesUserData_ButKeepsMarkup()
    {
        var text = TelegramTemplates.StartLinkedText("<script>Ali & Vali");

        text.Should().Contain("&lt;script&gt;Ali &amp; Vali",
            "foydalanuvchi ma'lumoti ekranlanishi shart");

        text.Should().Contain("<b>", "shablonning O'Z teglari ekranlanmasligi kerak");
    }

    [Fact]
    public void ContactLinkedText_EscapesUserData()
    {
        TelegramTemplates.ContactLinkedText("A<B>C").Should().Contain("A&lt;B&gt;C");
    }

    [Fact]
    public void StartLinkedText_WithoutName_DoesNotThrow()
    {
        TelegramTemplates.StartLinkedText(null).Should().NotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------------ chegara

    /// <summary>
    /// Har matn Telegram chegarasiga (va navbat ustuniga) sig'ishi kerak:
    /// undan uzun bo'lsa `INotificationOutbox` yozuvni RAD ETADI.
    /// </summary>
    [Fact]
    public void AllTexts_FitTelegramLimit()
    {
        string[] texts =
        [
            TelegramTemplates.StartUnlinkedText(),
            TelegramTemplates.StartLinkedText("Demo O'quvchi"),
            TelegramTemplates.ContactLinkedText("Demo O'quvchi"),
            TelegramTemplates.ContactUnknownText(),
            TelegramTemplates.ContactStaffText(),
            TelegramTemplates.ContactMismatchText(),
            TelegramTemplates.ContactProfileTakenText(),
            TelegramTemplates.ContactTelegramTakenText(),
            TelegramTemplates.ContactInactiveText(),
            TelegramTemplates.HelpText(),
        ];

        foreach (var text in texts)
        {
            text.Should().NotBeNullOrWhiteSpace();
            text.Length.Should().BeLessThan(
                Zinnur.Application.Notifications.NotificationText.MaxBodyLength);
        }
    }

    /// <summary>Kalitlar navbat ustuniga (64 belgi) sig'ishi shart.</summary>
    [Fact]
    public void AllTemplateKeys_FitColumn()
    {
        string[] keys =
        [
            TelegramTemplates.StartUnlinked,
            TelegramTemplates.StartLinked,
            TelegramTemplates.ContactLinked,
            TelegramTemplates.ContactUnknown,
            TelegramTemplates.ContactStaff,
            TelegramTemplates.ContactMismatch,
            TelegramTemplates.ContactProfileTaken,
            TelegramTemplates.ContactTelegramTaken,
            TelegramTemplates.ContactInactive,
            TelegramTemplates.Help,
        ];

        foreach (var key in keys)
            key.Length.Should().BeLessThanOrEqualTo(64);
    }
}
