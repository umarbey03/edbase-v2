using Sentry;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// Sentry'ga hodisa YUBORILISHIDAN OLDIN maxfiy ma'lumotni o'chiradi.
///
/// NIMA UCHUN: Sentry — TASHQI xizmat. Unga ketgan har bayt bizning
/// nazoratimizdan chiqadi. Standart holatda SDK so'rov sarlavhalarini
/// (<c>Authorization: Bearer ...</c>), cookie'larni va so'rov satrini
/// (<c>?access_token=...</c>) hodisaga qo'shib yuboradi — ya'ni JONLI
/// TOKENLAR uchinchi tomon serveriga tushardi. Kim log'ni ko'ra olsa,
/// o'sha foydalanuvchi nomidan tizimga kira olardi.
///
/// Shuning uchun bu filtr <c>SetBeforeSend</c> orqali MAJBURIY ulanadi va
/// hech qanday sozlama bilan o'chirilmaydi.
///
/// QOIDA: o'chirilgan qiymat OLIB TASHLANMAYDI, balki
/// <see cref="Filtered"/> bilan almashtiriladi — shunda operator maydon
/// bor edi-yu tozalangan ekanini ko'radi (jimgina yo'qolib qolmaydi).
/// </summary>
internal static class SentryScrubber
{
    /// <summary>Tozalangan qiymat o'rniga qo'yiladigan belgi.</summary>
    public const string Filtered = "[Filtered]";

    /// <summary>
    /// Nom ichida shu bo'laklardan biri uchrasa — qiymat maxfiy deb hisoblanadi.
    /// Solishtirish registrga sezgir emas.
    ///
    /// DIQQAT: <c>traceId</c> bu ro'yxatga TUSHMAYDI — u aynan qidirish uchun kerak.
    /// </summary>
    private static readonly string[] SensitiveFragments =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",          // access_token, refresh_token, id_token, X-Auth-Token
        "authorization",  // Authorization, Proxy-Authorization
        "cookie",         // Cookie, Set-Cookie
        "apikey",
        "api_key",
        "api-key",
        "credential",
        "signature",
        "sessionid",
    ];

    /// <summary>
    /// <c>SetBeforeSend</c> uchun ulanadigan filtr.
    /// Hodisani <c>null</c> qaytarib bekor qilmaydi — faqat tozalaydi.
    /// </summary>
    public static SentryEvent Scrub(SentryEvent sentryEvent)
    {
        ScrubUser(sentryEvent);
        ScrubRequest(sentryEvent);
        ScrubBag(sentryEvent);

        return sentryEvent;
    }

    /// <summary>Nom maxfiymi (registrga sezgir emas).</summary>
    public static bool IsSensitive(string name) =>
        Array.Exists(SensitiveFragments, fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>a=1&amp;access_token=xyz</c> -&gt; <c>a=1&amp;access_token=[Filtered]</c>.
    /// Kalit nomlari saqlanadi (nosozlikni tekshirishda foydali), faqat qiymat ketadi.
    /// </summary>
    public static string ScrubQuery(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return string.Empty;

        var body = query.StartsWith('?') ? query[1..] : query;
        if (body.Length == 0)
            return string.Empty;

        var pairs = body.Split('&', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < pairs.Length; i++)
        {
            var separator = pairs[i].IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var name = pairs[i][..separator];
            if (IsSensitive(Uri.UnescapeDataString(name)))
                pairs[i] = string.Concat(name, "=", Filtered);
        }

        return string.Join('&', pairs);
    }

    /// <summary>URL ning faqat so'rov satri qismini tozalaydi.</summary>
    public static string? ScrubUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var mark = url.IndexOf('?', StringComparison.Ordinal);
        if (mark < 0)
            return url;

        var scrubbed = ScrubQuery(url[(mark + 1)..]);
        return scrubbed.Length == 0 ? url[..mark] : string.Concat(url[..mark], "?", scrubbed);
    }

    /// <summary>
    /// Shaxsiy ma'lumot (PII). <c>SendDefaultPii = false</c> bo'lsa ham
    /// ATAYLAB takroran tozalaymiz: sozlama kelajakda kimdir tomonidan
    /// yoqib yuborilsa ham email/IP chiqib ketmasin.
    /// Foydalanuvchi <c>Id</c> si QOLADI — u ismsiz raqam va "kimda buzilgan?"
    /// degan savolga javob beradi.
    /// </summary>
    private static void ScrubUser(SentryEvent sentryEvent)
    {
        sentryEvent.User.Email = null;
        sentryEvent.User.Username = null;
        sentryEvent.User.IpAddress = null;
        sentryEvent.User.Other.Clear();
    }

    private static void ScrubRequest(SentryEvent sentryEvent)
    {
        var request = sentryEvent.Request;

        foreach (var header in request.Headers.Keys.Where(IsSensitive).ToList())
            request.Headers[header] = Filtered;

        foreach (var variable in request.Env.Keys.Where(IsSensitive).ToList())
            request.Env[variable] = Filtered;

        foreach (var key in request.Other.Keys.Where(IsSensitive).ToList())
            request.Other[key] = Filtered;

        // Cookie'lar TO'LIQ olib tashlanadi: ular ichida nima borligini
        // oldindan bilmaymiz, demak bittasini ham yubormaymiz.
        request.Cookies = null;

        request.QueryString = ScrubQuery(request.QueryString);
        request.Url = ScrubUrl(request.Url);

        // So'rov tanasi (body) hech qachon yuborilmaydi — unda parol, telefon,
        // to'lov ma'lumoti bo'lishi mumkin. (MaxRequestBodySize=None bo'lsa ham
        // ikkinchi to'siq sifatida qoldiramiz.)
        if (request.Data is not null)
            request.Data = Filtered;
    }

    /// <summary>Teg va qo'shimcha maydonlar (<c>Extra</c>) — log xususiyatlari shu yerga tushadi.</summary>
    private static void ScrubBag(SentryEvent sentryEvent)
    {
        foreach (var tag in sentryEvent.Tags.Keys.Where(IsSensitive).ToList())
            sentryEvent.SetTag(tag, Filtered);

        // Sentry SDK'da `UnsetExtra` yo'q — qiymatni almashtiramiz.
        foreach (var extra in sentryEvent.Extra.Keys.Where(IsSensitive).ToList())
            sentryEvent.SetExtra(extra, Filtered);
    }
}
