using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Autentifikatsiya oqimi — HAQIQIY baza va HAQIQIY JWT bilan.
///
/// Bu yerdagi testlar qatlamlar CHEGARASINI tekshiradi: JWT yaratish →
/// claim xaritalash → ruxsat tekshiruvi → baza. Aynan shu chegarada eng
/// qimmat buglar yashiringan bo'ladi.
///
/// ⚠️ EMAIL VA PAROL BILAN KIRISH TESTLARI OLIB TASHLANDI (2026-08-13) —
/// endpointning o'zi yo'q. Telefon + bir martalik kod oqimi butunlay
/// alohida sinf'da sinaladi: <c>PhoneLoginEndpointsTests</c>.
/// </summary>
public sealed class AuthEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ------------------------------------------------------------------ kirish

    /// <summary>
    /// ★ REGRESSIYA QULFI: eski kirish endpointi QAYTIB KELMASIN.
    ///
    /// NIMA UCHUN BU TEST BOR: parol yo'li olib tashlangan, lekin
    /// `IPasswordHasher`, `User.PasswordHash` va `SetPassword` kod bazasida
    /// QOLDI (sabab `User.PasswordHash` izohida). Ya'ni "ikkinchi eshikni"
    /// tasodifan tiklash uchun bir necha qator yetarli. Bu test shu
    /// qadamni DARHOL qizartiradi.
    ///
    /// 405 ham qabul qilinadi: marshrut boshqa metod uchun band bo'lsa
    /// ASP.NET 404 emas, 405 qaytaradi — ikkalasi ham "bu yerda kirish
    /// yo'q" degani.
    /// </summary>
    [Fact]
    public async Task Login_EndpointIsGone()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin@zinnur.uz", password = "Admin!2345" });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
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

    /// <summary>
    /// ========================================================================
    /// 🔴 R8: SUV BELGISI UCHUN TELEFON — FAQAT SHU YO'LDAN
    /// ========================================================================
    ///
    /// Video ustidagi suv belgisi o'quvchining O'Z raqamini ko'rsatadi.
    /// Raqam manbai ATAYLAB <c>/auth/me</c>: bu endpoint tokendagi
    /// <c>sub</c> dan kelib chiqadi, ya'ni undan HECH QACHON boshqa
    /// odamning raqami chiqmaydi ("kimning profili" degan parametr yo'q).
    ///
    /// ★ MUQOBIL YO'LLAR NEGA RAD ETILDI: guruh a'zolari ro'yxati, davomat
    /// varag'i va qatnashuvchilar ro'yxati ham raqamni bilardi, LEKIN
    /// ularning hammasi USTOZGA ham ochiq va R27 aynan o'sha yo'llarni
    /// yopadi. Suv belgisini o'shalardan yig'ish yopilgan teshikni qayta
    /// ochardi.
    ///
    /// Bu test aynan shu bog'lanishni qulflaydi: maydon YO'QOLSA suv belgisi
    /// jimgina ism+id ga tushib qolardi va buni hech kim sezmasdi.
    /// </summary>
    [Fact]
    public async Task Me_ReturnsOwnPhone_ForWatermark()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var expected = await factory.WithDbAsync(db => db.Users
            .AsNoTracking()
            .Where(u => u.Email == "admin@zinnur.uz")
            .Select(u => u.Phone)
            .FirstAsync());

        var user = await client.GetFromJsonAsync<AuthUser>("/api/v1/auth/me");

        user!.Phone.Should().Be(expected);
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

    /// <summary>Xato so'rov RFC 7807 shaklida va `traceId` bilan qaytadi.</summary>
    [Fact]
    public async Task Refresh_ResponseIsProblemDetails_OnFailure()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = "qalbaki-token" });

        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        problem!.TraceId.Should().NotBeNullOrWhiteSpace(
            "har javobda traceId bo'lishi kerak — foydalanuvchi shikoyat qilganda logdan topish uchun");
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
