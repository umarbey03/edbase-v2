using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// TIZIM SOZLAMALARI PANELI (super-admin)
/// ========================================================================
///
/// Bu testlar UCHTA narsani ENDPOINT darajasida qotiradi:
///
///  1) 🔴 DARVOZA: faqat <c>Admin</c>. <c>Academic</c>, ustoz va o'quvchi —
///     403. Eski tizimning eng og'ir zaifligi (audit X-4) aynan
///     <c>academic</c> rolining ortiqcha huquqidan boshlangan edi.
///
///  2) 🔴 SIR SIZIB CHIQMASLIGI: javob tanasida BIRORTA ham haqiqiy sir
///     bo'lmasligi kerak. Test buni "maskalangan ko'rinish to'g'rimi" deb
///     emas, XOM JAVOB MATNIDA sirni QIDIRIB tekshiradi — chunki sizib
///     chiqish ko'pincha kutilmagan joydan (yangi maydon, xato xabari,
///     standart qiymat) bo'ladi.
///
///  3) MANBA USTUNLIGI: bazadagi qiymat muhitdan USTUN, "standartga
///     qaytarish" esa qatorni o'chirib, yana muhitga qaytaradi.
///
/// ★ HAR TEST O'Z HOLATINI O'ZI O'RNATADI: sinf ichidagi testlar BITTA
/// bazani bo'lishadi va sozlamalar UMUMIY. Tartibga tayanish testlarni
/// sababsiz "flaky" qilardi.
/// </summary>
public sealed class SettingsEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string ThresholdKey = "finance.block_threshold";
    private const string ScopeKey = "finance.block_scope";
    private const string JwtSecretKey = "security.jwt_secret";

    /// <summary><c>ZinnurApiFactory</c> testlar uchun o'rnatadigan JWT siri.</summary>
    private const string ConfiguredJwtSecret = "integration_test_secret_min_32_chars_0123456789";

    /// <summary><c>ZinnurApiFactory</c> testlar uchun o'rnatadigan LiveKit siri.</summary>
    private const string ConfiguredLiveKitSecret = "integration_test_livekit_secret_min_32_ch";

    // ================================================================= 1) darvoza

    [Fact]
    public async Task List_AsAdmin_ReturnsGroupedRegistryWithMetadata()
    {
        using var admin = await AdminAsync();

        var response = await admin.GetAsync(SettingsUri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = (await response.Content.ReadFromJsonAsync<SettingsPage>())!;

        page.Groups.Should().NotBeEmpty();

        var threshold = Find(page, ThresholdKey);

        // Panel formani AYNAN shu maydonlardan quradi — ular yo'q bo'lsa
        // interfeys chegaralarni kodda takrorlashga majbur bo'lardi.
        threshold.Name.Should().NotBeNullOrWhiteSpace();
        threshold.Description.Should().NotBeNullOrWhiteSpace();
        threshold.Kind.Should().Be("Money");
        threshold.IsEditable.Should().BeTrue();
        threshold.Constraints.Minimum.Should().Be(0m);
        threshold.Constraints.Maximum.Should().BeGreaterThan(0m);

        var scope = Find(page, ScopeKey);

        scope.Kind.Should().Be("Choice");
        scope.Constraints.Choices.Should().Contain(nameof(PaymentBlockScope.Platform));
    }

    /// <summary>
    /// 🔴 <c>Academic</c> — boshqa modullarda kengroq huquqli rol, LEKIN
    /// sozlamalarga umuman kirmaydi. Ko'rish ham, yozish ham taqiq.
    /// </summary>
    [Fact]
    public async Task List_AsAcademic_ReturnsForbidden()
    {
        using var academic = await RoleClientAsync(UserRole.Academic, "setacad");

        var read = await academic.GetAsync(SettingsUri);
        var readOne = await academic.GetAsync(KeyUri(ThresholdKey));

        var write = await academic.PutAsJsonAsync(
            KeyUri(ThresholdKey), new { value = "1" });

        var reset = await academic.PostAsJsonAsync(
            ResetUri(ThresholdKey), new { });

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        readOne.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reset.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Teacher)]
    [InlineData(UserRole.Assistant)]
    [InlineData(UserRole.Student)]
    public async Task List_AsOtherRoles_ReturnsForbidden(UserRole role)
    {
        using var client = await RoleClientAsync(role, "setrole");

        var response = await client.GetAsync(SettingsUri);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(SettingsUri);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= 2) sirlar

    /// <summary>
    /// 🔴 Javob TANASIDA haqiqiy sir bo'lmasligi kerak. Tekshiruv ataylab
    /// "xom matnda qidirish" usulida: yangi maydon qo'shilganda yoki
    /// standart qiymat qaytarilganda sir kutilmagan joydan sizib chiqishi
    /// mumkin, DTO'ni maydonma-maydon tekshiradigan test esa buni
    /// ko'rmasdi.
    /// </summary>
    [Fact]
    public async Task List_NeverLeaksFullSecretValue()
    {
        using var admin = await AdminAsync();

        var response = await admin.GetAsync(SettingsUri);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(ConfiguredJwtSecret);
        body.Should().NotContain(ConfiguredLiveKitSecret);

        var page = (await response.Content.ReadFromJsonAsync<SettingsPage>())!;
        var secret = Find(page, JwtSecretKey);

        secret.IsSecret.Should().BeTrue();
        secret.IsSet.Should().BeTrue();

        // Sir uchun `value` HAR DOIM null — bu shartnomaning qattiq qismi.
        secret.Value.Should().BeNull();
        secret.DefaultValue.Should().BeNull();

        // Maskalangan ko'rinish esa bor: admin kalitini tanib olishi kerak.
        secret.MaskedValue.Should().NotBeNullOrEmpty();
        secret.MaskedValue.Should().NotBe(ConfiguredJwtSecret);
        secret.MaskedValue!.Should().EndWith(ConfiguredJwtSecret[^4..]);
    }

    [Fact]
    public async Task GetOne_ForSecret_AlsoMasksValue()
    {
        using var admin = await AdminAsync();

        var response = await admin.GetAsync(KeyUri(JwtSecretKey));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain(ConfiguredJwtSecret);
    }

    // ================================================================= 3) "faqat o'qish"

    /// <summary>
    /// Tizimni qulflab qo'yadigan kalitni o'zgartirishga urinish RAD
    /// ETILADI, va javobda SABAB bo'ladi — panel uni maydon yonida
    /// ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task Update_ReadOnlyKey_IsRejectedWithReason()
    {
        using var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync(
            KeyUri(JwtSecretKey), new { value = "yangi_juda_uzun_sir_kamida_32_belgi_0123456789" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain(JwtSecretKey);

        // Sabab matni bo'sh bo'lmasin — "nega bo'lmaydi?" degan savol
        // javobsiz qolmasin.
        body.Length.Should().BeGreaterThan(JwtSecretKey.Length + 40);

        // Va qiymat baribir o'zgarmagan bo'lsin.
        var stored = await factory.WithDbAsync(db =>
            db.AppSettings.CountAsync(s => s.Key == "security.jwt_secret"));

        stored.Should().Be(0);
    }

    [Fact]
    public async Task Reset_ReadOnlyKey_IsRejected()
    {
        using var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(ResetUri("general.time_zone"), new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_UnknownKey_ReturnsNotFound()
    {
        using var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync(
            KeyUri("shunday.kalit.yoq"), new { value = "1" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================= 4) validatsiya

    [Theory]
    [InlineData("-1")]
    [InlineData("999999999")]
    [InlineData("olti yuz ming")]
    public async Task Update_WithInvalidValue_ReturnsBadRequestWithFieldError(string value)
    {
        using var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync(KeyUri(ThresholdKey), new { value });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Frontend `problem.errors[<kalit>]` ni o'qiydi — shakl shu bo'lishi shart.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("errors");
        body.Should().Contain(ThresholdKey);
    }

    [Fact]
    public async Task Update_WithUnknownChoice_IsRejected()
    {
        using var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync(KeyUri(ScopeKey), new { value = "Hammasi" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================= 5) saqlash va audit

    /// <summary>
    /// Qiymat saqlanadi, QAYTA O'QILGANDA turadi va manbai <c>Database</c>
    /// bo'lib qoladi (ya'ni bazadagi qiymat muhitdan USTUN).
    /// </summary>
    [Fact]
    public async Task Update_PersistsValue_AndOverridesEnvironment()
    {
        using var admin = await AdminAsync();

        var updated = await admin.PutAsJsonAsync(KeyUri(ThresholdKey), new { value = "612345" });

        updated.StatusCode.Should().Be(HttpStatusCode.OK, await Body(updated));

        var afterWrite = (await updated.Content.ReadFromJsonAsync<Setting>())!;

        afterWrite.Value.Should().Be("612345");
        afterWrite.Origin.Should().Be("Database");
        afterWrite.UpdatedById.Should().NotBeNull();

        // Qayta o'qish — yangi so'rov, yangi `DbContext`.
        var read = await admin.GetAsync(KeyUri(ThresholdKey));
        var afterRead = (await read.Content.ReadFromJsonAsync<Setting>())!;

        afterRead.Value.Should().Be("612345");
        afterRead.Origin.Should().Be("Database");
    }

    /// <summary>
    /// Audit izi: kim, qachon, qaysi kalitni, nimadan-nimaga o'zgartirdi.
    /// </summary>
    [Fact]
    public async Task Update_WritesAuditTrailWithOldAndNewValue()
    {
        using var admin = await AdminAsync();

        // Boshlang'ich holat OSHKOR o'rnatiladi — tartibga tayanmaymiz.
        await admin.PutAsJsonAsync(KeyUri(ThresholdKey), new { value = "500000" });
        await admin.PutAsJsonAsync(KeyUri(ThresholdKey), new { value = "700000" });

        var audit = await factory.WithDbAsync(db => db.PaymentAudits
            .Where(a => a.Entity == "settings" && a.Field == ThresholdKey)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync());

        audit.Should().NotBeNull();
        audit!.Action.Should().Be("update");
        audit.OldValue.Should().Be("500000");
        audit.NewValue.Should().Be("700000");
        audit.ActorId.Should().NotBeNull();
        audit.CreatedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    /// <summary>
    /// Qiymat O'ZGARMASA audit yozilmaydi: takroriy saqlash izni shovqin
    /// bilan to'ldirib, haqiqiy o'zgarishlarni ko'rinmas qilardi.
    /// </summary>
    [Fact]
    public async Task Update_WithSameValue_DoesNotAddAuditNoise()
    {
        using var admin = await AdminAsync();

        await admin.PutAsJsonAsync(KeyUri(ScopeKey), new { value = "Live" });

        var before = await CountScopeAuditsAsync();

        await admin.PutAsJsonAsync(KeyUri(ScopeKey), new { value = "Live" });

        var after = await CountScopeAuditsAsync();

        after.Should().Be(before);
    }

    /// <summary>Eski tizimdagi kichik harfli qiymat qabul qilinadi va kanonikga keltiriladi.</summary>
    [Fact]
    public async Task Update_LegacyLowercaseChoice_IsNormalized()
    {
        using var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync(KeyUri(ScopeKey), new { value = "video" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await Body(response));

        var setting = (await response.Content.ReadFromJsonAsync<Setting>())!;

        setting.Value.Should().Be("Video");

        var stored = await factory.WithDbAsync(db => db.AppSettings
            .Where(s => s.Key == "payment_block_scope")
            .Select(s => s.Value)
            .FirstOrDefaultAsync());

        stored.Should().Be("Video");
    }

    // ================================================================= 6) standartga qaytarish

    [Fact]
    public async Task Reset_RemovesRow_AndFallsBackToEnvironment()
    {
        using var admin = await AdminAsync();

        await admin.PutAsJsonAsync(KeyUri(ThresholdKey), new { value = "623456" });

        var reset = await admin.PostAsJsonAsync(ResetUri(ThresholdKey), new { });

        reset.StatusCode.Should().Be(HttpStatusCode.OK, await Body(reset));

        var setting = (await reset.Content.ReadFromJsonAsync<Setting>())!;

        // Qator o'chirilgani uchun manba endi baza EMAS.
        setting.Origin.Should().NotBe("Database");
        setting.Value.Should().NotBe("623456");

        var rows = await factory.WithDbAsync(db =>
            db.AppSettings.CountAsync(s => s.Key == "payment_block_threshold"));

        rows.Should().Be(0);
    }

    // ================================================================= 7) moliya bilan yagona manba

    /// <summary>
    /// ★ ENG MUHIM UMUMLASHTIRISH TESTI: paneldan yozilgan chegara MOLIYA
    /// yo'lida ham o'qiladi. Ikki parallel sozlamalar tizimi bo'lganda bu
    /// test yiqilardi.
    /// </summary>
    [Fact]
    public async Task ValueWrittenBySettingsPanel_IsVisibleToFinanceModule()
    {
        using var admin = await AdminAsync();

        await admin.PutAsJsonAsync(KeyUri(ThresholdKey), new { value = "634567" });
        await admin.PutAsJsonAsync(KeyUri(ScopeKey), new { value = "Platform" });

        var finance = await admin.GetAsync(new Uri("/api/v1/payments/settings", UriKind.Relative));

        finance.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = (await finance.Content.ReadFromJsonAsync<FinanceSettingsResponse>())!;

        settings.BlockThreshold.Should().Be(634_567m);
        settings.BlockScope.Should().Be(nameof(PaymentBlockScope.Platform));
    }

    /// <summary>
    /// Va teskarisi: moliya sahifasidan yozilgan qiymat panelda ko'rinadi.
    /// Ikkala yo'l AYNI qatorga yozadi (kalit nomi eski tizimniki).
    /// </summary>
    [Fact]
    public async Task ValueWrittenByFinancePage_IsVisibleInSettingsPanel()
    {
        using var admin = await AdminAsync();

        var saved = await admin.PutAsJsonAsync("/api/v1/payments/settings", new
        {
            blockThreshold = 645_678m,
            blockScope = nameof(PaymentBlockScope.Live),
        });

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await Body(saved));

        var read = await admin.GetAsync(KeyUri(ThresholdKey));
        var setting = (await read.Content.ReadFromJsonAsync<Setting>())!;

        decimal.Parse(setting.Value!, CultureInfo.InvariantCulture).Should().Be(645_678m);
        setting.Origin.Should().Be("Database");
    }

    // ================================================================= yordamchilar

    private static readonly Uri SettingsUri = new("/api/v1/settings", UriKind.Relative);

    private static Uri KeyUri(string key) =>
        new($"/api/v1/settings/{key}", UriKind.Relative);

    private static Uri ResetUri(string key) =>
        new($"/api/v1/settings/{key}/reset", UriKind.Relative);

    private Task<int> CountScopeAuditsAsync() =>
        factory.WithDbAsync(db =>
            db.PaymentAudits.CountAsync(a => a.Entity == "settings" && a.Field == ScopeKey));

    private async Task<HttpClient> AdminAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> RoleClientAsync(UserRole role, string prefix)
    {
        using var admin = await AdminAsync();

        var user = await WorldBuilder.CreateUserAsync(admin, role, prefix);
        var tokens = await factory.LoginAsync(user.Email);

        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private static Setting Find(SettingsPage page, string key)
    {
        var item = page.Groups
            .SelectMany(g => g.Items)
            .FirstOrDefault(i => i.Key == key);

        item.Should().NotBeNull($"registrda '{key}' bo'lishi kerak.");
        return item!;
    }

    private static async Task<string> Body(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    // Javob shakli ATAYLAB testda qayta e'lon qilinadi (server DTO'si emas):
    // shunda shartnomaning buzilishi (maydon nomi o'zgarishi) test yiqilishi
    // bo'lib chiqadi, jimgina `null` bo'lib emas.
    private sealed record SettingsPage(IReadOnlyList<SettingGroupRow> Groups);

    private sealed record SettingGroupRow(
        string Group, string Name, string Description, IReadOnlyList<Setting> Items);

    private sealed record Setting(
        string Key,
        string Group,
        string GroupName,
        string Name,
        string Description,
        string Kind,
        bool IsSecret,
        bool IsEditable,
        string? ReadOnlyReason,
        string Origin,
        bool IsSet,
        string? Value,
        string? MaskedValue,
        string? DefaultValue,
        Constraints Constraints,
        DateTimeOffset? UpdatedAt,
        long? UpdatedById);

    private sealed record Constraints(
        IReadOnlyList<string> Choices,
        decimal? Minimum,
        decimal? Maximum,
        int MaxLength,
        string Format);

    private sealed record FinanceSettingsResponse(
        decimal BlockThreshold, string BlockScope, bool Enforce);
}
