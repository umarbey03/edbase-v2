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
///   • TimeoutSeconds va LargeUploadTimeoutSeconds — registrda umuman
///     yo'q, ya'ni FAQAT muhitdan (`Seed`). Ular amalning TURIGA qarab
///     tanlanadi (`StorageTimeout`), ombor ulanishining bir qismi emas —
///     shuning uchun "paneldan boshqarilsin" talabiga kirmaydi. Ikkalasi
///     ham `Compose` da ko'chirilishi SHART: sabab pastda, o'sha qator
///     ustida.
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

            // ══════════════════════════════════════════════════════════
            // 🔴 2026-09-05: BU QATOR TUSHIB QOLGAN EDI
            //
            // `Compose` kesimdan YANGI obyekt yasaydi, ya'ni bu yerda
            // ko'chirilmagan har qanday maydon standart qiymatiga
            // qaytadi. `LargeUploadTimeoutSeconds` uchun standart — 1800 s,
            // lekin u KONSTRUKTORDA emas, `Seed` da sozlanadigan qiymat:
            // muhitda boshqa raqam berilgan bo'lsa, baza kesimi
            // yuklangan zahoti u JIMGINA yo'qolardi.
            //
            // ⚠️ ANIQLIK KIRITISH — SPEC-RECORDING-V2 (5.9-3) da "har safar
            //    60 soniyalik standartga tushib qolardi" deyilgan. BU
            //    ANIQ EMAS: tushib qolgan maydon `StorageOptions` dagi
            //    O'Z standartini (1800 s) oladi, `TimeoutSeconds` ning
            //    60 sini emas. Haqiqiy nuqson boshqacha va TORROQ:
            //    muhitda ataylab boshqa raqam berilgan bo'lsa
            //    (`Storage__LargeUploadTimeoutSeconds`), u baza kesimi
            //    yuklangan zahoti JIMGINA yo'qolardi — ya'ni operator
            //    chegarani oshira olmasdi va sabab hech qayerda
            //    ko'rinmasdi.
            //
            //    Nima uchun bu baribir tuzatiladi: tungi yig'uvchi
            //    o'lchangan 1.75 GB faylni yuklaydi va aynan shu chegarani
            //    sozlash kerak bo'ladigan yagona yo'l — muhit qiymati.
            //
            // ⚠️ QOIDA: `StorageOptions` ga yangi maydon qo'shilsa, u SHU
            //    YERGA ham yozilishi shart. Aks holda u faqat SOVUQ
            //    STARTDA ishlaydi va birinchi kesim yuklanishi bilan
            //    yo'qoladi — takrorlash qiyin, sababi ko'rinmaydigan
            //    nosozlik.
            // ══════════════════════════════════════════════════════════
            LargeUploadTimeoutSeconds = Seed.LargeUploadTimeoutSeconds,
        };
    }

    private static string Fallback(string? value, string seed) =>
        string.IsNullOrWhiteSpace(value) ? seed : value;
}
