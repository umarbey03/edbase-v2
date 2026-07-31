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

    public string Bucket { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>R2 uchun <c>auto</c>; AWS S3 uchun haqiqiy region (<c>eu-central-1</c>).</summary>
    public string Region { get; set; } = "auto";

    /// <summary>Kalit prefiksi — bitta bucket'ni modullar bo'yicha ajratish uchun.</summary>
    public string KeyPrefix { get; set; } = "submissions";

    /// <summary>Yuklash uchun timeout (sekund). Sekin tarmoqda 10 MB ~30-60 s.</summary>
    public int TimeoutSeconds { get; set; } = 60;

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
    public bool HasValidServiceUrl =>
        string.IsNullOrWhiteSpace(ServiceUrl)
        || (Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
