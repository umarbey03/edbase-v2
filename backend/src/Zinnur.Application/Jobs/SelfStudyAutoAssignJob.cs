using Zinnur.Application.SelfStudy.Pool;

namespace Zinnur.Application.Jobs;

/// <summary>
/// MUSTAQIL O'QUVCHILARNI KURATORGA AVTOMATIK BIRIKTIRISH (2026-11-20).
///
/// Obuna qayerdan kelmasin (xodim berdi, buyurtma tasdiqlandi, balansdan
/// to'landi) — kuratorsiz o'quvchi shu yurishda ro'yxatdagi birinchi bo'sh
/// kuratorga biriktiriladi; hammasi to'lsa xodimlarga kunlik eslatma boradi.
/// Qoida: <see cref="CuratorPoolService.AutoAssignAsync"/>.
///
/// SHARTSIZ ro'yxatdan o'tadi: ro'yxat bo'sh bo'lsa (kurator qo'shilmagan)
/// navbat ham taqsimlanmaydi — bitta arzon so'rov va chiqish.
/// </summary>
public sealed class SelfStudyAutoAssignJob(ICuratorPoolService pool, TimeSpan interval) : IScheduledJob
{
    public string Name => "selfstudy-auto-assign";

    public TimeSpan Interval => interval;

    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var result = await pool.AutoAssignAsync(ct).ConfigureAwait(false);

        return result.Assigned == 0 && result.StillWaiting == 0
            ? JobRunResult.Nothing
            : new JobRunResult(Processed: result.Assigned, Skipped: result.StillWaiting, Note: "mustaqil kurator");
    }
}
