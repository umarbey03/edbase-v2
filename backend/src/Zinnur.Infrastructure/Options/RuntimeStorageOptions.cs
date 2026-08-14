using Microsoft.Extensions.Options;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>Storage:*</c> — bazadan boshqariladigan qismi bilan birga.
///
/// ★ NIMA UCHUN BU KERAK BO'LDI: R2 kalitlari AYLANTIRILADIGAN (rotate)
/// ma'lumot. Ilgari ularni almashtirish uchun `.env` ni tahrirlab, API'ni
/// qayta joylashtirish kerak edi — ya'ni kalit sizib chiqqan paytda,
/// eng shoshilinch daqiqada, eng sekin yo'l. Endi panelda saqlash kifoya.
///
/// ★ QAYSI MAYDONLAR BAZADAN, QAYSILARI YO'Q:
///   • ServiceUrl, Bucket, AccessKey, SecretKey, Region — BAZADAN
///     (registrda <c>Source = Database</c>);
///   • PublicUrl — 2026-08-13 dan BAZADAN. Ilgari u muhitdan o'qilardi;
///     loyiha egasining "Cloudflare ulanish joylari paneldan
///     boshqarilsin" talabi bilan o'tkazildi. To'liq sabab va eski
///     qarorning dalillariga javob — registrdagi izohda.
///     🔴 BU AYNIQSA MUHIM O'ZGARISH: xato <c>PublicUrl</c> "jim 403"
///     beradi (SigV4 host'ni imzolaydi) va ilgari uni tuzatishning
///     yagona yo'li QAYTA JOYLASHTIRISH edi;
///   • KeyPrefix — muhitdan: u ombor ICHIDAGI joylashuv sxemasi,
///     o'zgartirilsa eski fayllarga yo'l uzilardi (registrdagi sabab).
///     ★ U ulanish nuqtasi EMAS, shuning uchun "paneldan boshqarilsin"
///     talabiga kirmaydi;
///   • TimeoutSeconds — registrda umuman yo'q: u `HttpClient` ga ishga
///     tushishda beriladi va ish jarayonida o'zgartirib bo'lmaydi.
/// </summary>
public sealed class RuntimeStorageOptions(IRuntimeSettings runtime, IOptions<StorageOptions> seed)
    : RuntimeOptions<StorageOptions>(runtime, seed)
{
    protected override StorageOptions Compose(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new StorageOptions
        {
            // Kesimdagi qiymat ALLAQACHON ustunlik qoidasidan o'tgan
            // (baza -> muhit -> registrdagi standart), shuning uchun bu yerda
            // yana bir marta muhitga qarash NOTO'G'RI bo'lardi: paneldan
            // ataylab tozalangan qiymat muhitdagisiga qaytib ketardi.
            ServiceUrl = snapshot.Value(SettingsRegistry.Keys.StorageServiceUrl) ?? string.Empty,
            Bucket = snapshot.Value(SettingsRegistry.Keys.StorageBucket) ?? string.Empty,
            AccessKey = snapshot.Value(SettingsRegistry.Keys.StorageAccessKey) ?? string.Empty,
            SecretKey = snapshot.Value(SettingsRegistry.Keys.StorageSecretKey) ?? string.Empty,

            // Region registrda standart qiymatga ega ("auto"), ya'ni bo'sh
            // qaytmasligi kerak. `Seed` — oxirgi himoya: bo'sh region S3
            // imzo zanjirini buzardi va har so'rov 403 bilan qaytardi.
            Region = Fallback(snapshot.Value(SettingsRegistry.Keys.StorageRegion), Seed.Region),

            KeyPrefix = Seed.KeyPrefix,

            // ★ ZAXIRA `Seed` GA, `ServiceUrl` GA EMAS. Bo'sh `PublicUrl`
            //   MA'NOLI qiymat: "ko'rish havolasi ham `ServiceUrl` dan
            //   qurilsin" (bu qoida `R2RecordingStorage` ichida, bitta
            //   joyda turadi). Bu yerda uni takrorlash ikkinchi ta'rif
            //   yasardi va ular bir kun ajralib ketardi.
            PublicUrl = snapshot.Value(SettingsRegistry.Keys.StoragePublicUrl) ?? Seed.PublicUrl,

            TimeoutSeconds = Seed.TimeoutSeconds,
        };
    }

    private static string Fallback(string? value, string seed) =>
        string.IsNullOrWhiteSpace(value) ? seed : value;
}
