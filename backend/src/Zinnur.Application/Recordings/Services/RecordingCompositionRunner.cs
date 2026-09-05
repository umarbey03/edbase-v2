using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI YIG'ISHNING BITTA AYLANISHI
/// ════════════════════════════════════════════════════════════════════════
///
/// <c>egallash → reja → kodlash → yozib qo'yish → xomni tozalash</c>
///
/// ── 🔴 BIR VAQTDA AYNAN BITTA YIG'ISH (§4.3) ────────────────────────────
///
/// Bu sinf bir aylanishda AYNAN BITTA qatorni oladi va boshqa hech
/// qanday parallellik yaratmaydi. Sabab arifmetik, did emas:
///
///   • x264 <c>-threads 0</c> bilan barcha yadrolarga O'ZI tarqaladi,
///     ya'ni ikkita ish 4 yadroda AYNI umumiy vaqtda tugaydi — faqat
///     xotira ikki barobar bo'ladi;
///   • 09:00 da uzilganda YO'QOTILADIGAN ish ikki barobar bo'ladi;
///   • 4 yadro — butun server: API, Postgres, Redis, LiveKit va tungi
///     <c>pg_dump</c> ham shu yerda.
///
/// ── NOSOZLIK VA UZILISH — IKKI BOSHQA NARSA ─────────────────────────────
///
/// ffmpeg yiqildi / tekshiruv o'tmadi / yuklash yiqildi
///   → <c>CompositionAttempts++</c>, 3 tadan keyin YAKUNIY xato.
///
/// Tungi oyna tugadi (yoki konteyner to'xtatilmoqda)
///   → <c>CompositionInterruptions++</c>, urinish SARFLANMAYDI,
///     10 kechadan keyingina taslim bo'linadi.
///
/// 🔴 Ikkalasini bitta hisoblagichga qo'shish mutlaqo sog'lom yozuvni
/// beshta band kechadan keyin o'ldirardi.
///
/// ── UZILGAN ISH BUZUQ EMAS, NAVBATDA ────────────────────────────────────
///
/// Uzilishda: ishchi papka <c>finally</c> da o'chiriladi, YAKUNIY kalitga
/// esa hech narsa yozilmagan (yuklash — eng oxirgi qadam va u faqat
/// tekshiruvdan o'tgan fayl uchun bo'ladi). Ya'ni qator <c>Queued</c> ga
/// qaytadi, xom fayllari joyida turadi va keyingi kecha ish BOSHIDAN
/// boshlanadi. Yarim natijani "davom ettirish" HECH QACHON qilinmaydi.
/// </summary>
public sealed class RecordingCompositionRunner(
    IApplicationDbContext db,
    IRecordingCompositionStore store,
    IRecordingComposer composer,
    IRecordingStorage storage,
    ISettingsResolver settings,
    TimeProvider clock,
    RecordingCompositionSettings options,
    ILogger<RecordingCompositionRunner> logger) : IRecordingCompositionRunner
{
    /// <summary>
    /// Xom faylning o'lchangan uzunligi kutilganidan shuncha ko'p farq
    /// qilsa OGOHLANTIRISH yoziladi (§9.1-1).
    ///
    /// ⚠️ ISH TO'XTATILMAYDI: farq — signal, nosozlik emas. Xona ovozi
    /// qatoridagi raqam esa ALOHIDA qimmatli: u vaqt o'qining O'ZI, ya'ni
    /// uning siljishi butun yozuvning siljishi.
    /// </summary>
    private static readonly TimeSpan DriftWarningThreshold = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<CompositionCycleResult> RunOnceAsync(CancellationToken ct = default)
    {
        // ★ OMBOR SOZLANMAGAN BO'LSA UMUMAN EGALLAMAYMIZ (§9.2). Aks holda
        //   sozlama vaqtincha yo'qolgan kechada har aylanish bitta
        //   urinishni yeb, uchta aylanishdan keyin butunlay sog'lom
        //   yozuvni YAKUNIY xato qilib qo'yardi.
        if (!storage.IsConfigured)
            return CompositionCycleResult.Idle();

        var claim = await store.ClaimAsync(options.Lease, ct).ConfigureAwait(false);

        if (claim is null)
        {
            // Navbat bo'sh — o'tgan kechalardan qolgan tozalanmagan xom
            // fayllarni yig'ishtiramiz (§4.5-9).
            var purged = await PurgeBacklogAsync(ct).ConfigureAwait(false);

            return CompositionCycleResult.Idle(purged);
        }

        RecordingLog.CompositionClaimed(logger, claim.RecordingId, claim.TookOverExpiredLease);

        return await ComposeAsync(claim, ct).ConfigureAwait(false);
    }

    // ═════════════════════════════════════════════════════════ bitta yozuv

    private async Task<CompositionCycleResult> ComposeAsync(
        CompositionClaim claim, CancellationToken ct)
    {
        var recording = await db.SessionRecordings
            .AsTracking()
            .Include(r => r.Tracks)
            .FirstOrDefaultAsync(r => r.Id == claim.RecordingId, ct)
            .ConfigureAwait(false);

        // Egallash bilan o'qish orasida qator o'chirilgan (dars o'chirilsa
        // yozuv ham kaskad bilan ketadi). Ijara o'z-o'zidan eskiradi.
        if (recording is null)
            return CompositionCycleResult.Idle();

        // ══════════════════════════════════════════════════════════════
        // 🔴 QULAGAN ISHCHIDAN QOLGAN IZLAR — BOSHLASHDAN OLDIN O'CHADI
        //
        // Ishchi papkani yig'uvchining o'zi tozalaydi. Bu yerda YAKUNIY
        // kalitdagi obyekt o'chiriladi: agar oldingi urinish yuklash
        // paytida qulagan bo'lsa, u yerda YARIM fayl turgan bo'lishi
        // mumkin. Yarim mp4 uch soniya o'ynab to'xtaydi va o'quvchi buni
        // "yozuv buzuq" deb emas, "dars uch soniya bo'lgan" deb ko'radi.
        //
        // ⚠️ TAYYOR faylni o'chirib yuborish XAVFI YO'Q: egallash faqat
        //    `Queued` yoki ijarasi eskirgan `Running` qatorni oladi;
        //    `Completed` qator bu yerga UMUMAN tushmaydi.
        // ══════════════════════════════════════════════════════════════
        if (claim.TookOverExpiredLease)
            await DiscardLeftoverOutputAsync(recording, ct).ConfigureAwait(false);

        var configuration = await ReadPlanSettingsAsync(ct).ConfigureAwait(false);

        var planned = RecordingCompositionPlanner.Create(
            recording, recording.Tracks, configuration);

        if (planned.Plan is not { } plan)
        {
            var reason = planned.Error ?? RecordingCompositionPlanner.NoTracksReason;

            recording.MarkCompositionFailed(reason, clock.GetUtcNow());

            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            RecordingLog.CompositionFailed(logger, recording.Id, reason);

            return CompositionCycleResult.For(CompositionCycleOutcome.Failed, recording.Id);
        }

        // ══════════════════════════════════════════════════════════════
        // IJARANI TIRIK USHLAB TURISH
        //
        // ⚠️ AYNI `DbContext` USTIDA ISHLAYDI VA BU XAVFSIZ, chunki
        //    kodlash davomida bazaga TEGADIGAN yagona narsa — shu
        //    yurakcha. Kodlash tugagach u BIRINCHI bo'lib to'xtatiladi
        //    va faqat keyin natija saqlanadi, ya'ni ikki amal hech
        //    qachon ustma-ust tushmaydi.
        // ══════════════════════════════════════════════════════════════
        using var beat = new CancellationTokenSource();

        var heartbeat = RenewLeaseAsync(claim, beat.Token);

        CompositionResult result;

        try
        {
            result = await composer.ComposeAsync(plan, ct).ConfigureAwait(false);

            // ══════════════════════════════════════════════════════════
            // 🔴 YO'QOLGAN XOM BO'LAK BUTUN DARSNI YIQITMAYDI
            //
            // Bo'lak `Completed` bo'lgan, ya'ni fayl bir paytlar OMBORDA
            // EDI. Endi yo'q bo'lsa (o'chirilgan, prefiks almashtirilgan,
            // R2 da yo'qolgan) — bu bitta bo'lakning yo'qolishi, butun
            // darsning emas. Reja O'SHA ZAHOTI, AYNI kechada, o'sha
            // bo'laksiz qayta quriladi.
            //
            // ★ ARZON: yo'qolgan obyekt YUKLAB OLISH bosqichida
            //   aniqlanadi, ya'ni birinchi urinishda kodlash UMUMAN
            //   boshlanmagan bo'ladi.
            //
            // ⚠️ FAQAT BIR MARTA: ikkinchi urinishda ham yo'qolgan bo'lak
            //    chiqsa, bu ombor bilan bog'liq kattaroq muammo va u
            //    yashirilmasligi kerak.
            // ══════════════════════════════════════════════════════════
            if (!result.Succeeded && result.MissingTrackIds.Count > 0)
            {
                RecordingLog.CompositionMissingRaw(
                    logger, recording.Id, result.MissingTrackIds.Count);

                var reduced = RecordingCompositionPlanner.Create(
                    recording, recording.Tracks, configuration, result.MissingTrackIds);

                if (reduced.Plan is { } fallback)
                {
                    plan = fallback;
                    result = await composer.ComposeAsync(plan, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            await StopAsync(beat, heartbeat).ConfigureAwait(false);

            return await InterruptAsync(recording).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Port shartnomasi bo'yicha bu yerga tushilmaydi. Tushilsa —
            // qatorni `Running` holida OSILGAN qoldirish mumkin emas:
            // uni faqat ijara muddati tugagach boshqa ishchi ko'tarardi,
            // ya'ni butun kecha behuda ketardi.
            await StopAsync(beat, heartbeat).ConfigureAwait(false);

            RecordingLog.CompositionCrashed(logger, ex, recording.Id);

            result = CompositionResult.Fail("Yig'ishda kutilmagan xato.");
        }

        await StopAsync(beat, heartbeat).ConfigureAwait(false);

        ApplyProbes(recording, result);

        return result.Succeeded
            ? await CompleteAsync(recording, plan, result, ct).ConfigureAwait(false)
            : await RetryOrGiveUpAsync(recording, result).ConfigureAwait(false);
    }

    // ═════════════════════════════════════════════════════════ yakunlar

    private async Task<CompositionCycleResult> CompleteAsync(
        SessionRecording recording,
        CompositionPlan plan,
        CompositionResult result,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        recording.MarkCompositionCompleted(
            result.SizeBytes, result.DurationSeconds, endedAt: now, now: now);

        // ⚠️ OGOHLANTIRISH YAKUNDAN KEYIN YOZILADI: `MarkCompositionCompleted`
        //    `CompositionError` ni TOZALAYDI (u nosozlik sababi uchun
        //    o'ylangan). Bu yerdagi yagona holat — "ovoz yozib olinmadi":
        //    fayl tayyor va ochiladi, lekin JIM. Xodim buni ochmasdan
        //    bilishi kerak, aks holda "yozuv buzuq" degan xabar keladi
        //    (§4.6). Boshqa staff-ga ko'rinadigan maydon yo'q.
        if (plan.Warning is { Length: > 0 } warning)
            recording.CompositionError = warning;

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        RecordingLog.CompositionCompleted(
            logger, recording.Id, result.SizeBytes ?? 0, result.DurationSeconds ?? 0);

        // ★ TOZALASH YAKUNDAN KEYIN VA ALOHIDA `SaveChanges` BILAN:
        //   o'chirish yiqilsa yozuv TAYYORLIGICHA qoladi. Yetim xom fayl
        //   PUL turadi, orqaga qaytarilgan sog'lom yozuv esa BUTUN DARSNI.
        await PurgeRawAsync(recording, ct).ConfigureAwait(false);

        return CompositionCycleResult.For(CompositionCycleOutcome.Completed, recording.Id);
    }

    private async Task<CompositionCycleResult> RetryOrGiveUpAsync(
        SessionRecording recording, CompositionResult result)
    {
        var now = clock.GetUtcNow();
        var reason = result.Error is { Length: > 0 } error ? error : "Yig'ish yiqildi.";

        // ⚠️ TARTIB MUHIM: avval hisoblagich oshadi, KEYIN chegara
        //    tekshiriladi (`SessionRecording.CanRetryComposition` izohi).
        recording.ReleaseCompositionForRetry(reason, now);

        var final = !recording.CanRetryComposition;

        if (final) recording.MarkCompositionFailed(reason, now);

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        if (final)
        {
            RecordingLog.CompositionFailed(logger, recording.Id, reason);

            return CompositionCycleResult.For(CompositionCycleOutcome.Failed, recording.Id);
        }

        RecordingLog.CompositionRetrying(
            logger, recording.Id, recording.CompositionAttempts, reason);

        return CompositionCycleResult.For(CompositionCycleOutcome.Retrying, recording.Id);
    }

    /// <summary>
    /// Tungi oyna tugadi yoki konteyner to'xtatilmoqda.
    ///
    /// 🔴 SAQLASH <c>CancellationToken.None</c> BILAN: bekor qilish
    /// signali aynan shu yerga olib kelgan, ya'ni <c>ct</c> ni uzatsak
    /// <c>SaveChanges</c> darhol uzilardi va qator <c>Running</c> holida,
    /// ijarasi eskirguncha OSILIB qolardi. O'shanda ish keyingi kechada
    /// "qulagan ishchi" sifatida olinib, URINISH sarflanardi — ya'ni
    /// sog'lom yozuv sekin-asta o'ladi.
    /// </summary>
    private async Task<CompositionCycleResult> InterruptAsync(SessionRecording recording)
    {
        var now = clock.GetUtcNow();

        recording.InterruptComposition(now);

        var final = !recording.CanResumeComposition;

        if (final)
        {
            recording.MarkCompositionFailed(
                "Tungi oyna ketma-ket ko'p marta yetmadi — yig'ish to'xtatildi.", now);
        }

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        RecordingLog.CompositionInterrupted(
            logger, recording.Id, recording.CompositionInterruptions, final);

        return CompositionCycleResult.For(
            final ? CompositionCycleOutcome.Failed : CompositionCycleOutcome.Interrupted,
            recording.Id);
    }

    // ═════════════════════════════════════════════════════════ o'lchovlar

    /// <summary>
    /// O'lchangan uzunliklarni qatorlarga yozadi va SILJISHNI tekshiradi.
    ///
    /// ★ IKKALA RAQAM HAM SAQLANADI (<c>DurationSeconds</c> — Egress
    /// aytgani, <c>ProbedDurationMs</c> — o'lchangani), chunki QIMMATLISI
    /// ularning FARQI. O'n darsdan keyin bu farqlar siljish tezligini
    /// beradi va §9.1 aynan shu raqamlar bo'yicha hal qilinadi.
    /// </summary>
    private void ApplyProbes(SessionRecording recording, CompositionResult result)
    {
        foreach (var probe in result.Probes)
        {
            var track = recording.Tracks.FirstOrDefault(t => t.Id == probe.TrackId);

            if (track is null) continue;

            track.ProbedDurationMs = probe.DurationMs;

            if (track.StartedAt is not { } startedAt || track.EndedAt is not { } endedAt)
                continue;

            var expected = (endedAt - startedAt).TotalMilliseconds;

            if (Math.Abs(expected - probe.DurationMs) <= DriftWarningThreshold.TotalMilliseconds)
                continue;

            RecordingLog.CompositionDrift(
                logger,
                recording.Id,
                track.Id,
                track.Kind.ToString(),
                (int)Math.Round(expected),
                probe.DurationMs);
        }
    }

    // ═════════════════════════════════════════════════════════ tozalash

    /// <summary>
    /// Yakuniy kalitdagi obyektni o'chiradi (qulagan urinishdan qolgan
    /// yarim fayl bo'lishi mumkin).
    /// </summary>
    private async Task DiscardLeftoverOutputAsync(
        SessionRecording recording, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(recording.ObjectKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O'chirilmasa ham davom etamiz: yuklash baribir USTIDAN
            // yozadi. Bu qadam faqat "yuklash ham yiqilsa yarim fayl
            // qolib ketmasin" degan qo'shimcha himoya.
            RecordingLog.CompositionLeftoverNotRemoved(logger, ex, recording.Id, recording.ObjectKey);
        }
    }

    /// <summary>
    /// Bitta yozuvning xom fayllarini o'chiradi.
    ///
    /// ⚠️ NOSOZLIK YIG'ISHNI YIQITMAYDI: <c>RawPurgedAt</c> bo'sh qoladi
    /// va keyingi kecha navbat bo'sh bo'lgan lahzada qayta uriniladi
    /// (<see cref="PurgeBacklogAsync"/>).
    /// </summary>
    private async Task<bool> PurgeRawAsync(SessionRecording recording, CancellationToken ct)
    {
        var keys = recording.Tracks
            .Select(t => t.ObjectKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordingLog.RawPurgeFailed(logger, ex, recording.Id, key);

                return false;
            }
        }

        recording.MarkRawPurged(clock.GetUtcNow());

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        RecordingLog.RawPurged(logger, recording.Id, keys.Count);

        return true;
    }

    /// <summary>
    /// O'tgan kechalarda tozalanmay qolgan xom fayllarni yig'ishtiradi.
    ///
    /// ★ FAQAT NAVBAT BO'SH BO'LGANDA: tozalash kechikishga chidaydi,
    /// kodlash esa yo'q — tungi oyna cheklangan resurs.
    /// </summary>
    private async Task<int> PurgeBacklogAsync(CancellationToken ct)
    {
        var stale = await db.SessionRecordings
            .AsTracking()
            .Include(r => r.Tracks)
            .Where(r => r.Pipeline == RecordingPipeline.TrackComposition
                     && r.CompositionStatus == RecordingCompositionStatus.Completed
                     && r.RawPurgedAt == null)
            .OrderBy(r => r.Id)
            .Take(options.PurgeBatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var purged = 0;

        foreach (var recording in stale)
        {
            ct.ThrowIfCancellationRequested();

            if (await PurgeRawAsync(recording, ct).ConfigureAwait(false)) purged++;
        }

        return purged;
    }

    // ═════════════════════════════════════════════════════════ ijara

    private async Task RenewLeaseAsync(CompositionClaim claim, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(options.RenewEvery, clock, ct).ConfigureAwait(false);

                var held = await store
                    .RenewAsync(claim.RecordingId, claim.ClaimedAt, options.Lease, ct)
                    .ConfigureAwait(false);

                if (held) continue;

                // 🔴 Ijara BIZDA EMAS. Ish davom etaveradi (uni yarmida
                //    to'xtatish hech narsa yutmaydi), lekin bu qator
                //    LOGDA ko'rinishi SHART: u ikki kodlovchi bitta
                //    kalitga yozayotganining yagona alomati.
                RecordingLog.CompositionLeaseLost(logger, claim.RecordingId);

                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Kodlash tugadi — bu normal yakun.
        }
        catch (Exception ex)
        {
            // Yurakchaning yiqilishi kodlashni to'xtatmasin.
            RecordingLog.CompositionLeaseRenewFailed(logger, ex, claim.RecordingId);
        }
    }

    private static async Task StopAsync(CancellationTokenSource beat, Task heartbeat)
    {
        if (heartbeat.IsCompleted) return;

        await beat.CancelAsync().ConfigureAwait(false);

        await heartbeat.ConfigureAwait(false);
    }

    // ═════════════════════════════════════════════════════════ sozlamalar

    /// <summary>
    /// Kodlash sozlamalarini o'qiydi.
    ///
    /// ⚠️ SOZLAMALAR HALI REGISTRDA BO'LMASLIGI MUMKIN: ularni
    /// <c>SettingsRegistry</c> ga M7 qo'shadi va o'sha fayl ATAYLAB bitta
    /// modulga biriktirilgan (takroriy kalit ilovani ishga tushishda
    /// yiqitadi). Registrda yo'q kalit SPEC dagi standartga tushadi, ya'ni
    /// modul M7 dan OLDIN ham to'g'ri ishlaydi va M7 kelgach hech narsa
    /// o'zgartirilmasdan sozlamaga bo'ysunadi — M5 dagi AYNI naqsh.
    /// </summary>
    private async Task<CompositionPlanSettings> ReadPlanSettingsAsync(CancellationToken ct)
    {
        var defaults = CompositionPlanSettings.Default;

        var preset = await ReadAsync(PresetKey, ct).ConfigureAwait(false);
        var crf = await ReadAsync(CrfKey, ct).ConfigureAwait(false);
        var offset = await ReadAsync(AudioOffsetKey, ct).ConfigureAwait(false);

        return new CompositionPlanSettings(
            Preset: string.IsNullOrWhiteSpace(preset) ? defaults.Preset : preset.Trim(),
            Crf: Number(crf, defaults.Crf, MinCrf, MaxCrf),
            AudioOffsetMs: Number(offset, defaults.AudioOffsetMs, MinAudioOffsetMs, MaxAudioOffsetMs));
    }

    private async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        if (!SettingsRegistry.TryGet(key, out var definition))
            return null;

        var resolved = await settings.ResolveAsync(definition, ct).ConfigureAwait(false);

        return resolved.Value;
    }

    /// <summary>
    /// ★ CHEGARALAR BU YERDA HAM QO'YILADI, garchi registr ularni
    /// allaqachon tekshirsa ham: qiymat bazadan keladi va u yerga qo'lda
    /// yozib qo'yish mumkin. <c>crf=0</c> esa yo'qotishsiz kodlash, ya'ni
    /// bitta dars uchun o'nlab gigabayt.
    /// </summary>
    private static int Number(string? value, int fallback, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;

    private const int MinCrf = 16;
    private const int MaxCrf = 28;
    private const int MinAudioOffsetMs = -2000;
    private const int MaxAudioOffsetMs = 2000;

    /// <summary>
    /// ⚠️ SATR SIFATIDA, <c>SettingsRegistry.Keys</c> ORQALI EMAS: o'sha
    /// konstantalar M7 bilan birga keladi (§5.7). M7 qo'shilgach bu uch
    /// qatorni almashtirish kifoya — mantiq o'zgarmaydi.
    /// </summary>
    private const string PresetKey = "recordings.compose_preset";

    private const string CrfKey = "recordings.compose_crf";

    private const string AudioOffsetKey = "recordings.compose_audio_offset_ms";
}

/// <summary>
/// Yig'ish aylanishining muhitga oid chegaralari.
///
/// ★ NIMA UCHUN ALOHIDA YOZUV VA <c>IOptions</c> EMAS: Application
/// qatlami konfiguratsiya tizimini BILMAYDI (u WebApi'ning ishi).
/// Qiymatlar DI ro'yxatidan o'tkazishda uzatiladi —
/// <c>RecordingWatchdogSettings</c> bilan AYNI naqsh.
/// </summary>
/// <param name="Lease">
/// Ijara muddati. Bu "ish qancha davom etadi" EMAS: ishlayotgan ishchi
/// uni muntazam uzaytiradi. Bu — "ishchi qulaganini qancha vaqtda
/// sezamiz".
/// </param>
/// <param name="RenewEvery">
/// Ijara qancha vaqtda bir uzaytiriladi.
///
/// ⚠️ <paramref name="Lease"/> DAN SEZILARLI KICHIK bo'lishi SHART: ular
/// yaqinlashsa, bir marta kechikkan uzaytirish ijarani eskirtirib,
/// qatorni boshqa ishchiga berib yuborardi.
/// </param>
/// <param name="PurgeBatchSize">
/// Bir aylanishda ko'pi bilan nechta yozuvning xom fayllari tozalanadi.
/// </param>
public sealed record RecordingCompositionSettings(
    TimeSpan Lease,
    TimeSpan RenewEvery,
    int PurgeBatchSize)
{
    /// <summary>SPEC §4.4 dagi standart qiymatlar.</summary>
    public static RecordingCompositionSettings Default { get; } = new(
        Lease: TimeSpan.FromMinutes(5),
        RenewEvery: TimeSpan.FromSeconds(60),
        PurgeBatchSize: 20);
}
