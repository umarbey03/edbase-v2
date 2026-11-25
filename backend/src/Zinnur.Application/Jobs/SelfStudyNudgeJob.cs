using Zinnur.Application.SelfStudy.Nudges;

namespace Zinnur.Application.Jobs;

/// <summary>
/// Mustaqil o'quvchilarga faolsizlik eslatmalari (2026-11-25).
///
/// Har 15 daqiqada yuradi, lekin ish faqat kechki oynada (panel:
/// <c>selfstudy.nudge_hour</c>, 2 soat) bajariladi — oyna va yoqish
/// qarori servis ichida HAR YURISHDA o'qiladi (<c>PointsSyncJob</c> naqshi).
/// Oyna ichida necha marta yursa ham xavfsiz: bosqich unikal indeks va
/// «kuniga bitta xabar» qoidasi bilan himoyalangan.
/// </summary>
public sealed class SelfStudyNudgeJob(
    ISelfStudyNudgeService nudges,
    SelfStudyNudgeSettings settings) : IScheduledJob
{
    public string Name => "selfstudy-nudges";

    public TimeSpan Interval => settings.Interval;

    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var sent = await nudges.RunAsync(ct).ConfigureAwait(false);

        return sent == 0
            ? JobRunResult.Nothing
            : new JobRunResult(Processed: sent, Skipped: 0, Note: "mustaqil eslatmalar");
    }
}

public sealed record SelfStudyNudgeSettings(TimeSpan Interval);
