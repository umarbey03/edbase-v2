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
/// <see cref="ILiveKitEgress"/> — LiveKit Egress API (Twirp/JSON)
/// ════════════════════════════════════════════════════════════════════════
///
/// ── PROTOKOL ────────────────────────────────────────────────────────────
///
/// LiveKit server API'si — Twirp: oddiy <c>POST</c>, yo'l
/// <c>/twirp/livekit.Egress/&lt;Metod&gt;</c>, tana JSON. Ya'ni SDK ham,
/// gRPC ham kerak emas (<c>R2SubmissionStorage</c> AWS SDK'siz ishlagani
/// bilan AYNI mulohaza: bizga IKKI amal kerak, ular esa oddiy HTTP).
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
    ILogger<LiveKitEgressClient> logger) : ILiveKitEgress
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

    // ================================================================= Twirp

    /// <summary>
    /// Twirp chaqiruvi: <c>POST {base}/twirp/livekit.Egress/{method}</c>.
    ///
    /// ★ ISTISNO CHIQMAYDI — natija qaytadi. Sabab port izohida: yozuvning
    /// boshlanmasligi DARSNI to'xtatmasligi shart.
    /// </summary>
    private async Task<TwirpResponse> CallAsync(
        string method, byte[] payload, LiveKitOptions keys, CancellationToken ct)
    {
        Uri uri;

        try
        {
            uri = new Uri(BaseUrl(keys.Url) + "/twirp/livekit.Egress/" + method);
        }
        catch (UriFormatException)
        {
            return TwirpResponse.Fail("LiveKit manzili noto'g'ri (`LiveKit:Url`).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        using var content = new ByteArrayContent(payload);

        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Content = content;

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateEgressToken(keys));

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

        var signingInput = string.Concat(
            EncodedHeader, ".", Base64Url.EncodeToString(payload.WrittenSpan));

        Span<byte> signature = stackalloc byte[HMACSHA256.HashSizeInBytes];

        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(keys.ApiSecret),
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
}
