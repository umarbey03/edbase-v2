using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Media;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IMediaStorage"/> ning Cloudflare R2 / S3 amalga oshirilishi
/// (dev'da MinIO — AYNI kod yo'li, faqat `Storage:*` qiymatlari boshqa).
///
/// ★ IMZO NUSXALANMAGAN: SigV4 <see cref="S3SigV4"/> da — YAGONA joyda.
///
/// ★★ FARQI `R2SubmissionStorage` DAN — UCHTA, hammasi katta fayl uchun:
///
///  1) YOZISH OQIM BILAN. `StreamContent` ishlatiladi, ya'ni 1 GB video
///     API xotirasiga TUSHMAYDI. (Vazifa javobi yo'li faylni
///     `ByteArrayContent` bilan yuboradi — u yerda chegara 10 MB va bu
///     to'g'ri qaror.)
///
///  2) `Range` OMBORGA UZATILADI. Video oxiriga o'tish (seek) uchun brauzer
///     `Range: bytes=…` yuboradi; biz uni AYNAN o'sha ko'rinishda S3 ga
///     beramiz va u `206` bilan faqat so'ralgan bo'lakni qaytaradi. Ya'ni
///     izlash TARMOQ OQIMIDA emas, OMBORDA bo'ladi — bu ~1 GB fayl uchun
///     yagona ishlaydigan yo'l.
///     🔴 `range` sarlavhasi IMZOGA ham kiritiladi (`extraSignedHeaders`):
///     imzolangan sarlavhalar ro'yxati yuborilganlar bilan mos kelmasa
///     ombor `403 SignatureDoesNotMatch` beradi.
///
///  3) O'CHIRISH bor va IDEMPOTENT: `404` ni muvaffaqiyat deb qabul qiladi.
///     O'chirish BAZADAN KEYIN chaqiriladi, ya'ni takror urinish normal
///     holat (sabab: `LessonAssetService.DeleteAsync`).
///
/// ⚠️ QIYMATLAR HAR AMALDA BIR MARTA olinadi (`options.Current` bir marta)
/// — `R2SubmissionStorage` dagi AYNI qoida: amal o'rtasida kesh yangilansa
/// imzo bir kalit bilan, `Authorization` sarlavhasi boshqasi bilan chiqib,
/// har so'rov `403` bo'lardi va sabab hech qayerda ko'rinmasdi.
/// </summary>
public sealed class R2MediaStorage(
    IHttpClientFactory httpClientFactory,
    IRuntimeOptions<StorageOptions> options,
    ILogger<R2MediaStorage> logger,
    TimeProvider clock) : IMediaStorage
{
    /// <summary>
    /// Nomlangan HTTP klient — `R2SubmissionStorage` bilan AYNI hovuz.
    ///
    /// ★ NIMA UCHUN ALOHIDA KLIENT EMAS: ombor bitta, ulanish hovuzi ham
    /// bitta bo'lishi kerak. Ikki klient bo'lsa TCP ulanishlari ikki
    /// baravar ko'p bo'lardi va timeout ikki joyda sozlanardi.
    ///
    /// ⚠️ TIMEOUT: DI'da `Storage:TimeoutSeconds` bo'yicha o'rnatiladi.
    /// Katta video uchun bu qiymat yetarli bo'lishi kerak — hisobotdagi
    /// eslatmaga qarang.
    /// </summary>
    public const string HttpClientName = R2SubmissionStorage.HttpClientName;

    /// <inheritdoc />
    public bool IsConfigured => options.Current.IsConfigured;

    /// <inheritdoc />
    public async Task<string> SaveAsync(MediaUpload upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Fayl saqlanmadi.");
        }

        if (!upload.Content.CanSeek)
        {
            // Bu DASTURCHI xatosi, foydalanuvchi xatosi emas: SigV4 tananing
            // xeshini talab qiladi, ya'ni oqim ikki marta o'qilishi kerak.
            throw new InvalidOperationException(
                "Yuklash oqimi izlanadigan (seekable) bo'lishi shart — SigV4 xeshi uchun.");
        }

        var key = BuildKey(upload, settings);

        // ★ XESH OQIMDAN, XOTIRAGA OLMASDAN hisoblanadi: `ComputeHashAsync`
        //   oqimni bo'lak-bo'lak o'qiydi. 1 GB fayl uchun bu vaqtinchalik
        //   DISKDAN bir marta o'qish (ASP.NET katta form-faylni diskka
        //   buferlaydi) — xotira sarfi doimiy.
        var payloadHash = S3SigV4.Hex(
            await SHA256.HashDataAsync(upload.Content, ct).ConfigureAwait(false));

        upload.Content.Position = 0;

        using var request = new HttpRequestMessage(HttpMethod.Put, S3SigV4.BuildUri(key, settings));
        using var content = new StreamContent(upload.Content);

        content.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);

        // `Content-Length` ANIQ berilishi kerak: aks holda `StreamContent`
        // chunked transfer ishlatadi va S3 uni QABUL QILMAYDI.
        content.Headers.ContentLength = upload.Length;

        request.Content = content;

        S3SigV4.Sign(
            request, payloadHash, upload.ContentType, clock.GetUtcNow().UtcDateTime, settings);

        var client = httpClientFactory.CreateClient(HttpClientName);

        // ═══════════════════════════════════════════════════════════════
        // 🔴 KATTA FAYL CHEGARASI — AYNAN SHU YERDA NOSOZLIK BOR EDI
        //
        // Chegara ilgari klientda, `Storage:TimeoutSeconds` (60 s) dan
        // olinardi va u TANA UZATISHNI ham qamrab olardi. Ya'ni 2 GB
        // dars videosi 60 soniyaga sig'ishi kerak bo'lardi — amalda
        // ~100-200 MB dan katta har qanday video yuklanmasdi.
        // Arifmetika: `StorageOptions.LargeUploadTimeoutSeconds`.
        // ═══════════════════════════════════════════════════════════════
        using var timeout = StorageTimeout.Start(settings.LargeUploadTimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            MediaStorageLog.UploadFailed(logger, ex, key);

            throw new ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi. Iltimos, keyinroq urinib ko'ring.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            MediaStorageLog.UploadTimedOut(logger, ex, key);

            throw new ServiceUnavailableException(
                "Fayl yuklash juda uzoq davom etdi. Juda sekin kanalda "
                + "`Storage:LargeUploadTimeoutSeconds` qiymatini oshirish kerak bo'lishi "
                + "mumkin (standarti 1800 s — 2 GB uchun ~9 Mbit/s).");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return key;

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            MediaStorageLog.UploadRejected(logger, key, (int)response.StatusCode, Trim(body));

            throw new ServiceUnavailableException(
                "Faylni saqlab bo'lmadi (ombor xatosi). Iltimos, keyinroq urinib ko'ring.");
        }
    }

    /// <inheritdoc />
    public async Task<StoredMedia?> OpenReadAsync(
        string objectKey, MediaByteRange? range = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Faylni ochib bo'lmadi.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, S3SigV4.BuildUri(objectKey, settings));

        // `Range` sarlavhasi: qiymat IMZOGA ham, so'rovga ham AYNAN bir xil
        // ko'rinishda tushishi shart.
        List<KeyValuePair<string, string>>? extraHeaders = null;

        if (range is not null)
        {
            var value = string.Create(
                CultureInfo.InvariantCulture, $"bytes={range.From}-{range.To}");

            request.Headers.TryAddWithoutValidation("Range", value);

            extraHeaders = [new KeyValuePair<string, string>("range", value)];
        }

        // GET da tana yo'q => bo'sh yuk xeshi.
        S3SigV4.Sign(
            request,
            S3SigV4.EmptyPayloadHash,
            contentType: null,
            clock.GetUtcNow().UtcDateTime,
            settings,
            extraHeaders);

        var client = httpClientFactory.CreateClient(HttpClientName);

        // ★★ CHEGARA FAQAT SARLAVHAGA. `using` tugagach taymer to'xtaydi,
        //    video oqimi esa chegarasiz davom etadi — 40 daqiqalik darsni
        //    "ombor timeout'i" o'rtasidan uzib qo'ymasin. Nega `Dispose`
        //    oqimni buzmasligi: `StorageTimeout` izohi.
        using var timeout = StorageTimeout.Start(settings.TimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            // ★ ResponseHeadersRead: javob TANASI KUTILMAYDI — baytlar
            //   klientga bevosita uzatiladi. Busiz butun video avval API
            //   xotirasiga tushardi.
            response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            MediaStorageLog.DownloadFailed(logger, ex, objectKey);

            throw new ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi. Iltimos, keyinroq urinib ko'ring.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            MediaStorageLog.DownloadTimedOut(logger, ex, objectKey);

            throw new ServiceUnavailableException(
                "Faylni ochish juda uzoq davom etdi. Iltimos, qaytadan urinib ko'ring.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Bazada yozuv bor, omborda obyekt yo'q — ma'lumot va ombor
            // ajralib qolgan. Bu 503 EMAS (ombor sog'lom).
            response.Dispose();

            return null;
        }

        // 🔴 416 — so'ralgan oraliq obyekt hajmidan tashqarida. Bu HOLAT
        // normal oqimda yuz bermaydi (oraliq bazadagi hajm bo'yicha
        // normallashtirilgan), lekin baza va ombor ajralib qolganda
        // (obyekt qayta yozilgan) mumkin. `null` — chaqiruvchi 404 qiladi.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            MediaStorageLog.RangeNotSatisfiable(logger, objectKey);
            response.Dispose();

            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            MediaStorageLog.DownloadRejected(logger, objectKey, (int)response.StatusCode, Trim(body));
            response.Dispose();

            throw new ServiceUnavailableException(
                "Faylni ochib bo'lmadi (ombor xatosi). Iltimos, keyinroq urinib ko'ring.");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var isPartial = response.StatusCode == HttpStatusCode.PartialContent;
        var contentType = response.Content.Headers.ContentType?.MediaType;

        return new StoredMedia(
            stream,
            string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType,
            response.Content.Headers.ContentLength,
            TotalLength(response),
            isPartial,

            // `response` — OQIM EGASI: u yopilmasa ulanish hovuzga qaytmaydi.
            response);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Faylni o'chirib bo'lmadi.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete, S3SigV4.BuildUri(objectKey, settings));

        S3SigV4.Sign(
            request, S3SigV4.EmptyPayloadHash, contentType: null,
            clock.GetUtcNow().UtcDateTime, settings);

        var client = httpClientFactory.CreateClient(HttpClientName);

        // O'chirish — kichik amal: tana yo'q, javob bir necha bayt.
        using var timeout = StorageTimeout.Start(settings.TimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            MediaStorageLog.DeleteFailed(logger, ex, objectKey);

            throw new ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi — fayl o'chirilmadi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            MediaStorageLog.DeleteFailed(logger, ex, objectKey);

            throw new ServiceUnavailableException(
                "Faylni o'chirish juda uzoq davom etdi.");
        }

        using (response)
        {
            // IDEMPOTENT: S3 yo'q obyektni o'chirishda ham `204` beradi,
            // lekin ba'zi mos xizmatlar `404` qaytaradi — ikkalasi ham
            // "endi yo'q" degani, ya'ni MUVAFFAQIYAT.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return;

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            MediaStorageLog.DeleteRejected(logger, objectKey, (int)response.StatusCode, Trim(body));

            throw new ServiceUnavailableException(
                "Faylni ombordan o'chirib bo'lmadi (ombor xatosi).");
        }
    }

    // ================================================================= ichki

    /// <summary>
    /// Obyekt kaliti: <c>&lt;prefiks&gt;/lesson-assets/2026-08/9f8e…a1.mp4</c>.
    ///
    /// OYLIK papkalar — bitta "papkada" millionlab obyekt bo'lib ketmasin.
    /// TASODIFIY qism (8 bayt = 64 bit) — kalitni taxmin qilib omborga
    /// to'g'ridan-to'g'ri murojaat qilish mumkin bo'lmasin.
    ///
    /// ⚠️ O'QUVCHI/FOYDALANUVCHI ID'si kalitda YO'Q (vazifa javobidan farqli):
    /// dars videosi shaxsiy ma'lumot emas, uni kim yuklagani esa bazada
    /// (`CreatedById`) turadi.
    /// </summary>
    private string BuildKey(MediaUpload upload, StorageOptions settings)
    {
        var month = clock.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        var prefix = string.IsNullOrWhiteSpace(settings.KeyPrefix)
            ? "media"
            : settings.KeyPrefix.Trim('/');

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}/{upload.Folder}/{month}/{random}.{upload.Extension}");
    }

    /// <summary>
    /// Obyektning TO'LIQ hajmi.
    ///
    /// Qisman javobda `Content-Length` faqat BO'LAK uzunligi, to'liq hajm esa
    /// `Content-Range: bytes 100-199/12345` ning oxirida turadi.
    /// </summary>
    private static long? TotalLength(HttpResponseMessage response)
    {
        var range = response.Content.Headers.ContentRange;

        if (range?.Length is { } total) return total;

        return response.Content.Headers.ContentLength;
    }

    private static string Trim(string body) =>
        body.Length <= MaxLoggedBodyLength ? body : body[..MaxLoggedBodyLength];

    private const int MaxLoggedBodyLength = 500;

    /// <summary>Ombor turni aytmasa — "noma'lum ikkilik", brauzer uni RENDER QILMAYDI.</summary>
    private const string DefaultContentType = "application/octet-stream";
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848).
/// EventId'lar 5100 dan boshlanadi — `StorageLog` (5000) bilan
/// to'qnashmasin, aks holda ikki xil hodisa bir raqam ostida qolardi.
/// </summary>
internal static partial class MediaStorageLog
{
    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Error,
        Message = "Media omboriga ulanish xatosi (yuklash). key={Key}")]
    internal static partial void UploadFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Error,
        Message = "Media yuklash timeout. key={Key}")]
    internal static partial void UploadTimedOut(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Error,
        Message = "Ombor media faylni rad etdi. key={Key} status={Status} javob={Body}")]
    internal static partial void UploadRejected(ILogger logger, string key, int status, string body);

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Error,
        Message = "Media faylni ombordan o'qishda ulanish xatosi. key={Key}")]
    internal static partial void DownloadFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Error,
        Message = "Media faylni ombordan o'qish timeout. key={Key}")]
    internal static partial void DownloadTimedOut(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5105,
        Level = LogLevel.Error,
        Message = "Ombor media faylni bermadi. key={Key} status={Status} javob={Body}")]
    internal static partial void DownloadRejected(ILogger logger, string key, int status, string body);

    [LoggerMessage(
        EventId = 5106,
        Level = LogLevel.Warning,
        Message = "Ombor `Range` so'rovini bajarmadi (416) — baza hajmi va obyekt "
                  + "ajralib qolgan bo'lishi mumkin. key={Key}")]
    internal static partial void RangeNotSatisfiable(ILogger logger, string key);

    [LoggerMessage(
        EventId = 5107,
        Level = LogLevel.Error,
        Message = "Media faylni o'chirishda ulanish xatosi. key={Key}")]
    internal static partial void DeleteFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 5108,
        Level = LogLevel.Error,
        Message = "Ombor media faylni o'chirmadi. key={Key} status={Status} javob={Body}")]
    internal static partial void DeleteRejected(ILogger logger, string key, int status, string body);
}
