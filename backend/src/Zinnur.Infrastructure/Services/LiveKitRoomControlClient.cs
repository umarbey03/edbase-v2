using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ILiveKitRoomControl"/> — LiveKit RoomService API (Twirp/JSON).
///
/// ── PROTOKOL ────────────────────────────────────────────────────────────
/// <c>POST {base}/twirp/livekit.RoomService/&lt;Metod&gt;</c>, tana JSON —
/// <see cref="LiveKitEgressClient"/> bilan AYNI naqsh (SDK ham, gRPC ham
/// kerak emas: bizga ikkita amal kerak).
///
/// ── IKKI QADAM ──────────────────────────────────────────────────────────
/// <c>MutePublishedTrack</c> trek SID'ini so'raydi, brauzer esa SID'ni
/// bilmasligi mumkin (trek qayta e'lon qilinganda SID almashadi). Shuning
/// uchun avval <c>GetParticipant</c> bilan ishtirokchining treklari
/// o'qiladi va manba (<c>MICROPHONE</c>/<c>CAMERA</c>) bo'yicha SID
/// topiladi. Ikki so'rov — ikkalasi ham Docker tarmog'i ichida, millisekund.
///
/// ── TOKEN ───────────────────────────────────────────────────────────────
/// <c>video: { roomAdmin: true, room: "&lt;xona&gt;" }</c> — FAQAT shu xona
/// uchun va umri 5 daqiqa. <c>LiveKitTokenService</c> qayta ishlatilmaydi:
/// u KIRISH tokenini yasaydi (<c>roomJoin</c> + identity), bu esa boshqa
/// grant (Egress klientidagi mulohaza bilan bir xil).
/// </summary>
public sealed class LiveKitRoomControlClient(
    IHttpClientFactory httpClientFactory,
    IRuntimeOptions<LiveKitOptions> liveKit,
    ILogger<LiveKitRoomControlClient> logger) : ILiveKitRoomControl
{
    /// <summary>Nomlangan HTTP klient (timeout DI'da sozlanadi).</summary>
    public const string HttpClientName = "zinnur-livekit-room";

    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(5);

    private static readonly string EncodedHeader =
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));

    private const int MaxErrorLength = 300;

    /// <inheritdoc />
    public async Task<RoomControlResult> MuteTrackAsync(
        string roomName, string identity, ParticipantMediaSource source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        // Kesim BIR MARTA — token va so'rov AYNI juftlik bilan ketsin.
        var keys = liveKit.Current;

        if (string.IsNullOrWhiteSpace(keys.ApiKey)
            || string.IsNullOrWhiteSpace(keys.ApiSecret)
            || string.IsNullOrWhiteSpace(keys.Url))
        {
            return RoomControlResult.Fail("Video xizmati sozlanmagan (`LiveKit:*`).");
        }

        // 1-QADAM: ishtirokchi va uning treklari.
        var participant = await CallAsync(
            "GetParticipant", BuildParticipantPayload(roomName, identity), keys, roomName, ct)
            .ConfigureAwait(false);

        if (!participant.Succeeded)
            return RoomControlResult.Fail(participant.Error!);

        var track = FindTrack(participant.Body, source);

        if (track is null)
        {
            // Trek yo'q — o'quvchi allaqachon o'chirgan yoki hech qachon
            // yoqmagan. Bu XATO emas, natija allaqachon kerakli holatda.
            RoomControlLog.TrackAbsent(logger, roomName, identity, source);

            return RoomControlResult.Ok();
        }

        if (track.Value.Muted)
            return RoomControlResult.Ok();

        // 2-QADAM: o'chirish.
        var muted = await CallAsync(
            "MutePublishedTrack", BuildMutePayload(roomName, identity, track.Value.Sid), keys, roomName, ct)
            .ConfigureAwait(false);

        if (!muted.Succeeded)
            return RoomControlResult.Fail(muted.Error!);

        RoomControlLog.Muted(logger, roomName, identity, source);

        return RoomControlResult.Ok();
    }

    // ================================================================= Twirp

    private async Task<TwirpResponse> CallAsync(
        string method, byte[] payload, LiveKitOptions keys, string roomName, CancellationToken ct)
    {
        Uri uri;

        try
        {
            uri = new Uri(BaseUrl(keys.Url) + "/twirp/livekit.RoomService/" + method);
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
            new AuthenticationHeaderValue("Bearer", CreateAdminToken(keys, roomName));

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            RoomControlLog.CallFailed(logger, ex, method);

            return TwirpResponse.Fail("Video xizmatiga ulanib bo'lmadi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            RoomControlLog.CallTimedOut(logger, ex, method);

            return TwirpResponse.Fail("Video xizmati javob bermadi (timeout).");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return TwirpResponse.Ok(body);

            RoomControlLog.CallRejected(logger, method, (int)response.StatusCode, Trim(body));

            return TwirpResponse.Fail(TwirpMessage(body, (int)response.StatusCode));
        }
    }

    /// <summary><c>ws(s)://</c> → <c>http(s)://</c> — sabab <see cref="LiveKitEgressClient"/> da.</summary>
    private static string BaseUrl(string url)
    {
        var trimmed = url.TrimEnd('/');

        if (trimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return string.Concat("https://", trimmed.AsSpan("wss://".Length));

        if (trimmed.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            return string.Concat("http://", trimmed.AsSpan("ws://".Length));

        return trimmed;
    }

    // ================================================================= so'rov tanasi

    /// <summary>⚠️ Maydon nomlari <c>snake_case</c> — protojson (Egress klientidagi izoh).</summary>
    private static byte[] BuildParticipantPayload(string roomName, string identity)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 128);

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("room", roomName);
            json.WriteString("identity", identity);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildMutePayload(string roomName, string identity, string trackSid)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 160);

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("room", roomName);
            json.WriteString("identity", identity);
            json.WriteString("track_sid", trackSid);
            json.WriteBoolean("muted", true);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// HS256 JWT: <c>video: { roomAdmin: true, room }</c>. Grant kalitlari
    /// AYNAN camelCase (sabab: <c>LiveKitTokenService</c>).
    /// </summary>
    private static string CreateAdminToken(LiveKitOptions keys, string roomName)
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
            json.WriteString("room", roomName);
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

    private readonly record struct TrackRef(string Sid, bool Muted);

    /// <summary>
    /// <c>ParticipantInfo.tracks[]</c> ichidan manbaga mos trek.
    ///
    /// ★ Enum IKKI shaklda kelishi mumkin: protojson odatda NOM yozadi
    /// (<c>"MICROPHONE"</c>), lekin ba'zi versiyalar/sozlamalar RAQAM
    /// (<c>2</c>) qaytaradi. Ikkalasi ham qabul qilinadi — aks holda
    /// server yangilanganda tugma jimgina ishlamay qolardi.
    /// Maydon nomlari ham ikki shaklda (<c>snake_case</c>/<c>camelCase</c>)
    /// bo'lishi mumkin; bu javobda faqat bir so'zli nomlar bor
    /// (<c>sid</c>, <c>source</c>, <c>muted</c>, <c>tracks</c>) — muammo yo'q.
    /// </summary>
    private static TrackRef? FindTrack(string? body, ParticipantMediaSource source)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        // livekit.TrackSource: CAMERA = 1, MICROPHONE = 2
        var (wantName, wantNumber) = source switch
        {
            ParticipantMediaSource.Camera => ("CAMERA", 1),
            _ => ("MICROPHONE", 2),
        };

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tracks", out var tracks)
                || tracks.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var track in tracks.EnumerateArray())
            {
                if (!track.TryGetProperty("source", out var src))
                    continue;

                var matches = src.ValueKind switch
                {
                    JsonValueKind.String => string.Equals(src.GetString(), wantName, StringComparison.Ordinal),
                    JsonValueKind.Number => src.TryGetInt32(out var n) && n == wantNumber,
                    _ => false,
                };

                if (!matches)
                    continue;

                if (!track.TryGetProperty("sid", out var sid) || sid.ValueKind != JsonValueKind.String)
                    continue;

                var muted = track.TryGetProperty("muted", out var m) && m.ValueKind == JsonValueKind.True;

                return new TrackRef(sid.GetString()!, muted);
            }
        }
        catch (JsonException)
        {
            // Javob JSON emas — "trek yo'q" deb qaraladi.
        }

        return null;
    }

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
                    // Eng ko'p uchraydigan holat — o'quvchi xonadan chiqib ketgan.
                    // LiveKit matni: "participant does not exist" (sinovda
                    // ko'rildi, 2026-09-09); eski versiyalarda "not found".
                    if (text.Contains("participant", StringComparison.OrdinalIgnoreCase)
                        && (text.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                    {
                        return "O'quvchi hozir xonada emas.";
                    }

                    return Trim("Video xizmati rad etdi: " + text);
                }
            }
            catch (JsonException)
            {
                // Tana JSON emas — pastdagi umumiy matn.
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture, $"Video xizmati rad etdi (HTTP {status}).");
    }

    private static string Trim(string value) =>
        value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    private readonly record struct TwirpResponse(bool Succeeded, string? Body, string? Error)
    {
        public static TwirpResponse Ok(string? body) => new(true, body, null);

        public static TwirpResponse Fail(string error) => new(false, null, error);
    }
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). EventId makoni: <c>6630–6639</c>.
/// 🔴 TOKEN HECH QACHON YOZILMAYDI.
/// </summary>
internal static partial class RoomControlLog
{
    [LoggerMessage(
        EventId = 6630,
        Level = LogLevel.Information,
        Message = "Xona boshqaruvi: o'chirildi — xona={Room}, identity={Identity}, manba={Source}")]
    internal static partial void Muted(
        ILogger logger, string room, string identity, ParticipantMediaSource source);

    [LoggerMessage(
        EventId = 6631,
        Level = LogLevel.Debug,
        Message = "Xona boshqaruvi: trek yo'q — xona={Room}, identity={Identity}, manba={Source}")]
    internal static partial void TrackAbsent(
        ILogger logger, string room, string identity, ParticipantMediaSource source);

    [LoggerMessage(
        EventId = 6632,
        Level = LogLevel.Warning,
        Message = "Xona boshqaruvi: LiveKit rad etdi — metod={Method}, HTTP {Status}: {Body}")]
    internal static partial void CallRejected(ILogger logger, string method, int status, string body);

    [LoggerMessage(
        EventId = 6633,
        Level = LogLevel.Error,
        Message = "Xona boshqaruvi: LiveKit'ga ulanib bo'lmadi — metod={Method}")]
    internal static partial void CallFailed(ILogger logger, Exception exception, string method);

    [LoggerMessage(
        EventId = 6634,
        Level = LogLevel.Warning,
        Message = "Xona boshqaruvi: LiveKit javob bermadi (timeout) — metod={Method}")]
    internal static partial void CallTimedOut(ILogger logger, Exception exception, string method);
}
