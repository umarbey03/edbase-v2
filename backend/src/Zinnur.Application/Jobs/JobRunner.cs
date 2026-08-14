using Microsoft.Extensions.Logging;

namespace Zinnur.Application.Jobs;

/// <summary>
/// <see cref="IJobRunner"/> ning amalga oshirilishi.
///
/// TARTIB QAT'IY: qulf OLINADI → vazifa bajariladi → qulf HALI HAM
/// bizdami tekshiriladi → qulf bo'shatiladi → natija logga yoziladi.
///
/// ★ QULF `finally` DA BO'SHATILADI: vazifa istisno bilan yiqilsa ham qulf
/// osilib qolmasin. Bu ikkinchi himoya — birinchisi ulanishning o'zi
/// (izoh: <see cref="IJobLock"/>).
/// </summary>
public sealed class JobRunner(
    IJobLock jobLock,
    TimeProvider clock,
    ILogger<JobRunner> logger) : IJobRunner
{
    /// <inheritdoc />
    public async Task<JobExecution> RunAsync(IScheduledJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var startedAt = clock.GetTimestamp();
        IJobLockHandle? handle = null;

        try
        {
            handle = await jobLock.TryAcquireAsync(job.Name, ct).ConfigureAwait(false);

            if (handle is null)
            {
                // Boshqa instance bajaryapti — bu NORMAL. `Debug` darajasi
                // ataylab: ikki instance'da bu xabar har yurishda chiqadi va
                // `Information` bo'lsa log foydali ma'lumotni ko'mib
                // tashlardi.
                JobLog.SkippedLocked(logger, job.Name);
                return new JobExecution(
                    job.Name, JobOutcome.SkippedLocked, JobRunResult.Nothing, Elapsed(startedAt));
            }

            var result = await job.RunAsync(ct).ConfigureAwait(false);
            var duration = Elapsed(startedAt);

            // Ish uzoq davom etgan bo'lsa qulf hamon bizdami — tekshiramiz.
            // Sabab: `IJobLockHandle.IsHeldAsync` izohida.
            if (!await handle.IsHeldAsync(ct).ConfigureAwait(false))
                JobLog.LockLost(logger, job.Name, duration.TotalMilliseconds);

            if (result.HasWork)
                JobLog.Completed(logger, job.Name, result.Processed, result.Skipped,
                    duration.TotalMilliseconds, result.Note);
            else
                JobLog.CompletedIdle(logger, job.Name, duration.TotalMilliseconds);

            return new JobExecution(job.Name, JobOutcome.Completed, result, duration);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Ilova to'xtatilyapti — bu xato emas, yuqoriga o'tkazamiz.
            throw;
        }
        catch (Exception ex)
        {
            // ★ VAZIFA YIQILSA HAM XIZMAT O'LMAYDI. Keyingi yurishda
            // qaytadan urinamiz: vazifalar idempotent, ya'ni takror
            // bajarish xavfsiz.
            var duration = Elapsed(startedAt);
            JobLog.Failed(logger, ex, job.Name, duration.TotalMilliseconds);

            return new JobExecution(
                job.Name, JobOutcome.Failed, JobRunResult.Nothing, duration, ex.Message);
        }
        finally
        {
            if (handle is not null)
                await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobExecution>> RunAllAsync(
        IEnumerable<IScheduledJob> jobs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var executions = new List<JobExecution>();

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();

            // `RunAsync` istisno tashlamaydi (bekor qilishdan tashqari),
            // shuning uchun bu yerda try/catch KERAK EMAS — bitta vazifaning
            // yiqilishi keyingisini to'xtatmaydi.
            executions.Add(await RunAsync(job, ct).ConfigureAwait(false));
        }

        return executions;
    }

    private TimeSpan Elapsed(long startedAt) => clock.GetElapsedTime(startedAt);
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). ATAYLAB alohida sinf —
/// <c>ApiLog</c> ga tegilmaydi, modulning loglari bir joyda tursin.
/// </summary>
internal static partial class JobLog
{
    [LoggerMessage(
        EventId = 6400,
        Level = LogLevel.Information,
        Message = "Fon vazifasi bajarildi: {JobName} — o'zgardi={Processed} "
                  + "o'tkazildi={Skipped} vaqt={DurationMs}ms izoh={Note}")]
    internal static partial void Completed(
        ILogger logger, string jobName, int processed, int skipped, double durationMs, string? note);

    [LoggerMessage(
        EventId = 6401,
        Level = LogLevel.Debug,
        Message = "Fon vazifasi bajarildi, ish topilmadi: {JobName} — vaqt={DurationMs}ms")]
    internal static partial void CompletedIdle(ILogger logger, string jobName, double durationMs);

    [LoggerMessage(
        EventId = 6402,
        Level = LogLevel.Debug,
        Message = "Fon vazifasi o'tkazib yuborildi (qulf boshqa instance'da): {JobName}")]
    internal static partial void SkippedLocked(ILogger logger, string jobName);

    [LoggerMessage(
        EventId = 6403,
        Level = LogLevel.Error,
        Message = "Fon vazifasi YIQILDI: {JobName} — vaqt={DurationMs}ms. "
                  + "Keyingi yurishda qaytadan urinamiz.")]
    internal static partial void Failed(
        ILogger logger, Exception exception, string jobName, double durationMs);

    [LoggerMessage(
        EventId = 6404,
        Level = LogLevel.Warning,
        Message = "Qulf ish DAVOMIDA yo'qolgan: {JobName} — vaqt={DurationMs}ms. "
                  + "Baza ulanishi uzilgan bo'lishi mumkin; natija ikkinchi "
                  + "instance bilan kesishgan bo'lishi ehtimoli bor.")]
    internal static partial void LockLost(ILogger logger, string jobName, double durationMs);

    // ---------------------------------------------------------------- darslar

    [LoggerMessage(
        EventId = 6410,
        Level = LogLevel.Information,
        Message = "Dars avto-yakunlandi: id={SessionId} sabab={Reason}")]
    internal static partial void SessionClosed(ILogger logger, long sessionId, string reason);

    [LoggerMessage(
        EventId = 6411,
        Level = LogLevel.Warning,
        Message = "Darsni avto-yakunlab bo'lmadi, o'tkazib yuborildi: id={SessionId} sabab={Reason}")]
    internal static partial void SessionSkipped(ILogger logger, long sessionId, string reason);

    // ---------------------------------------------------------------- moliya

    [LoggerMessage(
        EventId = 6420,
        Level = LogLevel.Information,
        Message = "Oylik to'lov yozuvlari ochildi: oy={Period} yangi={Created} "
                  + "avvaldan bor={AlreadyOpen} balansdan yopildi={MonthsClosed}")]
    internal static partial void PeriodOpened(
        ILogger logger, string period, int created, int alreadyOpen, int monthsClosed);

    [LoggerMessage(
        EventId = 6421,
        Level = LogLevel.Warning,
        Message = "Oy ochishda {Count} a'zolikka tarif topilmadi (oy={Period}): {Warning}")]
    internal static partial void PeriodWarning(
        ILogger logger, int count, string period, string warning);

    // ---------------------------------------------------------------- chat tarixi

    /// <summary>
    /// 🔴 QAYTARIB BO'LMAYDIGAN AMAL — SHUNING UCHUN HAR YURISH LOGDA.
    ///
    /// Kesim SANASI ham, qo'llangan MUDDAT ham ataylab yoziladi: "nega bu
    /// guruhda mart oyidagi xabarlar yo'q?" degan savolga javob AYNAN shu
    /// qatordan topiladi, va zaxiradan tiklashda qaysi oraliq kerakligi
    /// shundan bilinadi.
    /// </summary>
    [LoggerMessage(
        EventId = 6440,
        Level = LogLevel.Information,
        Message = "Guruh chati tarixi tozalandi: o'chirildi={Deleted} muddat={Months} oy "
                  + "kesim={Cutoff:yyyy-MM-dd HH:mm}Z paket={Batches}")]
    internal static partial void ChatHistoryPurged(
        ILogger logger, int deleted, int months, DateTimeOffset cutoff, int batches);

    [LoggerMessage(
        EventId = 6441,
        Level = LogLevel.Warning,
        Message = "Guruh chati tozalash bir yurishdagi chegaraga yetdi: o'chirildi={Deleted} "
                  + "paket chegarasi={MaxBatches}. Qolgani KEYINGI yurishda davom etadi "
                  + "(bu birinchi yoqilganda normal holat).")]
    internal static partial void ChatHistoryPurgeCapped(
        ILogger logger, int deleted, int maxBatches);

    [LoggerMessage(
        EventId = 6442,
        Level = LogLevel.Debug,
        Message = "Chat tarixini tozalash o'chiq (`chat.retention_enabled`) — o'tkazib yuborildi.")]
    internal static partial void RetentionDisabled(ILogger logger);

    // ---------------------------------------------------------------- umumiy

    [LoggerMessage(
        EventId = 6430,
        Level = LogLevel.Warning,
        Message = "Fon vazifasi uchun tizim aktyori topilmadi (faol Admin yoki "
                  + "Academic yo'q): {JobName}. Vazifa o'tkazib yuborildi.")]
    internal static partial void NoSystemActor(ILogger logger, string jobName);
}
