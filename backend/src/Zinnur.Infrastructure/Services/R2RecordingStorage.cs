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
///   • <see cref="HeadAsync"/>, <see cref="OpenReadAsync"/>,
///     <see cref="PutAsync"/>, <see cref="DeleteAsync"/> — imzo
///     SARLAVHADA, chunki so'rovni BIZ yuboramiz (watchdog va tungi
///     yig'uvchi).
///
/// ⚠️ SARLAVHALI YO'L ENDI KO'PCHILIKNI TASHKIL QILADI: sarlavha tepasidagi
/// "presigned GET + HEAD" nomi 2026-09 dan to'liq emas — tungi yig'uvchi
/// yo'li (SPEC-RECORDING-V2, M4) shu sinfga xom fayllarni O'QISH, tayyor
/// faylni QO'YISH va xomlarni O'CHIRISH amallarini qo'shdi. Presigned imzo
/// AVVALGIDEK faqat <see cref="CreateViewLink"/> da.
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

    /// <summary>
    /// XOM (raw) fayllarning ildizi — <see cref="RootFolder"/> dan ALOHIDA.
    ///
    /// ★ NIMA UCHUN ALOHIDA ILDIZ, <c>recordings/raw/…</c> EMAS: bucket'ning
    /// umr sikli (lifecycle) qoidalari, zaxira skriptlari va admin ro'yxati
    /// PREFIKS bo'yicha ishlaydi. Xom fayllar <c>recordings/</c> ostida
    /// tursa, ular avtomatik ravishda "yozuv" toifasiga tushardi va bir kun
    /// kimdir yarim tayyor faylni o'quvchiga ko'rsatib qo'yardi. Sabab
    /// to'liq — port izohida (<c>BuildRawObjectKey</c>).
    /// </summary>
    private const string RawRootFolder = "raw";

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

    // ══════════════════════════════════════════════════════════════════
    // TUNGI YIG'UVCHI YO'LI — XOM FAYLLAR (SPEC-RECORDING-V2, M4)
    // ══════════════════════════════════════════════════════════════════
    //
    // Quyidagi to'rt amalning hammasi SARLAVHA bilan imzolanadi
    // (`S3SigV4.Sign`) va ICHKI manzilga (`Storage:ServiceUrl`) boradi —
    // so'rovni BIZ yuboramiz, brauzer emas. Ya'ni `CreateViewLink` dagi
    // query-string imzosi bu yerda UMUMAN qatnashmaydi.
    //
    // ⚠️ HAMMASI FON JARAYONIDAN chaqiriladi (tungi ishchi), ya'ni
    //    chegarasiz qolgan so'rov foydalanuvchiga emas, KECHAGA zarar
    //    beradi: bitta osilgan yuklash butun navbatni to'xtatib qo'yardi.
    //    Shuning uchun har amalda `StorageTimeout` bor.

    /// <summary>
    /// Xom trek obyektining kaliti: <c>raw/1234/77/TR_ab12.webm</c>.
    /// Sxema va sabablari — port izohida.
    /// </summary>
    public string BuildRawObjectKey(
        long sessionId, long recordingId, string trackSid, string extension)
    {
        // 🔴 NOL ID — HAQIQIY TUZOQ, DIQQAT: `RecordingTrack` qatoriga
        //    identifikator faqat `SaveChangesAsync` dan KEYIN beriladi.
        //    Kalit undan oldin yasalsa, HAR darsning HAR treki
        //    `raw/{dars}/0/…` ga tushardi va ular bir-birining ustidan
        //    yozilardi. Nosozlik esa faqat kechasi, "video yarim joyda
        //    uzilib qoldi" ko'rinishida chiqardi.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordingId);

        ArgumentException.ThrowIfNullOrWhiteSpace(trackSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var sid = trackSid.Trim();

        // Kengaytma nuqta bilan ham, nuqtasiz ham berilishi mumkin
        // (`.webm` / `webm`) — chaqiruvchi MIME jadvalidan oladi va u
        // jadval kelajakda tuzatiladi (SPEC 2.8: bashorat ISHONCHSIZ).
        var ext = extension.Trim().TrimStart('.').ToLowerInvariant();

        // `/` yo'l ajratgichi: sid yoki kengaytma ichida uchrasa kalit
        // sxemasi jimgina o'zgarardi (qo'shimcha "papka"), tozalash esa
        // prefiks bo'yicha ishlagani uchun xom fayl abadiy qolib ketardi.
        if (sid.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Trek identifikatorida `/` bo'lishi mumkin emas.", nameof(trackSid));
        }

        if (ext.Length == 0 || ext.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Kengaytma bo'sh yoki `/` belgisini o'z ichiga oladi.", nameof(extension));
        }

        return string.Create(
            CultureInfo.InvariantCulture, $"{RawRootFolder}/{sessionId}/{recordingId}/{sid}.{ext}");
    }

    /// <inheritdoc />
    public async Task<StoredRecordingObject?> OpenReadAsync(
        string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Xom faylni o'qib bo'lmadi.");
        }

        // ICHKI manzil: faylni yig'uvchi konteyneri o'ziga tortib oladi.
        var uri = S3SigV4.BuildUri(settings.ServiceUrl, settings.Bucket, objectKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // GET da tana yo'q => bo'sh yuk xeshi; `Range` ham yo'q, ya'ni
        // kanonik sarlavhalar ro'yxati `HeadAsync` dagidek eng qisqa.
        S3SigV4.Sign(
            request,
            S3SigV4.EmptyPayloadHash,
            contentType: null,
            clock.GetUtcNow().UtcDateTime,
            settings);

        var client = httpClientFactory.CreateClient(R2SubmissionStorage.HttpClientName);

        // ★★ CHEGARA FAQAT SARLAVHAGA. `using` tugagach taymer to'xtaydi,
        //    faylni yuklab olish esa chegarasiz davom etadi — 1 GB xom
        //    videoni "ombor timeout'i" o'rtasidan uzib qo'ymasin. Nega
        //    `Dispose` oqimni buzmasligi: `StorageTimeout` izohi.
        //
        // ⚠️ Yuklab olishni to'xtatadigan YAGONA narsa — chaqiruvchining
        //    `ct` si, ya'ni tungi oynaning 09:00 dagi qat'iy chegarasi.
        using var timeout = StorageTimeout.Start(settings.TimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            // ★ ResponseHeadersRead: javob TANASI KUTILMAYDI — baytlar
            //   bevosita diskka oqadi. Busiz butun xom fayl yig'uvchi
            //   konteynerining XOTIRASIGA tushardi.
            response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            RecordingStorageLog.RawDownloadFailed(logger, ex, objectKey);

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi — xom fayl olinmadi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            RecordingStorageLog.RawDownloadTimedOut(logger, ex, objectKey);

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Xom faylni ochish juda uzoq davom etdi.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Bazada trek `Completed`, omborda obyekt yo'q — baza va ombor
            // ajralib qolgan. Bu 503 EMAS (ombor sog'lom): yig'uvchi shu
            // bitta segmentni tashlab, qolganini yig'averadi. Butun darsni
            // yo'qotgandan ko'ra bir bo'lakni yo'qotgan afzal.
            response.Dispose();

            RecordingStorageLog.RawObjectMissing(logger, objectKey);

            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            RecordingStorageLog.RawDownloadRejected(
                logger, objectKey, (int)response.StatusCode, Trim(body));

            response.Dispose();

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Xom faylni ombordan o'qib bo'lmadi (ombor xatosi).");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        return new StoredRecordingObject(
            stream,
            response.Content.Headers.ContentLength,

            // `response` — OQIM EGASI: u yopilmasa ulanish hovuzga qaytmaydi.
            response);
    }

    /// <inheritdoc />
    public async Task PutAsync(
        string objectKey,
        Stream content,
        long length,
        string contentType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Yig'ilgan yozuv saqlanmadi.");
        }

        if (!content.CanSeek)
        {
            // Bu DASTURCHI xatosi, foydalanuvchi xatosi emas: SigV4 tananing
            // xeshini talab qiladi, ya'ni oqim ikki marta o'qilishi kerak.
            throw new InvalidOperationException(
                "Yuklash oqimi izlanadigan (seekable) bo'lishi shart — SigV4 xeshi uchun.");
        }

        // 🔴 POZITSIYA XESHDAN OLDIN NOLGA QAYTARILADI.
        //
        // Aks holda chaqiruvchi oqimni yarim o'qigan holda bersa, xesh
        // faqat QOLGAN qismni qamrab olardi, tana esa BUTUNLAY yuborilardi
        // — ombor javobi `403 SignatureDoesNotMatch` bo'lardi va logda
        // buning sababini ko'rsatadigan hech nima qolmasdi.
        content.Position = 0;

        // Uzunlik SigV4 uchun emas, `Content-Length` uchun kerak, lekin u
        // xato bo'lsa nosozlik ombor tomonda, tushunarsiz shaklda chiqardi
        // (yuborilgan baytlar soni e'lon qilinganidan farq qiladi). Shu
        // yerda to'xtatgan afzal — bu DASTURCHI xatosi.
        if (content.Length != length)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Yuklash uzunligi oqim hajmiga mos emas: e'lon qilingan {length}, "
                + $"oqimda {content.Length} bayt."));
        }

        var uri = S3SigV4.BuildUri(settings.ServiceUrl, settings.Bucket, objectKey);

        // ★ XESH OQIMDAN, XOTIRAGA OLMASDAN hisoblanadi. 2 GB fayl uchun bu
        //   LOKAL diskdan bir marta qo'shimcha o'qish (yig'uvchi faylni
        //   scratch papkasida yasagan), ya'ni tarmoqqa tegmaydi va xotira
        //   sarfi doimiy.
        //
        // ⚠️ `UNSIGNED-PAYLOAD` bu yerda ISHLATILMADI: u imzoni tana
        //    bilan bog'lamaydi, ya'ni yo'lda buzilgan bayt ombor tomonda
        //    aniqlanmasdi. Yozuv — tunda bir marta yasaladigan, qayta
        //    tiklab bo'lmaydigan artefakt.
        var payloadHash = S3SigV4.Hex(
            await SHA256.HashDataAsync(content, ct).ConfigureAwait(false));

        // Xeshlash oqimni OXIRIGACHA o'qidi — yuborishdan oldin yana
        // boshiga qaytariladi.
        content.Position = 0;

        using var request = new HttpRequestMessage(HttpMethod.Put, uri);
        using var body = new StreamContent(content);

        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        // `Content-Length` ANIQ berilishi kerak: aks holda `StreamContent`
        // chunked transfer ishlatadi va S3 uni QABUL QILMAYDI.
        body.Headers.ContentLength = length;

        request.Content = body;

        S3SigV4.Sign(request, payloadHash, contentType, clock.GetUtcNow().UtcDateTime, settings);

        var client = httpClientFactory.CreateClient(R2SubmissionStorage.HttpClientName);

        // ═══════════════════════════════════════════════════════════════
        // 🔴 KATTA YUKLASH CHEGARASI — `TimeoutSeconds` (60 s) EMAS
        //
        // Chegara TANA UZATISHNI ham qamrab oladi. O'lchangan bitta dars
        // 1.75 GB chiqqan; 60 soniya unga ~250 Mbit/s DOIMIY tezlik talab
        // qilardi, ya'ni tungi yig'ish HAR SAFAR uzilardi va sabab faqat
        // "yuklash juda uzoq davom etdi" ko'rinishida chiqardi.
        //
        // ⚠️ Bu qiymat `RuntimeStorageOptions.Compose` da ham
        //    UZATILISHI shart — u yerda tushib qolsa, baza kesimi
        //    yuklangan zahoti standart 60 s ga qaytardi (o'sha faylda
        //    izohi bilan tuzatilgan).
        // ═══════════════════════════════════════════════════════════════
        using var timeout = StorageTimeout.Start(settings.LargeUploadTimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            RecordingStorageLog.UploadFailed(logger, ex, objectKey);

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi — yig'ilgan yozuv saqlanmadi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            RecordingStorageLog.UploadTimedOut(logger, ex, objectKey);

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Yig'ilgan yozuvni yuklash juda uzoq davom etdi "
                + "(`Storage:LargeUploadTimeoutSeconds`).");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return;

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            RecordingStorageLog.UploadRejected(
                logger, objectKey, (int)response.StatusCode, Trim(text));

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Yig'ilgan yozuvni saqlab bo'lmadi (ombor xatosi).");
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var settings = options.Current;

        if (!settings.IsConfigured)
        {
            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Xom faylni o'chirib bo'lmadi.");
        }

        var uri = S3SigV4.BuildUri(settings.ServiceUrl, settings.Bucket, objectKey);

        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);

        S3SigV4.Sign(
            request,
            S3SigV4.EmptyPayloadHash,
            contentType: null,
            clock.GetUtcNow().UtcDateTime,
            settings);

        var client = httpClientFactory.CreateClient(R2SubmissionStorage.HttpClientName);

        // O'chirish — kichik amal: tana yo'q, javob bir necha bayt.
        using var timeout = StorageTimeout.Start(settings.TimeoutSeconds, ct);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            RecordingStorageLog.DeleteFailed(logger, ex, objectKey);

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Fayl omboriga ulanib bo'lmadi — xom fayl o'chirilmadi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            RecordingStorageLog.DeleteFailed(logger, ex, objectKey);

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Xom faylni o'chirish juda uzoq davom etdi.");
        }

        using (response)
        {
            // IDEMPOTENT: S3 yo'q obyektni o'chirishda ham `204` beradi,
            // lekin ba'zi mos xizmatlar `404` qaytaradi — ikkalasi ham
            // "endi yo'q" degani, ya'ni MUVAFFAQIYAT.
            if (response.IsSuccessStatusCode
                || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return;
            }

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            RecordingStorageLog.DeleteRejected(
                logger, objectKey, (int)response.StatusCode, Trim(text));

            throw new Application.Common.Exceptions.ServiceUnavailableException(
                "Xom faylni ombordan o'chirib bo'lmadi (ombor xatosi).");
        }
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

    // ── TUNGI YIG'UVCHI YO'LI: 6621–6629 ────────────────────────────────
    //
    // ⚠️ Bu hodisalarni HECH KIM real vaqtda ko'rmaydi — ular kechasi,
    //    fon jarayonida yoziladi. Ya'ni xabar matni "ertalab logdan o'qib
    //    tushunarli" bo'lishi SHART: kalit, status va omborning javobi
    //    har birida bor.

    [LoggerMessage(
        EventId = 6621,
        Level = LogLevel.Error,
        Message = "Xom faylni o'qishda omborga ulanib bo'lmadi. kalit={Key}")]
    internal static partial void RawDownloadFailed(
        ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 6622,
        Level = LogLevel.Error,
        Message = "Xom faylni o'qish timeout (sarlavha kutildi). kalit={Key}")]
    internal static partial void RawDownloadTimedOut(
        ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 6623,
        Level = LogLevel.Error,
        Message = "Ombor xom faylni bermadi. kalit={Key} status={Status} javob={Body}")]
    internal static partial void RawDownloadRejected(
        ILogger logger, string key, int status, string body);

    /// <summary>
    /// ★ WARNING, ERROR EMAS: bitta xom segment yo'qolishi darsni
    /// YO'QOTMAYDI — yig'uvchi qolganini yig'averadi. Ammo bu holat baza va
    /// ombor ajralib qolganini bildiradi, ya'ni jimgina o'tkazib yuborish
    /// ham mumkin emas.
    /// </summary>
    [LoggerMessage(
        EventId = 6624,
        Level = LogLevel.Warning,
        Message = "Xom fayl omborda topilmadi — bu segment yig'ishdan tushib qoladi. kalit={Key}")]
    internal static partial void RawObjectMissing(ILogger logger, string key);

    [LoggerMessage(
        EventId = 6625,
        Level = LogLevel.Error,
        Message = "Yig'ilgan yozuvni yuklashda omborga ulanib bo'lmadi. kalit={Key}")]
    internal static partial void UploadFailed(ILogger logger, Exception exception, string key);

    /// <summary>
    /// 🔴 BU XABAR CHIQSA BIRINCHI GUMON — `LargeUploadTimeoutSeconds`.
    /// Aynan shu qiymat `RuntimeStorageOptions.Compose` da tushib qolgan
    /// edi va natijada 1.75 GB fayl 60 soniyaga sig'ishi kerak bo'lardi.
    /// </summary>
    [LoggerMessage(
        EventId = 6626,
        Level = LogLevel.Error,
        Message = "Yig'ilgan yozuvni yuklash timeout. kalit={Key}")]
    internal static partial void UploadTimedOut(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 6627,
        Level = LogLevel.Error,
        Message = "Ombor yig'ilgan yozuvni rad etdi. kalit={Key} status={Status} javob={Body}")]
    internal static partial void UploadRejected(
        ILogger logger, string key, int status, string body);

    /// <summary>
    /// Ulanish xatosi HAM, timeout HAM shu yerga tushadi (istisno matni
    /// ularni ajratadi). Sabab: o'chirish — NAZORATSIZ, TAKRORLANADIGAN
    /// qadam; keyingi kecha uni qaytadan urinib ko'radi, ya'ni ikki
    /// alohida hodisa raqami hech kimga qo'shimcha ma'lumot bermasdi.
    /// </summary>
    [LoggerMessage(
        EventId = 6628,
        Level = LogLevel.Error,
        Message = "Xom faylni o'chirishda omborga ulanib bo'lmadi. kalit={Key}")]
    internal static partial void DeleteFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 6629,
        Level = LogLevel.Error,
        Message = "Xom faylni o'chirib bo'lmadi. kalit={Key} status={Status} javob={Body}")]
    internal static partial void DeleteRejected(
        ILogger logger, string key, int status, string body);
}
