using Zinnur.Application.Jobs;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Fon vazifalarini vaqti-vaqti bilan yurgizadigan rejalashtiruvchi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN "REJALASHTIRUVCHI ILOVA ICHIDA" ENDI XAVFSIZ:
/// eski tizim <c>APScheduler</c> ni ayni shunday ishlatardi va ikkinchi
/// instance ko'tarilishi bilan HAR VAZIFA IKKI MARTA bajarilardi. Farq
/// bitta, lekin hal qiluvchi: bu yerda har vazifa <see cref="IJobRunner"/>
/// orqali BAZA QULFI ostida yurgiziladi (<see cref="IJobLock"/>). Ya'ni
/// xizmat har konteynerda ishlaydi va bu NORMAL — ishni faqat bittasi
/// bajaradi, qolganlari jimgina o'tkazib yuboradi.
///
/// ★ SINF YUPQA — biznes mantiqi yo'q. "Nima yopiladi, qachon yopiladi,
/// qanday qulflanadi" — hammasi Application qatlamida
/// (<see cref="SessionAutoCloseJob"/>, <see cref="MonthlyBillingJob"/>,
/// <see cref="JobRunner"/>) va u yerda bazaga qarab testdan o'tadi. Bu
/// yerda faqat HOSTING: sikl, kutish, navbat va to'xtatish.
///
/// ★ VAQT JADVALI SHU YERDA (vazifada emas): "qachon" — hosting savoli.
/// Har vazifaning O'Z oralig'i bor (<see cref="IScheduledJob.Interval"/>),
/// tick esa eng mayda o'lchov birligi. Shu tufayli oylik hisob har
/// daqiqada emas, yarim soatda bir marta yuradi, dars tekshiruvi esa
/// tez-tez.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class JobSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IJobRunner runner,
    JobsOptions options,
    TimeProvider clock,
    ILogger<JobSchedulerWorker> logger) : BackgroundService
{
    /// <summary>
    /// Ishga tushishdagi kechikish. Migratsiya va seed <c>Program.cs</c> da
    /// hosted service'lardan OLDIN tugaydi, lekin ilova endigina
    /// ko'tarilgan paytda baza baribir band bo'ladi — birinchi tekshiruv
    /// shoshilmasin.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    /// <summary>Kutilmagan xatodan keyingi tanaffus — log sekundiga to'lib ketmasin.</summary>
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Vazifa nomi -> keyingi yurish vaqti.
    ///
    /// ★ HOLAT INSTANCE'GA XOS va bu TO'G'RI: u faqat "men bu vazifani
    /// qachon KO'RIB CHIQAMAN" degani. Ishni haqiqatan kim bajarishini
    /// baza qulfi hal qiladi, shuning uchun instance'lar jadvali
    /// sinxronlanishi shart emas.
    ///
    /// Faqat SHU sikldan yoziladi/o'qiladi — qulf kerak emas.
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _nextRunAt =
        new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobSchedulerLog.Started(logger, options.TickSeconds);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunTickAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.Tick, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal to'xtatish.
        }

        JobSchedulerLog.Stopped(logger);
    }

    /// <summary>Bitta uyg'onish: muddati kelgan vazifalarni yurgizadi.</summary>
    private async Task RunTickAsync(CancellationToken ct)
    {
        try
        {
            // `BackgroundService` — singleton, vazifalar esa scoped (ular
            // `DbContext` ga tayanadi). HAR aylanishda YANGI scope.
            await using var scope = scopeFactory.CreateAsyncScope();

            var jobs = scope.ServiceProvider.GetServices<IScheduledJob>();
            var now = clock.GetUtcNow();

            var due = new List<IScheduledJob>();

            foreach (var job in jobs)
            {
                if (_nextRunAt.TryGetValue(job.Name, out var next) && now < next)
                    continue;

                // Keyingi vaqt ISHDAN OLDIN belgilanadi: vazifa uzoq
                // ishlasa, tugagan zahoti darhol qayta yurgizilmasin.
                _nextRunAt[job.Name] = now + job.Interval;
                due.Add(job);
            }

            if (due.Count == 0) return;

            // `RunAllAsync` istisno tashlamaydi: bitta vazifaning yiqilishi
            // qolganlarini ham, siklni ham to'xtatmaydi (izoh: `IJobRunner`).
            await runner.RunAllAsync(due, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Bu yerga faqat scope yaratish yoki DI xatosi tushadi. Fon
            // xizmati O'LMASIN: u qayta ishga tushmaydi va vazifalar
            // jimgina bajarilmay qolardi — eski tizimdagi eng yashirin
            // nosozlik.
            JobSchedulerLog.TickFailed(logger, ex);

            await Task.Delay(ErrorDelay, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). ATAYLAB alohida sinf —
/// <c>ApiLog</c> ga tegilmaydi, modulning loglari bir joyda tursin.
/// </summary>
internal static partial class JobSchedulerLog
{
    [LoggerMessage(
        EventId = 6450,
        Level = LogLevel.Information,
        Message = "Fon rejalashtiruvchisi ishga tushdi: tick={TickSeconds}s")]
    internal static partial void Started(ILogger logger, int tickSeconds);

    [LoggerMessage(
        EventId = 6451,
        Level = LogLevel.Information,
        Message = "Fon rejalashtiruvchisi to'xtadi.")]
    internal static partial void Stopped(ILogger logger);

    [LoggerMessage(
        EventId = 6452,
        Level = LogLevel.Error,
        Message = "Fon rejalashtiruvchisi aylanishida xato — keyingi urinishgacha kutamiz.")]
    internal static partial void TickFailed(ILogger logger, Exception exception);
}
