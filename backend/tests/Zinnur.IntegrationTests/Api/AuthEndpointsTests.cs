using System.Net;
using System.Net.Http.Json;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Autentifikatsiya oqimi — HAQIQIY baza va HAQIQIY JWT bilan.
///
/// Bu yerdagi testlar qatlamlar CHEGARASINI tekshiradi: JWT yaratish →
/// claim xaritalash → ruxsat tekshiruvi → baza. Aynan shu chegarada eng
/// qimmat buglar yashiringan bo'ladi.
/// </summary>
public sealed class AuthEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ------------------------------------------------------------------ kirish

    [Fact]
    public async Task Login_WithSeededAdminCredentials_ReturnsTokens()
    {
        var tokens = await factory.LoginAsAdminAsync();

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.User.Email.Should().Be("admin@zinnur.uz");
        tokens.User.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin@zinnur.uz", password = "noto'g'ri-parol" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Mavjud bo'lmagan email ham AYNAN shu javobni berishi kerak —
    /// aks holda javobdan qaysi email ro'yxatda borligini aniqlash mumkin
    /// (user enumeration).
    /// </summary>
    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameUnauthorizedAsWrongPassword()
    {
        using var client = factory.CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "yoq@zinnur.uz", password = "Admin!2345" });

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ResponseIsProblemDetails_OnFailure()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin@zinnur.uz", password = "xato" });

        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        problem!.TraceId.Should().NotBeNullOrWhiteSpace(
            "har javobda traceId bo'lishi kerak — foydalanuvchi shikoyat qilganda logdan topish uchun");
    }

    // ------------------------------------------------------------------ /me

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var user = await client.GetFromJsonAsync<AuthUser>("/api/v1/auth/me");

        user!.Email.Should().Be("admin@zinnur.uz");
    }

    /// <summary>
    /// ★ Bu test `name` claim'ining `ClaimTypes.Name` ga xaritalanishini
    /// bilvosita qo'riqlaydi. Jonli sinovda aniqlangan edi: ASP.NET default
    /// xaritasi `name` ni EMAS, `unique_name` ni `ClaimTypes.Name` ga
    /// bog'laydi — natijada chatda har xabar "Noma'lum" bo'lib chiqardi.
    /// </summary>
    [Fact]
    public async Task Me_ReturnsFullName_NotPlaceholder()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var user = await client.GetFromJsonAsync<AuthUser>("/api/v1/auth/me");

        user!.FullName.Should().NotBeNullOrWhiteSpace();
        user.FullName.Should().NotBe("Noma'lum");
    }

    [Fact]
    public async Task Me_WithGarbageToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateAuthorizedClient("soxta.token.qalbaki");

        var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ refresh

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ReturnsNewTokens()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await response.Content.ReadFromJsonAsync<AuthTokens>();
        refreshed!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Access token refresh sifatida ishlatilmasligi kerak.</summary>
    [Fact]
    public async Task Refresh_WithAccessToken_ReturnsUnauthorized()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = tokens.AccessToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ chiqish

    /// <summary>
    /// ★ ENG MUHIM XAVFSIZLIK TESTI.
    ///
    /// Eski tizimda "Chiqish" faqat brauzerdagi cookie'ni o'chirardi va token
    /// 14 kun yaroqli bo'lib qolardi — o'g'irlangan tokenni bekor qilishning
    /// iloji yo'q edi. Yangi tizimda `TokenVersion` oshiriladi va MAVJUD
    /// barcha tokenlar darhol kuchsizlanadi.
    /// </summary>
    [Fact]
    public async Task Logout_InvalidatesExistingRefreshToken()
    {
        var tokens = await factory.LoginAsAdminAsync();

        using (var authorized = factory.CreateAuthorizedClient(tokens.AccessToken))
        {
            var logout = await authorized.PostAsync(
                new Uri("/api/v1/auth/logout", UriKind.Relative), content: null);
            logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Chiqishdan OLDIN olingan refresh token endi ishlamasligi kerak
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "chiqish TokenVersion'ni oshiradi va eski tokenlar bekor bo'ladi");
    }

    // ------------------------------------------------------------------ sog'liq

    [Fact]
    public async Task Health_IsAnonymous()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>RFC 7807 ProblemDetails javobi.</summary>
public sealed record ProblemResponse(string? Title, int? Status, string? Detail, string? TraceId);
