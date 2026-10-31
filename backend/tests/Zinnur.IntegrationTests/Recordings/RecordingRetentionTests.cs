using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Jobs;
using Zinnur.Application.Recordings.Jobs;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// YOZUVLARNI SAQLASH MUDDATI (2026-10-31) — <see cref="RecordingRetentionJob"/>
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 VAZIFA MA'LUMOTNI DOIMIY O'CHIRADI. Shuning uchun testlarning ko'pi
/// "ORTIQCHASINI o'chirmadimi" degan savolni qulflaydi:
///
///   • muddatdan YANGI yozuvga tegilmaydi;
///   • jarayondagi (yozilayotgan / montaj navbatidagi) yozuvga tegilmaydi;
///   • ombor o'chirmasa QATOR QOLADI (fayl yetim qolmasin) va keyingi
///     yurishda o'chiriladi;
///   • ikkinchi yurish hech nima qilmaydi (idempotent).
///
/// ── NIMA UCHUN OMBOR SOXTA ─────────────────────────────────────────────
/// Haqiqiy MinIO "o'chirdimi" ni tasdiqlaydi, lekin "ombor yiqildi" holatini
/// yasay olmaydi — eng muhim qoida (qator qolishi) aynan o'sha holatda.
/// SigV4 bilan o'chirishning O'ZI <c>RecordingRawStorageTests</c> da haqiqiy
/// omborda sinalgan.
///
/// ⚠️ Sinf ichidagi testlar BITTA bazani bo'lishadi, vazifa esa GLOBAL —
/// shuning uchun tasdiqlar SANOQQA emas, aniq QATOR va KALITLARGA qaraydi.
/// </summary>
public sealed class RecordingRetentionTests(RetentionFactory factory)
    : IClassFixture<RetentionFactory>
{
    private static readonly TimeSpan LongAgo = TimeSpan.FromDays(45);

    private static readonly TimeSpan Recently = TimeSpan.FromDays(3);

    [Fact]
    public async Task OldFinishedRecording_IsDeletedFromStorageAndDatabase()
    {
        var sessionId = await SessionAsync("retold");

        var id = await SeedAsync(sessionId, LongAgo, RecordingStatus.Completed, withTrack: true);
        var keys = await KeysOfAsync(id);

        var result = await factory.RunRetentionAsync();

        result.Processed.Should().BeGreaterThanOrEqualTo(1);
        (await ExistsAsync(id)).Should().BeFalse("muddatdan eski yozuv ro'yxatda qolmasligi kerak");
        factory.Storage.Deleted.Should().Contain(keys, "yakuniy fayl ham, xom bo'lak ham ombordan ketadi");
    }

    /// <summary>🔴 ENG MUHIM XAVFSIZLIK TESTI: muddat ichidagi yozuvga tegilmaydi.</summary>
    [Fact]
    public async Task RecentRecording_IsNeverTouched()
    {
        var sessionId = await SessionAsync("retnew");

        var id = await SeedAsync(sessionId, Recently, RecordingStatus.Completed, withTrack: true);
        var keys = await KeysOfAsync(id);

        await factory.RunRetentionAsync();

        (await ExistsAsync(id)).Should().BeTrue();
        factory.Storage.Deleted.Should().NotContain(keys);
    }

    /// <summary>
    /// <c>EndedAt</c> yangi bo'lsa (dars uzoq davom etgan yoki qator kech
    /// yaratilgan), <c>CreatedAt</c> eski bo'lsa ham — tegilmaydi: muddat
    /// ikkalasining KECHROG'idan sanaladi.
    /// </summary>
    [Fact]
    public async Task RecentEnd_ProtectsAnOldCreatedRow()
    {
        var sessionId = await SessionAsync("retend");

        var id = await SeedAsync(
            sessionId, LongAgo, RecordingStatus.Completed, endedAgo: Recently);

        await factory.RunRetentionAsync();

        (await ExistsAsync(id)).Should().BeTrue();
    }

    /// <summary>Hali yozilayotgan yoki montaj navbatidagi yozuvga tegilmaydi.</summary>
    [Fact]
    public async Task UnfinishedRecordings_AreSkipped()
    {
        var sessionId = await SessionAsync("retbusy");

        var active = await SeedAsync(sessionId, LongAgo, RecordingStatus.Active);
        var queued = await SeedAsync(
            sessionId, LongAgo, RecordingStatus.Completed,
            composition: RecordingCompositionStatus.Queued);

        await factory.RunRetentionAsync();

        (await ExistsAsync(active)).Should().BeTrue();
        (await ExistsAsync(queued)).Should().BeTrue();
    }

    /// <summary>
    /// 🔴 OMBOR O'CHIRMASA QATOR QOLADI — aks holda fayl R2'da MANGU qolardi
    /// va uni hech kim topa olmasdi. Ombor tiklangach keyingi yurish
    /// tugatadi (idempotentlik).
    /// </summary>
    [Fact]
    public async Task StorageFailure_KeepsTheRow_AndNextRunFinishes()
    {
        var sessionId = await SessionAsync("retfail");

        var id = await SeedAsync(sessionId, LongAgo, RecordingStatus.Failed);
        var key = (await KeysOfAsync(id)).Single();

        factory.Storage.Failing.TryAdd(key, 0);

        var first = await factory.RunRetentionAsync();

        first.Skipped.Should().BeGreaterThanOrEqualTo(1);
        (await ExistsAsync(id)).Should().BeTrue("fayl o'chmagan — qator uni topishning yagona yo'li");

        factory.Storage.Failing.TryRemove(key, out _);

        await factory.RunRetentionAsync();

        (await ExistsAsync(id)).Should().BeFalse();

        // Uchinchi yurish shu qatorga qaytmaydi.
        var before = factory.Storage.Deleted.Count(k => k == key);
        await factory.RunRetentionAsync();
        factory.Storage.Deleted.Count(k => k == key).Should().Be(before);
    }

    /// <summary>Ro'yxat ekranlari "N kungacha saqlanadi" ni AYNI qiymatdan ko'rsatadi.</summary>
    [Fact]
    public async Task SectionEndpoint_ReportsTheRetentionPeriod()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var section = await client.GetFromJsonAsync<SectionBody>("/api/v1/recordings/section");

        section!.RetentionDays.Should().Be(RecordingRetentionJob.DefaultDays);
    }

    private sealed record SectionBody(bool Visible, int RetentionDays);

    // ================================================================= yordamchilar

    private async Task<long> SessionAsync(string prefix)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);

        return await RecordingWorld.AddSessionAsync(factory, world.GroupId, SessionStatus.Ended);
    }

    private Task<long> SeedAsync(
        long sessionId,
        TimeSpan age,
        RecordingStatus status,
        bool withTrack = false,
        TimeSpan? endedAgo = null,
        RecordingCompositionStatus? composition = null) =>
        factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;

            var recording = new SessionRecording
            {
                SessionId = sessionId,
                Status = status,
                ObjectKey = $"recordings/test/{Guid.NewGuid():N}.mp4",
                CreatedAt = now - age,
                EndedAt = status is RecordingStatus.Completed or RecordingStatus.Failed
                    ? now - (endedAgo ?? age)
                    : null,
                CompositionStatus = composition,
                Pipeline = composition is null
                    ? RecordingPipeline.RoomComposite
                    : RecordingPipeline.TrackComposition,
            };

            if (withTrack)
            {
                recording.Tracks.Add(new RecordingTrack
                {
                    TrackSid = "TR_" + Guid.NewGuid().ToString("N")[..10],
                    Kind = RecordingTrackKind.CameraVideo,
                    ObjectKey = $"raw/test/{Guid.NewGuid():N}.webm",
                    Status = RecordingStatus.Completed,
                });
            }

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();

            return recording.Id;
        });

    private Task<List<string>> KeysOfAsync(long recordingId) =>
        factory.WithDbAsync(async db =>
        {
            var recording = await db.SessionRecordings.AsNoTracking()
                .Include(r => r.Tracks)
                .FirstAsync(r => r.Id == recordingId);

            return recording.Tracks.Select(t => t.ObjectKey).Append(recording.ObjectKey).ToList();
        });

    private Task<bool> ExistsAsync(long recordingId) =>
        factory.WithDbAsync(db => db.SessionRecordings.AnyAsync(r => r.Id == recordingId));
}

