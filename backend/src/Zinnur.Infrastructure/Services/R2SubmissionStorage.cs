using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ISubmissionStorage"/> ning Cloudflare R2 / S3 amalga oshirilishi.
///
/// NIMA UCHUN AWS SDK EMAS: bizga BITTA amal kerak — <c>PUT object</c>. SDK
/// ~5 MB bog'liqlik, o'zining qayta urinish/credential zanjiri va yangilanish
/// jadvali bilan keladi. SigV4 imzosi esa 60 qatorlik aniq algoritm
/// (<c>LiveKitTokenService</c> ham JWT'ni shunday qo'lda imzolaydi — bir xil
/// uslub).
///
/// TO'LIQ URL QAYTARILMAYDI — faqat OBYEKT KALITI. Presigned URL muddatli
/// (bir soat) va uni bazaga yozish "linkim ishlamayapti" muammosini keltirardi
/// (eski tizimning kamchiligi: bazada `/media/...` URL saqlanardi).
/// </summary>
public sealed class R2SubmissionStorage(
    IHttpClientFactory httpClientFactory,
    IOptions<StorageOptions> options,
    ILogger<R2SubmissionStorage> logger,
    TimeProvider clock) : ISubmissionStorage
{
    /// <summary>Nomlangan HTTP klient (timeout DI'da sozlanadi).</summary>
    public const string HttpClientName = "zinnur-storage";

    private readonly StorageOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public bool IsConfigured => _options.IsConfigured;

    /// <inheritdoc />
    public async Task<string> SaveAsync(SubmissionUpload upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        if (!IsConfigured)
        {
            // Bu holatga normal oqimda tushilmaydi — `AssignmentService`
            // `IsConfigured` ni OLDIN tekshiradi. Lekin port to'g'ridan-to'g'ri
            // chaqirilsa ham lokal diskka yozib qo'ymaslik uchun shu yerda ham
            // qat'iy to'siq bor.
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Fayl saqlanmadi.");
        }

        var key = BuildKey(upload);
        var now = clock.GetUtcNow().UtcDateTime;

        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(key));
        using var content = new ByteArrayContent(upload.Content.ToArray());

        content.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        request.Content = content;

        Sign(request, upload.Content.Span, now);

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            StorageLog.UploadFailed(logger, ex, key);

            throw new ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi. Iltimos, keyinroq urinib ko'ring.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout (bekor qilish EMAS — uni chaqiruvchi hal qiladi).
            StorageLog.UploadTimedOut(logger, ex, key);

            throw new ServiceUnavailableException(
                "Fayl yuklash juda uzoq davom etdi. Iltimos, qaytadan urinib ko'ring.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return key;

            // Javob MATNINI logga yozamiz (S3 xatosi XML'da tushuntiriladi),
            // foydalanuvchiga esa ichki tafsilot CHIQMAYDI.
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            StorageLog.UploadRejected(logger, key, (int)response.StatusCode, Trim(body));

            throw new ServiceUnavailableException(
                "Faylni saqlab bo'lmadi (ombor xatosi). Iltimos, keyinroq urinib ko'ring.");
        }
    }

    /// <summary>
    /// Obyekt kaliti: <c>submissions/2026-07/42/9f8e...a1.jpg</c>.
    ///
    /// OYLIK papkalar — bitta "papkada" millionlab obyekt bo'lib ketmasin
    /// (ro'yxatlash va zaxiralash sekinlashadi). O'quvchi ID'si kalitda —
    /// muammo tekshirilganda kimning fayli ekani darhol ko'rinadi.
    /// TASODIFIY qism — kalitni taxmin qilib boshqaning faylini so'rash
    /// mumkin bo'lmasin.
    /// </summary>
    private string BuildKey(SubmissionUpload upload)
    {
        var month = clock.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var prefix = string.IsNullOrWhiteSpace(_options.KeyPrefix) ? "submissions" : _options.KeyPrefix.Trim('/');

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}/{month}/{upload.StudentId}/{random}.{upload.Extension}");
    }

    private Uri BuildUri(string key) =>
        new($"{_options.ServiceUrl.TrimEnd('/')}/{_options.Bucket}/{EncodeKey(key)}");

    // ================================================================= SigV4

    /// <summary>
    /// AWS Signature Version 4 (S3 uchun).
    ///
    /// Tartib QAT'IY: kanonik so'rov -> imzolanadigan satr -> imzo kaliti ->
    /// imzo. Sarlavhalar ALFABIT tartibda va kichik harfda bo'lishi shart,
    /// aks holda xizmat 403 (`SignatureDoesNotMatch`) qaytaradi.
    /// </summary>
    private void Sign(HttpRequestMessage request, ReadOnlySpan<byte> payload, DateTime utcNow)
    {
        var amzDate = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var payloadHash = Hex(SHA256.HashData(payload));
        var host = request.RequestUri!.Host;
        var contentType = request.Content!.Headers.ContentType!.ToString();

        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);

        // Kanonik sarlavhalar — alfabit tartibda (content-type, host, x-amz-*).
        var canonicalHeaders = string.Create(
            CultureInfo.InvariantCulture,
            $"content-type:{contentType}\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n");

        const string SignedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";

        var canonicalRequest = string.Join('\n',
            "PUT",
            request.RequestUri.AbsolutePath,
            string.Empty,                       // query yo'q
            canonicalHeaders,
            SignedHeaders,
            payloadHash);

        var scope = string.Create(
            CultureInfo.InvariantCulture, $"{dateStamp}/{_options.Region}/s3/aws4_request");

        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signingKey = SigningKey(dateStamp);
        var signature = Hex(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

        request.Headers.TryAddWithoutValidation(
            "Authorization",
            string.Create(
                CultureInfo.InvariantCulture,
                $"AWS4-HMAC-SHA256 Credential={_options.AccessKey}/{scope}, "
                + $"SignedHeaders={SignedHeaders}, Signature={signature}"));
    }

    /// <summary>Imzo kaliti: sana -> region -> xizmat -> `aws4_request` zanjiri.</summary>
    private byte[] SigningKey(string dateStamp)
    {
        var key = Encoding.UTF8.GetBytes("AWS4" + _options.SecretKey);

        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(dateStamp));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(_options.Region));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("s3"));

        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("aws4_request"));
    }

    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    /// <summary>
    /// Kalitni URL uchun kodlaydi, <c>/</c> ni SAQLAB (u yo'l ajratgichi).
    /// Bizning kalitlarimiz xavfsiz belgilardan iborat, lekin kodlash
    /// prefiks konfiguratsiyadan kelgani uchun MAJBURIY.
    /// </summary>
    private static string EncodeKey(string key) =>
        string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    private static string Trim(string body) =>
        body.Length <= MaxLoggedBodyLength ? body : body[..MaxLoggedBodyLength];

    private const int MaxLoggedBodyLength = 500;
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848 — oddiy <c>LogError("...")</c>
/// har chaqiruvda massiv ajratadi va bokslash qiladi).
/// </summary>
internal static partial class StorageLog
{
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Fayl omboriga ulanish xatosi. key={Key}")]
    internal static partial void UploadFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Fayl yuklash timeout. key={Key}")]
    internal static partial void UploadTimedOut(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Error,
        Message = "Ombor faylni rad etdi. key={Key} status={Status} javob={Body}")]
    internal static partial void UploadRejected(ILogger logger, string key, int status, string body);
}
