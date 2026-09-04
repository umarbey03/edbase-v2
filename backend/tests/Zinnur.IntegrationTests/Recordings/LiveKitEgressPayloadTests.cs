using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Infrastructure.Options;
using Zinnur.Infrastructure.Services;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// EGRESS SO'ROVINING TANASI — LiveKit'siz, lekin BAYT DARAJASIDA
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN HAQIQIY LiveKit KERAK EMAS: bu yerda tekshiriladigan
/// narsa — BIZ YUBORADIGAN tana va token. LiveKit'ning javobi esa
/// soxta: uni tekshirish LiveKit'ni sinash bo'lardi, bizning kodimizni
/// emas.
///
/// 🔴 ENG MUHIM TEST — <see cref="RoomAudioPayload_NeverCarriesLayoutOrCustomBaseUrl"/>.
///
/// Xonaning ovozi faqat-ovozli <c>RoomCompositeEgress</c> bilan olinadi va
/// Egress bunday so'rovni BRAUZERSIZ (SDK manbasi) bajaradi — ya'ni bitta
/// dars ~0.15 yadro turadi, 1.5 yadro emas. Bu tanlovni uchta maydon hal
/// qiladi: <c>audio_only</c> BOR, <c>layout</c> va <c>custom_base_url</c>
/// esa UMUMAN YO'Q.
///
/// Agar kimdir bir kuni ikkita tana quruvchini "bitta metod +
/// <c>audioOnly</c> bayrog'i" ga birlashtirsa, <c>layout</c> qaytib
/// keladi va NOSOZLIK JIMGINA bo'ladi: yozuv ishlaydi, fayl chiqadi,
/// faqat serverga bir vaqtda oltita dars o'rniga bittasi sig'adi va
/// qolganlari "Yozuv xizmati javob bermadi (timeout)" bilan yiqiladi.
/// Shu testning butun vazifasi — o'sha tahrirni MASHINAGA to'xtatish.
/// </summary>
public sealed class LiveKitEgressPayloadTests
{
    private const string RoomName = "session-1042";

    // ═══════════════════════════════════════════════════════ xona ovozi (mikser)

    /// <summary>
    /// 🔴 QIMMAT MODELINING QO'RIQCHISI — sinf izohiga qarang.
    /// </summary>
    [Fact]
    public async Task RoomAudioPayload_NeverCarriesLayoutOrCustomBaseUrl()
    {
        using var world = new EgressWorld();

        await world.Client.StartRoomAudioRecordingAsync(
            new RoomAudioEgressStartRequest(RoomName, "raw/12/34/ROOM.ogg"));

        using var body = world.LastBody();

        body.RootElement.TryGetProperty("layout", out _).Should().BeFalse(
            "joylashuv — CHIZILGAN sahifaning xossasi; uni yuborish Egress'ni "
            + "brauzer manbasiga burib, dars boshiga ~1.5 yadro qo'shishi mumkin");

        body.RootElement.TryGetProperty("custom_base_url", out _).Should().BeFalse(
            "shablon manzilini faqat brauzer chiza oladi — bo'sh satr ham "
            + "yaramaydi, kalitning O'ZI bo'lmasligi kerak");

        body.RootElement.GetProperty("audio_only").GetBoolean().Should().BeTrue(
            "SDK manbasining sharti");
    }

    /// <summary>
    /// Mikser ESKI quvur bilan AYNI Twirp metodiga boradi — farq faqat
    /// tanada. Shu ikkilik ataylab, shuning uchun u ham qulflanadi.
    /// </summary>
    [Fact]
    public async Task StartRoomAudioRecordingAsync_PostsOggFileOutputToRoomCompositeEndpoint()
    {
        using var world = new EgressWorld();

        var result = await world.Client.StartRoomAudioRecordingAsync(
            new RoomAudioEgressStartRequest(RoomName, "raw/12/34/ROOM.ogg"));

        result.Succeeded.Should().BeTrue(result.Error);
        result.EgressId.Should().Be(EgressWorld.EgressId);

        world.LastPath().Should().Be("/twirp/livekit.Egress/StartRoomCompositeEgress");

        using var body = world.LastBody();

        body.RootElement.GetProperty("room_name").GetString().Should().Be(RoomName);

        var output = body.RootElement.GetProperty("file_outputs")[0];

        output.GetProperty("file_type").GetString().Should().Be("OGG",
            "Opus — SFU allaqachon tashiydigan kodek, ya'ni eng arzon yo'l");
        output.GetProperty("filepath").GetString().Should().Be("raw/12/34/ROOM.ogg");
        output.GetProperty("disable_manifest").GetBoolean().Should().BeTrue();

        AssertStorageTarget(output);
    }

