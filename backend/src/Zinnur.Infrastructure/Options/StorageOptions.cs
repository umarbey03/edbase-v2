namespace Zinnur.Infrastructure.Options;

/// <summary>
/// Obyekt ombori (Cloudflare R2 yoki S3 bilan mos xizmat) sozlamalari.
///
/// IXTIYORIY: bo'sh bo'lsa ilova ODATDAGIDEK ko'tariladi, lekin fayl yuklash
/// 503 qaytaradi (<see cref="Zinnur.Application.Assignments.Services.ISubmissionStorage"/>).
/// LOKAL DISKKA HECH QACHON YOZILMAYDI — eski tizim shunday qilgani uchun
/// fayllar bitta konteynerga bog'lanib qolgan va deploy'da yo'qolgan edi.
///
/// TO'LIQ yoki BO'SH: yarim to'ldirilgan konfiguratsiya ilovani ishga
/// tushirishda YIQITADI (`ValidateOnStart`). Aks holda xato faqat birinchi
/// yuklashda, ya'ni haqiqiy o'quvchi javob topshirayotganda ko'rinardi.
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

    /// <summary>
    /// Yarim to'ldirilganmi (bittasi bor, boshqasi yo'q) — bu ALBATTA xato.
    /// </summary>
    public bool IsPartiallyConfigured
    {
        get
        {
            var filled = 0;

            if (!string.IsNullOrWhiteSpace(ServiceUrl)) filled++;
            if (!string.IsNullOrWhiteSpace(Bucket)) filled++;
            if (!string.IsNullOrWhiteSpace(AccessKey)) filled++;
            if (!string.IsNullOrWhiteSpace(SecretKey)) filled++;

            return filled is > 0 and < 4;
        }
    }

    /// <summary>Manzil absolyut va <c>http(s)</c> bo'lishi kerak.</summary>
    public bool HasValidServiceUrl =>
        string.IsNullOrWhiteSpace(ServiceUrl)
        || (Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
