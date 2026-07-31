using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Services;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.WebApi.Controllers;

namespace Zinnur.IntegrationTests.Telegram;

/// <summary>
/// Telegram SOZLANMAGAN holat.
///
/// ★ ILOVA ODATDAGIDEK KO'TARILADI (`StorageOptions` falsafasi): dev
/// mashinasida hech kimda bot tokeni yo'q va bu butun platformani
/// to'xtatib qo'ymasligi kerak. Lekin Telegram funksiyalari BUTUNLAY
/// o'chiq bo'ladi.
///
/// Odatiy <see cref="ZinnurApiFactory"/> ishlatiladi — u `Telegram:*`
/// kalitlarini umuman bermaydi.
/// </summary>
public sealed class TelegramDisabledTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>
    /// ★ SIR SOZLANMAGAN BO'LSA WEBHOOK UMUMAN ISHLAMAYDI.
    /// Ochiq qolgandan ko'ra o'chiq bo'lgani xavfsiz: sirsiz webhook
    /// "istalgan odam qalbaki kontakt yuborishi mumkin" degani.
    /// </summary>
    [Fact]
    public async Task Webhook_WhenNotConfigured_IsNotFound()
    {
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/telegram/webhook")
        {
            Content = new StringContent(
                """{"update_id":1,"message":{"message_id":1,"from":{"id":1},"chat":{"id":1,"type":"private"},"text":"/start"}}""",
                Encoding.UTF8,
                "application/json"),
        };

        // Hatto "to'g'ri ko'rinishdagi" sir bilan ham.
        request.Headers.TryAddWithoutValidation(TelegramController.SecretHeader, "har-qanday-sir");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "sozlanmagan webhook mavjud emasdek ko'rinishi kerak");
    }

    /// <summary>Mini App kirishi 503 — bu bizning bug'imiz emas, sozlanmagan xizmat.</summary>
    [Fact]
    public async Task MiniAppAuth_WhenNotConfigured_IsServiceUnavailable()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            new Uri("/api/v1/telegram/mini-app/auth", UriKind.Relative),
            new StringContent("""{"initData":"user=x&hash=y"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Sozlanmagan holatda vaqtinchalik log-yuboruvchi KUCHDA QOLADI —
    /// navbat oqimini dev mashinasida uchdan-uchgacha sinash mumkin bo'lsin.
    /// </summary>
    [Fact]
    public void MessageSender_WhenNotConfigured_StaysLogging()
    {
        using var scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider
            .GetServices<IMessageSender>()
            .Last(s => s.Channel == NotificationChannel.Telegram);

        sender.Should().BeOfType<LoggingMessageSender>();
    }
}

/// <summary>
/// Telegram SOZLANGAN holatda haqiqiy yuboruvchi ishlatilishini qo'riqlaydi.
///
/// ★ NIMA UCHUN ALOHIDA TEST: `OutboxDispatcher` bir kanalga ikkita
/// yuboruvchi bo'lsa OXIRGISINI tanlaydi, ya'ni to'g'ri ishlash
/// `Program.cs` dagi QATOR TARTIBIGA bog'liq. Kimdir `AddZinnurTelegram`
/// ni yuqoriga ko'chirsa, xabarlar Telegram'ga emas LOGGA ketardi va buni
/// hech kim sezmasdi — jimgina ishlamay qolish eng yomon nosozlik turi.
/// </summary>
public sealed class TelegramSenderRegistrationTests(TelegramApiFactory factory)
    : IClassFixture<TelegramApiFactory>
{
    [Fact]
    public void MessageSender_WhenConfigured_IsTelegram()
    {
        using var scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider
            .GetServices<IMessageSender>()
            .Last(s => s.Channel == NotificationChannel.Telegram);

        sender.Should().BeOfType<TelegramMessageSender>(
            "`AddZinnurTelegram` `AddZinnurNotifications` dan KEYIN chaqirilishi shart");
    }

    /// <summary>
    /// ★★ SIR SIZIB CHIQISHIGA QARSHI REGRESSIYA TESTI.
    ///
    /// `IHttpClientFactory` ning standart log ilgagi har so'rovda TO'LIQ
    /// manzilni yozadi, Telegram tokeni esa AYNAN manzil ichida
    /// (`/bot&lt;token&gt;/sendMessage`). Jonli sinovda bu token konteyner
    /// logida 8 marta ochiq ko'rindi. `RemoveAllLoggers()` uni o'chiradi va
    /// shu bayroq (`SuppressDefaultLogging`) uning kuchda ekanini isbotlaydi.
    ///
    /// Kimdir uni tasodifan olib tashlasa, test darhol qizaradi — aks holda
    /// bot tokeni prod loglariga JIMGINA oqib ketardi.
    /// </summary>
    [Fact]
    public void TelegramHttpClient_HasNoLoggingHandlers()
    {
        var handler = factory.Services
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(TelegramMessageSender.HttpClientName);

        // Ilgaklar zanjirini oshkor API bilan aylanamiz
        // (`DelegatingHandler.InnerHandler` — public).
        var chain = new List<string>();

        for (var current = handler; current is DelegatingHandler node; current = node.InnerHandler)
            chain.Add(node.GetType().Name);

        chain.Should().NotContain(
            name => name.Contains("Logging", StringComparison.Ordinal),
            "bot tokeni so'rov manzilining ICHIDA keladi va log'ga tushmasligi SHART");
    }
}
