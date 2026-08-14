using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Jonli dars endpointlari: ruxsat matritsasi va LiveKit tokeni.
///
/// Bu yerdagi asosiy maqsad — RUXSATNI tekshirish. Eski tizimning eng qimmat
/// zaifliklari aynan shu joyda edi: begona darsga kirish, boshqa rol nomidan
/// amal bajarish.
/// </summary>
public sealed class LiveSessionEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ------------------------------------------------------------------ ro'yxat

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/v1/live-sessions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsSeededDemoSession()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var sessions = await client.GetFromJsonAsync<List<SessionDto>>("/api/v1/live-sessions");

        sessions.Should().NotBeNull();
        sessions!.Should().NotBeEmpty("seed bitta demo dars yaratadi");
    }

    // ------------------------------------------------------------------ LiveKit token

    /// <summary>
    /// ★ Token LiveKit talab qiladigan AYNAN shaklda bo'lishi kerak.
    /// `video` claim'i ichma-ich JSON obyekt bo'lishi shart; agar u satrga
    /// aylantirilsa LiveKit tokenni JIMGINA rad etadi — hech qanday xato
    /// ko'rinmaydi, video shunchaki ishlamaydi.
    /// </summary>
    [Fact]
    public async Task CreateToken_ForHost_ReturnsLiveKitJoinPayload()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var sessionId = await FirstSessionIdAsync(client);

        var response = await client.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/token", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var join = await response.Content.ReadFromJsonAsync<LiveKitJoinDto>();

        join!.Token.Should().NotBeNullOrWhiteSpace();
        join.RoomName.Should().NotBeNullOrWhiteSpace();
        join.ServerUrl.Should().StartWith("ws", "brauzer WebSocket manzilini kutadi");
        join.IsHost.Should().BeTrue("admin har doim host hisoblanadi");

        // JWT uch qismdan iborat va `video` grant'i BOR
        var parts = join.Token.Split('.');
        parts.Should().HaveCount(3, "HS256 JWT: header.payload.signature");

        var payload = DecodeJwtPayload(parts[1]);
        payload.Should().Contain("\"video\"");
        payload.Should().Contain("\"roomJoin\":true");
        payload.Should().Contain("\"room\":");
    }

    [Fact]
    public async Task CreateToken_ForUnknownSession_ReturnsNotFound()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await client.PostAsync(
            new Uri("/api/v1/live-sessions/999999/token", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ ruxsat matritsasi

    /// <summary>
    /// ★ Guruhga a'zo BO'LMAGAN o'quvchi darsga kira olmasligi kerak.
    /// Eski tizimda bu tekshiruv ba'zi endpointlarda tushib qolgan edi.
    /// </summary>
    [Fact]
    public async Task CreateToken_AsOutsiderStudent_ReturnsForbidden()
    {
        var (email, password) = await CreateStudentOutsideAnyGroupAsync();

        var studentTokens = await factory.LoginAsync(email);
        using var studentClient = factory.CreateAuthorizedClient(studentTokens.AccessToken);

        var adminTokens = await factory.LoginAsAdminAsync();
        using var adminClient = factory.CreateAuthorizedClient(adminTokens.AccessToken);
        var sessionId = await FirstSessionIdAsync(adminClient);

        var response = await studentClient.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/token", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "guruh a'zosi bo'lmagan o'quvchi darsga kira olmaydi");
    }

    /// <summary>Boshlash — faqat host roli uchun ochiq endpoint.</summary>
    [Fact]
    public async Task Start_AsStudent_ReturnsForbidden()
    {
        var (email, password) = await CreateStudentOutsideAnyGroupAsync();
        var studentTokens = await factory.LoginAsync(email);
        using var studentClient = factory.CreateAuthorizedClient(studentTokens.AccessToken);

        var adminTokens = await factory.LoginAsAdminAsync();
        using var adminClient = factory.CreateAuthorizedClient(adminTokens.AccessToken);
        var sessionId = await FirstSessionIdAsync(adminClient);

        var response = await studentClient.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/start", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ xabarlar

    [Fact]
    public async Task Messages_AsAdmin_ReturnsOkAndRespectsTakeLimit()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);
        var sessionId = await FirstSessionIdAsync(client);

        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/messages?take=5", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var messages = await response.Content.ReadFromJsonAsync<List<ChatMessageDto>>();
        messages.Should().NotBeNull();
        messages!.Count.Should().BeLessThanOrEqualTo(5);
    }

    // ------------------------------------------------------------------ yordamchi

    private static async Task<long> FirstSessionIdAsync(HttpClient client)
    {
        var sessions = await client.GetFromJsonAsync<List<SessionDto>>("/api/v1/live-sessions");
        sessions.Should().NotBeNullOrEmpty();
        return sessions![0].Id;
    }

    /// <summary>Hech qanday guruhga a'zo bo'lmagan o'quvchi yaratadi.</summary>
    private async Task<(string Email, string Password)> CreateStudentOutsideAnyGroupAsync()
    {
        const string password = "Student!2345";
        var email = $"begona-{Guid.NewGuid():N}"[..20] + "@zinnur.uz";

        await factory.WithDbAsync(async db =>
        {
            var hasher = new HasherProxy(factory);
            db.Users.Add(new User
            {
                FullName = "Begona O'quvchi",
                Email = email,
                PasswordHash = await hasher.HashAsync(password),
                Role = UserRole.Student,
                IsActive = true,
            });
            return await db.SaveChangesAsync();
        });

        return (email, password);
    }

    private static string DecodeJwtPayload(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private sealed record SessionDto(long Id, long GroupId, string GroupName, string Status);

    private sealed record LiveKitJoinDto(
        string ServerUrl, string Token, string RoomName, bool IsHost);

    private sealed record ChatMessageDto(
        long Id, long SenderId, string SenderName, string Body);
}

/// <summary>DI'dan `IPasswordHasher` ni olish uchun kichik yordamchi.</summary>
internal sealed class HasherProxy(ZinnurApiFactory factory)
{
    public Task<string> HashAsync(string password)
    {
        using var scope = Microsoft.Extensions.DependencyInjection
            .ServiceProviderServiceExtensions.CreateScope(factory.Services);

        var hasher = Microsoft.Extensions.DependencyInjection
            .ServiceProviderServiceExtensions
            .GetRequiredService<Zinnur.Application.Common.Interfaces.IPasswordHasher>(
                scope.ServiceProvider);

        return hasher.HashAsync(password);
    }
}
