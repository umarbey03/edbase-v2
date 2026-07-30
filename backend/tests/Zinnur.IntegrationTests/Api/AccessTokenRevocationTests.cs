using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// KIRISH TOKENI BEKOR QILINISHI.
///
/// ★ Bu testlar jonli tekshiruvda topilgan zaiflikni qulflaydi:
/// `JwtTokenService` tokenga `ver` (sessiya versiyasi) qo'yardi va uning
/// izohida "WebApi ham SHU nomni tekshiradi" deb yozilgan edi — lekin
/// tekshiruv YOZILMAGAN edi. Natijada imzosi to'g'ri kirish tokeni 15 daqiqa
/// davomida so'zsiz qabul qilinardi:
///
///   * `logout` qilingan foydalanuvchi ishlayverardi;
///   * O'CHIRILGAN o'quvchi jonli darsga LiveKit tokeni olib, video/audio
///     efirga chiqa olardi va chatga yozardi.
///
/// Endi tekshiruv token tasdiqlash bosqichida (`OnTokenValidated`), ya'ni
/// HTTP endpointlar ham, SignalR ulanishi ham shu darvozadan o'tadi.
/// </summary>
public sealed class AccessTokenRevocationTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>★ O'chirilgan foydalanuvchining ESKI tokeni darhol o'lishi kerak.</summary>
    [Fact]
    public async Task DeactivatedUser_ExistingAccessToken_IsRejected()
    {
        var (email, password, userId) = await CreateStudentAsync();
        var tokens = await factory.LoginAsync(email, password);
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        // O'chirishdan OLDIN token ishlaydi.
        (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var adminTokens = await factory.LoginAsAdminAsync();
        using var adminClient = factory.CreateAuthorizedClient(adminTokens.AccessToken);
        var deactivate = await adminClient.PostAsync(
            new Uri($"/api/v1/users/{userId}/deactivate", UriKind.Relative), content: null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterDeactivation = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        afterDeactivation.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "o'chirilgan foydalanuvchi eski token bilan ichkarida qolmasligi kerak");
    }

    /// <summary>
    /// ★ Jonli dars YO'LI — zaiflik amalda aynan shu yerda eng qimmat edi:
    /// o'chirilgan o'quvchi LiveKit tokeni olib video efirga chiqardi.
    /// </summary>
    [Fact]
    public async Task DeactivatedUser_CannotRequestLiveKitToken()
    {
        var (email, password, userId) = await CreateStudentAsync();
        var tokens = await factory.LoginAsync(email, password);
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var sessionId = await FirstSessionIdAsync();

        var adminTokens = await factory.LoginAsAdminAsync();
        using var adminClient = factory.CreateAuthorizedClient(adminTokens.AccessToken);
        await adminClient.PostAsync(
            new Uri($"/api/v1/users/{userId}/deactivate", UriKind.Relative), content: null);

        var response = await client.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/token", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "o'chirilgan o'quvchi video xonaga kira olmasligi kerak");
    }

    /// <summary>Chiqishdan keyin eski kirish tokeni ham o'lishi kerak.</summary>
    [Fact]
    public async Task AfterLogout_ExistingAccessToken_IsRejected()
    {
        var (email, password, _) = await CreateStudentAsync();
        var tokens = await factory.LoginAsync(email, password);
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var logout = await client.PostAsJsonAsync(
            "/api/v1/auth/logout", new { refreshToken = tokens.RefreshToken });
        logout.IsSuccessStatusCode.Should().BeTrue();

        var afterLogout = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "\"chiqdim\" bosgan odam ichkarida qolmasligi kerak");
    }

    /// <summary>
    /// Qayta faollashtirilgan foydalanuvchi YANGI token bilan kira olishi kerak —
    /// kesh "faol emas" holatida qotib qolmasin.
    /// </summary>
    [Fact]
    public async Task ReactivatedUser_CanLogInAgain()
    {
        var (email, password, userId) = await CreateStudentAsync();

        var adminTokens = await factory.LoginAsAdminAsync();
        using var adminClient = factory.CreateAuthorizedClient(adminTokens.AccessToken);

        await adminClient.PostAsync(
            new Uri($"/api/v1/users/{userId}/deactivate", UriKind.Relative), content: null);
        await adminClient.PostAsync(
            new Uri($"/api/v1/users/{userId}/activate", UriKind.Relative), content: null);

        var tokens = await factory.LoginAsync(email, password);
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ yordamchi

    private async Task<(string Email, string Password, long Id)> CreateStudentAsync()
    {
        const string password = "Student!2345";
        var email = $"revoke-{Guid.NewGuid():N}"[..20] + "@zinnur.uz";

        var id = await factory.WithDbAsync(async db =>
        {
            using var scope = factory.Services.CreateScope();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var user = new User
            {
                FullName = "Bekor Qilish Testi",
                Email = email,
                PasswordHash = await hasher.HashAsync(password),
                Role = UserRole.Student,
                IsActive = true,
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });

        return (email, password, id);
    }

    private Task<long> FirstSessionIdAsync() =>
        factory.WithDbAsync(db => db.LiveSessions.Select(s => s.Id).FirstAsync());
}
