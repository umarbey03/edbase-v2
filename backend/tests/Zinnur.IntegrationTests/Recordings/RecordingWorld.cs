using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Options;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// Dars yozuvi testlari uchun fixture.
///
/// ── NIMA UCHUN EGRESS SOXTA (spy), OMBOR esa HAQIQIY ────────────────────
///
/// Ular ikki xil turdagi xavfni ifodalaydi:
///
///   • EGRESS — tashqi XIZMAT. Uni testda ko'tarish alohida konteyner
///     (livekit/egress + Redis) talab qiladi va u CI'da yo'q. Bizga esa
///     uning javobi emas, BIZNING xatti-harakatimiz kerak: urinish
///     sanaladimi, xato yozuvni `Requested` da qoldiradimi, ikkinchi
///     tugma bosilganda ikkinchi egress boshlanmaydimi. Soxta xizmat
///     buni ANIQ va tez tekshiradi.
///
///   • OMBOR — PROTOKOL. Bu yerda tekshiriladigan narsa aynan qatlamlar
///     chegarasi: SigV4 query-string imzosi, path-style manzil, port
///     bilan host. Soxta ombor bularning hammasini "to'g'ri" deb qabul
///     qilardi va yashil natija hech nimani isbotlamasdi
///     (<c>StorageBackedApiFactory</c> izohidagi AYNI mulohaza).
///
/// ⚠️ <c>StorageBackedApiFactory</c> QAYTA ISHLATILMADI: u <c>sealed</c>
/// va bu fixture'ga qo'shimcha ravishda <c>Storage:PublicUrl</c> hamda
/// egress josusi kerak. Ombor qiymatlari AYNI muhit o'zgaruvchilaridan
/// olinadi — ikkita boshqa-boshqa manzil bo'lib qolmasin.
/// </summary>
public class RecordingFactory : ZinnurApiFactory
{
    /// <summary>Egress josusi — testlar uning yozuvlarini tekshiradi.</summary>
    public EgressSpy Egress { get; } = new();

    /// <summary>
    /// Har yugurishga O'Z prefiksi: bitta bucket ko'p marta ishlatiladi va
    /// eski yugurishlardan qolgan obyektlar aralashib ketmasin.
    /// </summary>
    public string KeyPrefix { get; } = "itest-rec/" + Guid.NewGuid().ToString("N")[..8];

    private static string ServiceUrl =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_URL") ?? "http://localhost:9010";

    private static string Bucket =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_BUCKET") ?? "zinnur-dev";

    private static string AccessKey =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_ACCESS_KEY") ?? "zinnur_dev_minio";

    private static string SecretKey =>
        Environment.GetEnvironmentVariable("TEST_STORAGE_SECRET_KEY") ?? "zinnur_dev_minio_secret";

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Storage:ServiceUrl", ServiceUrl),
        new("Storage:Bucket", Bucket),
        new("Storage:AccessKey", AccessKey),
        new("Storage:SecretKey", SecretKey),
        new("Storage:Region", "us-east-1"),
        new("Storage:KeyPrefix", KeyPrefix),
        new("Storage:TimeoutSeconds", "15"),

        // ★ TESTDA IKKALA MANZIL BIR XIL: test jarayoni ham, "brauzer" ham
        //   AYNI mashinada. Prod'da esa ular farq qiladi va aynan shuning
        //   uchun ikkita sozlama bor (izoh: `StorageOptions.PublicUrl`).
        new("Storage:PublicUrl", ServiceUrl),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            // Haqiqiy klientni OLIB TASHLAYMIZ — aks holda qaysi biri
            // ishlashini ro'yxat tartibi hal qilardi (`JobFactory` naqshi).
            services.RemoveAll<ILiveKitEgress>();
            services.AddSingleton<ILiveKitEgress>(Egress);
        });
    }
}

/// <summary>
/// Soxta Egress: chaqiruvlarni yozib boradi va javobni test boshqaradi.
/// </summary>
public sealed class EgressSpy : ILiveKitEgress
{
    private int _sequence;

