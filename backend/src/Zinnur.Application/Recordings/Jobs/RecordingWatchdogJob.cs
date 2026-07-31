using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Jobs;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARS YOZUVI WATCHDOG'I — yarim qolgan yozuvlarni yakunlaydi
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ MUAMMO: yozuv jarayonining KATTA QISMI bizdan tashqarida. Uchta
/// nuqtada u jimgina "osilib" qolishi mumkin:
///
///   1) Egress'ga so'rov ketmadi (u yiqilgan, tarmoq uzilgan) — qator
///      <c>Requested</c> holida qoladi;
///   2) so'rov ketdi, lekin <c>egress_started</c> KELMADI — <c>Starting</c>;
///   3) yozuv ketdi, dars tugadi, lekin <c>egress_ended</c> YO'QOLDI
///      (deploy, qayta ishga tushish, tarmoq) — <c>Active</c> abadiy.
///
/// Watchdog'siz bularning uchalasi ham "yozuv yo'q, sababi noma'lum"
/// ko'rinishida qolardi — eski tizimda AYNAN shunday edi.
///
/// ── HAQIQAT MANBAI — OMBORNING O'ZI ─────────────────────────────────────
///
/// 🔴 Webhook YO'QOLGAN holatda vazifa LiveKit'dan emas, OMBORDAN so'raydi
/// (<see cref="IRecordingStorage.HeadAsync"/>): fayl bormi va hajmi
/// qancha. Fayl bor bo'lsa yozuv YAKUNLANADI — hodisa kelmagani faylning
/// yo'qligini anglatmaydi. Faqat fayl ham yo'q bo'lsa yozuv
/// <c>Failed</c> deb belgilanadi.
///
/// ── NIMA QILMAYDI ───────────────────────────────────────────────────────
///
/// ⚠️ YANGI YOZUV BOSHLAMAYDI. Faqat MAVJUD qatorlarni tuzatadi. Eski
/// tizimning watchdog'i aksincha edi: u `record_enabled` guruhlarning
/// jonli darslarini o'zi qidirib, yozuvni O'ZI boshlardi — ya'ni AYNI
/// ishni uch joy (dars boshlash, `room_started` webhook'i va watchdog)
/// bir-biridan bexabar bajarardi. Bu yerda yozuvni BOSHLASH qarori faqat
/// ustozda (sabab: <see cref="IRecordingService"/>), watchdog esa faqat
/// TUZATADI.
///
/// ⚠️ TUGALLANGAN YOZUVGA TEGMAYDI — buni Domain kafolatlaydi
/// (<c>MarkFailed</c> ichida <c>if (Status == Completed) return;</c>).
///
/// ★ QULFNI VAZIFA OLMAYDI — buni <c>IJobRunner</c> bajaradi (Postgres
/// advisory lock). Ya'ni bir necha konteynerda ish AYNAN BIR MARTA ketadi.
/// </summary>
public sealed class RecordingWatchdogJob(
    IApplicationDbContext db,
    ILiveKitEgress egress,
    IRecordingStorage storage,
    TimeProvider clock,
    RecordingWatchdogSettings settings,
    ILogger<RecordingWatchdogJob> logger) : IScheduledJob
{
    /// <inheritdoc />
    public string Name => "recording-watchdog";

    /// <inheritdoc />
    public TimeSpan Interval => settings.Interval;

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        // ★ SOZLANMAGAN BO'LSA HECH NARSA QILINMAYDI.
        //
        // Aks holda LiveKit/ombor vaqtincha o'chirilgan muhitda (yoki
        // sozlama yo'qolganda) watchdog BARCHA kutayotgan yozuvni
        // `Failed` deb belgilab qo'yardi — va ular qaytmas edi, chunki
        // `Failed` YAKUNIY holat.
        if (!egress.IsConfigured)
            return JobRunResult.Nothing;

        var pending = await db.SessionRecordings
            .AsTracking()
            .Include(r => r.Session)
            .Where(r => r.Status != RecordingStatus.Completed
                     && r.Status != RecordingStatus.Failed)
            .OrderBy(r => r.Id)
            .Take(settings.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
            return JobRunResult.Nothing;

        var now = clock.GetUtcNow();
        var processed = 0;
        var skipped = 0;

        foreach (var recording in pending)
        {
            ct.ThrowIfCancellationRequested();

            var changed = recording.Status switch
            {
                RecordingStatus.Requested =>
                    await RetryOrGiveUpAsync(recording, now, ct).ConfigureAwait(false),

                RecordingStatus.Starting or RecordingStatus.Active =>
                    await FinalizeAsync(recording, now, ct).ConfigureAwait(false),

                _ => false,
            };

            if (changed) processed++;
            else skipped++;
        }

        // BITTA `SaveChanges`: bir yurishdagi barcha tuzatish AYNI
        // tranzaksiyada yozilsin. Qator-qator saqlash 100 ta yozuvda
        // 100 ta borish-kelish bo'lardi.
        if (processed > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new JobRunResult(processed, skipped);
    }

    // ================================================================= Requested

    /// <summary>
    /// Egress so'rovi ketmagan yozuv: qayta urinamiz yoki taslim bo'lamiz.
    /// </summary>
    private async Task<bool> RetryOrGiveUpAsync(
        SessionRecording recording, DateTimeOffset now, CancellationToken ct)
    {
        // Urinishlar orasida KUTAMIZ. Busiz Egress yiqilgan paytda vazifa
        // har yurishda (30 s) urinaverib, urinishlar chegarasini bir
        // daqiqada tugatardi va yozuv sababsiz `Failed` bo'lardi.
        if (recording.LastAttemptAt is { } last && now - last < settings.RetryDelay)
            return false;

        var session = recording.Session;

        // ★ DARS TUGAGAN BO'LSA QAYTA URINISH MA'NOSIZ: yoziladigan xona
        //   endi yo'q. Bu yerda darhol yakuniy xato qo'yiladi — aks holda
        //   qator chegara tugagunicha bekordan Egress'ni bezovta qilardi.
        if (session is null || session.Status is SessionStatus.Ended or SessionStatus.Cancelled)
        {
            const string Reason = "Dars yakunlandi, yozuv esa boshlanmadi.";

            recording.MarkFailed(Reason, now);
            RecordingLog.WatchdogGaveUp(logger, recording.Id, Reason);

            return true;
        }

        if (!recording.CanRetry(settings.MaxAttempts))
        {
            var reason = recording.Error is { Length: > 0 } error
                ? error
                : "Yozuvni boshlab bo'lmadi (urinishlar tugadi).";

            recording.MarkFailed(reason, now);
            RecordingLog.WatchdogGaveUp(logger, recording.Id, reason);

            return true;
        }

        RecordingLog.WatchdogRetry(logger, recording.Id, recording.Attempts);

        // Natijadan qat'i nazar qator O'ZGARADI (urinish sanog'i, vaqt,
        // xato matni) — shuning uchun `true`.
        await RecordingStarter
            .TryAsync(egress, recording, session.RoomName, now, logger, ct)
            .ConfigureAwait(false);

        return true;
    }

    // ================================================================= Starting / Active

    /// <summary>
    /// Boshlangan, lekin yakunlanmagan yozuv.
    ///
    /// Uch bosqich: (a) dars tugagan bo'lsa Egress'ni to'xtatamiz;
    /// (b) juda uzoq cho'zilgan bo'lsa ham to'xtatamiz; (c) to'xtatish
    /// so'ralganidan keyin belgilangan muhlat o'tsa — OMBORDAN so'raymiz.
    /// </summary>
    private async Task<bool> FinalizeAsync(
        SessionRecording recording, DateTimeOffset now, CancellationToken ct)
    {
        var session = recording.Session;

        var sessionOver = session is null
            || session.Status is SessionStatus.Ended or SessionStatus.Cancelled;

        // Yozuv juda uzoq ketyapti: xona ochiq qolgan yoki Egress osilgan.
        // Chegarasiz holatda bitta unutilgan xona kunlab yozib, omborni va
        // Egress resursini yeb turardi.
        var startedAt = recording.StartedAt ?? recording.LastAttemptAt ?? recording.CreatedAt;
        var tooLong = now - startedAt >= settings.MaxDuration;

        if (recording.StopRequestedAt is null && (sessionOver || tooLong))
        {
            if (!string.IsNullOrWhiteSpace(recording.EgressId))
            {
                var accepted = await egress
                    .StopRecordingAsync(recording.EgressId, ct)
                    .ConfigureAwait(false);

                RecordingLog.StopRequested(logger, recording.Id, recording.EgressId, accepted);
            }

            // Belgilanadi HAR HOLDA — shu paytdan `FinalizeGrace` sanaladi.
            // Aks holda `StopEgress` rad etilgan (allaqachon tugagan)
            // yozuv hech qachon yakuniy holatga o'tmasdi.
            recording.MarkStopRequested(now);

            return true;
        }

        // ── Muhlat: hodisa kelishini kutamiz ───────────────────────────
        //
        // `egress_ended` odatda bir necha soniyada keladi. Muhlat esa
        // saxovatli: uzun yozuvni omborga yuklash daqiqalar olishi mumkin
        // va erta "Failed" qo'yish TAYYOR faylni ko'rinmas qilardi.
        var waitingSince = recording.StopRequestedAt ?? startedAt;
        var deadline = recording.StopRequestedAt is null ? settings.StartTimeout : settings.FinalizeGrace;

        if (now - waitingSince < deadline)
            return false;

        // ── OMBORDAN SO'RAYMIZ (haqiqat manbai) ────────────────────────
        StoredObjectInfo? stored;

        try
        {
            stored = await storage.HeadAsync(recording.ObjectKey, ct).ConfigureAwait(false);
        }
        catch (ServiceUnavailableException ex)
        {
            // Ombor javob bermadi. 🔴 BU YERDA `Failed` QO'YISH MUMKIN
            // EMAS: fayl bor bo'lishi ham mumkin, biz shunchaki ko'ra
            // olmadik. Keyingi yurishga qoldiramiz.
            RecordingLog.WatchdogStorageUnavailable(logger, ex, recording.Id);

            return false;
        }

        if (stored is not null)
        {
            recording.MarkCompleted(
                objectKey: null,             // kalit BIZNIKI, u o'zgarmagan
                sizeBytes: stored.SizeBytes,
                durationSeconds: null,       // videoning uzunligini ombor bilmaydi
                endedAt: now,
                now: now);

            RecordingLog.WatchdogRecovered(
                logger, recording.Id, recording.ObjectKey, stored.SizeBytes);

            return true;
        }

        const string Missing = "Yozuv fayli omborga tushmadi.";

        recording.MarkFailed(Missing, now);
        RecordingLog.WatchdogGaveUp(logger, recording.Id, Missing);

        return true;
    }
}

/// <summary>
/// Watchdog chegaralari.
///
/// ★ NIMA UCHUN ALOHIDA YOZUV (record) VA <c>IOptions</c> EMAS: Application
/// qatlami konfiguratsiya tizimini BILMAYDI (u WebApi'ning ishi). Qiymatlar
/// DI ro'yxatidan o'tkazishda uzatiladi — <c>SessionAutoCloseSettings</c>
/// bilan AYNI naqsh. Shu tufayli vazifani testda istalgan (juda qisqa)
/// chegaralar bilan yurgizish mumkin.
/// </summary>
/// <param name="Interval">Ikki yurish orasidagi masofa.</param>
/// <param name="RetryDelay">
/// Ikki urinish orasidagi eng qisqa tanaffus. Busiz Egress yiqilgan paytda
/// urinishlar chegarasi bir daqiqada tugab qolardi.
/// </param>
/// <param name="StartTimeout">
/// <c>egress_started</c> hodisasi shuncha vaqt kelmasa yozuv "boshlanmadi"
/// deb hisoblanadi va ombordan tekshiriladi.
/// </param>
/// <param name="FinalizeGrace">
/// To'xtatish so'ralganidan keyin <c>egress_ended</c> ni kutish muddati.
/// ⚠️ Saxovatli bo'lishi SHART: uzun videoni omborga yuklash daqiqalar
/// olishi mumkin va erta yakun TAYYOR faylni ko'rinmas qilardi.
/// </param>
/// <param name="MaxDuration">
/// Bitta yozuvning eng uzun umri. Undan keyin Egress majburan to'xtatiladi
/// — unutilgan xona kunlab yozib turmasin.
/// </param>
/// <param name="MaxAttempts">Egress'ni boshlashga eng ko'p necha urinish.</param>
/// <param name="BatchSize">Bir yurishda ko'pi bilan nechta yozuv.</param>
public sealed record RecordingWatchdogSettings(
    TimeSpan Interval,
    TimeSpan RetryDelay,
    TimeSpan StartTimeout,
    TimeSpan FinalizeGrace,
    TimeSpan MaxDuration,
    int MaxAttempts,
    int BatchSize)
{
    /// <summary>
    /// Ishlab chiqarish uchun standart chegaralar.
    ///
    /// ★ RAQAMLAR QAYERDAN: dars 80 daqiqa + 10 daqiqa uzaytirish
    /// (<c>LiveSession.MaxExtendMinutes</c>), ya'ni <c>MaxDuration = 4
    /// soat</c> — ruxsat etilgan eng uzun darsdan ikki barobardan ko'p:
    /// hali ketayotgan yozuvni uzib qo'yish amalda mumkin emas.
    /// <c>MaxAttempts = 5</c> — 2 daqiqalik tanaffus bilan ~10 daqiqa
    /// urinish; bundan uzog'i darsning yarmini yeb qo'yardi.
    /// </summary>
    public static RecordingWatchdogSettings Default { get; } = new(
        Interval: TimeSpan.FromSeconds(60),
        RetryDelay: TimeSpan.FromMinutes(2),
        StartTimeout: TimeSpan.FromMinutes(10),
        FinalizeGrace: TimeSpan.FromMinutes(10),
        MaxDuration: TimeSpan.FromHours(4),
        MaxAttempts: 5,
        BatchSize: 100);
}
