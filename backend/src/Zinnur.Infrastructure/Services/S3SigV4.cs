using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ========================================================================
/// AWS SIGNATURE VERSION 4 (S3) — SARLAVHA BILAN IMZOLASH: YAGONA JOY
/// ========================================================================
///
/// ★ NIMA UCHUN AJRATILDI: imzo algoritmi `R2SubmissionStorage` ichida
/// yozilgan edi va yangi ombor xizmati (dars mediasi) qo'shilganda uni
/// NUSXALASH kerak bo'lardi. Nusxalangan imzo eng yomon turdagi texnik
/// qarz: nosozlik faqat `403 SignatureDoesNotMatch` ko'rinishida, SABABSIZ
/// chiqadi — logda ham, javobda ham nima farq qilganini ko'rsatadigan
/// hech nima bo'lmaydi. Endi imzo BITTA joyda va u yerda tuzatilgan har
/// bir nozik holat (masalan `Authority` bilan bog'liq port bugi)
/// AVTOMATIK ravishda hamma iste'molchiga tegishli.
///
/// ★ TARTIB QAT'IY: kanonik so'rov -> imzolanadigan satr -> imzo kaliti ->
/// imzo. Sarlavhalar ALFABIT tartibda va kichik harfda bo'lishi shart.
///
/// ⚠️ QUERY-STRING (presigned) IMZOSI BU YERDA YO'Q: u boshqa variant
/// (`X-Amz-*` query parametrlari, `UNSIGNED-PAYLOAD`) va hozircha faqat
/// `R2RecordingStorage` da ishlatiladi. Uni ham shu yerga ko'chirish
/// mumkin, lekin bu alohida ish (hisobotda qayd etilgan).
/// </summary>
internal static class S3SigV4
{
    /// <summary>Bo'sh tananing SHA-256 xeshi — tanasi yo'q so'rovlar (GET/DELETE) uchun.</summary>
    internal const string EmptyPayloadHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>
    /// So'rovga `x-amz-*` va `Authorization` sarlavhalarini qo'yadi.
    /// </summary>
    /// <param name="request">
    /// Imzolanadigan so'rov. `RequestUri` TO'LIQ va OXIRGI holatida bo'lishi
    /// shart — imzo yo'lga bog'langan.
    /// </param>
    /// <param name="payloadHash">
    /// Tananing SHA-256 xeshi (hex, kichik harf) yoki
    /// <see cref="EmptyPayloadHash"/>.
    /// </param>
    /// <param name="contentType">
    /// Tana bo'lsa uning turi — imzoga KIRADI. Tanasi yo'q so'rovda
    /// <c>null</c>: sarlavha yuborilmagani uchun imzoga ham kirmasligi
    /// SHART (kanonik sarlavhalar ro'yxati AYNAN mos kelishi kerak).
    /// </param>
    /// <param name="extraSignedHeaders">
    /// Qo'shimcha imzolanadigan sarlavhalar (masalan <c>range</c>).
    /// ⚠️ Nomlar KICHIK harfda beriladi va so'rovga ham AYNI qiymat bilan
    /// qo'yilishi shart.
    /// </param>
    /// <param name="settings">
    /// Amal boshida BIR MARTA olingan sozlama kesimi. Bu yerda
    /// `options.Current` QAYTA CHAQIRILMAYDI: imzo kaliti va `Credential`
    /// sarlavhasi AYNI qiymatlardan chiqishi shart.
    /// </param>
    internal static void Sign(
        HttpRequestMessage request,
        string payloadHash,
        string? contentType,
        DateTime utcNow,
        StorageOptions settings,
        IReadOnlyList<KeyValuePair<string, string>>? extraSignedHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        var amzDate = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        // ★ `Authority`, `Host` EMAS — bu yerda BUG bor edi.
        //
        // Imzoga kiradigan `host` qiymati HTTP klient YUBORADIGAN `Host`
        // sarlavhasi bilan BAYT-BAYT bir xil bo'lishi shart. Klient
        // standart bo'lmagan portni sarlavhaga QO'SHADI (`minio:9000`),
        // `Uri.Host` esa portni TUSHIRIB QOLDIRADI. R2/S3 da bu ko'rinmasdi
        // (443 — standart port), MinIO'ga ulangan zahoti esa HAR SO'ROV
        // `403 SignatureDoesNotMatch` bilan qaytdi.
        var host = request.RequestUri!.Authority;

        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);

