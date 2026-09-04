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
/// ── NIMA QILMAYDI (2026-08-13 da QAYTA KO'RIB CHIQILDI) ─────────────────
///
/// ⚠️ YOZUVNI BOSHLASH QARORINI O'ZI QABUL QILMAYDI — va bu qoida
/// avtomatik yozuvga o'tilganda ham SAQLANDI. Eski tizimning watchdog'i
/// aksincha edi: u `record_enabled` guruhlarning jonli darslarini O'ZI
/// qidirib topib, yozuvni O'ZI boshlardi — ya'ni AYNI ishni uch joy (dars
/// boshlash, `room_started` webhook'i va watchdog) bir-biridan bexabar
/// bajarardi.
///
/// ★ FARQ NOZIK, LEKIN AYNAN U BUTUN ARXITEKTURANI USHLAB TURADI:
///
///   • QAROR ("bu dars yozilishi kerakmi") — BITTA joyda:
///     <c>LiveSessionService.StartAsync</c> → <see cref="IAutoRecordingScheduler"/>.
///     Watchdog guruhlarni SKANERLAMAYDI va <c>Group.RecordEnabled</c> ni
///     UMUMAN O'QIMAYDI.
///   • IJRO ("Egress'ga murojaat qilish") — BITTA joyda: shu vazifa,
///     <c>RecordingStarter</c> orqali.
///
/// Ya'ni <c>SessionRecordings</c> jadvali NAVBAT, watchdog esa uni
/// bo'shatuvchi. Qator qayerdan kelgani (host tugmasi yoki avtomatik
/// navbat) vazifaga BATAMOM AHAMIYATSIZ — u <c>Requested</c> holatini
/// ko'radi, xolos. AVTOMATIK YOZUVLAR SHU TUFAYLI QAYTA URINISH, MUHLAT
/// VA TASLIM BO'LISH MANTIQINI BEPUL MEROS QILIB OLADI: bitta qator ham
/// yangi kod yozilmadi.
///
/// ⚠️ Agar bu vazifaga "yozuvi yoqilgan guruhlarni qidirish" qo'shilsa,
/// eski tizimning aynan o'sha uch nusxali holati QAYTADI. Qo'shilmasin.
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
    IPresenceService presence,
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
            // ══════════════════════════════════════════════════════════
            // 🔴 FAQAT ESKI OQIM — `TrackComposition` QATORIGA TEGILMAYDI
            //
            // Bu vazifa BOSHDAN OXIRIGACHA `RoomComposite` mantig'i:
            // u `ObjectKey` ni ombordan qidiradi va topolmasa `Failed`
            // qo'yadi. Yangi oqimda esa o'sha kalit dars tugagach EMAS,
            // KECHASI montaj tugagach paydo bo'ladi — ya'ni filtr
            // bo'lmasa har bir yangi yozuv ertalabgacha yetmay o'lardi.
            //
            // ⚠️ IKKINCHI, OG'IRROQ OQIBAT (M5 aniqladi). Yangi qator
            //    `Requested` va `EgressId = null` holda tug'iladi, ya'ni
            //    watchdog uni `RetryOrGiveUpAsync` ga olib borardi va
            //    `RecordingStarter` orqali ESKI, Chrome'li
            //    `StartRoomRecordingAsync` ni ishga tushirardi. Natijada
            //    arzon bo'lishi kerak bo'lgan yozuv jimgina 1.5 yadroni
            //    yeb, ustiga bir darsda IKKI xil yozuv ketardi.
            //
            // ★ Yangi oqimning o'z qo'riqchisi bor —
            //   `RecordingTrackReconcileJob` (SPEC §4.1).
            // ══════════════════════════════════════════════════════════
            .Where(r => r.Status != RecordingStatus.Completed
                     && r.Status != RecordingStatus.Failed
                     && r.Pipeline == RecordingPipeline.RoomComposite)
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

        // ══════════════════════════════════════════════════════════════
        // 🔴 XONA BO'SH BO'LSA YOZUVNI BOSHLAMAYMIZ (2026-08-24)
        //
        // NIMA UCHUN QO'SHILDI. Yozuv qatori dars `Live` ga o'tganda
        // yaratiladi — ya'ni ustoz "Darsni boshlash" ni bosgan ZAHOTI.
        // Brauzer esa xonaga KEYIN kiradi: sahifa ochiladi, kamera/mikrofon
        // ruxsati so'raladi (birinchi safar bu o'nlab soniya bo'lishi
        // mumkin), keyingina trek e'lon qilinadi.
        //
        // Egress esa bo'sh xonani kutmaydi: Chrome kiradi, hech kim
        // e'lon qilmasa ~18 soniyada "Start signal not received" bilan
        // uziladi (O'LCHANDI, egress v1.14). Keyin watchdog faylni
        // ombordan topa olmaydi va yozuvni `Failed` deb belgilaydi —
        // 🔴 `Failed` esa YAKUNIY: o'sha darsning yozuvi BUTUNLAY
        // yo'qoladi va qayta urinilmaydi.
        //
        // ⚠️ ALOMATI ALDAYDI: dars a'lo o'tadi, hech kim hech narsa
        // sezmaydi. Nosozlik faqat keyin, "yozuv qani?" savoli bilan
        // ochiladi — ya'ni eng yomon turdagi nosozlik.
        //
        // ★ NEGA `IPresenceService`, LiveKit'dan SO'RASH EMAS: bu ma'lumot
        //   BIZDA allaqachon bor (Redis, SignalR hub'iga ulanish paytida
        //   yoziladi) va u tarmoqqa chiqishni talab qilmaydi. LiveKit'ning
        //   `ListParticipants` ini chaqirish yangi tashqi bog'liqlik
        //   bo'lardi — har 15 soniyada, har kutayotgan yozuv uchun.
        //
        // ★ URINISH SANALMAYDI: bu XATO emas, KUTISH. `false` qaytariladi
        //   va qator o'zgarmaydi — ya'ni `MaxAttempts` bu yerda sarflanmaydi.
        //   Ustoz umuman kirmasa, qator dars tugagunicha kutadi va yuqoridagi
        //   "Dars yakunlandi, yozuv esa boshlanmadi" qoidasi uni yopadi.
        //
        // ⚠️ XATOLIKDA — OCHIQ YO'L (fail-open). Redis javob bermasa
        //   yozuvni BOSHLASHGA urinamiz: aks holda Redis'ning vaqtinchalik
        //   nosozligi butun yozuv funksiyasini JIMGINA o'chirib qo'yardi.
        //   Bu loyihadagi umumiy qoida: "ma'lumot yo'q" bilan "shart
        //   bajarilmadi" ni aralashtirmaslik.
        // ══════════════════════════════════════════════════════════════
        if (!await RoomHasParticipantsAsync(recording, ct).ConfigureAwait(false))
            return false;

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

    /// <summary>
    /// Xonada kimdir bormi (Redis presence).
    ///
    /// <c>true</c> — boshlash mumkin; <c>false</c> — hali kutamiz.
    /// Xato bo'lsa <c>true</c> (sabab yuqorida: fail-open).
    /// </summary>
    private async Task<bool> RoomHasParticipantsAsync(
        SessionRecording recording, CancellationToken ct)
    {
        try
        {
            var count = await presence
                .CountAsync(recording.SessionId, ct)
                .ConfigureAwait(false);

            if (count > 0) return true;

            RecordingLog.WatchdogWaitingForParticipants(logger, recording.Id);

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordingLog.WatchdogPresenceUnavailable(logger, ex, recording.Id);

            return true;
        }
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

        // ══════════════════════════════════════════════════════════════
        // 🔴 DARS HALI KETYAPTI — TASLIM BO'LISH MUMKIN EMAS (2026-09-04)
        //
        // NIMA BO'LGANI. Webhook o'chirilgan holatda `egress_started`
        // HECH QACHON kelmaydi, ya'ni `StartedAt` bo'sh qoladi. Pastdagi
        // muhlat esa aynan shu bo'sh maydonga qarab `StartTimeout`
        // (10 daqiqa) ni tanlardi va o'sha 10 daqiqadan keyin ombordan
        // so'rardi. Egress esa mp4 ni faqat dars TUGAGACH yuklaydi —
        // moov atomi oxirida yoziladi. Natijada fayl topilmasdi va qator
        // `Failed` bo'lardi, `Failed` esa YAKUNIY.
        //
        // ⚠️ OQIBATI KATTA EDI: 10 daqiqadan uzun HAR QANDAY dars
        //    jimgina yo'qolardi. 2026-09-04 da o'lchandi — ATF 135 ning
        //    yozuvi 14:24 da `Failed` bo'lgan, fayl esa 15:48 da omborga
        //    tushgan (1.75 GB, joyida turibdi). O'sha oyda muvaffaqiyatli
        //    yagona yozuv 14 MB lik QISQA dars edi: u 10 daqiqa ichida
        //    yuklanib ulgurgan. Ya'ni nosozlik darsning UZUNLIGIGA
        //    bog'liq edi va shuning uchun tasodifiy ko'rinardi.
        //
        // ★ QOIDA: dars `Live` ekan va to'xtatish hali so'ralmagan bo'lsa
        //   — KUTAMIZ, xolos. Qator o'zgarmaydi (`false`).
        //
        // ★ ABADIY KUTIB QOLMAYDI — buni yuqoridagi shart kafolatlaydi:
        //   dars tugashi bilan (`sessionOver`) yoki `MaxDuration` o'tishi
        //   bilan (`tooLong`) `StopRequestedAt` qo'yiladi va shu paytdan
        //   `FinalizeGrace` sanala boshlaydi. Ya'ni bu yerdagi qaytish
        //   yo'lni yopmaydi, faqat KECHIKTIRADI.
        //
        // ⚠️ Webhook yoqilgach ham bu shart QOLSIN: u `StartedAt` ga
        //    umuman tayanmaydi va hodisa yana yo'qolganda tizimni
        //    o'sha eski nosozlikka qaytarmaydi.
        // ══════════════════════════════════════════════════════════════
        if (!sessionOver && recording.StopRequestedAt is null)
            return false;

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
/// <param name="Interval">
/// Ikki yurish orasidagi masofa.
///
/// 🔴 AVTOMATIK YOZUVDA BU QIYMAT ENDI FOYDALANUVCHI SEZADIGAN KECHIKISH.
/// Dars boshlanganda navbatga qator tushadi, Egress'ga esa AYNAN shu
/// vazifa murojaat qiladi — ya'ni yozuv darsdan ko'pi bilan
/// <c>Interval</c> qadar kech boshlanadi. Ilgari bu shunchaki "nosozlikni
/// qancha tez sezamiz" degan raqam edi.
/// </param>
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
    ///
    /// ★ <c>Interval</c> 60 s DAN 15 s GA TUSHIRILDI (2026-08-13,
    /// avtomatik yozuv bilan birga). Sabab: endi bu raqam darsning
    /// yozilmay qoladigan boshi (yuqoridagi 🔴). 15 s — darsning
    /// boshlanish shovqini (ustoz kirib, o'quvchilarni kutadi) ichida
    /// yo'qoladigan, lekin bo'sh yurishlarni ham ko'paytirmaydigan
    /// qiymat.
    ///
    /// ⚠️ QOLGAN CHEGARALARGA TA'SIR QILMAYDI va bu TASODIF EMAS:
    /// <c>RetryDelay</c>, <c>StartTimeout</c>, <c>FinalizeGrace</c> va
    /// <c>MaxDuration</c> — MUTLAQ muddatlar (qatordagi vaqt bilan
    /// solishtiriladi), yurishlar SONI bilan emas. Ya'ni tez-tez yurish
    /// faqat aniqlikni oshiradi: Egress yiqilganda urinishlar chegarasi
    /// baribir 2 daqiqalik tanaffus bilan sanaladi. Agar bu chegaralardan
    /// birortasi "har yurishda bir marta" mantiqiga o'tkazilsa, bu
    /// qiymatni o'zgartirish JIM ravishda ularni ham o'zgartirib
    /// yuborardi.
    ///
    /// ⚠️ BO'SH YURISH ARZON: kutayotgan yozuv bo'lmasa vazifa bitta
    /// indeksli so'rov qiladi (<c>IX_SessionRecordings_Status_LastAttemptAt</c>)
    /// va <c>JobRunResult.Nothing</c> qaytaradi — `SaveChanges` ham,
    /// tashqi chaqiruv ham yo'q.
    /// </summary>
    public static RecordingWatchdogSettings Default { get; } = new(
        Interval: TimeSpan.FromSeconds(15),
        RetryDelay: TimeSpan.FromMinutes(2),
        StartTimeout: TimeSpan.FromMinutes(10),
        FinalizeGrace: TimeSpan.FromMinutes(10),
        MaxDuration: TimeSpan.FromHours(4),
        MaxAttempts: 5,
        BatchSize: 100);
}
