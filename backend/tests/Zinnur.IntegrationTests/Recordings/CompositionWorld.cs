using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Jobs;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Jobs;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.IntegrationTests.Jobs;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// Tungi yig'ish testlari uchun fixture.
///
/// ── NIMA HAQIQIY, NIMA SOXTA VA NIMA UCHUN ──────────────────────────────
///
///   • BAZA — HAQIQIY. Bu to'plamdagi eng muhim narsa
///     <c>FOR UPDATE SKIP LOCKED</c> bilan qator egallash, ya'ni AYNAN
///     Postgres xatti-harakati. Soxta ombor bilan test hech nimani
///     isbotlamasdi.
///   • OMBOR — HAQIQIY (MinIO). Yakuniy kalitdagi obyektni o'chirish va
///     xom fayllarni tozalash SigV4 imzosi bilan tekshiriladi.
///   • LIVEKIT — SOXTA (<c>EgressSpy</c>, <see cref="RoomQuerySpy"/>):
///     uni testda ko'tarish alohida konteyner talab qiladi va bizga
///     uning javobi emas, BIZNING xatti-harakatimiz kerak.
///   • SOAT — BOSHQARILADIGAN: tungi oyna va ijara muddatlari soatlab
///     kutishni talab qilardi.
/// </summary>
public class CompositionFactory : RecordingFactory
{
    /// <summary>Vaqtni test boshqaradi (ijara muddati, tungi oyna).</summary>
    public MutableTimeProvider Clock { get; } = new();

    /// <summary>LiveKit'ning joriy holati — testlar uni to'ldiradi.</summary>
    public RoomQuerySpy Rooms { get; } = new();

    /// <summary>
    /// Ishchi papka HAR YUGURISHGA O'ZINIKI: parallel test sinflari
    /// bir-birining papkasini o'chirib yubormasin.
    /// </summary>
    public string ScratchPath { get; } = Path.Combine(
        Path.GetTempPath(), "zinnur-compose-tests", Guid.NewGuid().ToString("N")[..8]);

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        .. base.ExtraSettings(),

        // ⚠️ FON SIKLI O'CHIQ. Yoqilgan bo'lsa u test yaratgan qatorni
        //    "o'g'irlab" yig'ib qo'yardi va natija tasodifiy bo'lardi.
        //    Xizmatlarning O'ZI baribir DI'da qoladi (`CompositionSetup`)
        //    — testlar aylanishni o'zi chaqiradi.
        new("Composition:Enabled", "false"),
        new("Composition:ScratchPath", ScratchPath),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // 🔴 LiveKit holatini O'QISH porti — tiklash vazifasining
            //    eng nozik kirishi. Nosozlikni bo'sh ro'yxatdan ajratish
            //    aynan shu yerda tekshiriladi.
            services.RemoveAll<ILiveKitRoomQuery>();
            services.AddSingleton<ILiveKitRoomQuery>(Rooms);
        });
    }

    /// <summary>Bitta yig'ish aylanishi — kompozitor worker'i qanday chaqirsa, shundayligicha.</summary>
    public async Task<CompositionCycleResult> RunCompositionAsync(CancellationToken ct = default)
    {
        using var scope = Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IRecordingCompositionRunner>()
            .RunOnceAsync(ct);
    }

    /// <summary>
    /// Trek moslashtiruvchisining BITTA yurishi — AYNAN rejalashtiruvchi
    /// kabi: yangi scope + <c>IJobRunner</c> (ya'ni Postgres advisory
    /// lock ostida). <c>RecordingFactory.RunRecordingWatchdogAsync</c>
    /// bilan AYNI naqsh.
    /// </summary>
    public async Task<JobRunResult> RunReconcileAsync()
    {
        using var scope = Services.CreateScope();

        var job = scope.ServiceProvider
            .GetServices<IScheduledJob>()
            .OfType<RecordingTrackReconcileJob>()
            .Single();

        var execution = await Services.GetRequiredService<IJobRunner>().RunAsync(job);

        // Yiqilgan vazifa JIM qolmasin: yurgizuvchi istisnoni ataylab
        // yutadi (prod'da bu to'g'ri), testda esa sabab ko'rinishi kerak.
        return execution.Outcome == JobOutcome.Failed
            ? throw new InvalidOperationException($"Moslashtiruvchi yiqildi: {execution.ErrorMessage}")
            : execution.Result;
    }

    /// <summary>Navbatdan qator egallashga urinadi (o'z scope'ida).</summary>
    public async Task<CompositionClaim?> ClaimAsync(TimeSpan lease)
    {
        using var scope = Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IRecordingCompositionStore>()
            .ClaimAsync(lease);
    }

    public override string ToString() => $"CompositionFactory({DatabaseName})";
}

/// <summary>
/// Yig'uvchisi SOXTA fixture: ffmpeg yurgizmasdan aylanish mantig'ini
/// (uzilish, qayta urinish, tozalash) tekshirish uchun.
/// </summary>
public sealed class FakeComposerFactory : CompositionFactory
{
    public ComposerSpy Composer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRecordingComposer>();
            services.AddSingleton<IRecordingComposer>(Composer);
        });
    }
}

/// <summary>Soxta yig'uvchi: rejani yozib boradi, natijani test boshqaradi.</summary>
public sealed class ComposerSpy : IRecordingComposer
{
    /// <summary>Kelgan rejalar — testlar filtr grafini ham tekshiradi.</summary>
    public List<CompositionPlan> Plans { get; } = [];

