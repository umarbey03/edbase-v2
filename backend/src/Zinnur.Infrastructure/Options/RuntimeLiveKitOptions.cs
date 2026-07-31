using Microsoft.Extensions.Options;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>LiveKit:*</c> — bazadan boshqariladigan qismi bilan birga.
///
/// ★ QAYSI MAYDONLAR BAZADAN: faqat <c>ApiKey</c> va <c>ApiSecret</c>.
/// Ular LiveKit serveridagi <c>LIVEKIT_KEYS</c> bilan juftlikda ishlaydi
/// va AYLANTIRILADIGAN (rotate) ma'lumot.
///
/// ★ NIMA UCHUN MANZILLAR (<c>Url</c>, <c>PublicUrl</c>) BAZADA EMAS:
/// ichki manzilni sog'liq tekshiruvi (`/health/ready`) TO'G'RIDAN-TO'G'RI
/// konfiguratsiyadan o'qiydi. Bazadan boshqarilsa probe bir manzilni,
/// token esa boshqasini ko'rsatib, "sog'lom, lekin dars ochilmaydi" degan
/// chalg'ituvchi holat paydo bo'lardi. Ikkalasi ham sertifikat va DNS
/// bilan birga o'zgaradi — ya'ni bu DEPLOY qarori.
/// </summary>
public sealed class RuntimeLiveKitOptions(IRuntimeSettings runtime, IOptions<LiveKitOptions> seed)
    : RuntimeOptions<LiveKitOptions>(runtime, seed)
{
    protected override LiveKitOptions Compose(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new LiveKitOptions
        {
            Url = Seed.Url,
            PublicUrl = Seed.PublicUrl,

            ApiKey = snapshot.Value(SettingsRegistry.Keys.LiveKitApiKey) ?? string.Empty,
            ApiSecret = snapshot.Value(SettingsRegistry.Keys.LiveKitApiSecret) ?? string.Empty,
        };
    }
}
