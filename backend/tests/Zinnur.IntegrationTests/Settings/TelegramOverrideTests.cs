using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Settings;

/// <summary>
/// Muhit o'zgaruvchisi bot tokenini bazadagi qiymatdan USTUN qo'yadigan
/// fixture — ya'ni AVARIYA holatini modellaydi.
/// </summary>
public sealed class OverriddenTelegramApiFactory : ZinnurApiFactory
{
    /// <summary>Bazaga yoziladigan (buzuq deb faraz qilingan) qiymat.</summary>
    public const string DatabaseToken = "111111111:AAH-baza-dagi-buzuq-token-xxxxx";

    /// <summary>Muhitdan keladigan tiklash qiymati.</summary>
    public const string OverrideToken = "222222222:AAH-muhitdan-tiklash-token-yyy";

    public const string OverrideSecret = "zinnur_override_webhook_secret_2026";

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        // Boshlang'ich (env) qiymat — odatiy qoida bo'yicha bazadan PAST.
        new("Telegram:BotToken", "333333333:AAH-oddiy-boshlangich-token-zzzz"),
        new("Telegram:WebhookSecret", "zinnur_seed_webhook_secret_2026"),

        // 🔴 SHOSHILINCH KALITLAR — bazadan ham USTUN.
        new(SettingsRegistry.TelegramOverrideKeys.BotToken, OverrideToken),
        new(SettingsRegistry.TelegramOverrideKeys.WebhookSecret, OverrideSecret),

        new("Telegram:ApiBaseUrl", "http://127.0.0.1:9"),
        new("Notifications:Enabled", "false"),
    ];
}

