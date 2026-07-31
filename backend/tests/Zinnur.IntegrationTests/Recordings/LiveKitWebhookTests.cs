using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 LIVEKIT WEBHOOK — IMZO, TANA XESHI VA IDEMPOTENTLIK
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ BU FAYL ESKI TIZIMNING X-3 ZAIFLIGINI QO'RIQLAYDI.
///
/// Eski tizimda tekshiruv <c>if settings.LIVEKIT_API_SECRET:</c> shartida
/// edi — ya'ni sir bo'sh bo'lsa BUTUN himoya o'chib qolardi. Tana xeshi
/// esa faqat <c>if want:</c> bo'lganda solishtirilardi: <c>sha256</c>
/// da'vosisiz token bilan ISTALGAN tana o'tib ketardi. Endpoint manzili
/// esa sir emas — u deploy skriptlarida va tarmoq jurnallarida turadi.
///
/// Shuning uchun bu yerda TO'RTTA asosiy holat qotirilgan:
///   1) to'g'ri imzo                       -> 200
///   2) buzilgan imzo                      -> 401
///   3) YAROQLI token + O'ZGARTIRILGAN tana -> 401
///   4) takroriy hodisa                    -> 200, lekin holat O'ZGARMAYDI
/// </summary>
public sealed class LiveKitWebhookTests(RecordingFactory factory)
    : IClassFixture<RecordingFactory>
{
    // ================================================================= 1) to'g'ri imzo

    /// <summary>
    /// To'g'ri imzolangan <c>egress_started</c> yozuvni FAOL qiladi.
    /// </summary>
    [Fact]
    public async Task Webhook_WithValidSignature_ActivatesTheRecording()
    {
        var (recordingId, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent("egress_started", egressId, "EGRESS_ACTIVE");

        var response = await PostAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var ack = await response.Content.ReadFromJsonAsync<WebhookAckDto>();
        ack!.Outcome.Should().Be("Started");

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.Status.Should().Be(RecordingStatus.Active);
        recording.StartedAt.Should().NotBeNull();
    }

    // ================================================================= 2) buzilgan imzo

    /// <summary>Imzoning oxirgi belgisi o'zgartirilgan — rad etilishi SHART.</summary>
    [Fact]
    public async Task Webhook_WithTamperedSignature_IsRejected()
    {
        var (recordingId, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent("egress_ended", egressId, "EGRESS_COMPLETE",
            objectKey: "recordings/soxta.mp4", sizeBytes: 999);

        var keys = RecordingWorld.LiveKitOf(factory);
        var token = RecordingWorld.SignToken(keys, body);

        // Imzo bo'lagining oxirgi belgisini almashtiramiz.
        var broken = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, broken);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.Status.Should().Be(RecordingStatus.Starting, "soxta hodisa holatni O'ZGARTIRMASLIGI kerak");
    }

    /// <summary>Imzosiz so'rov ham rad etiladi (eng oddiy hujum).</summary>
    [Fact]
    public async Task Webhook_WithoutAuthorizationHeader_IsRejected()
    {
        var (_, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent("egress_started", egressId, "EGRESS_ACTIVE");

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, token: null);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// ★ <c>alg: none</c> — JWT'dagi eng mashhur zaiflik. Algoritm
    /// TOKENDAN o'qilsa, hujumchi imzoni umuman tashlab yuborardi.
    /// </summary>
    [Fact]
    public async Task Webhook_WithAlgNoneToken_IsRejected()
    {
        var (_, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent("egress_started", egressId, "EGRESS_ACTIVE");
        var keys = RecordingWorld.LiveKitOf(factory);

        var token = RecordingWorld.SignToken(keys, body, algorithm: "none");

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, token);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Muddati o'tgan token qayta ishlatilmasin.</summary>
    [Fact]
    public async Task Webhook_WithExpiredToken_IsRejected()
    {
        var (_, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent("egress_started", egressId, "EGRESS_ACTIVE");
        var keys = RecordingWorld.LiveKitOf(factory);

        // -1 soat: 2 daqiqalik `ClockSkew` dan ancha uzoq.
        var token = RecordingWorld.SignToken(keys, body, expiresInSeconds: -3600);

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, token);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= 3) tana xeshi

    /// <summary>
    /// 🔴 ENG MUHIM TEST: TOKEN TO'G'RI VA YAROQLI, LEKIN TANA BOSHQA.
    ///
    /// Bu aynan "ushlangan tokenni qayta ishlatish" hujumi: hujumchi
    /// yaroqli tokenni (masalan xato sozlangan proksi jurnalidan) olib,
    /// unga O'Z tanasini biriktiradi. Faqat JWT imzosini tekshiradigan
    /// tizim buni O'TKAZIB YUBORARDI.
    /// </summary>
    [Fact]
    public async Task Webhook_WithValidTokenButDifferentBody_IsRejected()
    {
        var (recordingId, egressId) = await NewRecordingAsync();

        // Token BEZARAR tana uchun imzolanadi...
        var signedBody = RecordingWorld.EgressEvent(
            "egress_started", egressId, "EGRESS_ACTIVE", eventId: "EV_haqiqiy");

        var keys = RecordingWorld.LiveKitOf(factory);
        var token = RecordingWorld.SignToken(keys, signedBody);

        // ...yuboriladigan tana esa BOSHQA: "yozuv tayyor, mana fayl".
        var forgedBody = RecordingWorld.EgressEvent(
            "egress_ended", egressId, "EGRESS_COMPLETE",
            eventId: "EV_soxta", objectKey: "recordings/hujumchi.mp4", sizeBytes: 1);

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(forgedBody, token);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "yaroqli imzo BOSHQA tanaga taalluqli bo'lishi mumkin emas");

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.Status.Should().Be(RecordingStatus.Starting);
        recording.ObjectKey.Should().NotContain("hujumchi");
    }

    /// <summary>
    /// Tokenda <c>sha256</c> da'vosi UMUMAN yo'q — rad etilishi SHART.
    /// Eski tizim aynan bu holatda tekshiruvni o'tkazib yuborardi.
    /// </summary>
    [Fact]
    public async Task Webhook_WithTokenMissingTheBodyHashClaim_IsRejected()
    {
        var (_, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent("egress_started", egressId, "EGRESS_ACTIVE");
        var keys = RecordingWorld.LiveKitOf(factory);

        var token = RecordingWorld.SignToken(keys, body, includeBodyHash: false);

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, token);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Boshqa API kaliti nomi bilan berilgan token — rad etiladi.</summary>
    [Fact]
    public async Task Webhook_WithForeignIssuer_IsRejected()
    {
        var (_, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent("egress_started", egressId, "EGRESS_ACTIVE");
        var keys = RecordingWorld.LiveKitOf(factory);

        var token = RecordingWorld.SignToken(keys, body, issuer: "boshqa-loyiha");

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, token);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= 4) idempotentlik

    /// <summary>
    /// ★ LiveKit hodisani QAYTA yuboradi (javob kechiksa yoki tarmoq
    /// uzilsa). Ikkinchi marta hech narsa o'zgarmasligi va javob baribir
    /// 200 bo'lishi kerak — 200 dan boshqa javob cheksiz qayta yuborish
    /// siklini boshlardi.
    /// </summary>
    [Fact]
    public async Task Webhook_WithRepeatedEvent_IsProcessedOnlyOnce()
    {
        var (recordingId, egressId) = await NewRecordingAsync();

        const string EventId = "EV_takror_1";

        var body = RecordingWorld.EgressEvent(
            "egress_ended", egressId, "EGRESS_COMPLETE",
            eventId: EventId, objectKey: "recordings/bir.mp4", sizeBytes: 1000, durationNanos: 60_000_000_000);

        var first = await PostAsync(body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<WebhookAckDto>())!.Outcome.Should().Be("Completed");

        var second = await PostAsync(body);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<WebhookAckDto>())!.Outcome.Should().Be("Duplicate");

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.Status.Should().Be(RecordingStatus.Completed);
        recording.ObjectKey.Should().Be("recordings/bir.mp4");
        recording.SizeBytes.Should().Be(1000);
        recording.DurationSeconds.Should().Be(60);
    }

    // ================================================================= mazmun

    /// <summary>
    /// Bizda yo'q egress — bu XATO EMAS (bitta LiveKit'ni dev va staging
    /// baham ko'rishi mumkin). Javob 200, holat "Unknown".
    /// </summary>
    [Fact]
    public async Task Webhook_WithUnknownEgress_IsAcknowledgedWithoutError()
    {
        var body = RecordingWorld.EgressEvent(
            "egress_ended", "EG_bizda_yoq_" + Guid.NewGuid().ToString("N")[..8], "EGRESS_COMPLETE",
            objectKey: "recordings/x.mp4");

        var response = await PostAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<WebhookAckDto>())!.Outcome.Should().Be("Unknown");
    }

    /// <summary>Yozuvga aloqasi yo'q hodisa jimgina chetlanadi.</summary>
    [Fact]
    public async Task Webhook_WithNonEgressEvent_IsIgnored()
    {
        const string Body = """{"event":"participant_joined","id":"EV_p1","room":{"name":"r-1"}}""";

        var response = await PostAsync(Body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<WebhookAckDto>())!.Outcome.Should().Be("Ignored");
    }

    /// <summary>
    /// Buzuq JSON ham 200 oladi (imzodan o'tgan bo'lsa): uni qayta
    /// yuborishning ma'nosi yo'q.
    /// </summary>
    [Fact]
    public async Task Webhook_WithMalformedJson_IsAcknowledged()
    {
        var response = await PostAsync("{ buzuq");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<WebhookAckDto>())!.Outcome.Should().Be("Malformed");
    }

    /// <summary>
    /// <c>EGRESS_FAILED</c> — yozuv YAKUNIY xato, sabab bazada qoladi
    /// ("nega bu darsning yozuvi yo'q?" degan savolga javob).
    /// </summary>
    [Fact]
    public async Task Webhook_WithFailedStatus_MarksTheRecordingFailed()
    {
        var (recordingId, egressId) = await NewRecordingAsync();

        var body = RecordingWorld.EgressEvent(
            "egress_ended", egressId, "EGRESS_FAILED", error: "room not found");

        var response = await PostAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<WebhookAckDto>())!.Outcome.Should().Be("Failed");

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.Status.Should().Be(RecordingStatus.Failed);
        recording.Error.Should().Contain("room not found");
    }

    /// <summary>
    /// 🔴 KECH KELGAN XATO HODISASI TAYYOR YOZUVNI BUZMAYDI.
    /// Fayl allaqachon omborda va uni o'quvchilar ochayotgan bo'lishi mumkin.
    /// </summary>
    [Fact]
    public async Task Webhook_LateFailureAfterCompletion_DoesNotDestroyTheFile()
    {
        var (recordingId, egressId) = await NewRecordingAsync();

        await PostAsync(RecordingWorld.EgressEvent(
            "egress_ended", egressId, "EGRESS_COMPLETE",
            objectKey: "recordings/tayyor.mp4", sizeBytes: 4242));

        await PostAsync(RecordingWorld.EgressEvent(
            "egress_ended", egressId, "EGRESS_FAILED", error: "kech kelgan xato"));

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.Status.Should().Be(RecordingStatus.Completed);
        recording.ObjectKey.Should().Be("recordings/tayyor.mp4");
        recording.SizeBytes.Should().Be(4242);
    }

    // ---------------------------------------------------------------- yordamchi

    /// <summary>To'g'ri imzolangan so'rov yuboradi.</summary>
    private async Task<HttpResponseMessage> PostAsync(string body)
    {
        var keys = RecordingWorld.LiveKitOf(factory);
        var token = RecordingWorld.SignToken(keys, body);

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, token);

        return await client.SendAsync(request);
    }

    /// <summary>Guruh + jonli dars + <c>Starting</c> holatidagi yozuv.</summary>
    private async Task<(long RecordingId, string EgressId)> NewRecordingAsync()
    {
        var world = await WorldBuilder.CreateAsync(factory, "wh");
        var sessionId = await RecordingWorld.AddSessionAsync(factory, world.GroupId);

        var egressId = "EG_" + Guid.NewGuid().ToString("N")[..16];

        var recordingId = await RecordingWorld.AddRecordingAsync(
            factory, sessionId, RecordingStatus.Starting, egressId);

        return (recordingId, egressId);
    }

    private sealed record WebhookAckDto(bool Ok, string Outcome);
}
