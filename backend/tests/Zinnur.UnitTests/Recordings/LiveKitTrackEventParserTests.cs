using System.Text;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;

namespace Zinnur.UnitTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TREK / XONA HODISASINI O'QISH (SPEC-RECORDING-V2 §3.3)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN ALOHIDA FAYL, MAVJUD <c>LiveKitWebhookParserTests</c> GA
/// QO'SHIMCHA EMAS: o'sha to'plam <c>egress_info</c> atrofidagi tahlilni
/// qo'riqlaydi va u ESKI quvurning jonli yo'li. Ikkalasi bitta faylda
/// tursa, yangi quvurga tegishli tuzatish eski testlarni ham
/// "yangilashga" undardi — aynan shu yo'l bilan qo'riqchi jimgina
/// yo'qoladi.
///
/// Bu yerda tekshiriladigan narsa uchta va uchalasi ham "LiveKit
/// yangilandi — treklar jimgina topilmay qoldi" turkumidagi nosozlik
/// manbai:
///
///   1) maydon nomlari ikki xil (<c>mime_type</c> / <c>mimeType</c>);
///   2) ichma-ich obyektlar (<c>room</c>, <c>participant</c>, <c>track</c>)
///      hodisaga qarab BO'LMASLIGI mumkin;
///   3) idempotentlik kaliti IKKALA tahlil yo'lida AYNI bo'lishi shart —
///      aks holda bitta hodisa ikki marta ishlanardi.
/// </summary>
public sealed class LiveKitTrackEventParserTests
{
    private static LiveKitTrackEventDto? Parse(string json) =>
        LiveKitWebhookParser.ParseTrackEvent(Encoding.UTF8.GetBytes(json));

    // ================================================================= shakl

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[1,2,3]")]
    [InlineData("\"salom\"")]
    public void ParseTrackEvent_MalformedBody_ReturnsNull(string body) =>
        Parse(body).Should().BeNull();

    /// <summary>
    /// To'liq <c>track_published</c> — LiveKit yuboradigan shakl.
    /// </summary>
    [Fact]
    public void ParseTrackEvent_TrackPublished_ReadsEveryField()
    {
        var result = Parse("""
            {"event":"track_published","id":"EV_1",
             "room":{"sid":"RM_1","name":"r-42"},
             "participant":{"sid":"PA_1","identity":"77","name":"Ustoz"},
             "track":{"sid":"TR_abc","type":"VIDEO","source":"SCREEN_SHARE","mimeType":"video/vp8"}}
            """);

        result.Should().NotBeNull();
        result!.EventName.Should().Be("track_published");
        result.EventId.Should().Be("EV_1");
        result.RoomName.Should().Be("r-42");
        result.ParticipantIdentity.Should().Be("77");
        result.TrackSid.Should().Be("TR_abc");
        result.TrackSource.Should().Be("SCREEN_SHARE");
        result.MimeType.Should().Be("video/vp8");
    }

    /// <summary>
    /// 🔴 IKKI XIL YOZILISH. protojson sozlamasiga qarab
    /// <c>mime_type</c> yoki <c>mimeType</c> keladi va bu LiveKit
    /// versiyalari orasida FARQ QILADI. Bitta nomga tayanish "yangilangandan
    /// keyin xom fayllar noto'g'ri kengaytma bilan yozila boshladi"
    /// degan nosozlik bo'lardi.
    /// </summary>
    [Theory]
    [InlineData("mime_type")]
    [InlineData("mimeType")]
    public void ParseTrackEvent_MimeType_IsReadUnderBothSpellings(string field)
    {
        // `$$$` (uchta dollar) ATAYLAB: satr oxiridagi `}}` — JSON'ning
        // LITERAL yopuvchi qavslari. `$$` bilan ular interpolatsiya
        // teshigining yopilishi deb o'qiladi (CS9007).
        var result = Parse($$$"""
            {"event":"track_published","id":"EV_2","room":{"name":"r-1"},
             "track":{"sid":"TR_1","source":"CAMERA","{{{field}}}":"video/h264"}}
            """);

        result!.MimeType.Should().Be("video/h264");
    }

