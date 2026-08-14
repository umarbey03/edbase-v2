using System.Globalization;
using Zinnur.Application.Notifications;
using Zinnur.Application.Telegram;

namespace Zinnur.UnitTests.Notifications;

/// <summary>
/// «Vazifa tekshirildi» matnining IKKI CHIQISH FORMATI.
///
/// ══════════════════════════════════════════════════════════════════════
/// Bu yerda AYNI hodisaning ikki shakli yonma-yon sinaladi va bu ataylab:
///
///   • <see cref="NotificationTemplates"/> — SOF MATN (Vue ro'yxati);
///   • <see cref="TelegramTemplates"/>     — TELEGRAM HTML.
///
/// Ular bir faylda tekshirilsa, kimdir bittasini "soddalashtirib"
/// ikkinchisini unutgan kunda farq DARHOL ko'zga tashlanadi. Ajratilsa,
/// eng ehtimolli xato — sof matnni Telegram'ga yuborish (ismdagi bitta
/// <c>&lt;</c> butun xabarni yo'q qiladi) — sezilmay o'tib ketardi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public class NotificationTemplatesTests
{
    // ================================================================= ball formati

    /// <summary>
    /// 🔴 ENG MUHIM TEST: BALL O'NLIK NUQTA BILAN, VERGUL BILAN EMAS.
    ///
    /// <c>Score</c> va <c>MaxScore</c> — <c>decimal</c>. Konteynerning
    /// madaniyati muhitga bog'liq: <c>ru-RU</c> yoki <c>uz-UZ</c> bo'lsa
    /// standart <c>ToString()</c> "4,5" berardi. Bu xato faqat
    /// PRODUKSIYADA chiqadigan turdan — ishlab chiquvchining mashinasida
    /// odatda <c>en-US</c> turadi va hech qachon ko'rinmasdi.
    ///
    /// Test madaniyatni ATAYLAB vergulli qilib qo'yadi va shu holatda ham
    /// nuqta chiqishini talab qiladi.
    /// </summary>
    [Theory]
    [InlineData("ru-RU")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void FormatScore_UnderCommaDecimalCulture_StillUsesDot(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            NotificationTemplates.FormatScore(4.5m).Should().Be("4.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// ★ BUTUN BALL "5" BO'LIB CHIQADI, "5.00" EMAS. Baza ustuni
    /// <c>numeric(5,2)</c>, ya'ni oddiy <c>ToString()</c> nol kasrlarni ham
    /// chizardi va o'quvchi ekranida "5.00/10.00" turardi.
    /// </summary>
    [Theory]
    [InlineData("5", "5")]
    [InlineData("5.00", "5")]
    [InlineData("4.50", "4.5")]
    [InlineData("4.25", "4.25")]
    [InlineData("0", "0")]
    public void FormatScore_TrimsTrailingZeros(string raw, string expected) =>
        NotificationTemplates.FormatScore(decimal.Parse(raw, CultureInfo.InvariantCulture))
            .Should().Be(expected);

    // ================================================================= sof matn (qo'ng'iroqcha)

    [Fact]
    public void SubmissionGradedBody_WithFeedback_IncludesScoreAndNote()
    {
        var text = NotificationTemplates.SubmissionGradedBody(
            "3-dars uy vazifasi", 4.5m, 5m, "Yaxshi, lekin oxirgi savol chala.");

        text.Should().Contain("3-dars uy vazifasi");
        text.Should().Contain("4.5/5");
        text.Should().Contain("Yaxshi, lekin oxirgi savol chala.");
    }

    /// <summary>
    /// ★ IZOH BO'LMASA — "Izoh:" so'zi ham YO'Q. Bo'sh "Izoh:" o'quvchida
    /// "ustoz nimadir yozganu ko'rinmayapti" degan taassurot qoldirardi.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SubmissionGradedBody_WithoutFeedback_OmitsNoteSection(string? feedback) =>
        NotificationTemplates.SubmissionGradedBody("Vazifa", 5m, 5m, feedback)
            .Should().NotContain("Izoh");

    [Fact]
    public void SubmissionGradedBody_WithoutTitle_UsesFallback() =>
        NotificationTemplates.SubmissionGradedBody(null, 3m, 5m, null)
            .Should().StartWith("Vazifa");

    /// <summary>
    /// ★ QATOR UZILISHI PROBELGA AYLANADI: qo'ng'iroqcha qatori bir-ikki
    /// satrda chiziladi, matn ichida <c>\n</c> bo'lsa qator balandligi
    /// sakrab ketardi.
    /// </summary>
    [Fact]
    public void SubmissionGradedBody_CollapsesNewlinesInFeedback()
    {
        var text = NotificationTemplates.SubmissionGradedBody(
            "Vazifa", 5m, 5m, "Birinchi qator\n\nIkkinchi qator");

        text.Should().NotContain("\n");
        text.Should().Contain("Birinchi qator Ikkinchi qator");
    }

    /// <summary>
    /// 🔴 SOF MATNDA HTML EKRANLASH BO'LMASLIGI SHART. Agar u yerga
    /// <c>NotificationText.Parameter</c> qo'shib qo'yilsa, o'quvchining
    /// Vue ro'yxatida <c>&amp;amp;</c> va <c>&amp;lt;</c> so'zma-so'z
    /// ko'rinardi.
    /// </summary>
    [Fact]
    public void SubmissionGradedBody_DoesNotEscapeHtml()
    {
        var text = NotificationTemplates.SubmissionGradedBody(
            "Ona & Bola", 5m, 5m, "javob < 5 bo'lishi kerak edi");

        text.Should().Contain("Ona & Bola");
        text.Should().Contain("javob < 5");
        text.Should().NotContain("&amp;");
        text.Should().NotContain("&lt;");
    }

    // ================================================================= Telegram HTML

    /// <summary>
    /// 🔴 TELEGRAM YO'LIDA ESKRANLASH SHART VA U AYNI SHU YERDA BUZILADI.
    ///
    /// Ustoz izohida bitta <c>&lt;</c> bo'lsa ("javob &lt; 5 bo'lishi kerak
    /// edi" — mutlaqo odatiy jumla), Telegram butun so'rovni
    /// <c>400: can't parse entities</c> bilan rad etadi va xabar UMUMAN
    /// yetib bormaydi. Navbatda esa u "yiqildi" bo'lib turadi — ya'ni
    /// nosozlik JIMGINA.
    /// </summary>
    [Fact]
    public void SubmissionGradedText_EscapesUserSuppliedHtml()
    {
        var text = TelegramTemplates.SubmissionGradedText(
            "Ona & Bola <3", 5m, 5m, "javob < 5 bo'lishi kerak edi");

        text.Should().Contain("Ona &amp; Bola &lt;3");
        text.Should().Contain("javob &lt; 5");

        // Shablonning O'Z teglari esa ekranlanmaydi — aks holda o'quvchi
        // so'zma-so'z `<b>` ni ko'rardi.
        text.Should().Contain("<b>Vazifangiz tekshirildi</b>");
    }

    /// <summary>Telegram matnida ham ball nuqta bilan formatlanadi.</summary>
    [Fact]
    public void SubmissionGradedText_UnderCommaDecimalCulture_StillUsesDot()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            TelegramTemplates.SubmissionGradedText("Vazifa", 4.5m, 5m, null)
                .Should().Contain("<b>4.5</b> / 5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SubmissionGradedText_WithoutFeedback_OmitsNoteLine(string? feedback) =>
        TelegramTemplates.SubmissionGradedText("Vazifa", 5m, 5m, feedback)
            .Should().NotContain("💬");

    /// <summary>
    /// ★ MATN NAVBATNING CHEGARASIGA SIG'ADI. <c>OutboxWriter</c> uzun
    /// matnni KESMAYDI — RAD ETADI (istisno bilan), ya'ni chegaradan
    /// oshgan xabar baholash tranzaksiyasini yiqitardi.
    /// </summary>
    [Fact]
    public void SubmissionGradedText_WithExtremeInput_FitsOutboxLimit()
    {
        var text = TelegramTemplates.SubmissionGradedText(
            new string('t', 5000), 999.99m, 1000m, new string('f', 5000));

        text.Length.Should().BeLessThan(NotificationText.MaxBodyLength);
    }

    /// <summary>
    /// ★ KALIT «TUGMASIZ» TARMOQQA TUSHADI. <c>MarkupFor</c> noma'lum
    /// kalitni <c>None</c> qiladi — bu test qarorni ATAYLAB qulflaydi:
    /// kimdir «Ilovani ochish» tugmasini qo'shsa, u Mini App'ni BOSH
    /// SAHIFADA ochib, tugmaning va'dasini buzardi (chuqur havola yo'q).
    /// </summary>
    [Fact]
    public void SubmissionGraded_HasNoKeyboard() =>
        TelegramTemplates.MarkupFor(TelegramTemplates.SubmissionGraded)
            .Should().Be(TelegramMarkup.None);
}
