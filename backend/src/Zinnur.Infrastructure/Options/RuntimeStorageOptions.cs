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
///   • KeyPrefix — muhitdan: u ombor ICHIDAGI joylashuv sxemasi,
///     o'zgartirilsa eski fayllarga yo'l uzilardi (registrdagi sabab);
///   • PublicUrl — MUHITDAN (FAZA 5.3). Bu BRAUZERGA ketadigan manzil,
///     ya'ni <c>livekit.public_url</c> bilan AYNI turkum: u DNS va
///     sertifikat bilan birga o'zgaradi — DEPLOY qarori, biznes qarori
///     emas. Bundan tashqari paneldan boshqarilsa, panelga kirgan odam
///     dars yozuvlarining butun oqimini o'z serveriga burib yubora
///     olardi (<c>telegram.api_base_url</c> dagi AYNI mulohaza);
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
            PublicUrl = Seed.PublicUrl,
            TimeoutSeconds = Seed.TimeoutSeconds,
        };
    }

    private static string Fallback(string? value, string seed) =>
        string.IsNullOrWhiteSpace(value) ? seed : value;
}
