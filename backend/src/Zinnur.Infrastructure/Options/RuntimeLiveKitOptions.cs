using Microsoft.Extensions.Options;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>LiveKit:*</c> — bazadan boshqariladigan qismi bilan birga.
///
/// ★ QAYSI MAYDONLAR BAZADAN: <c>ApiKey</c>, <c>ApiSecret</c> va
/// (2026-08-14 dan) <c>Url</c>. Kalit va sir LiveKit serveridagi
/// <c>LIVEKIT_KEYS</c> bilan juftlikda ishlaydi va AYLANTIRILADIGAN
/// (rotate) ma'lumot; ichki manzil esa ULANISH NUQTASI — loyiha egasining
/// "ulanish joylari panel orqali boshqarilsin" talabi aynan shu turkumga
/// tegishli (`storage.service_url` ham shu sababdan bazadan o'qiladi).
///
/// ★★ NIMA UCHUN ILGARI BO'LMAGAN VA NIMA O'ZGARDI: yagona to'siq —
/// `LiveKitHealthCheck` manzilni <c>IConfiguration</c> dan TO'G'RIDAN-TO'G'RI
/// o'qirdi. Ya'ni bazadan boshqarilsa probe bir manzilni, token esa
/// boshqasini ko'rsatib, "sog'lom, lekin dars ochilmaydi" degan chalg'ituvchi
/// holat paydo bo'lardi. Endi sog'liq tekshiruvi ham AYNI SHU obyektni
/// o'qiydi (izoh o'sha sinfda), ya'ni to'siq yo'q: manba BITTA.
///
/// ★ NIMA UCHUN <c>PublicUrl</c> HAMON MUHITDAN: u brauzerga ketadi va
/// SERTIFIKAT bilan juftlashgan — `wss://` domeni DNS va TLS bilan birga
/// o'zgaradi, ya'ni u haqiqatan DEPLOY qarori. Uni ham ochish ALOHIDA
/// qaror (`livekit.public_url` registrdagi izohi) — bu yerda ATAYLAB
/// qilinmadi.
///
/// ⚠️ DEV'DAGI BOG'LIQLIK: <c>PublicUrl</c> bo'sh bo'lsa
/// <c>EffectivePublicUrl</c> <c>Url</c> ga qaytadi, ya'ni dev'da ichki
/// manzilni paneldan o'zgartirish BRAUZER manzilini ham o'zgartiradi.
/// Prod'da <c>PublicUrl</c> to'ldirilgan bo'lishi shart, shuning uchun
/// bu bog'liqlik faqat dev qulayligi bo'lib qoladi.
///
/// ════════════════════════════════════════════════════════════════════════
/// ★★ LIVEKIT CLOUD — ZAXIRA YO'L (2026-09-21)
/// ════════════════════════════════════════════════════════════════════════
///
/// <c>livekit.provider = Cloud</c> bo'lsa obyekt BUTUNLAY Cloud
/// qiymatlaridan yig'iladi: manzil, brauzer manzili, kalit va sir. Token,
/// egress, xona boshqaruvi, webhook va sog'liq tekshiruvi shu obyektni
/// o'qigani uchun ular almashtirishdan BEXABAR — ya'ni boshqa hech qayerda
/// "agar Cloud bo'lsa" degan shart yo'q va bo'lmasligi kerak.
///
/// ★ NIMA UCHUN <c>PublicUrl</c> BU YERDA MUHITDAN EMAS: Cloud manzili
/// Cloud'ning O'Z sertifikati bilan keladi — bizning DNS/TLS ga bog'liq
/// emas, ya'ni yuqoridagi "deploy qarori" sababi unga tegishli emas.
///
/// ★ ZAXIRA: tanlov <c>Cloud</c>, lekin to'plam to'liq emas (panel buni
/// `SettingCoupling.Activations` bilan to'sadi, bu faqat bazani qo'lda
/// tahrirlash holati) — o'z serverimizga QAYTILADI. Bo'sh kalit bilan
/// imzolangan token HAMMA darsni o'chirardi; o'z serverimiz esa hech
/// bo'lmasa avvalgidek ishlaydi.
/// </summary>
public sealed class RuntimeLiveKitOptions(IRuntimeSettings runtime, IOptions<LiveKitOptions> seed)
    : RuntimeOptions<LiveKitOptions>(runtime, seed)
{
    protected override LiveKitOptions Compose(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // ★ ZAXIRA `Seed` GA: registr bo'sh qiymatni qabul qilmaydi
        //   (`MinLength = 1`), lekin kesim hali to'lmagan holatda
        //   (masalan kalit bazada ham, muhitda ham yo'q) bo'sh manzil
        //   sog'liq tekshiruvini `Unhealthy` qilib, ilovani sababsiz
        //   "nosoz" ko'rsatardi.
        var selfHostedUrl = Fallback(snapshot.Value(SettingsRegistry.Keys.LiveKitUrl), Seed.Url);
        var selfHostedKey = snapshot.Value(SettingsRegistry.Keys.LiveKitApiKey) ?? string.Empty;
        var selfHostedSecret = snapshot.Value(SettingsRegistry.Keys.LiveKitApiSecret) ?? string.Empty;

        var cloud = TryReadCloud(snapshot);

        if (cloud is { } active && IsCloudSelected(snapshot))
        {
            return new LiveKitOptions
            {
                // Backend (Twirp API, sog'liq tekshiruvi) — `https://`,
                // brauzer — `wss://`. Ikkalasi ham AYNI host: Cloud signal
                // va API'ni bitta domenda beradi.
                Url = "https://" + active.Host,
                PublicUrl = "wss://" + active.Host,
                ApiKey = active.ApiKey,
                ApiSecret = active.ApiSecret,

                // O'z serverimizdan kechikkan webhook'lar uchun (izoh:
                // `LiveKitOptions.StandbyApiKey`).
                StandbyApiKey = selfHostedKey,
                StandbyApiSecret = selfHostedSecret,
            };
        }

        return new LiveKitOptions
        {
            Url = selfHostedUrl,
            PublicUrl = Seed.PublicUrl,

            ApiKey = selfHostedKey,
            ApiSecret = selfHostedSecret,

            // Cloud'dan qaytgandan keyin kechikkan webhook'lar uchun.
            StandbyApiKey = cloud?.ApiKey ?? string.Empty,
            StandbyApiSecret = cloud?.ApiSecret ?? string.Empty,
        };
    }

    private static string Fallback(string? value, string seed) =>
        string.IsNullOrWhiteSpace(value) ? seed : value;

    private static bool IsCloudSelected(SettingsSnapshot snapshot) =>
        string.Equals(
            snapshot.Value(SettingsRegistry.Keys.LiveKitProvider)?.Trim(),
            SettingsRegistry.LiveKitProviders.Cloud,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cloud to'plami TO'LIQ bo'lsa — uning qiymatlari, aks holda <c>null</c>.
    /// Tanlovdan qat'i nazar o'qiladi: faol bo'lmaganda ham webhook
    /// tekshiruvi uchun zaxira juftlik sifatida kerak.
    /// </summary>
    private static CloudSet? TryReadCloud(SettingsSnapshot snapshot)
    {
        var apiKey = snapshot.Value(SettingsRegistry.Keys.LiveKitCloudApiKey)?.Trim();
        var apiSecret = snapshot.Value(SettingsRegistry.Keys.LiveKitCloudApiSecret)?.Trim();

        if (string.IsNullOrEmpty(apiKey)
            || string.IsNullOrEmpty(apiSecret)
            || !TryCloudHost(snapshot.Value(SettingsRegistry.Keys.LiveKitCloudUrl), out var host))
        {
            return null;
        }

        return new CloudSet(host, apiKey, apiSecret);
    }

    private sealed record CloudSet(string Host, string ApiKey, string ApiSecret);

    /// <summary>
    /// Kiritilgan manzildan faqat hostni (va port bo'lsa portni) oladi.
    ///
    /// ★ NIMA UCHUN SXEMA E'TIBORSIZ: Cloud panelida manzil ba'zan
    /// <c>wss://</c>, ba'zan <c>https://</c> ko'rinishida chiqadi va ikkalasi
    /// ham AYNI loyiha. Sxemani har ikki tomon uchun o'zimiz qo'yamiz —
    /// Cloud faqat TLS bilan ishlaydi, `ws://` esa HTTPS sahifadan baribir
    /// bloklanardi.
    /// </summary>
    private static bool TryCloudHost(string? url, out string host)
    {
        host = string.Empty;

        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return false;

        host = uri.IsDefaultPort ? uri.Host : uri.Authority;
        return true;
    }
}