        // Kanonik sarlavhalar ALFABIT tartibda bo'lishi SHART. Ro'yxat
        // saralanadi — chaqiruvchi tartibni o'ylab o'tirmasin va yangi
        // sarlavha qo'shilganda imzo jimgina buzilmasin.
        var headers = new List<KeyValuePair<string, string>>(5)
        {
            new("host", host),
            new("x-amz-content-sha256", payloadHash),
            new("x-amz-date", amzDate),
        };

        if (contentType is not null)
            headers.Add(new KeyValuePair<string, string>("content-type", contentType));

        if (extraSignedHeaders is not null)
            headers.AddRange(extraSignedHeaders);

        headers.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        var canonicalHeaders = new StringBuilder();
        var signedHeaders = new StringBuilder();

        foreach (var header in headers)
        {
            canonicalHeaders.Append(header.Key).Append(':').Append(header.Value).Append('\n');

            if (signedHeaders.Length > 0) signedHeaders.Append(';');

            signedHeaders.Append(header.Key);
        }

        var canonicalRequest = string.Join('\n',
            request.Method.Method,
            request.RequestUri.AbsolutePath,
            string.Empty,                       // query yo'q
            canonicalHeaders.ToString(),
            signedHeaders.ToString(),
            payloadHash);

        var scope = string.Create(
            CultureInfo.InvariantCulture, $"{dateStamp}/{settings.Region}/s3/aws4_request");

        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signingKey = SigningKey(dateStamp, settings);
        var signature = Hex(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

        request.Headers.TryAddWithoutValidation(
            "Authorization",
            string.Create(
                CultureInfo.InvariantCulture,
                $"AWS4-HMAC-SHA256 Credential={settings.AccessKey}/{scope}, "
                + $"SignedHeaders={signedHeaders}, Signature={signature}"));
    }

    /// <summary>Imzo kaliti: sana -> region -> xizmat -> `aws4_request` zanjiri.</summary>
    internal static byte[] SigningKey(string dateStamp, StorageOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var key = Encoding.UTF8.GetBytes("AWS4" + settings.SecretKey);

        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(dateStamp));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(settings.Region));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("s3"));

        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("aws4_request"));
    }

    internal static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    /// <summary>
    /// Kalitni URL uchun kodlaydi, <c>/</c> ni SAQLAB (u yo'l ajratgichi).
    /// Kalitlar xavfsiz belgilardan iborat, lekin kodlash prefiks
    /// konfiguratsiyadan kelgani uchun MAJBURIY.
    /// </summary>
    internal static string EncodeKey(string key) =>
        string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    /// <summary>
    /// Obyekt manzili (path-style — R2 ham, MinIO ham tushunadi) ICHKI
    /// manzildan: so'rovni SERVER yuboradi.
    /// </summary>
    internal static Uri BuildUri(string key, StorageOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return BuildUri(settings.ServiceUrl, settings.Bucket, key);
    }

    /// <summary>
    /// Obyekt manzili, asos manzil ANIQ berilgan holda.
    ///
    /// ⚠️ ALOHIDA OVERLOAD KERAK: imzolangan ko'rish havolasi
    /// <c>Storage:PublicUrl</c> dan quriladi (u BRAUZERGA ketadi), qolgan
    /// hamma amal esa <c>Storage:ServiceUrl</c> dan. Dev'da ular FARQ
    /// QILADI (`minio:9000` va `localhost:9010`), SigV4 imzosi esa HOSTGA
    /// bog'langan — ya'ni manzilni keyin almashtirib bo'lmaydi.
    /// </summary>
    internal static Uri BuildUri(string baseUrl, string bucket, string key) =>
        new($"{baseUrl.TrimEnd('/')}/{bucket}/{EncodeKey(key)}");
}
