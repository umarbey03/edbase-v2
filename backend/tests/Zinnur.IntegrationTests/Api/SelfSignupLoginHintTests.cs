using System.Net;
using System.Net.Http.Json;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// O'zi ro'yxatdan o'tish YOQILGAN API. Telegram `ThrottledApiFactory` dagi
/// AYNI naqsh bilan sozlangan (kod so'rash bot tokenisiz 503 qaytarardi),
/// tashqi tarmoqqa chiqish yo'q: worker o'chiq, API manzili javob bermaydi.
/// </summary>
public sealed class SelfSignupEnabledApiFactory : ZinnurApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Telegram:BotToken", "123456789:AAH-selfsignup-test-bot-token-x"),
        new("Telegram:WebhookSecret", "zinnur_selfsignup_webhook_secret_26"),
        new("Telegram:ApiBaseUrl", "http://127.0.0.1:9"),
        new("Notifications:Enabled", "false"),
        new("Sales:SelfSignupEnabled", "true"),
    ];
}

/// <summary>
/// ★ BAZADA YO'Q RAQAM (2026-11-19, loyiha egasi): o'zi ro'yxatdan o'tish
/// yoqilgan bo'lsa, telefon bilan kirish "murojaat qiling" demaydi — bot
/// orqali ro'yxatdan o'tish yo'lini ko'rsatadi (profil faqat botda ochiladi,
/// raqam egaligini Telegram kontakti tasdiqlaydi).
/// </summary>
public sealed class SelfSignupLoginHintTests(SelfSignupEnabledApiFactory factory)
    : IClassFixture<SelfSignupEnabledApiFactory>
{
    [Fact]
    public async Task Unknown_phone_is_pointed_to_bot_signup_when_self_signup_is_on()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone = TestPhones.Next() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Telegram bot orqali kirish", "yangi odamga ro'yxatdan o'tish yo'li ko'rsatiladi");
        // ASCII so'z: JSON apostrofni `'` qilib yozadi, "O'quv" bilan qidirish hech narsani tekshirmasdi.
        body.Should().NotContain("murojaat");
    }
}
