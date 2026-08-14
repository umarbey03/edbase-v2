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

    /// <summary>
    /// ★ TELEGRAM ATAYLAB SOZLANGAN: `phone/request-code` bot tokeni bo'sh
    ///   bo'lsa 503 qaytaradi (kod yuboradigan kanal yo'q). U holda bu
    ///   sinf "cheklov ichida 503, chegaradan keyin 429" ni tekshirgan
    ///   bo'lardi — ya'ni endpoint HAQIQATAN ishlayotganini umuman
    ///   isbotlamasdi.
    ///
    /// Tashqi tarmoqqa chiqish xavfi yo'q: xabar yuborish worker'i
    /// o'chirilgan va API manzili javob bermaydigan portga qaratilgan
    /// (`TelegramApiFactory` dagi AYNI naqsh).
    /// </summary>
    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Telegram:BotToken", "123456789:AAH-ratelimit-test-bot-token-xy"),
        new("Telegram:WebhookSecret", "zinnur_ratelimit_webhook_secret_2026"),
        new("Telegram:ApiBaseUrl", "http://127.0.0.1:9"),
        new("Notifications:Enabled", "false"),
    ];

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
/// ★ KIRISH OQIMINI TOSHIRISHGA QARSHI HIMOYA.
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
/// ══════════════════════════════════════════════════════════════════════
/// ⚠️ 2026-08-13: NISHON O'ZGARDI. Ilgari bu yerda `POST /auth/login`
///    sinalardi (parol topishga qarshi). Endi parol yo'q — o'rniga
///    `POST /auth/phone/request-code`.
///
/// 🔴 HAR SO'ROVDA YANGI RAQAM ISHLATILADI (`TestPhones.Next()`).
///    Sabab hal qiluvchi: endpointda IKKITA mustaqil chegara bor —
///      • IP bo'yicha (HTTP siyosati) — AYNAN shu sinf tekshiradi;
///      • RAQAM bo'yicha (Redis, 60 s) — `PhoneLoginQuotaTests` tekshiradi.
///    Bitta raqam takrorlansa IKKINCHI chegara birinchi bo'lib ishlab
///    ketardi va test IP cheklovini umuman sinamagan bo'lardi — lekin
///    baribir YASHIL chiqardi. Bu eng yomon turdagi test: himoya yo'q
///    bo'lsa ham "bor" deb ko'rsatadi.
/// ══════════════════════════════════════════════════════════════════════
///
/// Har test O'Z "IP"si bilan ishlaydi — aks holda birinchi test butun
/// budjetni yeb qo'yardi.
/// </summary>
public sealed class AuthRateLimitTests(ThrottledApiFactory factory)
    : IClassFixture<ThrottledApiFactory>
{
    /// <summary>Siyosatdagi budjet (<c>ThrottledApiFactory.AuthPermitLimit</c>).</summary>
    private const int PermitLimit = 10;

    /// <summary>
    /// Kod so'raydi. Raqam HAR CHAQIRUVDA yangi — sabab sinf izohida.
    /// </summary>
    private static Task<HttpResponseMessage> RequestCodeAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone = TestPhones.Next() });

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token });

    // ------------------------------------------------------------------ kirish

    /// <summary>
    /// ★ ASOSIY TEST: budjetdan keyingi urinish 429 oladi.
    ///
    /// Budjet ichidagi javoblar 200 bo'lishi kerak — raqam bazada
    /// bo'lmasa ham. Bu ikki narsani birdan isbotlaydi: cheklov haddan
    /// tashqari qattiq emas VA endpoint hisob sanashga yo'l bermaydi
    /// (noma'lum raqam ham 200 oladi).
    /// </summary>
    [Fact]
    public async Task RequestCode_AfterPermitLimit_ReturnsTooManyRequests()
    {
        using var client = factory.CreateClientFromIp("10.0.0.1");

        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt <= PermitLimit; attempt++)
        {
            using var response = await RequestCodeAsync(client);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(PermitLimit).Should().AllBeEquivalentTo(HttpStatusCode.OK,
            "budjet ichidagi so'rovlar odatdagidek javob olishi kerak");

        statuses[PermitLimit].Should().Be(HttpStatusCode.TooManyRequests,
            "11-so'rov chegaradan oshadi — aks holda kod so'rash cheksiz davom etardi");
    }

    /// <summary>
    /// ★ Cheklov IP BO'YICHA bo'linadi — bir mijozning bloklanishi
    /// boshqalarni ushlab qolmaydi.
    ///
    /// Bunsiz himoya o'zi xizmatdan voz kechirish (DoS) vositasi bo'lardi:
    /// bitta bot butun platformaga kirishni yopib qo'yardi.
    /// </summary>
    [Fact]
    public async Task RequestCode_LimitIsPerIp_AndDoesNotBlockOtherClients()
    {
        using var noisy = factory.CreateClientFromIp("10.0.0.3");

        for (var attempt = 0; attempt <= PermitLimit; attempt++)
        {
            using var burned = await RequestCodeAsync(noisy);
            burned.StatusCode.Should().Be(attempt < PermitLimit
                ? HttpStatusCode.OK
                : HttpStatusCode.TooManyRequests);
        }

        using var innocent = factory.CreateClientFromIp("10.0.0.4");
        using var response = await RequestCodeAsync(innocent);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "boshqa IP o'z budjetiga ega — qo'shni ayb bilan bloklanmasin");
    }

    /// <summary>
    /// Kodni TEKSHIRISH endpointi ham AYNI siyosat ostida.
    ///
    /// ★ NIMA UCHUN ALOHIDA TEST: `verify` — kodni taxmin qilish
    /// mumkin bo'lgan yagona joy. Uni cheklovsiz qoldirish 6 xonali
    /// kodni bir necha daqiqada topish imkonini berardi. Raqam bo'yicha
    /// urinishlar chegarasi ham bor (5 ta), lekin u FAQAT kod
    /// so'ralgan raqamlarga qo'llanadi — bu esa hamma so'rovga.
    /// </summary>
    [Fact]
    public async Task Verify_IsAlsoRateLimited()
    {
        using var client = factory.CreateClientFromIp("10.0.0.7");

        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt <= PermitLimit; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/v1/auth/phone/verify",
                new { phone = TestPhones.Next(), code = "000000" });

            statuses.Add(response.StatusCode);
        }

        statuses.Take(PermitLimit).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized,
            "kod so'ralmagan raqam uchun javob 'kod noto'g'ri' bo'ladi (401)");

        statuses[PermitLimit].Should().Be(HttpStatusCode.TooManyRequests);
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
    /// budjetda bir necha kod so'rovi butun binoni tizimdan chiqarib
    /// yuborardi — dars o'rtasida, sabab ko'rsatmasdan.
    /// </summary>
    [Fact]
    public async Task Refresh_HasItsOwnBudget_AndSurvivesLoginExhaustion()
    {
        using var client = factory.CreateClientFromIp("10.0.0.6");

        // Token HTTP orqali OLINMAYDI (`LoginAsAdminAsync` uni to'g'ridan-
        // to'g'ri yasaydi), ya'ni kirish budjetiga umuman tegmaydi — bu
        // testda aynan shu kerak.
        var tokens = await factory.LoginAsAdminAsync();

        // Kirish budjetini oxirigacha yeymiz.
        for (var attempt = 0; attempt < PermitLimit; attempt++)
        {
            using var burned = await RequestCodeAsync(client);
            burned.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var blockedLogin = await RequestCodeAsync(client);
        blockedLogin.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        using var refresh = await RefreshAsync(client, tokens.RefreshToken);

        refresh.StatusCode.Should().Be(HttpStatusCode.OK,
            "yangilashning budjeti alohida — kirish bloklangani uni to'smasligi kerak");
    }
}
