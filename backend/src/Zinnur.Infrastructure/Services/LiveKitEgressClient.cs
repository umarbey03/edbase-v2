using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// <see cref="ILiveKitEgress"/> va <see cref="ILiveKitRoomQuery"/> —
/// LiveKit server API'si (Twirp/JSON)
/// ════════════════════════════════════════════════════════════════════════
///
/// ── PROTOKOL ────────────────────────────────────────────────────────────
///
/// LiveKit server API'si — Twirp: oddiy <c>POST</c>, yo'l
/// <c>/twirp/&lt;xizmat&gt;/&lt;Metod&gt;</c>, tana JSON. Ya'ni SDK ham,
/// gRPC ham kerak emas (<c>R2SubmissionStorage</c> AWS SDK'siz ishlagani
/// bilan AYNI mulohaza: bizga bir necha amal kerak, ular esa oddiy HTTP).
///
/// ── NIMA UCHUN BITTA SINF IKKI PORTNI BAJARADI ──────────────────────────
///
/// Portlar ATAYLAB ikkita (biri boshqaradi, ikkinchisi faqat o'qiydi —
/// sabab <see cref="ILiveKitRoomQuery"/> izohida), lekin ularning
/// TEXNIKASI bitta: ayni manzil, ayni Twirp qobig'i, ayni imzolash usuli
/// va ayni xato ishlovi. Ikkinchi sinf yasasak, o'sha texnika ikki
/// nusxada qolardi va bir kuni faqat bittasi tuzatilardi.
///
/// ── FAYL BIZNING SERVERIMIZDAN O'TMAYDI ─────────────────────────────────
///
/// Ombor kalitlari Egress'ga SO'ROV ICHIDA beriladi va u videoni
/// TO'G'RIDAN-TO'G'RI omborga yozadi. Bizning API konteynerimiz orqali
/// bitta bayt ham o'tmaydi — 80 daqiqalik dars ~0.5 GB va u LiveKit SFU
/// bilan AYNI tarmoq kanalidan o'tishi jonli darsni sekinlashtirardi.
///
/// 🔴 SHUNING UCHUN <see cref="IsConfigured"/> IKKALASINI HAM TALAB QILADI.
/// Ombor sozlanmagan bo'lsa Egress'da "yozuv boshlandi" ko'rinardi, fayl
/// esa hech qayerga tushmasdi — eng yomon turdagi nosozlik: jimgina yolg'on.
///
/// ── NIMA UCHUN TOKEN SHU YERDA YIG'ILADI ────────────────────────────────
///
/// <c>LiveKitTokenService</c> XONAGA KIRISH tokenini yasaydi:
/// <c>video: { roomJoin, room, canPublish… }</c> va identity bilan. Egress
/// tokeni esa BOShQA grant talab qiladi — <c>roomRecord</c> — va unda
/// identity umuman yo'q, umri esa bir necha daqiqa (u server-server
/// chaqiruvida, bir marta ishlatiladi). Mavjud servisga ikkinchi rejim
/// qo'shish uning shartnomasini ikkiga bo'lardi; bu yerdagi payload esa
/// atigi to'rt maydondan iborat.
///
/// ★ KALIT, SIR VA OMBOR QIYMATLARI HAR CHAQIRUVDA QAYTA O'QILADI
/// (<see cref="IRuntimeOptions{TOptions}"/>) — ular paneldan
/// aylantiriladi. Har amal boshida kesim BIR MARTA olinadi: token bir
/// juftlik bilan imzolanib, so'rov boshqasiga ketib qolmasin.
/// </summary>
public sealed class LiveKitEgressClient(
    IHttpClientFactory httpClientFactory,
    IRuntimeOptions<LiveKitOptions> liveKit,
    IRuntimeOptions<StorageOptions> storage,
    ILogger<LiveKitEgressClient> logger) : ILiveKitEgress, ILiveKitRoomQuery
{
    /// <summary>Nomlangan HTTP klient (timeout DI'da sozlanadi).</summary>
    public const string HttpClientName = "zinnur-livekit-egress";

    /// <summary>
    /// Egress tokenining umri.
    ///
    /// 5 DAQIQA — ATAYLAB juda qisqa: token bitta server-server so'rovda
    /// ishlatiladi va boshqa hech qayerda saqlanmaydi. Uzoq umr faqat
    /// "ushlangan token" oynasini kengaytirardi.
    /// </summary>
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(5);

    /// <summary>{"alg":"HS256","typ":"JWT"} — o'zgarmas, bir marta hisoblanadi.</summary>
    private static readonly string EncodedHeader =
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));

    /// <summary>Xato matnining bazaga/logga tushadigan qismi.</summary>
    private const int MaxErrorLength = 300;

    /// <summary>
    /// Twirp yo'lining o'rtasidagi XIZMAT nomi. Ikkitasi ishlatiladi va
    /// ular BOSHQA-BOSHQA grantli token talab qiladi: Egress —
    /// <c>roomRecord</c>, RoomService — <c>roomAdmin</c>.
    /// </summary>
    private const string EgressService = "livekit.Egress";

    /// <inheritdoc cref="EgressService" />
    private const string RoomService = "livekit.RoomService";

    /// <inheritdoc />
    public bool IsConfigured
    {
        get
        {
            var keys = liveKit.Current;

            return !string.IsNullOrWhiteSpace(keys.ApiKey)
                && !string.IsNullOrWhiteSpace(keys.ApiSecret)
                && !string.IsNullOrWhiteSpace(keys.Url)
                && storage.Current.IsConfigured;
        }
    }

    /// <inheritdoc />
    public async Task<EgressStartResult> StartRoomRecordingAsync(
        EgressStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var keys = liveKit.Current;
        var bucket = storage.Current;

        if (!IsReady(keys, bucket))
        {
            return EgressStartResult.Fail(
                "Yozuv xizmati sozlanmagan (`LiveKit:*` yoki `Storage:*`).");
        }

        var payload = BuildStartPayload(request, bucket);

        var response = await CallAsync(
            "StartRoomCompositeEgress", payload, keys, ct).ConfigureAwait(false);

        if (!response.Succeeded)
            return EgressStartResult.Fail(response.Error!);

        var egressId = ReadEgressId(response.Body);

        if (string.IsNullOrWhiteSpace(egressId))
        {
            // Javob 200, lekin ichida Id yo'q. Bu holatni "muvaffaqiyat"
            // deb belgilash eng yomon variant bo'lardi: webhook keladigan
            // qatorni topa olmasdi va yozuv abadiy `Starting` bo'lib
            // qolardi.
            EgressLog.StartWithoutId(logger, request.RoomName);

            return EgressStartResult.Fail("Yozuv xizmati identifikator qaytarmadi.");
        }

        EgressLog.Started(logger, request.RoomName, egressId, request.ObjectKey);

        return EgressStartResult.Ok(egressId);
    }

    /// <inheritdoc />
    public async Task<EgressStartResult> StartTrackRecordingAsync(
        TrackEgressStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var keys = liveKit.Current;
        var bucket = storage.Current;

        if (!IsReady(keys, bucket))
        {
            return EgressStartResult.Fail(
                "Yozuv xizmati sozlanmagan (`LiveKit:*` yoki `Storage:*`).");
        }

        var payload = BuildTrackStartPayload(request, bucket);

        var response = await CallAsync(
            "StartTrackEgress", payload, keys, ct).ConfigureAwait(false);

        if (!response.Succeeded)
            return EgressStartResult.Fail(response.Error!);

        var egressId = ReadEgressId(response.Body);

        if (string.IsNullOrWhiteSpace(egressId))
        {
            // Sabab AYNI <see cref="StartRoomRecordingAsync"/> dagidek:
            // identifikatorsiz qator webhook bilan bog'lanmaydi va abadiy
            // `Requested` bo'lib qolardi. Farqi shundaki, bu yerda yo'qolgan
            // narsa butun dars emas, bitta bo'lak — shuning uchun tiklash
            // job'i uni qayta urinib ko'radi.
            EgressLog.TrackStartWithoutId(logger, request.RoomName, request.TrackId);

            return EgressStartResult.Fail("Yozuv xizmati identifikator qaytarmadi.");
        }

        EgressLog.TrackStarted(
            logger, request.RoomName, request.TrackId, egressId, request.ObjectKey);

        return EgressStartResult.Ok(egressId);
    }

    /// <inheritdoc />
    public async Task<EgressStartResult> StartRoomAudioRecordingAsync(
        RoomAudioEgressStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var keys = liveKit.Current;
        var bucket = storage.Current;

        if (!IsReady(keys, bucket))
        {
            return EgressStartResult.Fail(
                "Yozuv xizmati sozlanmagan (`LiveKit:*` yoki `Storage:*`).");
        }

        var payload = BuildRoomAudioStartPayload(request, bucket);

        // ⚠️ METOD ESKI QUVURNIKI BILAN AYNI — farq FAQAT tanada
        // (<see cref="BuildRoomAudioStartPayload"/> izohiga qarang).
        var response = await CallAsync(
            "StartRoomCompositeEgress", payload, keys, ct).ConfigureAwait(false);

        if (!response.Succeeded)
            return EgressStartResult.Fail(response.Error!);

        var egressId = ReadEgressId(response.Body);

        if (string.IsNullOrWhiteSpace(egressId))
        {
            // 🔴 BU YERDA YO'QOTISH ENG QIMMAT: mikser — butun darsning
            // ovozi. Bitta video bo'lagi yo'qolsa bir necha daqiqa tasvir
            // ketadi, mikser yo'qolsa darsning OVOZI umuman bo'lmaydi.
            EgressLog.RoomAudioStartWithoutId(logger, request.RoomName);

            return EgressStartResult.Fail("Yozuv xizmati identifikator qaytarmadi.");
        }

        EgressLog.RoomAudioStarted(logger, request.RoomName, egressId, request.ObjectKey);

        return EgressStartResult.Ok(egressId);
    }

    /// <inheritdoc />
    public async Task<bool> StopRecordingAsync(string egressId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(egressId);

        var keys = liveKit.Current;

        if (string.IsNullOrWhiteSpace(keys.ApiKey)
            || string.IsNullOrWhiteSpace(keys.ApiSecret)
            || string.IsNullOrWhiteSpace(keys.Url))
        {
            return false;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["egress_id"] = egressId });

        var response = await CallAsync("StopEgress", payload, keys, ct).ConfigureAwait(false);

        if (!response.Succeeded)
        {
            // ⚠️ BU NORMAL HOLAT BO'LISHI MUMKIN: xona yopilganda Egress
            // O'ZI to'xtaydi va allaqachon tugagan egress uchun LiveKit
            // xato qaytaradi. Shuning uchun `Warning`, `Error` emas —
            // aks holda har normal darsdan keyin log'da "xato" chiqardi.
            EgressLog.StopRejected(logger, egressId, response.Error!);

            return false;
        }

        return true;
    }

    // ================================================================= holatni o'qish

    /// <inheritdoc />
    public async Task<LiveKitTrackListResult> ListParticipantsAsync(
        string roomName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);

        var keys = liveKit.Current;

        // ⚠️ OMBOR TEKSHIRILMAYDI: bu chaqiruv hech narsa yozmaydi, ya'ni
        // S3 kalitlari umuman kerak emas. `IsReady` ni ishlatsak, ombor
        // sozlanmagan dev muhitida tiklash job'i sababsiz "ishlamayapti"
        // deb qolardi.
        if (!HasKeys(keys))
            return LiveKitTrackListResult.Fail("LiveKit sozlanmagan (`LiveKit:*`).");

        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 128);

        using (var json = new Utf8JsonWriter(buffer))
        {
            // ⚠️ `room`, `room_name` EMAS: RoomService proto'sida maydon
            //    AYNAN shunday nomlanadi. Egress xizmatida esa `room_name` —
            //    ikkalasini adashtirsak LiveKit "xona ko'rsatilmagan" deb
            //    rad etadi.
            json.WriteStartObject();
            json.WriteString("room", roomName);
            json.WriteEndObject();
        }

        var response = await SendAsync(
            RoomService,
            "ListParticipants",
            buffer.WrittenSpan.ToArray(),
            keys.Url,
            CreateRoomAdminToken(keys, roomName),
            ct).ConfigureAwait(false);

        if (!response.Succeeded)
        {
            // ⚠️ `Warning`, `Error` EMAS: bu chaqiruv har 60 soniyada
            // takrorlanadi va LiveKit bir daqiqaga yo'qolsa log'ni xato
            // bilan to'ldirardi. Chaqiruvchi natijadan xabar topadi.
            EgressLog.ParticipantsUnreadable(logger, roomName, response.Error!);

            return LiveKitTrackListResult.Fail(response.Error!);
        }

        var tracks = ReadPublishedTracks(response.Body);

        if (tracks is null)
        {
            EgressLog.ListResponseMalformed(logger, "ListParticipants", roomName);

            return LiveKitTrackListResult.Fail("Yozuv xizmatining javobi tushunarsiz.");
        }

        return LiveKitTrackListResult.Ok(tracks);
    }

    /// <inheritdoc />
    public async Task<LiveKitEgressListResult> ListEgressAsync(
        string roomName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);

        var keys = liveKit.Current;

        if (!HasKeys(keys))
            return LiveKitEgressListResult.Fail("LiveKit sozlanmagan (`LiveKit:*`).");

        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 128);

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("room_name", roomName);

            // "Faqat faol" — port shartnomasining bir qismi, chaqiruvchining
            // tanlovi emas (sabab `ILiveKitRoomQuery` izohida).
            json.WriteBoolean("active", true);

            json.WriteEndObject();
        }

        var response = await CallAsync(
            "ListEgress", buffer.WrittenSpan.ToArray(), keys, ct).ConfigureAwait(false);

        if (!response.Succeeded)
        {
            EgressLog.ActiveEgressUnreadable(logger, roomName, response.Error!);

            return LiveKitEgressListResult.Fail(response.Error!);
        }

        var items = ReadEgressItems(response.Body);

        if (items is null)
        {
            EgressLog.ListResponseMalformed(logger, "ListEgress", roomName);

            return LiveKitEgressListResult.Fail("Yozuv xizmatining javobi tushunarsiz.");
        }

        return LiveKitEgressListResult.Ok(items);
    }

    // ================================================================= Twirp

    /// <summary>
    /// EGRESS xizmatiga Twirp chaqiruvi:
    /// <c>POST {base}/twirp/livekit.Egress/{method}</c>, <c>roomRecord</c>
    /// grantli token bilan.
    ///
    /// ★ ISTISNO CHIQMAYDI — natija qaytadi. Sabab port izohida: yozuvning
    /// boshlanmasligi DARSNI to'xtatmasligi shart.
    /// </summary>
    private Task<TwirpResponse> CallAsync(
        string method, byte[] payload, LiveKitOptions keys, CancellationToken ct) =>
        SendAsync(EgressService, method, payload, keys.Url, CreateEgressToken(keys), ct);

    /// <summary>
    /// Twirp chaqiruvining XIZMATDAN QAT'I NAZAR bir xil qismi: manzilni
    /// yig'ish, tanani yuborish, xatoni matnga aylantirish.
    ///
    /// ★ NIMA UCHUN TOKEN TASHQARIDAN BERILADI: LiveKit'ning ikki xizmati
    /// ikki BOSHQA grant talab qiladi (<c>roomRecord</c> va
    /// <c>roomAdmin</c>). Tokenni shu yerda tanlash "xizmat nomiga qarab
    /// grant tanlaydigan" yashirin qoida yaratardi — chaqiruv joyidan
    /// ko'rinmaydigan, lekin xato bo'lsa LiveKit uni JIMGINA rad etadigan
    /// qoida.
    /// </summary>
    private async Task<TwirpResponse> SendAsync(
        string service,
        string method,
        byte[] payload,
        string url,
        string token,
        CancellationToken ct)
    {
        Uri uri;

        try
        {
            uri = new Uri(BaseUrl(url) + "/twirp/" + service + "/" + method);
        }
        catch (UriFormatException)
        {
            return TwirpResponse.Fail("LiveKit manzili noto'g'ri (`LiveKit:Url`).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        using var content = new ByteArrayContent(payload);

        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Content = content;

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            EgressLog.CallFailed(logger, ex, method);

            return TwirpResponse.Fail("Yozuv xizmatiga ulanib bo'lmadi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout (bekor qilish EMAS — uni chaqiruvchi hal qiladi).
            EgressLog.CallTimedOut(logger, ex, method);

            return TwirpResponse.Fail("Yozuv xizmati javob bermadi (timeout).");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return TwirpResponse.Ok(body);

            // Twirp xatosi JSON: {"code":"...","msg":"..."}. Foydalanuvchiga
            // (xodimga) `msg` ko'rsatiladi — u odatda tushunarli
            // ("egress not found", "room does not exist"). Butun tana esa
            // LOGDA qoladi.
            EgressLog.CallRejected(logger, method, (int)response.StatusCode, Trim(body));

            return TwirpResponse.Fail(TwirpMessage(body, (int)response.StatusCode));
        }
    }

    /// <summary>
    /// <c>ws://</c> / <c>wss://</c> ni <c>http(s)://</c> ga o'giradi.
    ///
    /// 🔴 NIMA UCHUN KERAK: <c>LiveKit:Url</c> loyihaning boshqa joyida
    /// SIGNAL manzili sifatida ishlatiladi va <c>appsettings.json</c> da
    /// <c>ws://livekit:7880</c> deb yozilgan. Egress API esa oddiy HTTP —
    /// <c>ws</c> sxemasi bilan <c>HttpClient</c> darhol xato beradi.
    /// Manzilni ikkiga ko'paytirish (yana bitta sozlama) o'rniga shu yerda
    /// bir qatorli tarjima: ikkalasi ham AYNI portga boradi.
    /// </summary>
    private static string BaseUrl(string url)
    {
        var trimmed = url.TrimEnd('/');

        if (trimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return string.Concat("https://", trimmed.AsSpan("wss://".Length));

        if (trimmed.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            return string.Concat("http://", trimmed.AsSpan("ws://".Length));

        return trimmed;
    }

    private static bool IsReady(LiveKitOptions keys, StorageOptions bucket) =>
        !string.IsNullOrWhiteSpace(keys.ApiKey)
        && !string.IsNullOrWhiteSpace(keys.ApiSecret)
        && !string.IsNullOrWhiteSpace(keys.Url)
        && bucket.IsConfigured;

    /// <summary>
    /// FAQAT LiveKit kalitlari bormi.
    ///
    /// ★ <see cref="IsReady"/> DAN FARQI ATAYLAB: u "yozuv boshlash uchun
    /// tayyormi" degan savolga javob beradi va shuning uchun omborni ham
    /// talab qiladi (Egress faylni O'ZI yozadi). Holatni O'QIYDIGAN
    /// chaqiruvlar esa hech narsa yozmaydi — ularga ombor kerak emas va
    /// uni talab qilish sun'iy to'siq bo'lardi.
    /// </summary>
    private static bool HasKeys(LiveKitOptions keys) =>
        !string.IsNullOrWhiteSpace(keys.ApiKey)
        && !string.IsNullOrWhiteSpace(keys.ApiSecret)
        && !string.IsNullOrWhiteSpace(keys.Url);

    // ================================================================= so'rov tanasi

    /// <summary>
    /// <c>StartRoomCompositeEgress</c> so'rovi.
    ///
    /// ⚠️ MAYDON NOMLARI <c>snake_case</c> — LiveKit protojson AYNAN proto
    /// nomlarini qabul qiladi va bu versiyalar orasida barqaror. Bizning
    /// global <c>JsonSerializerOptions</c> (camelCase, enum satr) bu yerga
    /// UMUMAN tegmasligi kerak — shuning uchun tana <c>Utf8JsonWriter</c>
    /// bilan bayt darajasida yoziladi (<c>LiveKitTokenService</c> tokenni
    /// AYNAN shu sababdan qo'lda yozgani bilan bir xil).
    /// </summary>
    private static byte[] BuildStartPayload(EgressStartRequest request, StorageOptions bucket)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 512);

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();

            json.WriteString("room_name", request.RoomName);

            // `grid` — barcha ishtirokchi teng katakchalarda. `speaker`
            // (faol so'zlovchi katta) dars uchun jozibaliroq ko'rinardi,
            // lekin u ustoz jim turganda ekranni o'quvchilar orasida
            // sakratib yuboradi. Dars yozuvida BARQARORLIK muhimroq.
            json.WriteString("layout", "grid");

            json.WriteStartArray("file_outputs");
            json.WriteStartObject();

            json.WriteString("file_type", "MP4");

            // 🔴 KALIT BIZ TANLAYMIZ va u bazadagi `ObjectKey` bilan AYNI.
            // Shablon (`{room_name}`, `{time}`) ATAYLAB ISHLATILMAYDI:
            // shablon bilan haqiqiy nom faqat yozuv tugagach ma'lum
            // bo'lardi va webhook yo'qolsa faylni topib bo'lmasdi.
            json.WriteString("filepath", request.ObjectKey);

            // Egress standart holatda fayl yonida `.json` manifest yozadi.
            // U bizga kerak emas va omborda "yetim" obyekt bo'lib qolardi.
            json.WriteBoolean("disable_manifest", true);

            json.WriteStartObject("s3");
            json.WriteString("access_key", bucket.AccessKey);
            json.WriteString("secret", bucket.SecretKey);
            json.WriteString("region", bucket.Region);
            json.WriteString("bucket", bucket.Bucket);

            // ⚠️ ICHKI manzil (`Storage:ServiceUrl`) — Egress konteyneri
            // brauzer emas: u omborga Docker tarmog'i ichidan yozadi.
            json.WriteString("endpoint", bucket.ServiceUrl);

            // MinIO va R2 ikkalasi ham path-style bilan ishlaydi
            // (`R2SubmissionStorage` ham AYNAN shunday manzil quradi).
            // Virtual-host uslubi MinIO'da DNS talab qilardi.
            json.WriteBoolean("force_path_style", true);

            json.WriteEndObject();     // s3
            json.WriteEndObject();     // file_outputs[0]
            json.WriteEndArray();      // file_outputs

            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// <c>StartTrackEgress</c> so'rovi — BITTA trek, qayta kodlashsiz.
    ///
    /// ★ TANA ATAYLAB QISQA: <c>file_type</c> YO'Q va bo'lishi ham mumkin
    /// emas — trek egress'i konteynerni trekning O'ZIDAN oladi (VP8 →
    /// <c>.webm</c>, H.264 → <c>.mp4</c>, Opus → <c>.ogg</c>). Fayl turini
    /// bu yerda "tanlash" qayta kodlashni talab qilardi, ya'ni butun
    /// tejamkorlikni yo'q qilardi.
    ///
    /// ⚠️ CHIQISH — <c>file</c> OBYEKTI, <c>file_outputs</c> MASSIVI EMAS
    /// (proto: <c>TrackEgressRequest.output</c> — <c>DirectFileOutput</c>).
    /// Massiv yuborilsa LiveKit so'rovni "chiqish belgilanmagan" deb rad
    /// etadi.
    ///
    /// Maydon nomlari <c>snake_case</c> — sabab
    /// <see cref="BuildStartPayload"/> izohida.
    /// </summary>
    private static byte[] BuildTrackStartPayload(
        TrackEgressStartRequest request, StorageOptions bucket)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 512);

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();

            json.WriteString("room_name", request.RoomName);
            json.WriteString("track_id", request.TrackId);

            json.WriteStartObject("file");

            // 🔴 KALITNI BIZ TANLAYMIZ — sabab `BuildStartPayload` da.
            // Bu yerda u yanada muhimroq: bitta darsda o'nlab bo'lak
            // bo'lishi mumkin va shablonli nom bilan qaysi fayl qaysi
            // bo'lakka tegishli ekanini keyin aniqlab bo'lmasdi.
            json.WriteString("filepath", request.ObjectKey);
            json.WriteBoolean("disable_manifest", true);

            WriteStorageTarget(json, bucket);

            json.WriteEndObject();     // file

            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// <c>StartRoomCompositeEgress</c> so'rovi — FAQAT OVOZ
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 BU METOD <see cref="BuildStartPayload"/> NING REJIMI EMAS VA
    /// HECH QACHON U BILAN BIRLASHTIRILMAYDI.
    ///
    /// Ikkala tana AYNI LiveKit metodiga boradi, lekin uchta maydon
    /// Egress'ning MANBA TANLASHIGA ta'sir qiladi — ya'ni dars boshida
    /// Chrome ishga tushadimi yoki yo'qmi:
    ///
    ///   • <c>audio_only: true</c> — SDK manbasining SHARTI;
    ///   • <c>custom_base_url</c> — UMUMAN yozilmaydi. Shablon manzilini
    ///     faqat brauzer chiza oladi, ya'ni bu maydonning har qanday
    ///     qiymati web manbasini majburlaydi. Bo'sh satr ham YARAMAYDI —
    ///     kalitning O'ZI bo'lmasligi kerak;
    ///   • <c>layout</c> — UMUMAN yozilmaydi. Joylashuv CHIZILGAN
    ///     sahifaning xossasi; ovozda joylashuv degan tushuncha yo'q.
    ///     Eski quvur bu yerda <c>"grid"</c> yuboradi.
    ///
    /// ⚠️ AGAR BU TANA BIR KUNI <c>layout</c> YOKI <c>custom_base_url</c>
    /// BILAN CHIQSA, NOSOZLIK JIMGINA BO'LADI: yozuv baribir ishlaydi,
    /// fayl ham chiqadi — faqat har dars uchun bitta Chrome ko'tariladi va
    /// serverda bir vaqtda oltita dars o'rniga bittasi sig'adi. Aynan shu
    /// sababdan buni tekshiradigan test bor
    /// (<c>LiveKitEgressPayloadTests</c>) va u shu izohning MASHINA
    /// TEKSHIRADIGAN nusxasi.
    ///
    /// ★ <c>OGG</c> (Opus): SFU allaqachon Opus tashiydi, ya'ni bu eng
    /// arzon yo'l; ffmpeg uni muammosiz o'qiydi. MP4/AAC tanlansak,
    /// mikser ovozni yana bir marta boshqa kodekka o'girardi.
    /// </summary>
    private static byte[] BuildRoomAudioStartPayload(
        RoomAudioEgressStartRequest request, StorageOptions bucket)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 512);

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();

            json.WriteString("room_name", request.RoomName);
            json.WriteBoolean("audio_only", true);

            json.WriteStartArray("file_outputs");
            json.WriteStartObject();

            json.WriteString("file_type", "OGG");
            json.WriteString("filepath", request.ObjectKey);
            json.WriteBoolean("disable_manifest", true);

            WriteStorageTarget(json, bucket);

            json.WriteEndObject();     // file_outputs[0]
            json.WriteEndArray();      // file_outputs

            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Egress'ga beriladigan S3 manzili va kalitlari.
    ///
    /// ⚠️ <see cref="BuildStartPayload"/> BU YORDAMCHIGA O'TKAZILMADI VA
    /// O'TKAZILMAYDI: u ishlab turgan ESKI quvurning tanasi va bu SPEC'da
    /// unga tegish TAQIQLANGAN. Uchta nusxa o'rniga ikkita — bu ongli
    /// murosa: eski tana muzlatilgan, yangilari esa bitta joydan o'qiladi.
    /// </summary>
    private static void WriteStorageTarget(Utf8JsonWriter json, StorageOptions bucket)
    {
        json.WriteStartObject("s3");
        json.WriteString("access_key", bucket.AccessKey);
        json.WriteString("secret", bucket.SecretKey);
        json.WriteString("region", bucket.Region);
        json.WriteString("bucket", bucket.Bucket);

        // ICHKI manzil — Egress konteyneri omborga Docker tarmog'i ichidan
        // yozadi (batafsil sabab `BuildStartPayload` da).
        json.WriteString("endpoint", bucket.ServiceUrl);
        json.WriteBoolean("force_path_style", true);

        json.WriteEndObject();
    }

    /// <summary>
    /// Egress uchun HS256 JWT: <c>video: { roomRecord: true }</c>.
    ///
    /// ⚠️ Grant kalitlari AYNAN camelCase bo'lishi shart — LiveKit
    /// noto'g'ri shakldagi tokenni XATO BERMASDAN rad etadi (batafsil
    /// sabab: <c>LiveKitTokenService</c>).
    /// </summary>
    private static string CreateEgressToken(LiveKitOptions keys)
    {
        var now = DateTimeOffset.UtcNow;

        var payload = new ArrayBufferWriter<byte>(initialCapacity: 256);

        using (var json = new Utf8JsonWriter(payload))
        {
            json.WriteStartObject();

            json.WriteString("iss", keys.ApiKey);

            // `sub` — Egress API uchun ahamiyatsiz, lekin LiveKit tokenda
            // identity bo'lishini kutadi. Barqaror texnik nom qo'yamiz:
            // server jurnalida chaqiruv MANBAI ko'rinib tursin.
            json.WriteString("sub", "zinnur-api");

            json.WriteNumber("nbf", now.ToUnixTimeSeconds());
            json.WriteNumber("exp", now.Add(TokenTtl).ToUnixTimeSeconds());

            json.WriteStartObject("video");
            json.WriteBoolean("roomRecord", true);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return SignJwt(keys.ApiSecret, payload.WrittenSpan);
    }

    /// <summary>
    /// Xona holatini O'QISH uchun HS256 JWT:
    /// <c>video: { roomAdmin: true, room: &lt;nom&gt; }</c>.
    ///
    /// 🔴 NIMA UCHUN EGRESS TOKENI YARAMAYDI: <c>roomRecord</c> —
    /// "yozuvni boshqarish" granti, <c>livekit.RoomService</c> esa
    /// <c>roomAdmin</c> talab qiladi. Noto'g'ri grantli token oddiy
    /// Twirp xatosi bilan qaytadi va tiklash job'i "xonada trek yo'q"
    /// degan xulosaga kelmasligi uchun bu holat aynan XATO deb
    /// qaytariladi (<see cref="LiveKitTrackListResult"/>).
    ///
    /// ★ <c>room</c> AYNAN SHU XONAGA CHEKLANADI. Grantni xonasiz
    /// berish token o'g'irlansa BUTUN serverga admin huquqi degani
    /// bo'lardi; bu yerda esa u 5 daqiqa yashaydi va bitta darsni
    /// ko'rsatadi.
    ///
    /// ⚠️ Grant kalitlari AYNAN camelCase — sabab
    /// <see cref="CreateEgressToken"/> da.
    /// </summary>
    private static string CreateRoomAdminToken(LiveKitOptions keys, string room)
    {
        var now = DateTimeOffset.UtcNow;

        var payload = new ArrayBufferWriter<byte>(initialCapacity: 256);

        using (var json = new Utf8JsonWriter(payload))
        {
            json.WriteStartObject();

            json.WriteString("iss", keys.ApiKey);
            json.WriteString("sub", "zinnur-api");

            json.WriteNumber("nbf", now.ToUnixTimeSeconds());
            json.WriteNumber("exp", now.Add(TokenTtl).ToUnixTimeSeconds());

            json.WriteStartObject("video");
            json.WriteBoolean("roomAdmin", true);
            json.WriteString("room", room);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return SignJwt(keys.ApiSecret, payload.WrittenSpan);
    }

    /// <summary>
    /// Tayyor da'volarni HS256 bilan imzolab, JWT satriga aylantiradi.
    ///
    /// ★ FAQAT IMZO QISMI UMUMIY. Da'volarni har token o'zi yozadi:
    /// grantlar orasidagi farq — bu tokenlarning YAGONA farqi va u
    /// chaqiruv joyidan ko'rinib turishi kerak. Grantni parametrga
    /// chiqarish (masalan <c>bool roomAdmin</c>) o'sha farqni mantiqiy
    /// bayroqqa aylantirardi.
    /// </summary>
    private static string SignJwt(string secret, ReadOnlySpan<byte> claims)
    {
        var signingInput = string.Concat(
            EncodedHeader, ".", Base64Url.EncodeToString(claims));

        Span<byte> signature = stackalloc byte[HMACSHA256.HashSizeInBytes];

        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(signingInput),
            signature);

        return string.Concat(signingInput, ".", Base64Url.EncodeToString(signature));
    }

    // ================================================================= javob

    /// <summary>
    /// <c>EgressInfo.egress_id</c>. Ikkala nom bilan qidiriladi — sabab
    /// <c>LiveKitWebhookParser</c> izohida (protojson versiyaga qarab
    /// <c>snake_case</c> yoki <c>camelCase</c> yozadi).
    /// </summary>
    private static string? ReadEgressId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in (string[])["egress_id", "egressId"])
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Javob JSON emas — chaqiruvchi buni "Id yo'q" deb ko'radi.
        }

        return null;
    }

    /// <summary>
    /// <c>ListParticipantsResponse</c> → e'lon qilingan treklarning YASSI
    /// ro'yxati.
    ///
    /// 🔴 <c>null</c> = JAVOBNI O'QIB BO'LMADI, bo'sh ro'yxat = XONA BO'SH.
    /// Bu farq shu metodning butun mas'uliyati: ikkalasini bir xil
    /// qaytarsak, buzilgan javob "xonada hech kim yo'q" degan ma'noni
    /// olardi va tiklash job'i mavjud egress ustiga yangisini
    /// qo'shardi.
    ///
    /// ⚠️ MAYDONLAR IKKI NOM BILAN qidiriladi (protojson
    /// <c>snake_case</c> ham, <c>camelCase</c> ham yozadi) — sabab
    /// <c>LiveKitWebhookParser</c> izohida.
    ///
    /// Identity yoki trek Id'si bo'sh bo'lgan yozuvlar TASHLAB
    /// KETILADI: ularni "noma'lum" deb qatorga yozish omborda hech qachon
    /// paydo bo'lmaydigan faylni kutishga olib kelardi.
    /// </summary>
    private static List<LiveKitPublishedTrackDto>? ReadPublishedTracks(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("participants", out var participants)
                || participants.ValueKind != JsonValueKind.Array)
            {
                // Xona bo'sh bo'lsa LiveKit maydonni umuman yubormaydi.
                return [];
            }

            var tracks = new List<LiveKitPublishedTrackDto>();

            foreach (var participant in participants.EnumerateArray())
            {
                if (participant.ValueKind != JsonValueKind.Object)
                    continue;

                var identity = TextOf(participant, "identity");

                if (string.IsNullOrWhiteSpace(identity))
                    continue;

                if (!participant.TryGetProperty("tracks", out var published)
                    || published.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var track in published.EnumerateArray())
                {
                    if (track.ValueKind != JsonValueKind.Object)
                        continue;

                    var sid = TextOf(track, "sid");

                    if (string.IsNullOrWhiteSpace(sid))
                        continue;

                    tracks.Add(new LiveKitPublishedTrackDto(
                        identity,
                        sid,
                        TextOf(track, "source"),
                        TextOf(track, "mime_type", "mimeType")));
                }
            }

            return tracks;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>ListEgressResponse</c> → egress'lar ro'yxati.
    ///
    /// 🔴 <see cref="ReadPublishedTracks"/> BILAN AYNI QOIDA VA U YERDAGIDAN
    /// HAM MUHIMROQ: bu ro'yxatning bo'shligi "mikser o'lgan" degan
    /// ma'noni bildiradi va chaqiruvchi o'shanda yangi mikser
    /// ko'taradi. Buzilgan javobni bo'sh ro'yxat deb ko'rsatish darsda
    /// ikkita ovoz fayli degani.
    /// </summary>
    private static List<LiveKitEgressInfoDto>? ReadEgressItems(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                // Faol egress bo'lmasa LiveKit maydonni yubormaydi.
                return [];
            }

            var result = new List<LiveKitEgressInfoDto>();

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var egressId = TextOf(item, "egress_id", "egressId");

                if (string.IsNullOrWhiteSpace(egressId))
                    continue;

                result.Add(new LiveKitEgressInfoDto(egressId, TextOf(item, "status")));
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Satr maydonni bir NECHTA nom bo'yicha qidiradi (protojson
    /// <c>snake_case</c> / <c>camelCase</c> ikkiligi).
    /// </summary>
    private static string? TextOf(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>Twirp xatosidagi <c>msg</c> — xodimga ko'rsatiladigan qism.</summary>
    private static string TwirpMessage(string? body, int status)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("msg", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && message.GetString() is { Length: > 0 } text)
                {
                    return Trim("Yozuv xizmati rad etdi: " + text);
                }
            }
            catch (JsonException)
            {
                // Tana JSON emas — pastdagi umumiy matn ishlatiladi.
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture, $"Yozuv xizmati rad etdi (HTTP {status}).");
    }

    private static string Trim(string value) =>
        value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    /// <summary>Twirp chaqiruvining natijasi (istisno o'rniga — izoh yuqorida).</summary>
    private readonly record struct TwirpResponse(bool Succeeded, string? Body, string? Error)
    {
        public static TwirpResponse Ok(string? body) => new(true, body, null);

        public static TwirpResponse Fail(string error) => new(false, null, error);
    }
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848).
///
/// ★ NIMA UCHUN <c>RecordingLog</c> QAYTA ISHLATILMAYDI: u
/// <c>Zinnur.Application</c> ichida <c>internal</c> va boshqa
/// assembly'dan ko'rinmaydi. Uni ochish esa Application qatlamining
/// ichki logini tashqariga chiqarardi.
///
/// EventId makoni: <c>6600–6619</c>.
///
/// 🔴 OMBOR KALITLARI VA TOKEN HECH QACHON YOZILMAYDI — log Sentry'ga va
/// konteyner chiqishiga ketadi.
/// </summary>
internal static partial class EgressLog
{
    [LoggerMessage(
        EventId = 6600,
        Level = LogLevel.Information,
        Message = "Egress boshlandi: xona={RoomName} egress={EgressId} kalit={ObjectKey}")]
    internal static partial void Started(
        ILogger logger, string roomName, string egressId, string objectKey);