/// <summary>
/// ========================================================================
/// 🔴 O'LIK HALQANI UZADIGAN KALIT ("break-glass") — XULQ TESTI
/// ========================================================================
///
/// ★ NIMA UCHUN BU TEST BOR
///
/// 2026-08-13 dan tizimga kirishning HAR IKKALA yo'li ham Telegram bot
/// tokeniga tayanadi (email va parol olib tashlandi). Token esa BAZADA va
/// uni faqat `Admin` o'zgartira oladi. Ya'ni buzuq token quyidagi halqani
/// yopardi:
///
///   token buzuq -> hech kim kira olmaydi -> tokenni tuzatadigan panel
///   ham o'sha kirish ortida -> faqat `psql` bilan tiklanadi.
///
/// Bu kalit halqani uzadi. LEKIN u faqat HAQIQATAN ustun bo'lgandagina
/// foyda beradi: ustunlik qoidasi bitta joyda (`SettingsResolver`) va uni
/// buzish oson — masalan kimdir keyinchalik "baza doim ustun" degan
/// soddalashtirish kiritsa. O'shanda bu test qizaradi, ishlab chiqarishda
/// esa nosozlik faqat AVARIYA paytida — ya'ni eng yomon daqiqada —
/// ma'lum bo'lardi.
/// </summary>
public sealed class TelegramOverrideTests(OverriddenTelegramApiFactory factory)
    : IClassFixture<OverriddenTelegramApiFactory>
{
    private const string BotTokenKey = SettingsRegistry.Keys.TelegramBotToken;

    /// <summary>
    /// ★★ ASOSIY TEST: bazada qator BOR bo'lsa ham muhit qiymati ishlaydi.
    ///
    /// Tekshiruv `ISettingsResolver` orqali — ya'ni tizim AMALDA
    /// o'qiydigan yo'l bilan, panel javobi orqali emas. Panel bir narsa,
    /// ishlaydigan kod boshqa narsa ko'rsatishi mumkin bo'lgan holat aynan
    /// shu registrning eng qattiq taqiqi ("jimgina yolg'on").
    /// </summary>
    [Fact]
    public async Task Override_BeatsDatabaseValue()
    {
        await WriteDatabaseTokenAsync();

        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();

        var value = await resolver.GetValueAsync(BotTokenKey);

        value.Should().Be(OverriddenTelegramApiFactory.OverrideToken,
            "muhitdagi shoshilinch qiymat bazadagi qatordan USTUN turishi kerak");
    }

    /// <summary>
    /// 🔴 PANEL MAYDONI QULFLANADI VA SABABI KO'RSATILADI.
    ///
    /// Bunsiz administrator tokenni o'zgartirib, "saqlandi" javobini olib,
    /// tizim esa eski qiymat bilan ishlayverardi.
    /// </summary>
    [Fact]
    public async Task Override_LocksThePanelField()
    {
        using var client = await AdminClientAsync();

        var setting = await client.GetFromJsonAsync<SettingRow>(
            "/api/v1/settings/" + BotTokenKey);

        setting!.IsEditable.Should().BeFalse(
            "ustidan yozilgan qiymatni paneldan o'zgartirish hech qanday ta'sir qilmaydi");

        setting.ReadOnlyReason.Should().NotBeNullOrWhiteSpace(
            "qulflangan maydon sababsiz qolmasin — administrator nima qilishni bilishi kerak");

        setting.Origin.Should().Be("EnvironmentOverride",
            "panel oddiy muhit qiymatidan (Environment) shoshilinch rejimni ajrata bilishi kerak");
    }

    /// <summary>
    /// 🔴 YOZISH HAM TO'SILADI — faqat UI'da yashirish yetarli emas.
    ///
    /// API to'g'ridan-to'g'ri chaqirilishi mumkin, va bazaga tushib qolgan
    /// qator muhit o'zgaruvchisi olib tashlangan kunda KUTILMAGANDA kuchga
    /// kirardi.
    /// </summary>
    [Fact]
    public async Task Override_RejectsPanelUpdate()
    {
        using var client = await AdminClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings/" + BotTokenKey,
            new { value = "444444444:AAH-panel-orqali-urinish-token" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "400: bu kalitni HOZIR hech kim, hatto administrator ham o'zgartira olmaydi");
    }

    /// <summary>Webhook siri ham AYNI qoidaga bo'ysunadi (token bilan juftlikda).</summary>
    [Fact]
    public async Task Override_AppliesToWebhookSecretToo()
    {
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();

        var value = await resolver.GetValueAsync(SettingsRegistry.Keys.TelegramWebhookSecret);

        value.Should().Be(OverriddenTelegramApiFactory.OverrideSecret);
    }

    /// <summary>
    /// ★ USTIDAN YOZISH FAQAT SHU IKKI KALITGA TEGISHLI.
    ///
    /// Registrdagi boshqa kalitlar odatdagidek ishlashda davom etishi
    /// kerak, aks holda "shoshilinch rejim" butun panelni jimgina
    /// o'chirib qo'ygan bo'lardi.
    /// </summary>
    [Fact]
    public async Task Override_DoesNotAffectOtherSettings()
    {
        using var client = await AdminClientAsync();

        var setting = await client.GetFromJsonAsync<SettingRow>(
            "/api/v1/settings/" + SettingsRegistry.Keys.TelegramBotUsername);

        setting!.IsEditable.Should().BeTrue();
    }

    // ================================================================ yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    /// <summary>
    /// Bazaga qator yozadi — "admin paneldan buzuq token qo'ydi" holati.
    ///
    /// Panel orqali yozib bo'lmaydi (aynan shu to'silgan), shuning uchun
    /// qator to'g'ridan-to'g'ri saqlash porti orqali qo'yiladi.
    /// </summary>
    private async Task WriteDatabaseTokenAsync()
    {
        using var scope = factory.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
        var db = scope.ServiceProvider
            .GetRequiredService<Zinnur.Application.Common.Interfaces.IApplicationDbContext>();

        await store.SetAsync(BotTokenKey, OverriddenTelegramApiFactory.DatabaseToken, actorId: null);
        await db.SaveChangesAsync();

        // Kesh yangilansin — aks holda test 10 sekundgacha eski kesimni ko'rardi.
        await scope.ServiceProvider.GetRequiredService<IRuntimeSettings>().RefreshAsync();
    }

    /// <summary>`SettingDto` ning testga kerak bo'lgan qismi.</summary>
    private sealed record SettingRow(
        string Key, bool IsEditable, string? ReadOnlyReason, string Origin, bool IsSet);
}
