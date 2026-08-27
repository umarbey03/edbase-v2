using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ISubmissionStorage"/> ning Cloudflare R2 / S3 amalga oshirilishi.
///
/// NIMA UCHUN AWS SDK EMAS: bizga IKKI amal kerak — <c>PUT object</c> va
/// <c>GET object</c>. SDK ~5 MB bog'liqlik, o'zining qayta urinish/credential
/// zanjiri va yangilanish jadvali bilan keladi. SigV4 imzosi esa 60 qatorlik
/// aniq algoritm (<c>LiveKitTokenService</c> ham JWT'ni shunday qo'lda
/// imzolaydi — bir xil uslub).
///
/// AYNI KOD YO'LI DEV'DA HAM, PROD'DA HAM: dev'da MinIO, prod'da Cloudflare
/// R2 — ikkalasi ham path-style URL va SigV4 tushunadi, ya'ni faqat
/// `Storage:*` qiymatlari o'zgaradi, KOD emas. Shu sababli "prod'da
/// ishlamadi" turkumidagi nosozliklar dev'da ham takrorlanadi.
///
/// TO'LIQ URL QAYTARILMAYDI — faqat OBYEKT KALITI. Presigned URL muddatli
/// (bir soat) va uni bazaga yozish "linkim ishlamayapti" muammosini keltirardi
/// (eski tizimning kamchiligi: bazada `/media/...` URL saqlanardi).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★★ QIYMATLAR HAR CHAQIRUVDA OLINADI — <c>IOptions&lt;T&gt;</c> EMAS.
///
/// Ilgari <c>_options</c> konstruktorda bir marta olinardi va bu SINGLETON
/// xizmatga QOTIB QOLARDI: paneldan almashtirilgan R2 kaliti tizimga umuman
/// yetib bormasdi. Endi manba — <see cref="IRuntimeOptions{TOptions}"/>.
///
/// ⚠️ QAT'IY QOIDA: bitta amal (yuklash yoki o'qish) davomida qiymatlar
/// BIR MARTA olinadi va pastga UZATILADI. Har yordamchi metod o'zi
/// <c>Current</c> ni chaqirsa, amal o'rtasida kesh yangilanib qolishi
/// mumkin edi — natijada SigV4 imzosi BIR kalit bilan hisoblanib,
/// `Authorization` sarlavhasi BOSHQA kalitni ko'rsatardi va ombor har
/// so'rovni `403 SignatureDoesNotMatch` bilan qaytarardi. Sabab esa hech
/// qayerda ko'rinmasdi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class R2SubmissionStorage(
    IHttpClientFactory httpClientFactory,
    IRuntimeOptions<StorageOptions> options,
    ILogger<R2SubmissionStorage> logger,
    TimeProvider clock) : ISubmissionStorage
{
    /// <summary>Nomlangan HTTP klient (timeout DI'da sozlanadi).</summary>
    public const string HttpClientName = "zinnur-storage";

    /// <inheritdoc />
    public bool IsConfigured => options.Current.IsConfigured;

    /// <inheritdoc />
    public async Task<string> SaveAsync(SubmissionUpload upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            // Bu holatga normal oqimda tushilmaydi — `AssignmentService`
            // `IsConfigured` ni OLDIN tekshiradi. Lekin port to'g'ridan-to'g'ri
            // chaqirilsa ham lokal diskka yozib qo'ymaslik uchun shu yerda ham
            // qat'iy to'siq bor.
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Fayl saqlanmadi.");
        }

        var key = BuildKey(upload, settings);
        var now = clock.GetUtcNow().UtcDateTime;

        using var request = new HttpRequestMessage(
            HttpMethod.Put, S3SigV4.BuildUri(key, settings));

        using var content = new ByteArrayContent(upload.Content.ToArray());

        content.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        request.Content = content;

        S3SigV4.Sign(
            request,
            S3SigV4.Hex(SHA256.HashData(upload.Content.Span)),
            upload.ContentType,
            now,
            settings);

        var client = httpClientFactory.CreateClient(HttpClientName);

        // Chegara AMALGA tegishli, klientga emas (sabab: `StorageTimeout`).
        // Bu yerda fayl ≤10 MB, ya'ni KICHIK amal chegarasi to'g'ri keladi.
        using var timeout = StorageTimeout.Start(settings.TimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<StoredFile?> OpenReadAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Faylni ochib bo'lmadi.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, S3SigV4.BuildUri(objectKey, settings));

        // GET da tana yo'q => bo'sh yuk (payload) xeshi. `UNSIGNED-PAYLOAD`
        // ham bo'lardi, lekin bo'sh xesh QAT'IYROQ va R2/MinIO ikkalasi ham
        // tushunadi. Content-Type sarlavhasi YO'Q — shuning uchun imzoga ham
        // kirmaydi (kanonik sarlavhalar ro'yxati AYNAN mos kelishi shart).
        S3SigV4.Sign(
            request,
            S3SigV4.EmptyPayloadHash,
            contentType: null,
            clock.GetUtcNow().UtcDateTime,
            settings);

        var client = httpClientFactory.CreateClient(HttpClientName);

        // Chegara faqat SARLAVHA olishga tegishli: `using` tugagach taymer
        // to'xtaydi va tana o'qishi chegarasiz davom etadi (sabab va
        // `Dispose` semantikasi: `StorageTimeout`).
        using var timeout = StorageTimeout.Start(settings.TimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            // ★ ResponseHeadersRead: javob TANASI KUTILMAYDI. Busiz
            // `SendAsync` butun faylni xotiraga yig'ib bo'lgach qaytardi va
            // 10 MB ovoz API xotirasidan ikki marta o'tardi.
            response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            StorageLog.DownloadFailed(logger, ex, objectKey);

            throw new ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi. Iltimos, keyinroq urinib ko'ring.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            StorageLog.DownloadTimedOut(logger, ex, objectKey);

            throw new ServiceUnavailableException(
                "Faylni ochish juda uzoq davom etdi. Iltimos, qaytadan urinib ko'ring.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Bazada yozuv bor, omborda obyekt yo'q — ma'lumot va ombor
            // ajralib qolgan. Bu 503 EMAS (ombor sog'lom), shuning uchun
            // chaqiruvchi buni 404 ga aylantiradi.
            response.Dispose();

            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            StorageLog.DownloadRejected(logger, objectKey, (int)response.StatusCode, Trim(body));
            response.Dispose();

            throw new ServiceUnavailableException(
                "Faylni ochib bo'lmadi (ombor xatosi). Iltimos, keyinroq urinib ko'ring.");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Ombor aytgan turga ISHONMAYMIZ, agar u yo'q bo'lsa: yuklashda tur
        // MAZMUNDAN aniqlangan va bazada saqlangan — chaqiruvchi kerak bo'lsa
        // o'sha qiymatni ustun qo'yadi.
        var contentType = response.Content.Headers.ContentType?.MediaType;

        // `response` — OQIM EGASI: u yopilmasa ulanish hovuzga qaytmaydi.
        return new StoredFile(
            stream,
            string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType,
            response.Content.Headers.ContentLength,
            response);
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
    private string BuildKey(SubmissionUpload upload, StorageOptions settings)
    {
        var month = clock.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var prefix = string.IsNullOrWhiteSpace(settings.KeyPrefix)
            ? "submissions"
            : settings.KeyPrefix.Trim('/');

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}/{month}/{upload.StudentId}/{random}.{upload.Extension}");
    }

    // ================================================================= SigV4
    //
    // ⚠️ IMZO ALGORITMI BU YERDAN OLIB TASHLANDI -> `S3SigV4`.
    //
    // ★ NIMA UCHUN: uchinchi ombor xizmati (dars mediasi, `R2MediaStorage`)
    //   qo'shilganda algoritm UCHINCHI nusxaga bo'linardi. Nusxalangan imzo
    //   eng yomon turdagi texnik qarz: nosozlik faqat
    //   `403 SignatureDoesNotMatch` ko'rinishida, SABABSIZ chiqadi.
    //
    // ★ BU YERDA TOPILGAN VA TUZATILGAN BUG `S3SigV4.Sign` ICHIDA, izohi
    //   bilan birga SAQLANGAN: imzoga `Uri.Authority` (port BILAN) kiradi,
    //   `Uri.Host` EMAS. `Uri.Host` portni tushirib qoldiradi va MinIO
    //   (`minio:9000`) har so'rovni 403 bilan qaytarardi; R2/S3 da esa
    //   (443 — standart port) muammo umuman ko'rinmasdi.
    //
    // Endi tuzatish BITTA joyda va u avtomatik ravishda uchala iste'molchiga
    // (vazifa javobi, dars yozuvi, dars mediasi) tegishli.

    private static string Trim(string body) =>
        body.Length <= MaxLoggedBodyLength ? body : body[..MaxLoggedBodyLength];

    private const int MaxLoggedBodyLength = 500;

    /// <summary>Ombor turni aytmasa — "noma'lum ikkilik", brauzer uni RENDER QILMAYDI.</summary>
    private const string DefaultContentType = "application/octet-stream";
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

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Error,
        Message = "Faylni ombordan o'qishda ulanish xatosi. key={Key}")]
    internal static partial void DownloadFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Error,
        Message = "Faylni ombordan o'qish timeout. key={Key}")]
    internal static partial void DownloadTimedOut(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Error,
        Message = "Ombor faylni bermadi. key={Key} status={Status} javob={Body}")]
    internal static partial void DownloadRejected(ILogger logger, string key, int status, string body);
}
