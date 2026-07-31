namespace Zinnur.IntegrationTests.Infrastructure;

/// <summary>
/// HAQIQIY obyekt ombori (MinIO) bilan ishlaydigan fixture.
///
/// NIMA UCHUN SOXTA OMBOR (mock) EMAS: bu yerda tekshiriladigan narsa
/// aynan qatlamlar CHEGARASI — SigV4 imzosi, path-style URL, sarlavhalar
/// tartibi va oqim bilan o'qish. Soxta ombor bularning hammasini
/// "to'g'ri" deb qabul qilardi va yashil natija hech nimani isbotlamasdi.
/// Imzo xatosi esa prod'da faqat `403 SignatureDoesNotMatch` ko'rinishida,
/// sababsiz chiqadi — uni aynan shu yerda tutish kerak.
///
/// MinIO R2 bilan BIR XIL protokolni gapiradi, ya'ni bu testlar prod
/// yo'lini ham qoplaydi (kod bitta, faqat `Storage:*` qiymatlari boshqa).
///
/// Manzillar muhitdan olinadi — <see cref="ZinnurApiFactory"/> dagi
/// `TEST_POSTGRES`/`TEST_REDIS` bilan bir xil uslub:
///     lokal -> ishlab turgan `zinnur-v2` stack (localhost:9010)
///     CI    -> workflow ko'targan MinIO konteyneri
/// </summary>
public sealed class StorageBackedApiFactory : ZinnurApiFactory
{
    private static string ServiceUrl =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_URL")
        ?? "http://localhost:9010";

    private static string Bucket =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_BUCKET")
        ?? "zinnur-dev";

    private static string AccessKey =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_ACCESS_KEY")
        ?? "zinnur_dev_minio";

    private static string SecretKey =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_SECRET_KEY")
        ?? "zinnur_dev_minio_secret";

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Storage:ServiceUrl", ServiceUrl),
        new("Storage:Bucket", Bucket),
        new("Storage:AccessKey", AccessKey),
        new("Storage:SecretKey", SecretKey),

        // MinIO ning standart regioni. Region IMZOGA kiradi — noto'g'ri
        // bo'lsa ombor 403 beradi va sabab hech qayerda ko'rinmaydi.
        new("Storage:Region", "us-east-1"),

        // HAR TEST ISHGA TUSHISHIGA O'Z PREFIKSI: bitta bucket ko'p marta
        // ishlatiladi va eski yugurishlardan qolgan obyektlar yangisiga
        // aralashib ketmasin.
        new("Storage:KeyPrefix", KeyPrefix),

        // Lokal ombor tez javob beradi; uzoq timeout yiqilgan MinIO'da
        // butun to'plamni bir daqiqaga osib qo'yardi.
        new("Storage:TimeoutSeconds", "15"),
    ];

    /// <summary>
    /// Testdagi tasdiqlar kalit shaklini shu prefiks bilan tekshiradi.
    ///
    /// MAYDONDA hisoblanadi, xossada EMAS: <c>ExtraSettings()</c> bir necha
    /// marta chaqirilishi mumkin va har safar yangi Guid hosil bo'lsa,
    /// tasdiqlar ilova ishlatgan prefiksdan boshqasini kutardi.
    /// </summary>
    public string KeyPrefix { get; } = "itest/" + Guid.NewGuid().ToString("N")[..8];
}