    [LoggerMessage(
        EventId = 6601,
        Level = LogLevel.Error,
        Message = "Egress javobida identifikator yo'q: xona={RoomName}")]
    internal static partial void StartWithoutId(ILogger logger, string roomName);

    [LoggerMessage(
        EventId = 6602,
        Level = LogLevel.Error,
        Message = "Egress API'ga ulanish xatosi: metod={Method}")]
    internal static partial void CallFailed(ILogger logger, Exception exception, string method);

    [LoggerMessage(
        EventId = 6603,
        Level = LogLevel.Error,
        Message = "Egress API javob bermadi (timeout): metod={Method}")]
    internal static partial void CallTimedOut(ILogger logger, Exception exception, string method);

    [LoggerMessage(
        EventId = 6604,
        Level = LogLevel.Error,
        Message = "Egress API rad etdi: metod={Method} status={Status} javob={Body}")]
    internal static partial void CallRejected(
        ILogger logger, string method, int status, string body);

    [LoggerMessage(
        EventId = 6605,
        Level = LogLevel.Warning,
        Message = "Egress'ni to'xtatib bo'lmadi (allaqachon tugagan bo'lishi mumkin): "
                  + "egress={EgressId} sabab={Reason}")]
    internal static partial void StopRejected(ILogger logger, string egressId, string reason);

