using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Chegarasi ATAYLAB pasaytirilgan API: auth endpointlariga 10 so'rov,
/// oyna esa uzun (5 daqiqa).
///
/// NEGA UZUN OYNA: qat'iy oyna (fixed window) BIRINCHI so'rovda ochiladi.
/// Prod'dagi 1 daqiqalik oyna so'rovlar orasida yopilib qolsa hisoblagich
/// nolga qaytardi va test tasodifan yashil chiqardi — ya'ni cheklov
/// umuman qo'llanmagan bo'lsa ham "o'tib" ketardi.
///
/// KLIENT IP'SI: `TestServer` da `RemoteIpAddress` — null, ya'ni bu
/// sinfdagi HAMMA so'rov bitta bo'limga (`"unknown"`) tushardi va birinchi
/// test butun budjetni yeb, qolganlarini yiqitardi. Shuning uchun quvurning
/// eng boshiga kichik middleware qo'yiladi: u `X-Test-Client-Ip` sarlavhasini
/// ulanish IP'siga o'girib beradi. Cheklovning O'ZI haqiqiy holicha qoladi —
/// biz faqat testga "boshqa mijoz" bo'lish imkonini beramiz.
/// </summary>
public sealed class ThrottledApiFactory : ZinnurApiFactory
{
    /// <summary>Testdagi soxta IP shu sarlavhada keladi.</summary>
    public const string ClientIpHeader = "X-Test-Client-Ip";

    protected override int AuthPermitLimit => 10;

    protected override int AuthRefreshPermitLimit => 10;

    protected override int AuthWindowSeconds => 300;

    /// <summary>Berilgan "IP" nomidan so'rov yuboradigan klient.</summary>
    public HttpClient CreateClientFromIp(string ip)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ClientIpHeader, ip);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        // `IStartupFilter` — quvurning ENG BOSHIGA middleware qo'yishning
        // yagona yo'li: `Configure` ni qayta yozish butun `Program.cs`
        // quvurini almashtirib yuborardi va biz boshqa ilovani sinagan
        // bo'lardik.
        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, ClientIpStartupFilter>());
    }

    private sealed class ClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return app =>
            {
                app.Use(async (context, proceed) =>
                {
                    if (context.Request.Headers.TryGetValue(ClientIpHeader, out var raw)
                        && System.Net.IPAddress.TryParse(raw.ToString(), out var ip))
                    {
                        context.Connection.RemoteIpAddress = ip;
                    }

                    await proceed();
                });

                next(app);
            };
        }
    }
}