    // ═══════════════════════════════════════════════════════ eski quvur (muzlatilgan)

    /// <summary>
    /// 🔴 ESKI QUVUR O'ZGARMAGANINING ISBOTI. U — orqaga qaytish yo'li:
    /// yangi quvurda nimadir buzilsa, guruh sozlamasidan <c>RoomComposite</c>
    /// ga qaytiladi. Shu tana buzilsa qaytadigan joy ham qolmasdi.
    /// </summary>
    [Fact]
    public async Task StartRoomRecordingAsync_StillSendsGridLayoutAndMp4()
    {
        using var world = new EgressWorld();

        await world.Client.StartRoomRecordingAsync(
            new EgressStartRequest(RoomName, "recordings/2026-09/42/abcdef0123456789.mp4"));

        world.LastPath().Should().Be("/twirp/livekit.Egress/StartRoomCompositeEgress");

        using var body = world.LastBody();

        body.RootElement.GetProperty("layout").GetString().Should().Be("grid");
        body.RootElement.TryGetProperty("audio_only", out _).Should().BeFalse();

        var output = body.RootElement.GetProperty("file_outputs")[0];

        output.GetProperty("file_type").GetString().Should().Be("MP4");
        output.GetProperty("filepath").GetString()
            .Should().Be("recordings/2026-09/42/abcdef0123456789.mp4");
    }

    // ═══════════════════════════════════════════════════════ trek yozuvi

    [Fact]
    public async Task StartTrackRecordingAsync_PostsDirectFileOutputToTrackEndpoint()
    {
        using var world = new EgressWorld();

        var result = await world.Client.StartTrackRecordingAsync(
            new TrackEgressStartRequest(RoomName, "TR_camera_1", "raw/12/34/TR_camera_1.webm"));

        result.Succeeded.Should().BeTrue(result.Error);
        result.EgressId.Should().Be(EgressWorld.EgressId);

        world.LastPath().Should().Be("/twirp/livekit.Egress/StartTrackEgress");

        using var body = world.LastBody();

        body.RootElement.GetProperty("room_name").GetString().Should().Be(RoomName);
        body.RootElement.GetProperty("track_id").GetString().Should().Be("TR_camera_1");

        // ⚠️ `file` OBYEKTI, `file_outputs` massivi EMAS: trek egress'ining
        //    proto'sida chiqish `DirectFileOutput`. Massiv yuborilsa LiveKit
        //    "chiqish belgilanmagan" deb rad etadi.
        body.RootElement.TryGetProperty("file_outputs", out _).Should().BeFalse();

        var file = body.RootElement.GetProperty("file");

        file.GetProperty("filepath").GetString().Should().Be("raw/12/34/TR_camera_1.webm");
        file.GetProperty("disable_manifest").GetBoolean().Should().BeTrue();

        // Trek "borligicha" yoziladi — fayl turini tanlash qayta kodlashni
        // talab qilardi va butun tejamkorlikni yo'q qilardi.
        file.TryGetProperty("file_type", out _).Should().BeFalse();

        AssertStorageTarget(file);
    }

    /// <summary>
    /// <c>ws://</c> manzili HTTP'ga o'giriladi — aks holda `HttpClient`
    /// darhol yiqilardi (`LiveKit:Url` loyihaning boshqa joyida signal
    /// manzili sifatida ishlatiladi va u `ws://` bo'ladi).
    /// </summary>
    [Fact]
    public async Task StartTrackRecordingAsync_TranslatesWebSocketUrlToHttp()
    {
        using var world = new EgressWorld();

        await world.Client.StartTrackRecordingAsync(
            new TrackEgressStartRequest(RoomName, "TR_1", "raw/1/1/TR_1.webm"));

        world.Handler.Requests[^1].Url.Scheme.Should().Be("http");
        world.Handler.Requests[^1].Url.Host.Should().Be("livekit");
    }