    /// <summary>
    /// <c>room_started</c> da <c>participant</c> ham, <c>track</c> ham
    /// yo'q — bu XATO EMAS, oddiy hol. Xona nomi esa bo'lishi SHART:
    /// yozuv qatorini topishning YAGONA yo'li shu.
    /// </summary>
    [Fact]
    public void ParseTrackEvent_RoomStarted_HasOnlyTheRoom()
    {
        var result = Parse("""
            {"event":"room_started","id":"EV_3","room":{"sid":"RM_9","name":"r-9"}}
            """);

        result.Should().NotBeNull();
        result!.EventName.Should().Be("room_started");
        result.RoomName.Should().Be("r-9");
        result.ParticipantIdentity.Should().BeNull();
        result.TrackSid.Should().BeNull();
        result.TrackSource.Should().BeNull();
        result.MimeType.Should().BeNull();
    }

    /// <summary>
    /// <c>participant_left</c> — trek yo'q, ishtirokchi bor.
    /// </summary>
    [Fact]
    public void ParseTrackEvent_ParticipantLeft_ReadsTheIdentity()
    {
        var result = Parse("""
            {"event":"participant_left","id":"EV_4","room":{"name":"r-5"},
             "participant":{"identity":"12","name":"Ustoz"}}
            """);

        result!.ParticipantIdentity.Should().Be("12");
        result.TrackSid.Should().BeNull();
    }

    /// <summary>
    /// Xona nomi umuman bo'lmasa <c>null</c> — chaqiruvchi hodisani
    /// chetlab o'tadi (yozuvni topib bo'lmaydi).
    /// </summary>
    [Fact]
    public void ParseTrackEvent_WithoutRoom_HasNoRoomName() =>
        Parse("""{"event":"track_published","id":"EV_5"}""")!.RoomName.Should().BeNull();

    /// <summary>
    /// Egress hodisasi ham o'qiladi (nomi va kaliti kerak) — hodisa
    /// nomiga qarab tarmoqlanish uchun. Ichida <c>track</c> obyekti yo'q.
    /// </summary>
    [Fact]
    public void ParseTrackEvent_EgressEvent_StillReportsTheName()
    {
        var result = Parse("""
            {"event":"egress_ended","id":"EV_6","egress_info":{"egress_id":"EG_1"}}
            """);

        result!.EventName.Should().Be("egress_ended");
        result.EventId.Should().Be("EV_6");
        result.TrackSid.Should().BeNull();
    }

    // ================================================================= idempotentlik kaliti

    /// <summary>
    /// 🔴 ENG MUHIM TEST. Takror jurnali BITTA jadval
    /// (<c>RecordingWebhookEvents</c>) va unga ikkala tahlil yo'lidan ham
    /// murojaat qilinadi. Ikki yo'l bitta tanaga IKKI XIL kalit yasasa,
    /// o'sha hodisa ikki marta ishlanardi — trek quvurida bu ikkinchi
    /// egress, ya'ni bitta darsda ikki fayl demakdir.
    /// </summary>
    [Fact]
    public void ParseTrackEvent_AndParse_ProduceTheSameEventKey()
    {
        const string Body = """
            {"event":"egress_ended","id":"EV_7","egress_info":{"egress_id":"EG_2"}}
            """;

        var bytes = Encoding.UTF8.GetBytes(Body);

        LiveKitWebhookParser.ParseTrackEvent(bytes)!.EventId
            .Should().Be(LiveKitWebhookParser.Parse(bytes)!.EventId);
    }

    /// <summary>
    /// <c>id</c> yo'q bo'lsa kalit TANA XESHI bo'ladi — va u ham ikkala
    /// yo'lda AYNI (yuqoridagi test bilan bir xil sabab).
    /// </summary>
    [Fact]
    public void ParseTrackEvent_WithoutEventId_FallsBackToTheSameBodyHash()
    {
        const string Body = """{"event":"track_published","room":{"name":"r-3"}}""";

        var bytes = Encoding.UTF8.GetBytes(Body);

        var track = LiveKitWebhookParser.ParseTrackEvent(bytes)!;

        track.EventId.Should().StartWith("sha256:");
        track.EventId.Should().Be(LiveKitWebhookParser.Parse(bytes)!.EventId);
    }

    /// <summary>Nomi yo'q hodisa ham yiqilmaydi — bo'sh satr qaytadi.</summary>
    [Fact]
    public void ParseTrackEvent_WithoutEventName_ReturnsEmptyName() =>
        Parse("""{"id":"EV_8","room":{"name":"r-1"}}""")!.EventName.Should().BeEmpty();
}