/// <summary>
/// ★ PAROL TOPISHGA (brute force) QARSHI HIMOYA.
///
/// Bu testlar mavjud bo'lishining sababi: rate-limit siyosati `Program.cs`
/// da e'lon qilingan va izohda "kirish endpointi cheklanadi" deb yozilgan
/// edi, LEKIN u hech qayerga qo'llanmagandi. Butun `backend/src` da na
/// `EnableRateLimiting`, na `RequireRateLimiting` bor edi — bitta IP'dan
/// 1500 ta kirish so'rovi to'siqsiz o'tardi.
///
/// Bunday xatoni kod o'qib topish qiyin: konfiguratsiya joyida, izoh
/// joyida, faqat bog'lovchi halqa yo'q. Shuning uchun himoyani
/// HULQ-ATVOR darajasida qulflaymiz.
///
/// Har test O'Z "IP"si bilan ishlaydi — aks holda birinchi test butun
/// budjetni yeb qo'yardi.
/// </summary>
public sealed class AuthRateLimitTests(ThrottledApiFactory factory)
    : IClassFixture<ThrottledApiFactory>
{
    private const string AdminEmail = "admin@zinnur.uz";
    private const string AdminPassword = "Admin!2345";
    private const string WrongPassword = "noto'g'ri-parol";

    /// <summary>Siyosatdagi budjet (<c>ThrottledApiFactory.AuthPermitLimit</c>).</summary>
    private const int PermitLimit = 10;

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email = AdminEmail, password });

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token });

    // ------------------------------------------------------------------ kirish

    /// <summary>
    /// ★ ASOSIY TEST: budjetdan keyingi urinish 429 oladi.
    ///
    /// Parol ATAYLAB noto'g'ri — hujum aynan MUVAFFAQIYATSIZ urinishlardan
    /// iborat. Birinchi 10 tasining 401 bo'lishi esa cheklov haddan tashqari
    /// qattiq emasligini ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task Login_AfterPermitLimit_ReturnsTooManyRequests()
    {
        using var client = factory.CreateClientFromIp("10.0.0.1");

        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt <= PermitLimit; attempt++)
        {
            using var response = await LoginAsync(client, WrongPassword);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(PermitLimit).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized,
            "budjet ichidagi urinishlar odatdagidek javob olishi kerak");

        statuses[PermitLimit].Should().Be(HttpStatusCode.TooManyRequests,
            "11-urinish chegaradan oshadi — aks holda parol topish cheksiz davom etardi");
    }

    /// <summary>
    /// Chegara TO'G'RI parolni ham sanaydi.
    ///
    /// NEGA MUHIM: faqat 401 javoblar sanalganda hujumchi parolni topgan
    /// ondan boshlab cheksiz token ola olardi. Bu — hisob egallanganidan
    /// keyingi zarar chegarasi.
    /// </summary>
    [Fact]
    public async Task Login_WithCorrectPassword_IsAlsoCounted()
    {
        using var client = factory.CreateClientFromIp("10.0.0.2");

        for (var attempt = 0; attempt < PermitLimit; attempt++)
        {
            using var response = await LoginAsync(client, AdminPassword);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var blocked = await LoginAsync(client, AdminPassword);

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// ★ Cheklov IP BO'YICHA bo'linadi — bir mijozning bloklanishi
    /// boshqalarni ushlab qolmaydi.
    ///
    /// Bunsiz himoya o'zi xizmatdan voz kechirish (DoS) vositasi bo'lardi:
    /// bitta bot butun platformaga kirishni yopib qo'yardi.
    /// </summary>
    [Fact]
    public async Task Login_LimitIsPerIp_AndDoesNotBlockOtherClients()
    {
        using var noisy = factory.CreateClientFromIp("10.0.0.3");

        for (var attempt = 0; attempt <= PermitLimit; attempt++)
        {
            using var burned = await LoginAsync(noisy, WrongPassword);
            burned.StatusCode.Should().Be(attempt < PermitLimit
                ? HttpStatusCode.Unauthorized
                : HttpStatusCode.TooManyRequests);
        }

        using var innocent = factory.CreateClientFromIp("10.0.0.4");
        using var response = await LoginAsync(innocent, AdminPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "boshqa IP o'z budjetiga ega — qo'shni ayb bilan bloklanmasin");
    }

    // ------------------------------------------------------------------ yangilash

    /// <summary>Yangilash endpointi ham anonim va tashqariga ochiq — u ham cheklanadi.</summary>
    [Fact]
    public async Task Refresh_AfterPermitLimit_ReturnsTooManyRequests()
    {
        using var client = factory.CreateClientFromIp("10.0.0.5");

        for (var attempt = 0; attempt < PermitLimit; attempt++)
        {
            using var response = await RefreshAsync(client, "qalbaki-token");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var blocked = await RefreshAsync(client, "qalbaki-token");

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// ★ IKKI SIYOSAT MUSTAQIL: kirish budjeti tugasa ham, allaqachon
    /// kirgan foydalanuvchi tokenini yangilay oladi.
    ///
    /// NEGA SHUNDAY: bitta maktab bitta NAT IP orqasida turadi. Umumiy
    /// budjetda bir necha noto'g'ri parol butun binoni tizimdan chiqarib
    /// yuborardi — dars o'rtasida, sabab ko'rsatmasdan.
    /// </summary>
    [Fact]
    public async Task Refresh_HasItsOwnBudget_AndSurvivesLoginExhaustion()
    {
        using var client = factory.CreateClientFromIp("10.0.0.6");

        // Haqiqiy token — kirish budjeti tugashidan OLDIN olinadi (1-ruxsat).
        using var login = await LoginAsync(client, AdminPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await login.Content.ReadFromJsonAsync<AuthTokens>();

        // Qolgan budjetni oxirigacha yeymiz (2..10-ruxsatlar).
        for (var attempt = 1; attempt < PermitLimit; attempt++)
        {
            using var burned = await LoginAsync(client, WrongPassword);
            burned.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var blockedLogin = await LoginAsync(client, WrongPassword);
        blockedLogin.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        using var refresh = await RefreshAsync(client, tokens!.RefreshToken);

        refresh.StatusCode.Should().Be(HttpStatusCode.OK,
            "yangilashning budjeti alohida — kirish bloklangani uni to'smasligi kerak");
    }
}