    // ───────────────────────── yangi quvur (TrackComposition) ─────────────

    [LoggerMessage(
        EventId = 6606,
        Level = LogLevel.Information,
        Message = "Trek yozuvi boshlandi: xona={RoomName} trek={TrackSid} "
                  + "egress={EgressId} kalit={ObjectKey}")]
    internal static partial void TrackStarted(
        ILogger logger, string roomName, string trackSid, string egressId, string objectKey);

    [LoggerMessage(
        EventId = 6607,
        Level = LogLevel.Error,
        Message = "Trek egress javobida identifikator yo'q: xona={RoomName} trek={TrackSid}")]
    internal static partial void TrackStartWithoutId(
        ILogger logger, string roomName, string trackSid);

    [LoggerMessage(
        EventId = 6608,
        Level = LogLevel.Information,
        Message = "Xona ovozi yozuvi boshlandi: xona={RoomName} egress={EgressId} "
                  + "kalit={ObjectKey}")]
    internal static partial void RoomAudioStarted(
        ILogger logger, string roomName, string egressId, string objectKey);

    /// <summary>
    /// 🔴 Bu xato darsning BUTUN ovozini yo'qotadi — bitta video
    /// bo'lagining yo'qolishidan farqli o'laroq.
    /// </summary>
    [LoggerMessage(
        EventId = 6609,
        Level = LogLevel.Error,
        Message = "Xona ovozi egress javobida identifikator yo'q: xona={RoomName}")]
    internal static partial void RoomAudioStartWithoutId(ILogger logger, string roomName);