    [Fact]
    public async Task StartTrackRecordingAsync_WhenLiveKitRejects_ReportsTheReasonInUzbek()
    {
        using var world = new EgressWorld();

        world.Handler.Status = HttpStatusCode.NotFound;
        world.Handler.ResponseBody = """{"code":"not_found","msg":"room does not exist"}""";

        var result = await world.Client.StartTrackRecordingAsync(
            new TrackEgressStartRequest(RoomName, "TR_1", "raw/1/1/TR_1.webm"));

        result.Succeeded.Should().BeFalse();
        result.EgressId.Should().BeNull();
        result.Error.Should().Be("Yozuv xizmati rad etdi: room does not exist");
    }

    /// <summary>
    /// Javob 200, lekin identifikatorsiz — bu MUVAFFAQIYAT EMAS: bunday
    /// qatorga webhook hech qachon bog'lanmasdi va u abadiy `Requested`
    /// bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task StartRoomAudioRecordingAsync_WhenResponseHasNoEgressId_Fails()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody = """{"status":"EGRESS_STARTING"}""";

        var result = await world.Client.StartRoomAudioRecordingAsync(
            new RoomAudioEgressStartRequest(RoomName, "raw/12/34/ROOM.ogg"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Yozuv xizmati identifikator qaytarmadi.");
    }

    /// <summary>
    /// Ombor sozlanmagan bo'lsa Egress faylni hech qayerga yoza olmaydi —
    /// so'rov UMUMAN yuborilmasligi kerak (aks holda "yozuv boshlandi"
    /// deb ko'rinardi, fayl esa yo'q).
    /// </summary>
    [Fact]
    public async Task StartTrackRecordingAsync_WhenStorageIsNotConfigured_DoesNotCallLiveKit()
    {
        using var world = new EgressWorld(storage: new StorageOptions());

        var result = await world.Client.StartTrackRecordingAsync(
            new TrackEgressStartRequest(RoomName, "TR_1", "raw/1/1/TR_1.webm"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Yozuv xizmati sozlanmagan (`LiveKit:*` yoki `Storage:*`).");
        world.Handler.Requests.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════ tokenlar

    /// <summary>
    /// Yozuvni boshlaydigan chaqiruvlar <c>roomRecord</c> granti bilan
    /// ketadi — eski quvurdagidek.
    /// </summary>
    [Fact]
    public async Task StartTrackRecordingAsync_SignsWithRoomRecordGrant()
    {
        using var world = new EgressWorld();

        await world.Client.StartTrackRecordingAsync(
            new TrackEgressStartRequest(RoomName, "TR_1", "raw/1/1/TR_1.webm"));

        using var claims = world.LastTokenClaims();

        var video = claims.RootElement.GetProperty("video");

        video.GetProperty("roomRecord").GetBoolean().Should().BeTrue();
        video.TryGetProperty("roomAdmin", out _).Should().BeFalse();
    }

    /// <summary>
    /// 🔴 <c>ListParticipants</c> BOSHQA GRANT talab qiladi. Noto'g'ri
    /// grantli token oddiy Twirp xatosi bilan qaytadi va tiklash yo'li
    /// jimgina ishlamay qolardi.
    ///
    /// Grant AYNAN shu xonaga cheklanadi: xonasiz <c>roomAdmin</c> token
    /// o'g'irlansa butun serverga admin huquqi degani bo'lardi.
    /// </summary>
    [Fact]
    public async Task ListParticipantsAsync_SignsWithRoomAdminGrantScopedToTheRoom()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody = """{"participants":[]}""";

        await world.Client.ListParticipantsAsync(RoomName);

        world.LastPath().Should().Be("/twirp/livekit.RoomService/ListParticipants");

        using var claims = world.LastTokenClaims();

        var video = claims.RootElement.GetProperty("video");

        video.GetProperty("roomAdmin").GetBoolean().Should().BeTrue();
        video.GetProperty("room").GetString().Should().Be(RoomName);
        video.TryGetProperty("roomRecord", out _).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════ holatni o'qish

    [Fact]
    public async Task ListParticipantsAsync_FlattensParticipantsAndTracks()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody =
            """
            {"participants":[
              {"identity":"7","tracks":[
                {"sid":"TR_cam","source":"CAMERA","mime_type":"video/vp8"},
                {"sid":"TR_mic","source":"MICROPHONE","mimeType":"audio/opus"}]},
              {"identity":"91","tracks":[
                {"sid":"TR_std","source":"MICROPHONE"}]}]}
            """;

        var result = await world.Client.ListParticipantsAsync(RoomName);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Tracks.Should().HaveCount(3);

        result.Tracks[0].Should().Be(
            new LiveKitPublishedTrackDto("7", "TR_cam", "CAMERA", "video/vp8"));

        // protojson ikkala imlo bilan yozadi — ikkalasi ham o'qilishi shart.
        result.Tracks[1].MimeType.Should().Be("audio/opus");

        result.Tracks[2].ParticipantIdentity.Should().Be("91");
        result.Tracks[2].MimeType.Should().BeNull("LiveKit uni bermasligi mumkin");
    }

    [Fact]
    public async Task ListParticipantsAsync_WhenRoomIsEmpty_SucceedsWithNoTracks()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody = "{}";

        var result = await world.Client.ListParticipantsAsync(RoomName);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Tracks.Should().BeEmpty();
    }

    /// <summary>
    /// 🔴 "LiveKit javob bermadi" ≠ "xonada trek yo'q". Ikkalasini bir xil
    /// qaytarsak, tarmoq uzilgan daqiqada tiklash job'i tirik egress
    /// ustiga ikkinchisini ishga tushirardi.
    /// </summary>
    [Fact]
    public async Task ListParticipantsAsync_WhenLiveKitRejects_FailsInsteadOfReturningEmptyList()
    {
        using var world = new EgressWorld();

        world.Handler.Status = HttpStatusCode.ServiceUnavailable;
        world.Handler.ResponseBody = """{"code":"unavailable","msg":"try again"}""";

        var result = await world.Client.ListParticipantsAsync(RoomName);

        result.Succeeded.Should().BeFalse();
        result.Tracks.Should().BeEmpty();
        result.Error.Should().Be("Yozuv xizmati rad etdi: try again");
    }

    /// <inheritdoc cref="ListParticipantsAsync_WhenLiveKitRejects_FailsInsteadOfReturningEmptyList" />
    [Fact]
    public async Task ListParticipantsAsync_WhenResponseIsNotJson_FailsInsteadOfReturningEmptyList()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody = "<html>502 Bad Gateway</html>";

        var result = await world.Client.ListParticipantsAsync(RoomName);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Yozuv xizmatining javobi tushunarsiz.");
    }

    /// <summary>
    /// ⚠️ RoomService xonani <c>room</c> deb ataydi, Egress esa
    /// <c>room_name</c> deb. Adashtirilsa LiveKit "xona ko'rsatilmagan"
    /// deb rad etadi va tiklash yo'li jimgina ishlamay qolardi.
    /// </summary>
    [Fact]
    public async Task ListParticipantsAsync_NamesTheRoomFieldAsRoomServiceExpects()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody = """{"participants":[]}""";

        await world.Client.ListParticipantsAsync(RoomName);

        using var body = world.LastBody();

        body.RootElement.GetProperty("room").GetString().Should().Be(RoomName);
        body.RootElement.TryGetProperty("room_name", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ListEgressAsync_AsksOnlyForTheActiveEgressOfThatRoom()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody =
            """{"items":[{"egress_id":"EG_mixer","status":"EGRESS_ACTIVE"}]}""";

        var result = await world.Client.ListEgressAsync(RoomName);

        world.LastPath().Should().Be("/twirp/livekit.Egress/ListEgress");

        using var body = world.LastBody();

        body.RootElement.GetProperty("room_name").GetString().Should().Be(RoomName);
        body.RootElement.GetProperty("active").GetBoolean().Should().BeTrue();

        result.Succeeded.Should().BeTrue(result.Error);
        result.Items.Should().ContainSingle()
            .Which.Should().Be(new LiveKitEgressInfoDto("EG_mixer", "EGRESS_ACTIVE"));
    }

    /// <summary>
    /// 🔴 BO'SH RO'YXAT — MA'NOLI JAVOB: "mikser o'lgan". Aynan shuning
    /// uchun xato holat bo'sh ro'yxat bilan ifodalanmaydi.
    /// </summary>
    [Fact]
    public async Task ListEgressAsync_WhenNothingIsRunning_SucceedsWithEmptyList()
    {
        using var world = new EgressWorld();

        world.Handler.ResponseBody = "{}";

        var result = await world.Client.ListEgressAsync(RoomName);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Items.Should().BeEmpty();
    }

    /// <inheritdoc cref="ListEgressAsync_WhenNothingIsRunning_SucceedsWithEmptyList" />
    [Fact]
    public async Task ListEgressAsync_WhenLiveKitIsUnreachable_FailsInsteadOfReturningEmptyList()
    {
        using var world = new EgressWorld();

        world.Handler.Throw = new HttpRequestException("connection refused");

        var result = await world.Client.ListEgressAsync(RoomName);

        result.Succeeded.Should().BeFalse();
        result.Items.Should().BeEmpty();
        result.Error.Should().Be("Yozuv xizmatiga ulanib bo'lmadi.");
    }

    [Fact]
    public async Task ListEgressAsync_WhenLiveKitIsNotConfigured_DoesNotCallAnything()
    {
        using var world = new EgressWorld(liveKit: new LiveKitOptions());

        var result = await world.Client.ListEgressAsync(RoomName);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("LiveKit sozlanmagan (`LiveKit:*`).");
        world.Handler.Requests.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════ yordamchilar

    /// <summary>
    /// Ombor kalitlari HAR IKKALA yangi tanada AYNI shaklda ketadi —
    /// `force_path_style` MinIO uchun ham, R2 uchun ham majburiy.
    /// </summary>
    private static void AssertStorageTarget(JsonElement output)
    {
        var s3 = output.GetProperty("s3");

        s3.GetProperty("access_key").GetString().Should().Be("test-access");
        s3.GetProperty("secret").GetString().Should().Be("test-secret");
        s3.GetProperty("region").GetString().Should().Be("auto");
        s3.GetProperty("bucket").GetString().Should().Be("zinnur-test");
        s3.GetProperty("endpoint").GetString().Should().Be("http://minio:9000");
        s3.GetProperty("force_path_style").GetBoolean().Should().BeTrue();
    }

    /// <summary>Mijoz + soxta transport, bitta joyda yig'ilgan.</summary>
    private sealed class EgressWorld : IDisposable
    {
        internal const string EgressId = "EG_test_0001";

        private readonly HttpClient _http;

        public EgressWorld(StorageOptions? storage = null, LiveKitOptions? liveKit = null)
        {
            Handler = new CapturingHandler
            {
                ResponseBody = $$"""{"egress_id":"{{EgressId}}"}""",
            };

            _http = new HttpClient(Handler);

            Client = new LiveKitEgressClient(
                new SingleClientFactory(_http),
                new FixedOptions<LiveKitOptions>(liveKit ?? new LiveKitOptions
                {
                    // ATAYLAB `ws://` — manzil tarjimasi ham sinaladi.
                    Url = "ws://livekit:7880",
                    ApiKey = "devkey",
                    ApiSecret = "test-secret-at-least-32-characters-long",
                }),
                new FixedOptions<StorageOptions>(storage ?? new StorageOptions
                {
                    ServiceUrl = "http://minio:9000",
                    Bucket = "zinnur-test",
                    AccessKey = "test-access",
                    SecretKey = "test-secret",
                    Region = "auto",
                }),
                NullLogger<LiveKitEgressClient>.Instance);
        }

        public CapturingHandler Handler { get; }

        public LiveKitEgressClient Client { get; }

        public string LastPath() => Handler.Requests[^1].Url.AbsolutePath;

        public JsonDocument LastBody() => JsonDocument.Parse(Handler.Requests[^1].Body);

        /// <summary>JWT'ning o'rtadagi (da'volar) qismi.</summary>
        public JsonDocument LastTokenClaims()
        {
            var parts = Handler.Requests[^1].Token.Split('.');

            parts.Should().HaveCount(3, "HS256 JWT uch qismdan iborat");

            return JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        }

        public void Dispose() => _http.Dispose();
    }

    private sealed record CapturedRequest(Uri Url, string Token, string Body);

    /// <summary>So'rovni yozib oladi va javobni test boshqaradi.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string ResponseBody { get; set; } = "{}";

        /// <summary>Tarmoq nosozligini taqlid qilish uchun.</summary>
        public Exception? Throw { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.Parameter ?? string.Empty,
                body));

            if (Throw is not null)
                throw Throw;

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedOptions<TOptions>(TOptions value) : IRuntimeOptions<TOptions>
        where TOptions : class
    {
        public TOptions Current => value;
    }
}
