namespace Zinnur.Infrastructure.Options;

/// <summary>
/// Obyekt ombori (Cloudflare R2 yoki S3 bilan mos xizmat) sozlamalari.
///
/// IXTIYORIY: bo'sh bo'lsa ilova ODATDAGIDEK ko'tariladi, lekin fayl yuklash
/// 503 qaytaradi (<see cref="Zinnur.Application.Assignments.Services.ISubmissionStorage"/>).
/// LOKAL DISKKA HECH QACHON YOZILMAYDI — eski tizim shunday qilgani uchun
/// fayllar bitta konteynerga bog'lanib qolgan va deploy'da yo'qolgan edi.
///
/// TO'LIQ yoki BO'SH: yarim to'ldirilgan to'plam INERT — <see cref="IsConfigured"/>
/// BARCHA maydonni talab qiladi, ya'ni fayl yuklash "sozlanmagan" holatdagidek
/// 503 qaytaradi.
///
/// ⚠️ BU QOIDA ENDI ISHGA TUSHISHDA EMAS, YOZISH PAYTIDA qo'riqlanadi
/// (`SettingCoupling`): qiymatlar bazadan keladi va ishga tushish paytida ular
/// hali o'qilgan ham bo'lmaydi. Batafsil sabab — `DependencyInjection.AddOptions`.
///
/// ★★ QIYMATLAR ISH JARAYONIDA O'ZGARADI. Bu sinf endi ikki manbadan
/// to'ldiriladi: <c>IOptions&lt;StorageOptions&gt;</c> — BOSHLANG'ICH
/// (muhit/appsettings), <c>IRuntimeOptions&lt;StorageOptions&gt;</c> — AMALDAGI
/// (baza ustun). Iste'molchi FAQAT ikkinchisini ishlatadi.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// S3 API manzili, masalan
    /// <c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c>.
    /// Bucket nomi YO'L (path-style) sifatida qo'shiladi.
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// BRAUZERGA beriladigan manzil — imzolangan ko'rish havolasi shundan
    /// quriladi (FAZA 5.3, dars yozuvi).
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// ★ NIMA UCHUN IKKINCHI MANZIL KERAK BO'LDI
    ///
    /// <see cref="ServiceUrl"/> — SERVER-SERVER manzil: uni API konteyneri
    /// (fayl yuklash/o'qish) va LiveKit Egress ishlatadi, dev'da u
    /// <c>http://minio:9000</c> — Docker tarmog'i ICHIDAGI DNS nomi.
    /// Brauzer bunday nomni umuman hal qila olmaydi.
    ///
    /// Dars yozuvi esa YAGONA joy bo'lib, unda havola BRAUZERGA beriladi
    /// (proxy emas, presigned — sabab <c>IRecordingStorage</c> izohida).
    /// SigV4 imzosi HOSTGA bog'langani uchun manzilni "keyin almashtirib
    /// qo'yish" mumkin emas: imzo o'sha zahoti buziladi va ombor 403
    /// qaytaradi.
    ///
    /// Bu AYNAN <c>LiveKit:Url</c> / <c>LiveKit:PublicUrl</c> juftligidagi
    /// mulohaza: bitta o'zgaruvchi ikki xil ish bajara olmaydi.
    ///
    /// Bo'sh qoldirilsa <see cref="ServiceUrl"/> ishlatiladi — bir xil
    /// manzil ikkala tomondan ham ko'rinadigan sozlamalarda (masalan
    /// prod'dagi R2) qo'shimcha qiymat yozish shart emas.
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    public string PublicUrl { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>R2 uchun <c>auto</c>; AWS S3 uchun haqiqiy region (<c>eu-central-1</c>).</summary>
    public string Region { get; set; } = "auto";

    /// <summary>Kalit prefiksi — bitta bucket'ni modullar bo'yicha ajratish uchun.</summary>
    public string KeyPrefix { get; set; } = "submissions";

    /// <summary>
    /// KICHIK amallar uchun timeout (sekund): vazifa javobi (≤10 MB),
    /// <c>HEAD</c>, <c>DELETE</c> va katta faylning SARLAVHASINI olish.
    ///
    /// ⚠️ BU QIYMAT KATTA VIDEO YUKLASHGA TEGISHLI EMAS — buning uchun
    /// <see cref="LargeUploadTimeoutSeconds"/> bor. Sabab u yerda.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 KATTA MEDIA (DARS VIDEOSI) YUKLASH UCHUN TIMEOUT — ALOHIDA
    /// ══════════════════════════════════════════════════════════════════
    ///
    /// NIMA UCHUN QO'SHILDI (2026-08-24). Ombor klientining timeout'i
    /// YAGONA edi va <see cref="TimeoutSeconds"/> (60 s) dan olinardi.
    /// Yuklashda esa u BUTUN so'rovni, ya'ni TANANI UZATISHNI ham
    /// qamrab oladi.
    ///
    /// ARIFMETIKA — nega bu ishlamasligi shart edi:
    ///
    ///   ruxsat etilgan hajm (`LessonAssetsController.MaxUploadBytes`) 2 GB
    ///   60 soniyada 2 GB  ->  ~273 Mbit/s DOIMIY tezlik kerak
    ///   60 soniyada 200 MB ->  ~27 Mbit/s
    ///
    /// Ya'ni nginx 2049 MB ni o'tkazardi, kontroller uni qabul qilardi,
    /// ombor esa yuklashni o'rtasida uzardi. Ustoz ko'radigan xabar:
    /// "Fayl yuklash juda uzoq davom etdi" — 40 daqiqa kutgandan keyin.
    ///
    /// ★ 1800 s (30 daqiqa) — 2 GB uchun ~9 Mbit/s. Bu O'zbekistondagi
    ///   odatiy yuklash tezligidan past, ya'ni chegara HAQIQIY sekin
    ///   kanalda ham to'siq bo'lmaydi, lekin "abadiy osilgan" so'rovni
    ///   baribir uzadi.
    ///
    /// ⚠️ NEGA UMUMAN CHEGARA BOR: chegarasiz so'rov ombor javob
    /// bermay qolganda mangu osilib turardi va thread pool asta-sekin
    /// tugab borardi (bu — klient timeout'ining ASL sababi, u o'z
    /// kuchida qoladi, faqat endi amalga qarab tanlanadi).
    /// </summary>
    public int LargeUploadTimeoutSeconds { get; set; } = 1800;

    /// <summary>Hamma majburiy maydon to'ldirilganmi.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl)
        && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);

    // ⚠️ `IsPartiallyConfigured` BU YERDAN OLIB TASHLANDI.
    //
    // ★ NIMA UCHUN: qoida endi `SettingCoupling` da — u kalitlar RO'YXATI
    //   ustida ishlaydi va yozish yo'lida qo'llanadi. Bu yerda ikkinchi
    //   nusxa qolsa, `Storage:*` ga yangi kalit qo'shilgan kuni ikkalasi
    //   bir-biridan chetga chiqardi va farq faqat "nega bu holat
    //   to'silmadi?" degan savol bilan bilinardi.

    /// <summary>Manzil absolyut va <c>http(s)</c> bo'lishi kerak.</summary>
    public bool HasValidServiceUrl => IsAbsoluteHttpOrEmpty(ServiceUrl);

    /// <summary>
    /// Brauzer uchun manzil ham absolyut <c>http(s)</c> bo'lishi kerak
    /// (bo'sh — ruxsat: u holda <see cref="ServiceUrl"/> ishlatiladi).
    /// </summary>
    public bool HasValidPublicUrl => IsAbsoluteHttpOrEmpty(PublicUrl);

    /// <summary>
    /// Imzolangan ko'rish havolasi quriladigan manzil:
    /// <see cref="PublicUrl"/>, u bo'sh bo'lsa <see cref="ServiceUrl"/>.
    ///
    /// <c>LiveKitOptions.EffectivePublicUrl</c> bilan AYNI naqsh — qoida
    /// bitta joyda tursin, iste'molchi "qaysi biri?" deb o'ylamasin.
    /// </summary>
    public string EffectivePublicUrl =>
        string.IsNullOrWhiteSpace(PublicUrl) ? ServiceUrl : PublicUrl;

    private static bool IsAbsoluteHttpOrEmpty(string value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
