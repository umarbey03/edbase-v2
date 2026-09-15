using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// KLIENT DIAGNOSTIKASI — <c>POST /live-sessions/{id}/client-events</c>
/// ========================================================================
///
/// Uch narsa isbotlanadi:
///
///  1) RUXSAT token bilan AYNI: a'zo o'quvchi va host — 204, begona
///     o'quvchi — 403, anonim — 401. Begona odam boshqa guruh darsining
///     logiga yozib, tergovni chalg'ita olmasligi kerak.
///
///  2) YAKUNLANGAN va REJADAGI dars ham qabul qilinadi — klient hodisalarni
///     dars tugagan zahoti yuboradi va aynan o'sha qatorlar eng qimmatli.
///
///  3) HAR HODISA — BITTA LOG QATORI, satrlar tozalangan va qirqilgan.
///     Endpointning butun natijasi shu qatorlar: 204 ning o'zi hech narsani
///     isbotlamaydi (bo'sh metod ham 204 qaytarardi).
/// </summary>
public sealed class LiveSessionClientEventsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>2026-05-14 14:00 UTC = 19:00 Toshkent.</summary>
    private static readonly DateTimeOffset MayEvening =
        new(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// <c>LiveSessionClientLog.ClientEvent</c> ning EventId'si. Sinf
    /// <c>internal</c>, shuning uchun raqam shu yerda takrorlanadi — u
    /// o'zgarsa loglarni <c>jq</c> bilan qidirish yo'riqnomasi ham eskiradi,
    /// ya'ni testning yiqilishi o'rinli.
    /// </summary>
    private const int ClientEventLogId = 6700;

    // ================================================================= ruxsat

    /// <summary>
    /// ★ DARS YAKUNLANGAN — va baribir 204. Token endpointi bunday darsga
    /// 409 beradi; bu yerda holat ATAYLAB tekshirilmaydi.
    /// </summary>
    [Fact]
    public async Task EnrolledStudent_OnEndedSession_ReturnsNoContent()
    {
        var (world, sessionId) = await WorldWithEndedSessionAsync("kev-oquv");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await PostAsync(student, sessionId, ValidBody(eventCount: 2));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));
    }

    [Fact]
    public async Task HostTeacher_OnScheduledSession_ReturnsNoContent()
    {
        var world = await WorldBuilder.CreateAsync(factory, "kev-host");
        var sessionId = await WorldBuilder.AddScheduledSessionAsync(factory, world.GroupId, MayEvening);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await PostAsync(teacher, sessionId, ValidBody(eventCount: 1));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));
    }

    [Fact]
    public async Task StudentOfAnotherGroup_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "kev-bizniki");
        var other = await WorldBuilder.CreateAsync(factory, "kev-begona");
        var sessionId = await EndedSessionAsync(world);

        using var outsider = await WorldBuilder.ClientAsync(factory, other.Student);

        var response = await PostAsync(outsider, sessionId, ValidBody(eventCount: 1));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));
    }

    [Fact]
    public async Task UnknownSession_ReturnsNotFound()
    {
        var world = await WorldBuilder.CreateAsync(factory, "kev-yoq");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await PostAsync(student, 999_999_999, ValidBody(eventCount: 1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await WorldBuilder.Body(response));
    }

    [Fact]
    public async Task Anonymous_ReturnsUnauthorized()
    {
        using var anonymous = factory.CreateClient();

        var response = await PostAsync(anonymous, 1, ValidBody(eventCount: 1));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= tuzilma

    [Fact]
    public async Task FiftyOneEvents_ReturnsBadRequest()
    {
        var (world, sessionId) = await WorldWithEndedSessionAsync("kev-51");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await PostAsync(student, sessionId, ValidBody(eventCount: 51));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await WorldBuilder.Body(response));

        // Sabab AYNAN soni — MVC'ning boshqa sababli 400 i bu testni aldamasin.
        (await ProblemText.ReadAsync(response)).Should().Contain("50 tadan ortiq");
    }

    [Fact]
    public async Task EmptyEvents_ReturnsBadRequest()
    {
        var (world, sessionId) = await WorldWithEndedSessionAsync("kev-bosh");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await PostAsync(student, sessionId, ValidBody(eventCount: 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await WorldBuilder.Body(response));
        (await ProblemText.ReadAsync(response)).Should().Contain("Kamida bitta hodisa");
    }

    /// <summary>
    /// ★ UZUN SATR — 400 EMAS. Telemetriya uzun <c>User-Agent</c> tufayli
    /// yiqilsa, eng g'alati qurilmalar (aynan tergov kerak bo'lganlar)
    /// logdan butunlay tushib qolardi.
    /// </summary>
    [Fact]
    public async Task ThousandCharUserAgent_IsAccepted()
    {
        var (world, sessionId) = await WorldWithEndedSessionAsync("kev-ua");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await PostAsync(
            student, sessionId, ValidBody(eventCount: 1, userAgent: new string('u', 1000)));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));
    }

    // ================================================================= log qatorlari

    /// <summary>
    /// ★ LOG USHLASH: ilova Serilog'da va u <c>ConfigureLogging</c> orqali
    /// qo'shilgan provayderlarni CHETLAB o'tadi (izoh:
    /// <c>UserProfileQueryCountTests</c>). Shuning uchun shu test hostida
    /// <see cref="ILoggerFactory"/> ning O'ZI almashtiriladi — servis
    /// <c>ILogger&lt;T&gt;</c> ni aynan undan oladi.
    /// </summary>
    [Fact]
    public async Task EachEvent_WritesOneCleanedLogLine()
    {
        var (world, sessionId) = await WorldWithEndedSessionAsync("kev-log");

        var capture = new ClientEventLogCapture();

        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILoggerFactory>(_ => new LoggerFactory([capture]))));

        var tokens = await factory.LoginAsync(world.Student.Id);

        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var clientAt = new DateTimeOffset(2026, 5, 14, 14, 30, 0, TimeSpan.Zero);

        var response = await PostAsync(client, sessionId, new
        {
            client = new { userAgent = new string('u', 1000), telegram = true, platform = "android" },
            events = new object[]
            {
                new
                {
                    type = "disconnected",
                    at = clientAt,
                    reason = "SIGNAL_CLOSE",
                    detail = "birinchi qator\r\nsoxta log qatori",
                    visibility = "hidden",
                    online = false,
                    network = "4g",
                    attempt = 3,
                    micOn = true,
                    cameraOn = false,
                },
                new { type = "reconnected", at = clientAt.AddSeconds(5) },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));

        var lines = capture.Entries
            .Where(e => Equals(e["SessionId"], sessionId))
            .ToList();

        lines.Should().HaveCount(2, "har hodisa AYNAN bitta log qatori");

        var first = lines.Single(e => Equals(e["EventType"], "disconnected"));

        first["UserId"].Should().Be(world.Student.Id);
        first["Role"].Should().Be(nameof(UserRole.Student));
        first["IsHost"].Should().Be(false);
        first["Reason"].Should().Be("SIGNAL_CLOSE");
        first["Visibility"].Should().Be("hidden");
        first["Online"].Should().Be(false);
        first["Network"].Should().Be("4g");
        first["Attempt"].Should().Be(3);
        first["MicOn"].Should().Be(true);
        first["CameraOn"].Should().Be(false);
        first["ClientAt"].Should().Be(clientAt);
        first["ServerAt"].Should().BeOfType<DateTimeOffset>();
        first["Telegram"].Should().Be(true);
        first["Platform"].Should().Be("android");

        first["Detail"].Should().Be(
            "birinchi qator  soxta log qatori",
            "yangi qator soxta log qatori yasay olardi — u bo'shliqqa almashadi");

        first["UserAgent"].Should().BeOfType<string>()
            .Which.Should().HaveLength(300, "uzun satr 400 emas, JIM qirqiladi");

        var second = lines.Single(e => Equals(e["EventType"], "reconnected"));

        second["Reason"].Should().BeNull();
        second["Attempt"].Should().BeNull();
        second["UserAgent"].Should().Be(first["UserAgent"], "qurilma ma'lumoti paketdagi HAR qatorga yoziladi");
    }

    // ================================================================= yordamchi

    private Task<long> EndedSessionAsync(StudentWorld world) =>
        WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>());

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, long sessionId, object body) =>
        client.PostAsJsonAsync($"/api/v1/live-sessions/{sessionId}/client-events", body);

    private static object ValidBody(int eventCount, string userAgent = "Mozilla/5.0 (Linux; Android 14)") =>
        new
        {
            client = new { userAgent, telegram = false },
            events = Enumerable.Range(0, eventCount)
                .Select(i => new
                {
                    type = "visibility",
                    at = MayEvening.AddSeconds(i),
                    visibility = "hidden",
                })
                .ToArray(),
        };

    private async Task<(StudentWorld World, long SessionId)> WorldWithEndedSessionAsync(string prefix)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);

        return (world, await EndedSessionAsync(world));
    }

    /// <summary>
    /// Faqat klient hodisasi qatorlarini ushlaydi — qolgan loglar (EF, MVC)
    /// o'lchovga aralashmasin.
    /// </summary>
    private sealed class ClientEventLogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> _entries = new();

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            ConcurrentQueue<IReadOnlyDictionary<string, object?>> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (eventId.Id != ClientEventLogId
                    || state is not IReadOnlyList<KeyValuePair<string, object?>> properties)
                    return;

                entries.Enqueue(properties.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
            }
        }
    }
}
