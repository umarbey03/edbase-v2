using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Zinnur.Application.Recordings.Dtos;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// LIVEKIT WEBHOOK JSON'INI O'QIYDI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN <c>JsonSerializer.Deserialize&lt;T&gt;</c> EMAS
///
/// Bu JSON BIZNIKI EMAS. Uni LiveKit protobuf'dan (protojson bilan)
/// yasaydi va uch xususiyati bizning global sozlamalarimizga UMUMAN
/// mos kelmaydi:
///
///   1) MAYDON NOMLARI IKKI XIL. protojson sozlamasiga qarab
///      <c>egress_info</c> yoki <c>egressInfo</c> yuboradi. LiveKit
///      versiyalari (va server konfiguratsiyasi) orasida bu FARQ QILADI.
///      Bitta nomga tayanish "yangilangandan keyin yozuvlar jimgina
///      to'xtadi" turkumidagi nosozlik demakdir.
///
///   2) <c>int64</c> MAYDONLAR SATR BO'LIB KELADI. protobuf JSON
///      xaritalash qoidasi: 64-bitli sonlar JSON'da SATR sifatida
///      yoziladi (<c>"size": "512345678"</c>), chunki JavaScript ularni
///      aniq saqlay olmaydi. `long` deb kutilgan DTO shu yerda 400 bilan
///      yiqilardi.
///
///   3) VAQT — UNIX NANOSEKUND. `EgressInfo.started_at` sekund ham,
///      millisekund ham emas.
///
/// Global <c>JsonSerializerOptions</c> ga (enum'lar satr, camelCase)
/// tayanish esa alohida xavf: u BIZNING API shartnomamiz uchun sozlangan
/// va istalgan payt o'zgarishi mumkin — o'sha o'zgarish jimgina
/// webhook'ni buzardi.
///
/// Shuning uchun bu yerda har maydon OSHKOR, ikkala nom bilan va tur
/// tekshiruvi bilan o'qiladi. Noma'lum maydonlar e'tiborsiz qoldiriladi —
/// LiveKit yangi maydon qo'shsa hech narsa buzilmasin.
/// </summary>
public static class LiveKitWebhookParser
{
    /// <summary>
    /// Tanani o'qiydi. Yaroqsiz JSON — <c>null</c> (chaqiruvchi buni
    /// <c>Malformed</c> ga aylantiradi va LiveKit'ga baribir 200 beradi:
    /// buzuq hodisani qayta yuborishning ma'nosi yo'q).
    /// </summary>
    public static LiveKitWebhookEventDto? Parse(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty) return null;

        JsonDocument document;

