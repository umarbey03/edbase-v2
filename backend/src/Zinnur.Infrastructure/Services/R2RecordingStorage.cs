using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// <see cref="IRecordingStorage"/> — S3/R2 (presigned GET + HEAD)
/// ════════════════════════════════════════════════════════════════════════
///
/// Nima uchun yozuv uchun PRESIGNED, vazifa fayllari uchun esa PROXY
/// tanlangani — port izohida (u yerda ikkala tomon ham yozilgan). Bu yerda
/// FAQAT "qanday" savoliga javob.
///
/// ── IKKI XIL SigV4 ──────────────────────────────────────────────────────
///
/// <c>R2SubmissionStorage</c> imzoni SARLAVHAGA qo'yadi
/// (<c>Authorization</c>). Bu yerda esa IKKALA usul ham kerak:
///
///   • <see cref="CreateViewLink"/> — imzo QUERY-STRING'da. Faqat shu
///     usulda havolani brauzerga berish mumkin: <c>&lt;video src&gt;</c>
///     hech qanday sarlavha yubormaydi.
///   • <see cref="HeadAsync"/> — imzo SARLAVHADA, chunki so'rovni BIZ
///     yuboramiz (watchdog).
///
/// ★ NIMA UCHUN IKKI SINF EMAS: ikkalasi ham AYNI kalitlar va AYNI imzo
/// zanjiridan foydalanadi (<c>SigningKey</c>). Nusxalansa, ular vaqt o'tib
/// bir-biridan uzoqlashardi va farq faqat "403 SignatureDoesNotMatch"
/// ko'rinishida — sababsiz — chiqardi.
///
/// ── MANZIL IKKI XIL ─────────────────────────────────────────────────────
///
/// 🔴 <see cref="CreateViewLink"/> — <c>Storage:PublicUrl</c> (brauzer
/// ko'radigan), <see cref="HeadAsync"/> — <c>Storage:ServiceUrl</c> (ichki).
/// Imzo HOSTGA bog'langani uchun bu farq MAJBURIY: dev'da ombor Docker
/// tarmog'i ichida (<c>http://minio:9000</c>) va brauzer u manzilga umuman
/// kira olmaydi.
///
/// ★★ QIYMATLAR HAR CHAQIRUVDA OLINADI — <c>IOptions&lt;T&gt;</c> EMAS.
/// ⚠️ Bitta amal davomida kesim BIR MARTA olinadi: imzo bir kalit bilan
/// hisoblanib, <c>Credential</c> boshqasini ko'rsatsa ombor har so'rovni
/// 403 bilan qaytarardi (batafsil: <c>R2SubmissionStorage</c>).
/// </summary>
public sealed class R2RecordingStorage(
    IHttpClientFactory httpClientFactory,
    IRuntimeOptions<StorageOptions> options,
    ILogger<R2RecordingStorage> logger,
    TimeProvider clock) : IRecordingStorage
{
    /// <summary>
    /// Yozuvlar saqlanadigan "papka".
    ///
    /// ★ NIMA UCHUN <c>Storage:KeyPrefix</c> ISHLATILMAYDI: o'sha sozlama
    /// UY VAZIFASI modulining prefiksi (standart qiymati —
    /// <c>submissions</c>) va uni o'zgartirish eski FAYLLARGA yo'lni
    /// uzadi. Yozuv boshqa modul, ya'ni unga o'z ildizi kerak. Bitta
    /// sozlamani ikki modulga bo'lishish "prefiksni o'zgartirdim —
    /// yozuvlar ham yo'qoldi" degan kutilmagan bog'lanishni yasardi.
    /// </summary>
    private const string RootFolder = "recordings";

    /// <summary>Kalitning tasodifiy qismi (bayt) — taxmin qilib bo'lmasin.</summary>
    private const int RandomBytes = 8;

    // Bo'sh tananing SHA-256 xeshi endi `S3SigV4.EmptyPayloadHash` da —
    // uchta ombor xizmatida bitta qiymat bo'lsin.

    /// <summary>
    /// Presigned so'rovda tananing xeshi o'rniga shu qiymat turadi:
    /// brauzer GET yuborganda tana umuman bo'lmaydi va uni imzoga kiritish
    /// mumkin emas.
    /// </summary>
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    private const int MaxLoggedBodyLength = 500;

    /// <inheritdoc />
    public bool IsConfigured => options.Current.IsConfigured;

    /// <summary>
    /// Yangi yozuv uchun obyekt kaliti:
    /// <c>recordings/2026-07/1234/9f8e…a1.mp4</c>.
    ///
    /// OYLIK papkalar — bitta "papkada" o'n minglab obyekt yig'ilib
    /// qolmasin (ro'yxatlash va zaxiralash sekinlashadi). DARS ID'si
    /// kalitda: muammo tekshirilganda fayl qaysi darsga tegishli ekani
    /// omborning O'ZIDAN ko'rinadi. TASODIFIY qism esa kalitni taxmin
    /// qilishning oldini oladi — presigned havola faqat kalitni BILGAN
    /// odam uchun so'raladi, lekin ombor xato sozlansa (ochiq bucket)
    /// taxmin qilinadigan nom yagona to'siq bo'lib qolardi.
    /// </summary>
    public string BuildObjectKey(long sessionId)
    {
        var month = clock.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(RandomBytes)).ToLowerInvariant();

        return string.Create(
            CultureInfo.InvariantCulture, $"{RootFolder}/{month}/{sessionId}/{random}.mp4");
    }

    /// <inheritdoc />
    public Uri CreateViewLink(string objectKey, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Yozuvni ochib bo'lmadi.");
        }

        // Muddat CHEGARALANADI. Yuqori chegara 12 soat — S3 SigV4
        // query-string imzosining O'ZI 7 kungacha ruxsat beradi, lekin
        // uzun muddat havolani ulashishga yaroqli qilardi (eski tizimning
        // 4 soatlik havolasi aynan shunday ishlatilgan). Pastki chegara —
        // 1 daqiqa: undan qisqasi pleyer havolani ochishga ham ulgurmasdi.
        var seconds = (int)Math.Clamp(ttl.TotalSeconds, 60, 12 * 3600);

        var now = clock.GetUtcNow().UtcDateTime;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var scope = string.Create(
            CultureInfo.InvariantCulture, $"{dateStamp}/{settings.Region}/s3/aws4_request");

        var baseUri = S3SigV4.BuildUri(settings.EffectivePublicUrl, settings.Bucket, objectKey);

        // ⚠️ `Authority`, `Host` EMAS — standart bo'lmagan port imzoga
        // KIRISHI shart (`R2SubmissionStorage` da topilgan va tuzatilgan
        // bug: MinIO har so'rovni 403 bilan qaytargan edi).
        var host = baseUri.Authority;

        // ★ QUERY PARAMETRLARI ALFABIT TARTIBDA bo'lishi SHART — kanonik
        //   so'rov qoidasi. Quyidagi beshtasi allaqachon tartibda
        //   (Algorithm < Credential < Date < Expires < SignedHeaders).
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"X-Amz-Algorithm=AWS4-HMAC-SHA256"
            + $"&X-Amz-Credential={Uri.EscapeDataString(settings.AccessKey + "/" + scope)}"
            + $"&X-Amz-Date={amzDate}"
            + $"&X-Amz-Expires={seconds}"
            + $"&X-Amz-SignedHeaders=host");

        var canonicalRequest = string.Join('\n',
            "GET",
            baseUri.AbsolutePath,
            query,
            string.Create(CultureInfo.InvariantCulture, $"host:{host}\n"),
            "host",
            UnsignedPayload);

        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            S3SigV4.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signature = S3SigV4.Hex(HMACSHA256.HashData(
            S3SigV4.SigningKey(dateStamp, settings), Encoding.UTF8.GetBytes(stringToSign)));

        // Imzo ENG OXIRIDA qo'shiladi — u kanonik so'rovga KIRMAYDI.
        return new Uri(string.Create(
            CultureInfo.InvariantCulture, $"{baseUri}?{query}&X-Amz-Signature={signature}"));
    }

    /// <inheritdoc />
    public async Task<StoredObjectInfo?> HeadAsync(
        string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`).");
        }

        // ICHKI manzil: so'rovni BIZ yuboramiz, brauzer emas.
        var uri = S3SigV4.BuildUri(settings.ServiceUrl, settings.Bucket, objectKey);

        using var request = new HttpRequestMessage(HttpMethod.Head, uri);

        // HEAD: tana yo'q, `Content-Type` yo'q — ya'ni kanonik sarlavhalar
        // ro'yxati eng qisqa shaklda (`host;x-amz-content-sha256;x-amz-date`).
        // AYNI imzo `R2SubmissionStorage` va `R2MediaStorage` da ham
        // ishlatiladi — algoritm `S3SigV4` da, YAGONA joyda.
        S3SigV4.Sign(
            request,
            S3SigV4.EmptyPayloadHash,
            contentType: null,
            clock.GetUtcNow().UtcDateTime,
            settings);

        var client = httpClientFactory.CreateClient(R2SubmissionStorage.HttpClientName);

        // `HEAD` — eng kichik amal (tana umuman yo'q). Chegara klientda
        // emas, shu yerda beriladi: sabab `StorageTimeout` izohida.
        //
        // ⚠️ BU YO'LNI WATCHDOG CHAQIRADI, ya'ni u ARQA FONDA ishlaydi:
        // chegarasiz qolsa osilgan so'rov fon vazifasini butunlay
        // to'xtatib qo'yardi va yozuvlar abadiy "Active" bo'lib qolardi.
        using var timeout = StorageTimeout.Start(settings.TimeoutSeconds, ct);

        using var response = await client
            .SendAsync(request, timeout.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            // 403 (kalit noto'g'ri) va 5xx ni "fayl yo'q" deb talqin qilish
            // XATO bo'lardi: watchdog o'shanda tayyor yozuvni `Failed` deb
            // belgilab qo'yardi. Shuning uchun istisno — chaqiruvchi uni
            // "hozircha noma'lum" deb ko'radi va keyingi yurishga qoldiradi.
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            RecordingStorageLog.HeadRejected(logger, objectKey, (int)response.StatusCode, Trim(body));

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Ombor javob bermadi (yozuv holati aniqlanmadi).");
        }

        return new StoredObjectInfo(response.Content.Headers.ContentLength);
    }

    // ================================================================= umumiy
    //
    // ⚠️ SARLAVHA BILAN IMZOLASH (HEAD) VA IMZO KALITI BU YERDAN OLIB
    //    TASHLANDI -> `S3SigV4`. Sabab: algoritm uch nusxaga bo'linib
    //    ketmasin (`R2SubmissionStorage`, `R2MediaStorage` ham AYNI kodni
    //    ishlatadi). `Uri.Authority` (port BILAN) qoidasi o'sha yerda,
    //    izohi bilan saqlangan.
    //
    // ★ QUERY-STRING (presigned) IMZOSI ATAYLAB SHU YERDA QOLDI: u boshqa
    //   variant — imzo `X-Amz-*` query parametrlarida, payload
    //   `UNSIGNED-PAYLOAD`, kanonik so'rovda query STRING sifatida
    //   qatnashadi. Uni umumiy metodga siqish "bitta metod ikki xil ish
    //   qiladi" degan holatga olib kelardi; umumiy qismlari
    //   (`SigningKey`, `Hex`, `EncodeKey`, `BuildUri`) esa allaqachon
    //   `S3SigV4` dan olinadi.

    private static string Trim(string body) =>
        body.Length <= MaxLoggedBodyLength ? body : body[..MaxLoggedBodyLength];
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). EventId makoni: <c>6620–6629</c>.
///
/// 🔴 PRESIGNED HAVOLA HECH QACHON LOGGA YOZILMAYDI: uning ichida imzo bor
/// va logni ko'ra olgan odam yozuvni ocha olardi.
/// </summary>
internal static partial class RecordingStorageLog
{
    [LoggerMessage(
        EventId = 6620,
        Level = LogLevel.Error,
        Message = "Ombor yozuv holatini bermadi. kalit={Key} status={Status} javob={Body}")]
    internal static partial void HeadRejected(
        ILogger logger, string key, int status, string body);
}
