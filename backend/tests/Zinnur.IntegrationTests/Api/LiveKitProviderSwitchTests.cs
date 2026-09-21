using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// LIVEKIT CLOUD — PANELDAN ALMASHTIRISH (2026-09-21)
/// ========================================================================
///
/// O'z serverimizdagi LiveKit nosoz bo'lganda loyiha egasi DEPLOYSIZ, panel
/// orqali LiveKit Cloud'ga o'tadi. Bu testlar ikki narsani ENDPOINT
/// darajasida qotiradi:
///
///  1) 🔴 kalitsiz Cloud'ga o'tib bo'lmaydi, Cloud ishlab turganda uning
///     kalitlarini o'chirib bo'lmaydi — aks holda bitta bosish BARCHA
///     jonli darsni o'chirardi;
///  2) tanlov o'zgarishi bilan brauzerga ketadigan manzil VA token imzosi
///     DARHOL boshqa serverga o'tadi (qayta ishga tushirishsiz).
///
/// ★ ALOHIDA SINF (alohida baza): `livekit.provider` butun ilovaga ta'sir
/// qiladi — umumiy bazada qolib ketsa boshqa sinflarning token testlari
/// sababsiz qizarardi.
/// </summary>
public sealed class LiveKitProviderSwitchTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string ProviderKey = "livekit.provider";
    private const string CloudUrlKey = "livekit.cloud_url";
    private const string CloudApiKeyKey = "livekit.cloud_api_key";
    private const string CloudApiSecretKey = "livekit.cloud_api_secret";

    private const string CloudApiKey = "APIzinnurtest";
    private const string CloudApiSecret = "cloud_test_secret_min_32_chars_0123456789";

    /// <summary><c>ZinnurApiFactory</c> o'rnatadigan o'z serverimiz qiymatlari.</summary>
    private const string SelfHostedPublicUrl = "ws://127.0.0.1:7880";
    private const string SelfHostedApiKey = "devkey";

    [Fact]
    public async Task SwitchToCloud_WithoutCloudKeys_IsRejected()
    {
        using var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync(KeyUri(ProviderKey), new { value = "Cloud" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ProviderKey);

        // Rad etilgan tanlov saqlanmagan: token hamon o'z serverimizga.
        var join = await JoinAsync(admin);
        join.ServerUrl.Should().Be(SelfHostedPublicUrl);
    }

    [Fact]
    public async Task SwitchToCloud_AndBack_MovesJoinTokenBetweenServers()
    {
        using var admin = await AdminAsync();

        // ---- Cloud to'plamini to'ldirish (o'z serverimiz hali ishlaydi) ----
        await PutOkAsync(admin, CloudUrlKey, "https://zinnur-test.livekit.cloud");
        await PutOkAsync(admin, CloudApiKeyKey, CloudApiKey);
        await PutOkAsync(admin, CloudApiSecretKey, CloudApiSecret);

        var beforeSwitch = await JoinAsync(admin);
        beforeSwitch.ServerUrl.Should().Be(SelfHostedPublicUrl);
        Issuer(beforeSwitch.Token).Should().Be(SelfHostedApiKey);

        // ---- Cloud'ga o'tish ----
        await PutOkAsync(admin, ProviderKey, "Cloud");

        var onCloud = await JoinAsync(admin);
        onCloud.ServerUrl.Should().Be("wss://zinnur-test.livekit.cloud");
        Issuer(onCloud.Token).Should().Be(CloudApiKey, "token Cloud kaliti bilan imzolanadi");

        // ---- Cloud ishlab turganda uning kalitini o'chirib bo'lmaydi ----
        var reset = await admin.PostAsync(ResetUri(CloudUrlKey), content: null);
        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest, await reset.Content.ReadAsStringAsync());

        (await JoinAsync(admin)).ServerUrl.Should().Be("wss://zinnur-test.livekit.cloud");

        // ---- Qaytish ----
        await PutOkAsync(admin, ProviderKey, "SelfHosted");

        var back = await JoinAsync(admin);
        back.ServerUrl.Should().Be(SelfHostedPublicUrl);
        Issuer(back.Token).Should().Be(SelfHostedApiKey);

        // O'z serverimizda Cloud to'plamini tozalash endi ruxsat.
        var cleanup = await admin.PostAsync(ResetUri(CloudUrlKey), content: null);
        cleanup.StatusCode.Should().Be(HttpStatusCode.OK, await cleanup.Content.ReadAsStringAsync());
    }

    // ================================================================= yordamchilar

    private static Uri KeyUri(string key) =>
        new($"/api/v1/settings/{key}", UriKind.Relative);

    private static Uri ResetUri(string key) =>
        new($"/api/v1/settings/{key}/reset", UriKind.Relative);

    private static async Task PutOkAsync(HttpClient admin, string key, string value)
    {
        var response = await admin.PutAsJsonAsync(KeyUri(key), new { value });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<LiveKitJoinDto> JoinAsync(HttpClient client)
    {
        var sessions = await client.GetFromJsonAsync<List<SessionDto>>("/api/v1/live-sessions");
        sessions.Should().NotBeNullOrEmpty("seed bitta demo dars yaratadi");

        var response = await client.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessions![0].Id}/token", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<LiveKitJoinDto>())!;
    }

    private static string? Issuer(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + ((4 - (segment.Length % 4)) % 4), '=');

        using var payload = JsonDocument.Parse(Convert.FromBase64String(segment));

        return payload.RootElement.GetProperty("iss").GetString();
    }

    private async Task<HttpClient> AdminAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private sealed record SessionDto(long Id);

    private sealed record LiveKitJoinDto(
        string ServerUrl, string Token, string RoomName, bool IsHost);
}
