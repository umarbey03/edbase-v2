using Zinnur.Application.Recordings.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI YIG'ISH WORKER'I (00:00–09:00, Asia/Tashkent)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ SINF YUPQA — biznes mantiqi yo'q (<c>OutboxWorker</c> dagi AYNI
/// qoida). "Qator qanday egallanadi, nosozlikda nima bo'ladi, uzilishda
/// nima bo'ladi" — hammasi <see cref="IRecordingCompositionRunner"/> da,
/// ya'ni Application qatlamida va u testda haqiqiy baza bilan sinaladi.
/// Bu yerda faqat HOSTING: sikl, uyqu, tungi oyna va to'xtatish.
///
/// ── 🔴 BU XIZMAT ALOHIDA KONTEYNERDA ISHLAYDI (§4.2) ────────────────────
///
/// API konteynerida EMAS, va buning ikkala sababi ham halokatli:
///
///   1) <c>JobRunner.RunAllAsync</c> muddati kelgan vazifalarni KETMA-KET
///      bajaradi va har birini kutadi. 90 daqiqalik ffmpeg
///      <c>SessionAutoCloseJob</c>, <c>MonthlyBillingJob</c>,
///      <c>PenaltyScanJob</c> va <c>ChatRetentionJob</c> ni 90 daqiqaga
///      bloklardi — darslar avto-yakunlanishdan to'xtardi.
///   2) ffmpeg API obrazida yo'q, API obrazi esa internetga ochilgan
///      yagona obraz.
///
/// ── 🔴 BIR VAQTDA AYNAN BITTA YIG'ISH (§4.3) ────────────────────────────
///
/// Sikl bir aylanishda BITTA qatorni oladi va uni OXIRIGACHA kutadi.
/// x264 <c>-threads 0</c> bilan barcha yadrolarga o'zi tarqaladi, ya'ni
/// ikkita ish 4 yadroda AYNI umumiy vaqtda tugaydi — faqat xotira ikki
/// barobar bo'ladi va 09:00 da YO'QOTILADIGAN ish ham ikki barobar.
///
/// ── TUNGI OYNA VA UMUMIY KALIT SHU YERDA TEKSHIRILADI ───────────────────
///
/// "Hozir umuman ishlaymizmi" — HOSTING savoli:
///
///   • <c>recordings.track_pipeline_enabled</c> — favqulodda tormoz.
///     🔴 STANDARTI <c>false</c> (§2.7), ya'ni sozlama registrga
///     qo'shilgunicha (M7) va admin panelidan yoqilgunicha bu worker
///     HECH NARSA qilmaydi. Bu ATAYLAB: P2 bosqichida quvur "o'rnatilgan,
///     lekin o'chiq" bo'lishi kerak.
///   • tungi oyna — <see cref="RecordingCompositionWindow"/> (sof
///     funksiya, alohida sinaladi).
///
/// Kodlashning bekor qilish signali oynaning OXIRIGA qo'yiladi: 09:00 da
/// ffmpeg to'xtaydi, ishchi papka o'chiriladi va qator <c>Queued</c> ga
/// qaytadi. Yakuniy kalitga hech narsa yozilmagan bo'ladi — yuklash eng
/// oxirgi qadam. Ya'ni uzilgan ish BUZUQ emas, NAVBATDA.
/// </summary>
internal sealed class RecordingCompositionWorker(
    IServiceScopeFactory scopeFactory,
    CompositionOptions options,
    TimeProvider clock,
    ILogger<RecordingCompositionWorker> logger) : BackgroundService
{
    /// <summary>
    /// Ishga tushishdagi kechikish: ilova endigina ko'tarilgan paytda baza
    /// migratsiya va seed bilan band bo'ladi.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    /// <summary>Kutilmagan xatodan keyingi tanaffus — log sekundiga ming marta to'lmasin.</summary>
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CompositionWorkerLog.Started(logger, options.ScratchPath, options.PollSeconds);

        try
        {
            await Task.Delay(StartupDelay, clock, stoppingToken).ConfigureAwait(false);

            await CleanScratchAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = await RunCycleAsync(stoppingToken).ConfigureAwait(false);

                // Ish bo'lgan bo'lsa DARHOL keyingi qatorga o'tamiz: tungi
                // oyna cheklangan resurs va navbatni "tomchilatib"
                // bo'shatish uni behuda yeb qo'yardi.
                if (worked) continue;

                await Task.Delay(options.Poll, clock, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal to'xtatish.
        }

        CompositionWorkerLog.Stopped(logger);
    }

    /// <summary>Bitta aylanish. <c>true</c> — yozuv bilan ish bajarildi.</summary>
    private async Task<bool> RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            // `BackgroundService` — singleton, ishlovchilar esa scoped
            // (`DbContext` ga tayanadi). Har aylanishda YANGI scope: aks
            // holda bitta `DbContext` ulanishni ilova umri davomida ushlab
            // turardi va o'zgarish kuzatuvchisi cheksiz o'sardi.
            await using var scope = scopeFactory.CreateAsyncScope();

            var settings = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();

            if (!await PipelineEnabledAsync(settings, stoppingToken).ConfigureAwait(false))
                return false;

            var timeZone = scope.ServiceProvider
                .GetRequiredService<IScheduleTimeZoneProvider>().TimeZone;

            var now = clock.GetUtcNow();

            var window = RecordingCompositionWindow.Evaluate(
                now,
                timeZone,
                await ReadTimeAsync(
                    settings, WindowStartKey, RecordingCompositionWindow.DefaultStart, stoppingToken)
                    .ConfigureAwait(false),
                await ReadTimeAsync(
                    settings, WindowEndKey, RecordingCompositionWindow.DefaultEnd, stoppingToken)
                    .ConfigureAwait(false),
                options.StartCutoff);

            if (!window.CanStart)
            {
                CompositionWorkerLog.OutsideWindow(logger, window.IsOpen, window.EndsAtUtc);

                return false;
            }

            // 🔴 BEKOR QILISH SIGNALI OYNANING OXIRIGA QO'YILADI. Busiz
            //    08:59 da boshlangan ish ertalabki darslar ustidan
            //    kodlashda davom etardi — aynan shu quvur oldini olishi
            //    kerak bo'lgan narsa.
            using var deadline = new CancellationTokenSource(window.EndsAtUtc - now, clock);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, deadline.Token);

            var runner = scope.ServiceProvider.GetRequiredService<IRecordingCompositionRunner>();

            var result = await runner.RunOnceAsync(linked.Token).ConfigureAwait(false);

            if (result.Outcome != CompositionCycleOutcome.Idle)
                CompositionWorkerLog.Cycle(logger, result.Outcome.ToString(), result.RecordingId ?? 0);

            return result.Outcome != CompositionCycleOutcome.Idle;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Bitta aylanishdagi xato (baza uzildi, ombor javob bermadi)
            // fon xizmatini O'LDIRMASIN: u qayta ishga tushmaydi va tungi
            // navbat jimgina to'planib qolardi.
            CompositionWorkerLog.CycleFailed(logger, ex);

            await Task.Delay(ErrorDelay, clock, stoppingToken).ConfigureAwait(false);

            return false;
        }
    }

    /// <summary>
    /// Ishga tushishda eski ishchi papkalarni tozalaydi (§4.5-1).
    ///
    /// ★ NIMA UCHUN AYNAN ISHGA TUSHISHDA: papka odatda <c>finally</c> da
    /// o'chiriladi, lekin konteyner OOM bilan o'ldirilsa u UMUMAN
    /// bajarilmaydi. Bir necha bunday hodisadan keyin 6 GB lik qoldiqlar
    /// diskni to'ldirardi va butun tungi navbat "no space left" bilan
    /// to'xtardi.
    /// </summary>
    private async Task CleanScratchAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var composer = scope.ServiceProvider.GetRequiredService<IRecordingComposer>();

            var removed = await composer
                .CleanScratchAsync(options.ScratchMaxAge, ct)
                .ConfigureAwait(false);

            if (removed > 0) CompositionWorkerLog.ScratchCleaned(logger, removed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Tozalash yiqilsa ham ishlaymiz: disk to'lgan bo'lsa buni
            // birinchi kodlash baribir aytadi.
            CompositionWorkerLog.CycleFailed(logger, ex);
        }
    }

    // ═════════════════════════════════════════════════════════ sozlamalar

    /// <summary>
    /// Favqulodda tormoz: <c>recordings.track_pipeline_enabled</c>.
    ///
    /// 🔴 REGISTRDA YO'Q BO'LSA — <c>false</c>. Sozlamani
    /// <c>SettingsRegistry</c> ga M7 qo'shadi (§5.7) va o'sha fayl
    /// ATAYLAB bitta modulga biriktirilgan (takroriy kalit ilovani ishga
    /// tushishda yiqitadi). Standart SPEC §2.7 da OSHKORA <c>false</c>:
    /// quvur "o'rnatilgan, lekin o'chiq" holatda yetkaziladi va uni
    /// admin panelidan yoqiladi.
    /// </summary>
    private static async Task<bool> PipelineEnabledAsync(
        ISettingsResolver settings, CancellationToken ct)
    {
        if (!SettingsRegistry.TryGet(PipelineEnabledKey, out var definition))
            return false;

        var resolved = await settings.ResolveAsync(definition, ct).ConfigureAwait(false);

        return bool.TryParse(resolved.Value?.Trim(), out var enabled) && enabled;
    }

    private static async Task<TimeOnly> ReadTimeAsync(
        ISettingsResolver settings, string key, TimeOnly fallback, CancellationToken ct)
    {
        if (!SettingsRegistry.TryGet(key, out var definition))
            return fallback;

        var resolved = await settings.ResolveAsync(definition, ct).ConfigureAwait(false);

        return RecordingCompositionWindow.Parse(resolved.Value, fallback);
    }

    /// <summary>
    /// ⚠️ SATR SIFATIDA, <c>SettingsRegistry.Keys</c> ORQALI EMAS: o'sha
    /// konstantalar M7 bilan birga keladi (§5.7).
    /// </summary>
    private const string PipelineEnabledKey = "recordings.track_pipeline_enabled";

    private const string WindowStartKey = "recordings.compose_window_start";

    private const string WindowEndKey = "recordings.compose_window_end";
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). EventId makoni:
/// <c>6660–6669</c>.
/// </summary>
internal static partial class CompositionWorkerLog
{
    [LoggerMessage(
        EventId = 6660,
        Level = LogLevel.Information,
        Message = "Tungi yig'ish worker'i ishga tushdi: papka={ScratchPath} "
                  + "tekshiruv={PollSeconds}s")]
    internal static partial void Started(ILogger logger, string scratchPath, int pollSeconds);

