using System.Net;
using System.Net.Http.Json;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ★ PROKSI ORTIDAGI HAQIQIY MIJOZ IP'SI (2026-11-19 xavfsizlik auditi).
///
/// Prod'da so'rov API'ga docker-proxy orqali keladi, ya'ni ulanish IP'si
/// HAR DOIM docker shlyuzi (172.18.0.1). `UseForwardedHeaders` bo'lmaganda
/// IP bo'yicha rate limit butun platformani BITTA bo'limga tiqardi: bitta
/// bot kod so'rash budjetini yeb, HAMMA o'quvchining kirishini yopardi.
///
/// Bu sinf uch narsani HULQ-ATVOR darajasida qulflaydi:
///   1. ishonchli proksi ortidagi har bir mijozning O'Z budjeti bor;
///   2. mijoz `X-Forwarded-For` ga qalbaki yozuv qo'shib cheklovdan
///      qochib qutula olmaydi (faqat nginx qo'shgan eng o'ngdagi yozuv);
///   3. proksi BO'LMAGAN manba sarlavha bilan o'zini boshqa mijoz qilib
///      ko'rsata olmaydi.
///
/// `ThrottledApiFactory` ulanish IP'sini `X-Test-Client-Ip` orqali
/// quvurning ENG BOSHIDA o'rnatadi — ya'ni `UseForwardedHeaders` uni
/// haqiqiy ulanish IP'si sifatida ko'radi.
/// </summary>
public sealed class ForwardedClientIpRateLimitTests(ThrottledApiFactory factory)
    : IClassFixture<ThrottledApiFactory>
{
    /// <summary><c>ThrottledApiFactory.AuthPermitLimit</c>.</summary>
    private const int PermitLimit = 10;

    /// <summary>Prod'dagi docker shlyuzi — standart ishonchli tarmoq (172.16.0.0/12) ichida.</summary>
    private const string DockerGateway = "172.18.0.1";

    private HttpClient ClientVia(string connectionIp, string forwardedFor)
    {
        var client = factory.CreateClientFromIp(connectionIp);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    private static Task<HttpResponseMessage> RequestCodeAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone = TestPhones.Next() });

    private static async Task BurnBudgetAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < PermitLimit; attempt++)
        {
            using var response = await RequestCodeAsync(client);
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                "budjet ichidagi so'rov cheklovga urilmasligi kerak");
        }

        using var blocked = await RequestCodeAsync(client);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "budjetdan keyingi so'rov bloklanishi kerak");
    }

    /// <summary>
    /// ★ ASOSIY TEST: bitta proksi (docker shlyuzi) orqali kelgan IKKI xil
    /// mijoz bir-birining budjetini yemaydi. Tuzatishdan oldin bu test
    /// yiqilardi — ikkalasi ham "172.18.0.1" bo'limiga tushardi.
    /// </summary>
    [Fact]
    public async Task BehindTrustedProxy_EachClientHasItsOwnBudget()
    {
        using var noisy = ClientVia(DockerGateway, "198.51.100.10");
        await BurnBudgetAsync(noisy);

        using var innocent = ClientVia(DockerGateway, "198.51.100.11");
        using var response = await RequestCodeAsync(innocent);

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "proksi ortidagi boshqa mijoz qo'shnining aybi bilan bloklanmasin");
    }

    /// <summary>
    /// ★ Qalbaki chap yozuvlar hisobga olinmaydi. nginx sarlavhaning OXIRIGA
    /// o'zi ko'rgan IP'ni qo'shadi; mijoz yozgan qism chapda qoladi. Hujumchi
    /// har so'rovda chap qismni almashtirib yangi budjet ola olmasligi kerak.
    /// </summary>
    [Fact]
    public async Task BehindTrustedProxy_SpoofedLeftEntries_DoNotEscapeTheLimit()
    {
        using var attacker = ClientVia(DockerGateway, "203.0.113.1, 198.51.100.20");
        await BurnBudgetAsync(attacker);

        using var spoofed = ClientVia(DockerGateway, "203.0.113.99, 198.51.100.20");
        using var response = await RequestCodeAsync(spoofed);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "haqiqiy mijoz (eng o'ngdagi yozuv) o'zgarmagan — budjet ham o'sha");
    }

    /// <summary>
    /// ★ Ishonchsiz manba sarlavhani BOSHQARA OLMAYDI. Proksi bo'lmagan
    /// ulanish (ochiq internet IP'si) `X-Forwarded-For` ni almashtirib har
    /// so'rovda "yangi mijoz" bo'la olsa, cheklov umuman ishlamasdi.
    /// </summary>
    [Fact]
    public async Task FromUntrustedPeer_ForwardedHeaderIsIgnored()
    {
        using var direct = ClientVia("203.0.113.50", "198.51.100.30");
        await BurnBudgetAsync(direct);

        using var disguised = ClientVia("203.0.113.50", "198.51.100.31");
        using var response = await RequestCodeAsync(disguised);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "sarlavha ishonchsiz manbadan — budjet ulanish IP'si bo'yicha qoladi");
    }
}
