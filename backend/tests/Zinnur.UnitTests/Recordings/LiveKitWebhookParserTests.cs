using System.Text;
using Zinnur.Application.Recordings.Services;

namespace Zinnur.UnitTests.Recordings;

/// <summary>
/// LiveKit webhook JSON'ini o'qish.
///
/// ★ NIMA UCHUN BU TESTLAR KERAK: bu JSON BIZNIKI EMAS va u uch joyda
/// bizning odatlarimizga MOS KELMAYDI — maydon nomlari ikki xil
/// (<c>snake_case</c> / <c>camelCase</c>), <c>int64</c> maydonlar SATR
/// bo'lib keladi, vaqt esa UNIX NANOSEKUND. Har uchtasi "LiveKit
/// yangilandi — yozuvlar jimgina to'xtadi" turkumidagi nosozlik manbai.
/// </summary>
public sealed class LiveKitWebhookParserTests
{
    private static Zinnur.Application.Recordings.Dtos.LiveKitWebhookEventDto? Parse(string json) =>
        LiveKitWebhookParser.Parse(Encoding.UTF8.GetBytes(json));

    // ================================================================= shakl

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[1,2,3]")]
    [InlineData("\"salom\"")]
    public void Parse_MalformedBody_ReturnsNull(string body) =>
        Parse(body).Should().BeNull();

    /// <summary>
    /// Yozuvga aloqasi yo'q hodisa — bu XATO EMAS. Xona nomi konvertdan
    /// olinadi, <c>EgressId</c> esa bo'sh qoladi va chaqiruvchi hodisani
    /// chetlab o'tadi.
    /// </summary>
    [Fact]
    public void Parse_NonEgressEvent_IsRecognisedButHasNoEgressId()
    {
        var result = Parse("""
            {"event":"participant_joined","id":"EV_1","room":{"name":"r-42"}}
            """);

        result.Should().NotBeNull();
        result!.EventName.Should().Be("participant_joined");
        result.EventId.Should().Be("EV_1");
        result.RoomName.Should().Be("r-42");
        result.EgressId.Should().BeNull();
    }

    // ================================================================= idempotentlik kaliti

    /// <summary>
    /// <c>id</c> yo'q bo'lsa TANA XESHI kalit bo'ladi: bir xil tana ikki
    /// marta kelsa baribir to'siladi. Kalitsiz qolish esa takrorni umuman
    /// to'smaslik degani bo'lardi.
    /// </summary>
    [Fact]
    public void Parse_WithoutEventId_FallsBackToTheBodyHash()
    {
        const string Body = """{"event":"egress_ended","egress_info":{"egress_id":"EG_1"}}""";

        var first = Parse(Body);
        var second = Parse(Body);

        first!.EventId.Should().StartWith("sha256:");
        second!.EventId.Should().Be(first.EventId, "bir xil tana -> bir xil kalit");
    }

    [Fact]
    public void Parse_DifferentBodies_ProduceDifferentFallbackKeys()
    {
        var a = Parse("""{"event":"egress_ended","egress_info":{"egress_id":"EG_1"}}""");
        var b = Parse("""{"event":"egress_ended","egress_info":{"egress_id":"EG_2"}}""");

        a!.EventId.Should().NotBe(b!.EventId);
    }

    // ================================================================= nom uslublari