    /// <inheritdoc />
    public bool IsConfigured { get; set; } = true;

    /// <summary>
    /// <c>null</c> — muvaffaqiyat; aks holda AYNAN shu matn bilan xato.
    /// </summary>
    public string? FailWith { get; set; }

    /// <summary><c>StopEgress</c> qabul qilinadimi (LiveKit rad etishi NORMAL).</summary>
    public bool StopAccepted { get; set; } = true;

    public List<EgressStartRequest> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    /// <summary>Oxirgi muvaffaqiyatli urinishda berilgan identifikator.</summary>
    public string LastEgressId { get; private set; } = string.Empty;

    /// <inheritdoc />
    public Task<EgressStartResult> StartRoomRecordingAsync(
        EgressStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Started.Add(request);

        if (FailWith is { Length: > 0 } error)
            return Task.FromResult(EgressStartResult.Fail(error));

        // ⚠️ HAR URINISHGA YANGI ID: `UX_SessionRecordings_EgressId`
        //    unikal, ya'ni takrorlanuvchi qiymat ikkinchi testda
        //    `SaveChanges` ni 23505 bilan yiqitardi.
        // Kesish (`[..24]`) `string.Create(...)` DAN TASHQARIDA: ichkarida
        // qolsa ikkinchi argument interpolatsiya ishlovchisi emas, oddiy satr
        // bo'lib qoladi va `ref` talab qilinadi (CS1620).
        LastEgressId = string.Create(
            CultureInfo.InvariantCulture,
            $"EG_{Interlocked.Increment(ref _sequence)}_{Guid.NewGuid():N}")[..24];

        return Task.FromResult(EgressStartResult.Ok(LastEgressId));
    }

    /// <inheritdoc />
    public Task<bool> StopRecordingAsync(string egressId, CancellationToken ct = default)
    {
        Stopped.Add(egressId);

        return Task.FromResult(StopAccepted);
    }
}