    /// <summary>Standart javob: kichik, tayyor fayl.</summary>
    public CompositionResult Result { get; set; } = CompositionResult.Ok(1024, 60, []);

    /// <summary>
    /// To'liq boshqaruv (masalan bekor qilishni taqlid qilish uchun).
    /// Berilgan bo'lsa <see cref="Result"/> ishlatilmaydi.
    /// </summary>
    public Func<CompositionPlan, CancellationToken, Task<CompositionResult>>? OnCompose { get; set; }

    public int ScratchCleanups { get; private set; }

    /// <inheritdoc />
    public async Task<CompositionResult> ComposeAsync(
        CompositionPlan plan, CancellationToken ct = default)
    {
        Plans.Add(plan);

        if (OnCompose is not null)
            return await OnCompose(plan, ct);

        ct.ThrowIfCancellationRequested();

        return Result;
    }

    /// <inheritdoc />
    public Task<int> CleanScratchAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        ScratchCleanups++;

        return Task.FromResult(0);
    }
}

/// <summary>
/// Soxta <see cref="ILiveKitRoomQuery"/>.
///
/// 🔴 STANDART JAVOB — MUVAFFAQIYATLI BO'SH RO'YXAT. Testlar nosozlikni
/// ATAYLAB yoqadi (<see cref="Participants"/> / <see cref="Egresses"/> ga
/// <c>Fail</c> berib), chunki aynan o'sha holatda ikkinchi mikser yoqilib
/// ketishi mumkin edi.
/// </summary>
public sealed class RoomQuerySpy : ILiveKitRoomQuery
{
    public LiveKitTrackListResult Participants { get; set; } = LiveKitTrackListResult.Ok([]);

    public LiveKitEgressListResult Egresses { get; set; } = LiveKitEgressListResult.Ok([]);

    public List<string> ParticipantCalls { get; } = [];

    public List<string> EgressCalls { get; } = [];

    /// <inheritdoc />
    public Task<LiveKitTrackListResult> ListParticipantsAsync(
        string roomName, CancellationToken ct = default)
    {
        ParticipantCalls.Add(roomName);

        return Task.FromResult(Participants);
    }

    /// <inheritdoc />
    public Task<LiveKitEgressListResult> ListEgressAsync(
        string roomName, CancellationToken ct = default)
    {
        EgressCalls.Add(roomName);

        return Task.FromResult(Egresses);
    }
}

/// <summary>Tungi yig'ish testlari uchun umumiy quruvchilar.</summary>
internal static class CompositionWorld
{
    /// <summary>
    /// <c>TrackComposition</c> yozuv qatori.
    ///
    /// ⚠️ TO'G'RIDAN-TO'G'RI BAZAGA, `AutoRecordingScheduler` orqali EMAS:
    /// uni quvurga sezgir qilish M7 ning ishi (§5.9-2) va u hali
    /// yozilmagan.
    /// </summary>
    public static Task<long> AddRecordingAsync(
        ZinnurApiFactory factory,
        long sessionId,
        RecordingCompositionStatus? composition = RecordingCompositionStatus.Queued,
        string? objectKey = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? leaseUntil = null,
        int attempts = 0,
        int interruptions = 0,
        RecordingStatus status = RecordingStatus.Active) =>
        factory.WithDbAsync(async db =>
        {
            var recording = new SessionRecording
            {
                SessionId = sessionId,
                Status = status,
                Pipeline = RecordingPipeline.TrackComposition,
                CompositionStatus = composition,
                CompositionAttempts = attempts,
                CompositionInterruptions = interruptions,
                CompositionLeaseUntil = leaseUntil,
                ObjectKey = objectKey ?? $"recordings/test/{Guid.NewGuid():N}.mp4",
                CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            };

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();

            return recording.Id;
        });

    /// <summary>Bitta xom bo'lak qatori.</summary>
    public static Task<long> AddTrackAsync(
        ZinnurApiFactory factory,
        long recordingId,
        RecordingTrackKind kind,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        RecordingStatus status = RecordingStatus.Completed,
        string? trackSid = null,
        string? objectKey = null,
        string? egressId = null,
        int attempts = 1,
        DateTimeOffset? lastAttemptAt = null,
        DateTimeOffset? stopRequestedAt = null) =>
        factory.WithDbAsync(async db =>
        {
            var sid = trackSid ?? $"TR_{Guid.NewGuid().ToString("N")[..10]}";

            var track = new RecordingTrack
            {
                RecordingId = recordingId,
                TrackSid = sid,
                Kind = kind,
                Status = status,
                ObjectKey = objectKey ?? $"raw/test/{recordingId}/{sid}.webm",
                EgressId = egressId,
                StartedAt = startedAt,
                EndedAt = endedAt,
                Attempts = attempts,
                LastAttemptAt = lastAttemptAt,
                StopRequestedAt = stopRequestedAt,
            };

            db.RecordingTracks.Add(track);
            await db.SaveChangesAsync();

            return track.Id;
        });

    public static Task<SessionRecording> ReloadAsync(ZinnurApiFactory factory, long recordingId) =>
        factory.WithDbAsync(db => db.SessionRecordings
            .AsNoTracking()
            .Include(r => r.Tracks)
            .FirstAsync(r => r.Id == recordingId));

    public static Task<List<RecordingTrack>> TracksAsync(
        ZinnurApiFactory factory, long recordingId) =>
        factory.WithDbAsync(db => db.RecordingTracks
            .AsNoTracking()
            .Where(t => t.RecordingId == recordingId)
            .OrderBy(t => t.Id)
            .ToListAsync());
}
