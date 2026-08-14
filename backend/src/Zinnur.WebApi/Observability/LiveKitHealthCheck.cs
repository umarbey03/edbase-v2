using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zinnur.Infrastructure.Options;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// LiveKit serveri javob berayotganini tekshiradi (oddiy HTTP GET).
///
/// NIMA UCHUN KERAK: postgres va redis tirik bo'lsa ham, LiveKit o'lgan
/// bo'lsa jonli dars OCHILMAYDI. Eski tizimda buni faqat o'quvchi
/// "video ishlamayapti" deb yozganda bilardik.
///
/// NIMA UCHUN <c>Degraded</c>, <c>Unhealthy</c> EMAS:
/// <c>/health/ready</c> — Docker'ning healthcheck'i, va <c>web</c>
/// konteyneri <c>api: service_healthy</c> ga bog'langan. Agar LiveKit
/// yiqilganda api ham "unhealthy" bo'lsa, BUTUN sayt (login, jadval,
/// hisobotlar — LiveKit'siz ham ishlaydigan hamma narsa) o'chib qolardi.
/// Degraded esa: umumiy holat halol ko'rsatiladi, HTTP 200 qaytadi,
/// konteyner tirik qoladi.
///
/// ════════════════════════════════════════════════════════════════════════
/// ★★ MANZIL <c>IConfiguration</c> DAN EMAS, ISH JARAYONIDAGI SOZLAMADAN
/// ════════════════════════════════════════════════════════════════════════
///
/// Ilgari bu sinf <c>configuration["LiveKit:Url"]</c> ni TO'G'RIDAN-TO'G'RI
/// o'qirdi va AYNAN shu bitta qator <c>livekit.url</c> ni panelda
/// tahrirlab bo'lmaydigan qilib turardi: bazadan boshqarilsa probe BIR
/// manzilni, token esa BOSHQASINI ko'rsatib, "sog'lom, lekin dars
/// ochilmaydi" degan chalg'ituvchi holat paydo bo'lardi.
///
/// Endi manba BITTA — <see cref="IRuntimeOptions{TOptions}"/>, ya'ni
/// AYNAN <c>LiveKitTokenService</c> va <c>LiveKitEgressClient</c> o'qiydigan
/// obyekt. Probe va token bir-biridan AJRALA OLMAYDI: ular bitta kesimning
/// bitta maydonini o'qiydi.
///
/// ★ SOVUQ START: kesim hali o'qilmagan bo'lsa <c>Current</c> muhitdagi
/// (boshlang'ich) qiymatni qaytaradi — ya'ni ilova ko'tarilgan birinchi
/// soniyalarda probe ham, token ham AYNI o'sha manzilga qaraydi.
/// </summary>
internal sealed class LiveKitHealthCheck(
    IHttpClientFactory httpClientFactory, IRuntimeOptions<LiveKitOptions> liveKit)
    : IHealthCheck
{
    public const string Name = "livekit";
    public const string HttpClientName = "livekit-health";

    /// <summary>Sog'liq tekshiruvi tez bo'lishi shart — aks holda probe o'zi osilib qoladi.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private const string UrlKey = "LiveKit:Url";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ Kesim BIR MARTA olinadi (`IRuntimeOptions` shartnomasi):
        //    tekshiruv o'rtasida sozlama yangilansa, xabar va probe
        //    boshqa-boshqa manzilni ko'rsatib qolardi.
        var configured = liveKit.Current.Url;

        if (!TryBuildProbeUri(configured, out var probe))
            return HealthCheckResult.Unhealthy($"{UrlKey} sozlanmagan yoki noto'g'ri manzil.");

        var failure = context.Registration.FailureStatus;

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(probe, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy();

            return new HealthCheckResult(
                failure,
                string.Create(CultureInfo.InvariantCulture, $"LiveKit HTTP {(int)response.StatusCode}"));
        }
        catch (HttpRequestException ex)
        {
            return new HealthCheckResult(failure, "LiveKit'ga ulanib bo'lmadi.", ex);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout tugadi (so'rovni bekor qilgan biz emasmiz).
            return new HealthCheckResult(failure, "LiveKit belgilangan vaqtda javob bermadi.");
        }
    }

    /// <summary>
    /// LiveKit ichki manzilini HTTP probe manziliga aylantiradi.
    ///
    /// NIMA UCHUN ALMASHTIRISH KERAK: sozlamada manzil <c>ws://</c> yoki
    /// <c>wss://</c> bo'lishi mumkin (appsettings.json da aynan shunday).
    /// <see cref="HttpClient"/> bu sxemalarni bilmaydi — <c>ws</c> -&gt; <c>http</c>,
    /// <c>wss</c> -&gt; <c>https</c> deb o'giramiz. LiveKit ikkalasini ham
    /// bitta portda tinglaydi va ildiz yo'lida <c>200 OK</c> qaytaradi.
    /// </summary>
    private static bool TryBuildProbeUri(string? configured, [NotNullWhen(true)] out Uri? probe)
    {
        probe = null;

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var parsed))
            return false;

        var scheme = parsed.Scheme switch
        {
            "ws" or "http" => Uri.UriSchemeHttp,
            "wss" or "https" => Uri.UriSchemeHttps,
            _ => null,
        };

        if (scheme is null)
            return false;

        probe = new UriBuilder(parsed)
        {
            Scheme = scheme,
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;

        return true;
    }
}