    /// <summary>★ IKKALA NOM USLUBI HAM QABUL QILINADI (snake_case).</summary>
    [Fact]
    public void Parse_SnakeCase_ReadsEveryField()
    {
        var result = Parse("""
            {
              "event": "egress_ended",
              "id": "EV_9",
              "egress_info": {
                "egress_id": "EG_abc",
                "room_name": "r-7",
                "status": "EGRESS_COMPLETE",
                "started_at": "1780000000000000000",
                "ended_at":   "1780000060000000000",
                "file_results": [
                  { "filename": "recordings/2026-07/7/a.mp4", "size": "1048576", "duration": "60000000000" }
                ]
              }
            }
            """);

        result.Should().NotBeNull();
        result!.EgressId.Should().Be("EG_abc");
        result.RoomName.Should().Be("r-7");
        result.EgressStatus.Should().Be("EGRESS_COMPLETE");
        result.ObjectKey.Should().Be("recordings/2026-07/7/a.mp4");
        result.FileSizeBytes.Should().Be(1_048_576);
        result.DurationSeconds.Should().Be(60);
        result.StartedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_780_000_000));
        result.EndedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_780_000_060));
    }

    /// <summary>★ ...va camelCase — LiveKit versiyasiga qarab shu shaklda keladi.</summary>
    [Fact]
    public void Parse_CamelCase_ReadsEveryField()
    {
        var result = Parse("""
            {
              "event": "egress_ended",
              "id": "EV_9",
              "egressInfo": {
                "egressId": "EG_abc",
                "roomName": "r-7",
                "status": "EGRESS_COMPLETE",
                "startedAt": 1780000000000000000,
                "endedAt":   1780000060000000000,
                "fileResults": [ { "filename": "k.mp4", "size": 2048, "duration": 60000000000 } ]
              }
            }
            """);

        result!.EgressId.Should().Be("EG_abc");
        result.RoomName.Should().Be("r-7");
        result.ObjectKey.Should().Be("k.mp4");
        result.FileSizeBytes.Should().Be(2048);
    }

    /// <summary>
    /// Eski LiveKit bitta <c>file</c> obyektini yuboradi, yangisi
    /// <c>file_results</c> MASSIVINI. Ikkalasi ham qabul qilinadi — aks
    /// holda server yangilangan kuni fayl kaliti jimgina saqlanmay
    /// qolardi.
    /// </summary>
    [Fact]
    public void Parse_LegacySingleFileObject_IsAccepted()
    {
        var result = Parse("""
            {
              "event":"egress_ended",
              "egress_info":{
                "egress_id":"EG_old",
                "status":"EGRESS_COMPLETE",
                "file":{"filename":"eski.mp4","size":"512"}
              }
            }
            """);

        result!.ObjectKey.Should().Be("eski.mp4");
        result.FileSizeBytes.Should().Be(512);
    }

    // ================================================================= davomiylik

    /// <summary>
    /// Fayl davomiyligi yo'q bo'lsa — boshlanish/tugash ayirmasi.
    /// ⚠️ DARS davomiyligi EMAS: eski tizim aynan shu joyda adashib,
    /// ro'yxatda "80 daqiqa" deb ko'rsatib, 12 daqiqalik video ochardi.
    /// </summary>
    [Fact]
    public void Parse_WithoutFileDuration_DerivesItFromTheTimestamps()
    {
        var result = Parse("""
            {
              "event":"egress_ended",
              "egress_info":{
                "egress_id":"EG_1",
                "started_at":"1780000000000000000",
                "ended_at":"1780000300000000000",
                "file_results":[{"filename":"a.mp4"}]
              }
            }
            """);

        result!.DurationSeconds.Should().Be(300);
    }

    /// <summary>Hech qanday manba yo'q — <c>null</c>, ya'ni "bilmayman" (nol EMAS).</summary>
    [Fact]
    public void Parse_WithoutAnyDurationSource_LeavesItUnknown()
    {
        var result = Parse("""
            {"event":"egress_ended","egress_info":{"egress_id":"EG_1","file_results":[{"filename":"a.mp4"}]}}
            """);

        result!.DurationSeconds.Should().BeNull();
    }

    // ================================================================= buzuq qiymatlar

    /// <summary>
    /// Nol yoki manfiy vaqt — "qo'yilmagan" degani: `StartedAt` bo'sh qoladi
    /// va istisno TASHLANMAYDI (webhook 500 bilan yiqilsa LiveKit hodisani
    /// cheksiz qayta yuborardi).
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Parse_MissingTimestamp_LeavesStartedAtEmpty(string value)
    {
        // `$$$` (uchta dollar) ATAYLAB: JSON oxiridagi `}}` — LITERAL yopuvchi
        // qavslar. `$$` bilan ular interpolatsiya teshigining yopilishi deb
        // o'qiladi (CS9007). Uchta dollarda teshik `{{{...}}}` bo'ladi, `}}`
        // esa matn bo'lib qoladi.
        var result = Parse($$$"""
            {"event":"egress_ended","egress_info":{"egress_id":"EG_1","started_at":"{{{value}}}"}}
            """);

        result.Should().NotBeNull();
        result!.StartedAt.Should().BeNull();
    }

    /// <summary>
    /// ★ JUDA KATTA nanosekund qiymati — istisno TASHLAMAYDI va sana beradi.
    ///
    /// NIMA UCHUN `null` EMAS: LiveKit vaqtni Unix NANOSEKUNDDA yuboradi va
    /// maydon <c>long</c>. Eng katta <c>long</c> (~9.2e18 ns) ham millisekundga
    /// bo'lingach ~2261-yilga to'g'ri keladi, ya'ni
    /// <see cref="DateTimeOffset.FromUnixTimeMilliseconds"/> chegarasidan
    /// CHIQIB BO'LMAYDI. Demak bu qiymat "buzuq" emas — u shunchaki 2001-yilga
    /// tushadigan haqiqiy sana.
    ///
    /// Parser'dagi diapazon tekshiruvi shu sababli amalda ishlamaydi, lekin
    /// ATAYLAB qoldirilgan: u kelajakda birlik (mikrosekund/sekund) o'zgarsa
    /// yoki manba ishonchsiz bo'lsa yagona himoya bo'lib qoladi.
    /// </summary>
    [Fact]
    public void Parse_HugeTimestamp_DoesNotThrow_AndStaysInRange()
    {
        var result = Parse("""
            {"event":"egress_ended","egress_info":{"egress_id":"EG_1","started_at":"999999999999999999"}}
            """);

        result.Should().NotBeNull();
        result!.StartedAt.Should().NotBeNull("`long` nanosekund diapazondan chiqa olmaydi");
        result.StartedAt!.Value.Year.Should().BeInRange(1970, 2262);
    }

    [Fact]
    public void Parse_ErrorMessage_IsCarriedThrough()
    {
        var result = Parse("""
            {"event":"egress_ended","egress_info":{"egress_id":"EG_1","status":"EGRESS_FAILED","error":"room not found"}}
            """);

        result!.EgressStatus.Should().Be("EGRESS_FAILED");
        result.Error.Should().Be("room not found");
    }

    /// <summary>Noma'lum maydon hech narsani buzmasin (LiveKit yangi maydon qo'shsa).</summary>
    [Fact]
    public void Parse_UnknownFields_AreIgnored()
    {
        var result = Parse("""
            {"event":"egress_ended","kelajakdagiMaydon":{"a":1},
             "egress_info":{"egress_id":"EG_1","yangiMaydon":[1,2]}}
            """);

        result!.EgressId.Should().Be("EG_1");
    }
}