    /// <summary>
    /// ⚠️ `Warning`: bu chaqiruv har 60 soniyada takrorlanadi va uni
    /// `Error` qilish LiveKit bir daqiqaga yo'qolganda log'ni to'ldirardi.
    /// </summary>
    [LoggerMessage(
        EventId = 6610,
        Level = LogLevel.Warning,
        Message = "Xona ishtirokchilarini o'qib bo'lmadi: xona={RoomName} sabab={Reason}")]
    internal static partial void ParticipantsUnreadable(
        ILogger logger, string roomName, string reason);

    /// <inheritdoc cref="ParticipantsUnreadable" />
    [LoggerMessage(
        EventId = 6611,
        Level = LogLevel.Warning,
        Message = "Faol egress ro'yxatini o'qib bo'lmadi: xona={RoomName} sabab={Reason}")]
    internal static partial void ActiveEgressUnreadable(
        ILogger logger, string roomName, string reason);

    /// <summary>
    /// Javob 200 bilan keldi, lekin uni o'qib bo'lmadi. Chaqiruvchi buni
    /// XATO deb ko'radi — bo'sh ro'yxat deb EMAS (sabab
    /// <c>ILiveKitRoomQuery</c> izohida).
    /// </summary>
    [LoggerMessage(
        EventId = 6612,
        Level = LogLevel.Warning,
        Message = "Egress API javobini o'qib bo'lmadi: metod={Method} xona={RoomName}")]
    internal static partial void ListResponseMalformed(
        ILogger logger, string method, string roomName);
}
