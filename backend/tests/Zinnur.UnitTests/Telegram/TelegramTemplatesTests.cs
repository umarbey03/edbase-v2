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
            TelegramTemplates.LoginCodeText("123456", TimeSpan.FromMinutes(5)),
            TelegramTemplates.LoginLinkExpiredText(),
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
            TelegramTemplates.LoginCode,
            TelegramTemplates.LoginLinkExpired,
        ];

        foreach (var key in keys)
            key.Length.Should().BeLessThanOrEqualTo(64);
    }

    // ------------------------------------------------------------------ kirish kodi

    /// <summary>
    /// 🔴 KOD `&lt;code&gt;` TEGIDA BO'LISHI SHART.
    ///
    /// Ikki sabab, ikkalasi ham amaliy:
    ///   1) Telegram `&lt;code&gt;` bloki bosilganda matnni BUFERGA nusxalaydi —
    ///      kodni qo'lda ko'chirish esa eng ko'p xato qilinadigan qadam va
    ///      har xato urinish 5 talik chegarani yeydi;
    ///   2) integratsion test kodni AYNAN shu teg bo'yicha topadi
    ///      (`PhoneLoginEndpointsTests`) — teg olib tashlansa butun
    ///      uchdan-uchgacha oqim testi jimgina ishlamay qolardi.
    /// </summary>
    [Fact]
    public void LoginCodeText_WrapsCodeInCodeTag()
    {
        var text = TelegramTemplates.LoginCodeText("045218", TimeSpan.FromMinutes(5));

        text.Should().Contain("<code>045218</code>");
    }

    /// <summary>
    /// ★ MUDDAT MATNDA KO'RSATILADI va u DAQIQAGA yaxlitlanadi.
    ///
    /// "Kod amal qiladi" degan noaniq jumla foydalanuvchini kutishga yoki
    /// darhol qayta so'rashga majbur qilardi — ikkinchisi 60 sekundlik
    /// oynaga urilib, "hech narsa ishlamayapti" taassurotini berardi.
    /// </summary>
    [Fact]
    public void LoginCodeText_MentionsLifetimeInMinutes()
    {
        var text = TelegramTemplates.LoginCodeText("045218", TimeSpan.FromMinutes(5));

        text.Should().Contain("5 daqiqa");
    }

    /// <summary>
    /// 🔴 IJTIMOIY MUHANDISLIKKA QARSHI OGOHLANTIRISH — MAJBURIY QISM.
    ///
    /// Bir martalik kodlarga qarshi eng keng tarqalgan hujum texnik emas:
    /// hujumchi qurbonga qo'ng'iroq qilib "o'quv markazi xodimiman, kodni
    /// ayting" deydi. Server tomonida bunga qarshi HECH QANDAY chora yo'q —
    /// faqat xabar matnining o'zi ogohlantira oladi. Shuning uchun bu
    /// jumla shablon "bezagi" emas, XAVFSIZLIK talabi va u test bilan
    /// qulflanadi.
    /// </summary>
    [Fact]
    public void LoginCodeText_WarnsAgainstSharingTheCode()
    {
        var text = TelegramTemplates.LoginCodeText("045218", TimeSpan.FromMinutes(5));

        text.Should().Contain("hech kimga aytmang");
    }

    /// <summary>
    /// Juda qisqa muddat ham matnda 0 emas, kamida 1 daqiqa deb ko'rinadi —
    /// "0 daqiqa yaroqli" degan xabar ma'nosiz bo'lardi.
    /// </summary>
    [Fact]
    public void LoginCodeText_NeverShowsZeroMinutes()
    {
        var text = TelegramTemplates.LoginCodeText("045218", TimeSpan.FromSeconds(20));

        text.Should().Contain("1 daqiqa");
    }
}