/// <summary>Soxta ombor bilan fixture (sabab: sinf izohida).</summary>
public sealed class RetentionFactory : RecordingFactory
{
    public RetentionStorageSpy Storage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRecordingStorage>();
            services.AddSingleton<IRecordingStorage>(Storage);
        });
    }

    /// <summary>Vazifani rejalashtiruvchi kabi yurgizadi: yangi scope + QULF.</summary>
    public async Task<JobRunResult> RunRetentionAsync()
    {
        using var scope = Services.CreateScope();

        var job = scope.ServiceProvider
            .GetServices<IScheduledJob>()
            .OfType<RecordingRetentionJob>()
            .Single();

        var execution = await Services.GetRequiredService<IJobRunner>().RunAsync(job);

        return execution.Outcome == JobOutcome.Failed
            ? throw new InvalidOperationException($"Vazifa yiqildi: {execution.ErrorMessage}")
            : execution.Result;
    }
}

/// <summary>
/// Faqat o'chirishni yozib boradigan soxta ombor. <see cref="Failing"/>
/// dagi kalit uchun ombor "javob bermaydi".
/// </summary>
public sealed class RetentionStorageSpy : IRecordingStorage
{
    public ConcurrentQueue<string> Deleted { get; } = new();

    public ConcurrentDictionary<string, byte> Failing { get; } = new(StringComparer.Ordinal);

    public bool IsConfigured => true;

    public Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        if (Failing.ContainsKey(objectKey))
            throw new ServiceUnavailableException("Ombor javob bermadi (test).");

        Deleted.Enqueue(objectKey);

        return Task.CompletedTask;
    }

    public string BuildObjectKey(long sessionId) => $"recordings/test/{sessionId}.mp4";

    public string BuildRawObjectKey(long sessionId, long recordingId, string trackSid, string extension) =>
        $"raw/{sessionId}/{recordingId}/{trackSid}.{extension.TrimStart('.')}";

    public Uri CreateViewLink(string objectKey, TimeSpan ttl) =>
        new($"http://storage.test/{objectKey}");

    public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken ct = default) =>
        Task.FromResult<StoredObjectInfo?>(null);

    public Task<StoredRecordingObject?> OpenReadAsync(string objectKey, CancellationToken ct = default) =>
        Task.FromResult<StoredRecordingObject?>(null);

    public Task PutAsync(
        string objectKey, Stream content, long length, string contentType, CancellationToken ct = default) =>
        Task.CompletedTask;
}