/// <summary>
/// Umumiy quruvchilar: dars, yozuv qatori va IMZOLANGAN webhook so'rovi.
/// </summary>
internal static class RecordingWorld
{
    /// <summary>Jonli (yoki boshqa holatdagi) dars — to'g'ridan-to'g'ri bazaga.</summary>
    public static Task<long> AddSessionAsync(
        ZinnurApiFactory factory,
        long groupId,
        SessionStatus status = SessionStatus.Live,
        long? hostId = null) =>
        factory.WithDbAsync(async db =>
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(-10);

            var session = new LiveSession
            {
                GroupId = groupId,
                HostId = hostId,
                Type = SessionType.Teacher,
                Status = status,
                ScheduledStart = start,
                ScheduledEnd = start.AddMinutes(80),
                ActualStart = status == SessionStatus.Scheduled ? null : start,
                ActualEnd = status == SessionStatus.Ended ? start.AddMinutes(80) : null,
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);
            await db.SaveChangesAsync();

            return session.Id;
        });

    /// <summary>Tayyor holatdagi yozuv qatori (webhook va watchdog testlari uchun).</summary>
    public static Task<long> AddRecordingAsync(
        ZinnurApiFactory factory,
        long sessionId,
        RecordingStatus status = RecordingStatus.Starting,
        string? egressId = null,
        string? objectKey = null,
        int attempts = 1,
        DateTimeOffset? lastAttemptAt = null,
        DateTimeOffset? stopRequestedAt = null) =>
        factory.WithDbAsync(async db =>
        {
            var recording = new SessionRecording
            {
                SessionId = sessionId,
                Status = status,
                EgressId = egressId,
                ObjectKey = objectKey ?? $"recordings/test/{Guid.NewGuid():N}.mp4",
                Attempts = attempts,
                LastAttemptAt = lastAttemptAt,
                StopRequestedAt = stopRequestedAt,
                StartedAt = status is RecordingStatus.Active ? DateTimeOffset.UtcNow.AddMinutes(-5) : null,
            };

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();

            return recording.Id;
        });

    public static Task<SessionRecording> ReloadAsync(ZinnurApiFactory factory, long recordingId) =>
        factory.WithDbAsync(db => db.SessionRecordings.AsNoTracking()
            .FirstAsync(r => r.Id == recordingId));

    // ================================================================= webhook

    /// <summary>LiveKit sozlamalarining AMALDAGI qiymatlari (kalit + sir).</summary>
    public static LiveKitOptions LiveKitOf(ZinnurApiFactory factory) =>
        factory.Services.GetRequiredService<IRuntimeOptions<LiveKitOptions>>().Current;

    /// <summary>
    /// LiveKit AYNAN shunday imzolaydi: HS256 JWT, ichida <c>iss</c>,
    /// <c>exp</c>/<c>nbf</c> va TANANING base64 SHA-256 xeshi.
    /// </summary>
    /// <param name="bodyForHash">
    /// Xesh hisoblanadigan tana. Testlar buni ATAYLAB haqiqiy tanadan
    /// FARQLI qilib beradi — "yaroqli token + o'zgartirilgan tana"
    /// holatini tekshirish uchun.
    /// </param>
    public static string SignToken(
        LiveKitOptions keys,
        string bodyForHash,
        string algorithm = "HS256",
        int expiresInSeconds = 300,
        string? issuer = null,
        bool includeBodyHash = true)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var header = $$"""{"alg":"{{algorithm}}","typ":"JWT"}""";
        var now = DateTimeOffset.UtcNow;

        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(bodyForHash)));

        var claims = new StringBuilder("{");

        claims.Append(CultureInfo.InvariantCulture, $"\"iss\":\"{issuer ?? keys.ApiKey}\",");
        claims.Append(CultureInfo.InvariantCulture, $"\"nbf\":{now.ToUnixTimeSeconds()},");
        claims.Append(
            CultureInfo.InvariantCulture,
            $"\"exp\":{now.AddSeconds(expiresInSeconds).ToUnixTimeSeconds()}");

        if (includeBodyHash)
            claims.Append(CultureInfo.InvariantCulture, $",\"sha256\":\"{hash}\"");

        claims.Append('}');

        var signingInput = string.Concat(
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header)),
            ".",
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes(claims.ToString())));

        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(keys.ApiSecret),
            Encoding.UTF8.GetBytes(signingInput));

        return string.Concat(signingInput, ".", Base64Url.EncodeToString(signature));
    }

    /// <summary>Webhook so'rovi: tana XOM baytlarda, imzo <c>Authorization</c> da.</summary>
    public static HttpRequestMessage Request(string body, string? token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri("/api/v1/livekit/webhook", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }

    /// <summary>Egress hodisasining tanasi (LiveKit protojson shakli).</summary>
    public static string EgressEvent(
        string eventName,
        string egressId,
        string status,
        string? eventId = null,
        string? objectKey = null,
        long? sizeBytes = null,
        long? durationNanos = null,
        string? error = null)
    {
        var id = eventId ?? "EV_" + Guid.NewGuid().ToString("N")[..12];

        var file = objectKey is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $$""","file_results":[{"filename":"{{objectKey}}","size":"{{sizeBytes ?? 0}}","duration":"{{durationNanos ?? 0}}"}]""");

        var errorField = error is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $$""","error":"{{error}}" """).TrimEnd();

        // `$$$` (uchta dollar) ATAYLAB: satr oxiridagi `}}` — JSON'ning LITERAL
        // yopuvchi qavslari (`egress_info` va ildiz obyekti). `$$` bilan ular
        // interpolatsiya teshigining yopilishi deb o'qiladi (CS9007). Uchta
        // dollarda teshik `{{{...}}}` bo'ladi, ikkita `}}` esa matn bo'lib qoladi.
        return string.Create(
            CultureInfo.InvariantCulture,
            $$$"""
              {"event":"{{{eventName}}}","id":"{{{id}}}","egress_info":{"egress_id":"{{{egressId}}}","status":"{{{status}}}"{{{errorField}}}{{{file}}}}}
              """);
    }
}
