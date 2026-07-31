using Zinnur.Application.Notifications.Services;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Navbatni doimiy aylantirib turadigan fon xizmati.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN XABAR HTTP SO'ROVI ICHIDA YUBORILMAYDI:
/// eski tizimda Telegram chaqiruvi so'rov ichida bo'lgani uchun, bot sekin
/// javob berganda foydalanuvchi ODDIY FORMANI saqlashda kutib turardi va
/// so'rov timeout bo'lardi. Endi so'rov faqat bazaga qator yozadi
/// (millisekundlar), yuborishni esa shu xizmat bajaradi.
///
/// ★ SINF YUPQA — biznes mantiqi yo'q. "Nechta olish, qancha kutish, qayta
/// urinishmi yoki yo'qmi" — hammasi <see cref="IOutboxDispatcher"/> da,
/// ya'ni Application qatlamida va u testda bazaga qarab tekshiriladi.
/// Bu yerda faqat HOSTING: sikl, kutish va to'xtatish.
///
/// ★ KO'P INSTANCE: bu xizmat HAR konteynerda ishlaydi va bu NORMAL.
/// Xabarlar takrorlanmasligini <c>FOR UPDATE SKIP LOCKED</c> ta'minlaydi
/// (izoh: <see cref="IOutboxStore"/>), tezlik chegarasi esa Redis'da
/// umumiy — shuning uchun "leader lock" kerak emas.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    NotificationsOptions options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    /// <summary>
    /// Ishga tushishdagi kechikish: ilova endigina ko'tarilgan paytda baza
    /// migratsiya va seed bilan band bo'ladi.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);

    /// <summary>Kutilmagan xatodan keyingi tanaffus — logni sekundiga ming marta to'ldirmasin.</summary>
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxWorkerLog.Started(logger, options.BatchSize, options.PollSeconds);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                var handled = await RunCycleAsync(stoppingToken).ConfigureAwait(false);

                // Ish bo'lgan bo'lsa DARHOL keyingi paketga o'tamiz: navbat
                // to'planib qolgan bo'lsa (uzilishdan keyin) uni sekin
                // "tomchilatib" emas, tez bo'shatish kerak. Tezlik chegarasi
                // baribir Redis'da ushlab turadi.
                if (handled) continue;

                await Task.Delay(options.Poll, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal to'xtatish.
        }

        OutboxWorkerLog.Stopped(logger);
    }

    /// <summary>Bitta aylanish. <c>true</c> — xabar bilan ish bajarildi.</summary>
    private async Task<bool> RunCycleAsync(CancellationToken ct)
    {
        try
        {
            // `BackgroundService` — singleton, `IOutboxDispatcher` esa scoped
            // (u `DbContext` ga tayanadi). Har aylanishda YANGI scope: aks
            // holda bitta `DbContext` ulanishni ilova umri davomida ushlab
            // turardi va o'zgarish kuzatuvchisi cheksiz o'sardi.
            await using var scope = scopeFactory.CreateAsyncScope();

            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

            var result = await dispatcher
                .DispatchAsync(options.BatchSize, options.Lease, ct)
                .ConfigureAwait(false);

            // Faqat keyinga surilgan bo'lsa "ish bo'ldi" deb hisoblamaymiz:
            // aks holda chegara to'lganda sikl bo'sh aylanaverardi.
            return result.Delivered + result.Rejected > 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Bitta aylanishdagi xato (baza uzildi, Redis javob bermadi) fon
            // xizmatini O'LDIRMASIN: u qayta ishga tushmaydi va xabarlar
            // jimgina to'planib qolardi — eski tizimdagi eng yashirin nosozlik.
            OutboxWorkerLog.CycleFailed(logger, ex);

            await Task.Delay(ErrorDelay, ct).ConfigureAwait(false);
            return false;
        }
    }
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). ATAYLAB alohida sinf —
/// <c>ApiLog</c> ga tegilmaydi, modulning loglari bir joyda tursin.
/// </summary>
internal static partial class OutboxWorkerLog
{
    [LoggerMessage(
        EventId = 6200,
        Level = LogLevel.Information,
        Message = "Notifikatsiya worker'i ishga tushdi: paket={BatchSize} tekshiruv={PollSeconds}s")]
    internal static partial void Started(ILogger logger, int batchSize, int pollSeconds);

    [LoggerMessage(
        EventId = 6201,
        Level = LogLevel.Information,
        Message = "Notifikatsiya worker'i to'xtadi.")]
    internal static partial void Stopped(ILogger logger);

    [LoggerMessage(
        EventId = 6202,
        Level = LogLevel.Error,
        Message = "Notifikatsiya aylanishida xato — keyingi urinishgacha kutamiz.")]
    internal static partial void CycleFailed(ILogger logger, Exception exception);
}
