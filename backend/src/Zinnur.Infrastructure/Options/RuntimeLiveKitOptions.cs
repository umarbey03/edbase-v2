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
/// </summary>
public sealed class RuntimeLiveKitOptions(IRuntimeSettings runtime, IOptions<LiveKitOptions> seed)
    : RuntimeOptions<LiveKitOptions>(runtime, seed)
{
    protected override LiveKitOptions Compose(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new LiveKitOptions
        {
            // ★ ZAXIRA `Seed` GA: registr bo'sh qiymatni qabul qilmaydi
            //   (`MinLength = 1`), lekin kesim hali to'lmagan holatda
            //   (masalan kalit bazada ham, muhitda ham yo'q) bo'sh manzil
            //   sog'liq tekshiruvini `Unhealthy` qilib, ilovani sababsiz
            //   "nosoz" ko'rsatardi.
            Url = Fallback(snapshot.Value(SettingsRegistry.Keys.LiveKitUrl), Seed.Url),
            PublicUrl = Seed.PublicUrl,

            ApiKey = snapshot.Value(SettingsRegistry.Keys.LiveKitApiKey) ?? string.Empty,
            ApiSecret = snapshot.Value(SettingsRegistry.Keys.LiveKitApiSecret) ?? string.Empty,
        };
    }

    private static string Fallback(string? value, string seed) =>
        string.IsNullOrWhiteSpace(value) ? seed : value;
}