        try
        {
            var reader = new Utf8JsonReader(body);
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var eventName = Text(root, "event") ?? string.Empty;

            // ★ IDEMPOTENTLIK KALITI. LiveKit `id` beradi (`EV_…`), lekin
            //   u yo'q bo'lgan holat ham bo'lishi mumkin — o'shanda TANANING
            //   xeshi ishlatiladi: bir xil tana ikki marta kelsa baribir
            //   to'siladi. Kalitsiz qolish esa takrorni umuman to'smaslik
            //   degani bo'lardi.
            var eventId = Text(root, "id");

            if (string.IsNullOrWhiteSpace(eventId))
                eventId = "sha256:" + Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

            var info = Child(root, "egress_info", "egressInfo");

            if (info is null)
            {
                // Yozuvga aloqasi yo'q hodisa (`room_started`,
                // `participant_joined` va h.k.) — bu XATO EMAS.
                return new LiveKitWebhookEventDto(
                    eventId, eventName, null,
                    RoomNameOf(root), null, null, null, null, null, null, null);
            }

            var file = FirstFile(info.Value);

            var startedAt = NanoTime(info.Value, "started_at", "startedAt");
            var endedAt = NanoTime(info.Value, "ended_at", "endedAt");

            return new LiveKitWebhookEventDto(
                EventId: eventId,
                EventName: eventName,
                EgressId: Text(info.Value, "egress_id", "egressId"),
                RoomName: Text(info.Value, "room_name", "roomName") ?? RoomNameOf(root),
                EgressStatus: Text(info.Value, "status"),
                ObjectKey: file is null ? null : Text(file.Value, "filename"),
                FileSizeBytes: file is null ? null : Int64(file.Value, "size"),
                DurationSeconds: DurationOf(file, startedAt, endedAt),
                StartedAt: startedAt,
                EndedAt: endedAt,
                Error: Text(info.Value, "error"));
        }
    }

    /// <summary>Xona nomi konvertda ham bo'lishi mumkin (`room.name`).</summary>
    private static string? RoomNameOf(JsonElement root)
    {
        var room = Child(root, "room");

        return room is null ? null : Text(room.Value, "name");
    }

    /// <summary>
    /// Yozilgan fayl haqidagi ma'lumot.
    ///
    /// ⚠️ IKKI SHAKL: yangi LiveKit <c>file_results</c> MASSIVINI, eskisi
    /// esa bitta <c>file</c> obyektini yuboradi. Ikkalasi ham qabul
    /// qilinadi — aks holda server versiyasi yangilangan kuni fayl kaliti
    /// jimgina saqlanmay qolardi.
    /// </summary>
    private static JsonElement? FirstFile(JsonElement info)
    {
        var results = Child(info, "file_results", "fileResults");

        if (results is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                    return item;
            }
        }

        var single = Child(info, "file");

        return single is { ValueKind: JsonValueKind.Object } ? single : null;
    }

    /// <summary>
    /// Videoning haqiqiy uzunligi (sekund).
    ///
    /// Birinchi manba — fayldagi <c>duration</c> (UNIX NANOSEKUND).
    /// Bo'lmasa egress boshlanish/tugash paytlari ayirmasi. Ikkalasi ham
    /// yo'q bo'lsa <c>null</c>: darsning davomiyligini video davomiyligi
    /// deb YOZIB QO'YMAYMIZ (eski tizimning aynan shu chalkashligi
    /// ro'yxatda "80 daqiqa" deb ko'rsatib, 12 daqiqalik video ochardi).
    /// </summary>
    private static int? DurationOf(JsonElement? file, DateTimeOffset? startedAt, DateTimeOffset? endedAt)
    {
        if (file is not null && Int64(file.Value, "duration") is { } nanos && nanos > 0)
            return (int)Math.Clamp(nanos / 1_000_000_000L, 0, int.MaxValue);

        if (startedAt is { } start && endedAt is { } end && end > start)
            return (int)Math.Clamp((end - start).TotalSeconds, 0, int.MaxValue);

        return null;
    }

    // ================================================================= o'qish yordamchilari

    private static JsonElement? Child(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                return value;
        }

        return null;
    }

    private static string? Text(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parent.TryGetProperty(name, out var value))
                continue;

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    /// <summary>
    /// <c>int64</c> maydon. protobuf JSON uni SATR sifatida yozadi, lekin
    /// ba'zi vositalar son bo'lib ham yuboradi — ikkalasi ham qabul qilinadi.
    /// </summary>
    private static long? Int64(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parent.TryGetProperty(name, out var value))
                continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetInt64(out var number):
                    return number;

                case JsonValueKind.String
                    when long.TryParse(
                        value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;

                default:
                    continue;
            }
        }

        return null;
    }

    /// <summary>UNIX NANOSEKUND -> <c>DateTimeOffset</c>. Nol yoki yo'q — <c>null</c>.</summary>
    private static DateTimeOffset? NanoTime(JsonElement parent, params string[] names)
    {
        if (Int64(parent, names) is not { } nanos || nanos <= 0)
            return null;

        // Millisekundga o'tkazamiz: `FromUnixTimeMilliseconds` chegaralari
        // 1970±… oralig'ida, ya'ni buzuq (juda katta) qiymat istisno
        // tashlashi mumkin — uni shu yerda to'samiz.
        var milliseconds = nanos / 1_000_000L;

        if (milliseconds is < 0 or > 253_402_300_799_000L)
            return null;

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
