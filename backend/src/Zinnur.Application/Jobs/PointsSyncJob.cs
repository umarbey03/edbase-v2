using Zinnur.Application.Progress.Services;

namespace Zinnur.Application.Jobs;

/// <summary>
/// Ball daftarini manba jadvallari bilan tenglashtiradi (2026-10-23).
///
/// ★ SHARTSIZ RO'YXATDAN O'TADI (`ChatRetentionJob` naqshi): yoqish/o'chirish
///   qarori paneldagi <c>gamification.points_enabled</c> sozlamasida va
///   servis uni HAR YURISHDA o'qiydi.
/// </summary>
public sealed class PointsSyncJob(
    IPointsSyncService points,
    PointsSyncSettings settings) : IScheduledJob
{
    public string Name => "points-sync";

    public TimeSpan Interval => settings.Interval;

    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var changed = await points.SyncAsync(ct).ConfigureAwait(false);

        return changed == 0
            ? JobRunResult.Nothing
            : new JobRunResult(Processed: changed, Skipped: 0, Note: "ball daftari");
    }
}

public sealed record PointsSyncSettings(TimeSpan Interval);