    [LoggerMessage(
        EventId = 6661,
        Level = LogLevel.Information,
        Message = "Tungi yig'ish worker'i to'xtadi.")]
    internal static partial void Stopped(ILogger logger);

    /// <summary>
    /// ★ `Debug` — bu NOSOZLIK EMAS, kunning normal holati. Worker
    /// sutkaning 15 soatini shu qatorni yozib o'tkazadi.
    /// </summary>
    [LoggerMessage(
        EventId = 6662,
        Level = LogLevel.Debug,
        Message = "Tungi oyna yopiq yoki oxirlab qoldi, yangi ish boshlanmaydi. "
                  + "ochiq={IsOpen} oyna_tugashi={EndsAtUtc}")]
    internal static partial void OutsideWindow(
        ILogger logger, bool isOpen, DateTimeOffset endsAtUtc);

    [LoggerMessage(
        EventId = 6663,
        Level = LogLevel.Information,
        Message = "Tungi yig'ish aylanishi: natija={Outcome} yozuv={RecordingId}")]
    internal static partial void Cycle(ILogger logger, string outcome, long recordingId);

    [LoggerMessage(
        EventId = 6664,
        Level = LogLevel.Error,
        Message = "Tungi yig'ish aylanishida xato — keyingi urinishgacha kutamiz.")]
    internal static partial void CycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6665,
        Level = LogLevel.Information,
        Message = "Eski ishchi papkalar tozalandi: soni={Count}")]
    internal static partial void ScratchCleaned(ILogger logger, int count);
}
