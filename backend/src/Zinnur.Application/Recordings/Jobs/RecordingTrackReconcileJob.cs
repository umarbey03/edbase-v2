using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Jobs;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TREK QUVURINING MOSLASHTIRUVCHISI — yo'qolgan webhook'larning zaxirasi
/// ════════════════════════════════════════════════════════════════════════
///
/// Yangi quvurda darsning bo'laklari WEBHOOK orqali topiladi
/// (<c>TrackRecordingWebhookHandler</c>). Webhook esa yetkazilmasligi
/// mumkin: API konteyneri dars o'rtasida qayta ishga tushsa, o'sha
/// oraliqdagi <c>room_started</c> va <c>track_published</c> hodisalari
/// BUTUNLAY yo'qoladi va ularni hech kim qayta yubormaydi.
///
/// Bu vazifa har daqiqada beshta savolni beradi:
///
///   1) boshlanmay qolgan bo'lak bormi (qayta uramiz);
///   2) 🔴 XONA OVOZI QATORI BORMI (yo'q bo'lsa — mikserni yoqamiz);
///   3) LiveKit hozir bizga noma'lum trekni ko'ryaptimi (webhook
///      yo'qolgan) va mikserimiz hali tirikmi;
///   4) mikser to'rt soatdan ortiq ishlayaptimi (unutilgan xona);
///   5) dars tugagan, bo'laklar esa hali ochiqmi (yakunlaymiz) —
///      va hammasi yopilgach yozuvni TUNGI NAVBATGA qo'yamiz.
///
/// ── 🔴 2-QADAM ENG QIMMATLISI ───────────────────────────────────────────
///
/// Yo'qolgan video bo'lak bir necha daqiqalik TASVIRNI yo'qotadi.
/// Yo'qolgan mikser esa BUTUN DARSNING OVOZINI — ya'ni yozuvning
/// ma'nosini: o'quv bo'limi tushuntirish sifatini baholaydi va u ovozda.
///
/// ── 🔴 LIVEKIT JAVOB BERMASA HECH QANDAY XULOSA CHIQARILMAYDI ───────────
///
/// <c>ILiveKitRoomQuery</c> natijalarida <c>Succeeded</c> bayrog'i ATAYLAB
/// alohida turadi. "Xonada faol egress yo'q" va "LiveKit'ga yetib
/// bo'lmadi" — IKKI BOSHQA javob. Ikkalasini bo'sh ro'yxat deb qabul
/// qilsak, tarmoq uzilgan daqiqada vazifa "mikser o'lgan" deb xulosa
/// chiqarib, TIRIK mikser ustiga ikkinchisini yoqardi: bitta darsda ikki
/// ovoz fayli va tungi montajda har bir ovoz IKKI MARTA.
///
/// ── NIMA UCHUN UMUMIY KALIT BU YERDA TEKSHIRILMAYDI ─────────────────────
///
/// <c>recordings.track_pipeline_enabled</c> — YANGI yozuv yaratilishini
/// to'xtatadigan tormoz (<c>AutoRecordingScheduler</c>). Bu vazifa esa
/// ALLAQACHON mavjud qatorlar bilan ishlaydi va ular AYNI SHU DAQIQADA
/// ketayotgan darsga tegishli. Ularni yarim yo'lda tashlab ketish
/// LiveKit'da ishlab turgan egress'larni to'xtatmaydi — faqat darsni
/// yo'qotadi. Webhook ishlovchisi ham aynan shu sababga ko'ra kalitni
/// tekshirmaydi; ikki joyda ikki xil qoida bo'lmasin.
///
/// ★ QULFNI VAZIFA OLMAYDI — buni <c>IJobRunner</c> bajaradi (Postgres
/// advisory lock), ya'ni bir necha konteynerda ish AYNAN BIR MARTA ketadi.
/// </summary>
public sealed class RecordingTrackReconcileJob(
    IApplicationDbContext db,
    ILiveKitEgress egress,
    ILiveKitRoomQuery rooms,
    IRecordingStorage storage,
    ISettingsResolver settings,
    TimeProvider clock,
    RecordingTrackReconcileSettings options,
    ILogger<RecordingTrackReconcileJob> logger) : IScheduledJob
{
    /// <inheritdoc />
    public string Name => "recording-track-reconcile";

    /// <inheritdoc />
    public TimeSpan Interval => options.Interval;

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        // ★ SOZLANMAGAN BO'LSA HECH NARSA QILINMAYDI — watchdog'dagi AYNI
        //   sabab: LiveKit vaqtincha o'chirilgan muhitda vazifa barcha
        //   kutayotgan bo'lakni `Failed` deb belgilab qo'yardi va
        //   `Failed` YAKUNIY holat.
        if (!egress.IsConfigured)
            return JobRunResult.Nothing;

        var pending = await db.SessionRecordings
            .AsTracking()
            .Include(r => r.Session)
            .Include(r => r.Tracks)

            // ⚠️ FAQAT YIG'ILAYOTGAN QATORLAR. `Queued` va undan keyingi
            //    holatlar tungi kompozitorning ishi; ularga bu yerdan
            //    tegish darsni ikkinchi marta navbatga qo'yardi.
            .Where(r => r.Pipeline == RecordingPipeline.TrackComposition
                     && r.CompositionStatus == RecordingCompositionStatus.Collecting
                     && r.Status != RecordingStatus.Completed
                     && r.Status != RecordingStatus.Failed)
            .OrderBy(r => r.Id)
            .Take(options.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
            return JobRunResult.Nothing;

        var roomAudioMode = await RoomAudioModeAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var processed = 0;
        var skipped = 0;

        foreach (var recording in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (await ReconcileAsync(recording, roomAudioMode, now, ct).ConfigureAwait(false))
                processed++;
            else
                skipped++;
        }

        // ⚠️ WATCHDOG'DAN FARQLI O'LAROQ BU YERDA BITTA UMUMIY
        //    `SaveChanges` YO'Q: yangi qator yozish va Egress'ni boshlash
        //    ORASIDA saqlash SHART (sabab `StartAllAsync` izohida), ya'ni
        //    saqlash yozuv-yozuv bajariladi. Bir yurishda odatda 0–3
        //    jonli dars bo'ladi, ya'ni narxi ham shunga yarasha.
        return new JobRunResult(processed, skipped);
    }

    // ═════════════════════════════════════════════════════════ bitta yozuv

    private async Task<bool> ReconcileAsync(
        SessionRecording recording, bool roomAudioMode, DateTimeOffset now, CancellationToken ct)
    {
        var session = recording.Session;
        var live = session?.Status == SessionStatus.Live;

        var over = session is null
            || session.Status is SessionStatus.Ended or SessionStatus.Cancelled;

        var changed = false;

        if (live)
        {
            changed |= await EnsureRoomAudioAsync(recording, roomAudioMode, now, ct)
                .ConfigureAwait(false);

            changed |= await RetryStuckAsync(recording, now, ct).ConfigureAwait(false);

            changed |= await DiscoverAsync(recording, roomAudioMode, now, ct).ConfigureAwait(false);

            changed |= await StopOverlongAsync(recording, now, ct).ConfigureAwait(false);
        }
        else if (over)
        {
            changed |= await FinalizeAsync(recording, session, now, ct).ConfigureAwait(false);

            changed |= TryQueue(recording, session, now);
        }

        if (changed) await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return changed;
    }

    // ═════════════════════════════════════════════════════════ 1) qayta urinish

    /// <summary>
    /// <c>Requested</c> da qotib qolgan bo'laklar: qayta uramiz yoki
    /// taslim bo'lamiz.
    ///
    /// ★ URINISHLAR ORASIDA KUTAMIZ — busiz LiveKit yiqilgan paytda
    /// vazifa har yurishda urinaverib, chegarani bir daqiqada tugatardi
    /// va bo'lak sababsiz <c>Failed</c> bo'lardi.
    /// </summary>
    private async Task<bool> RetryStuckAsync(
        SessionRecording recording, DateTimeOffset now, CancellationToken ct)
    {
        var roomName = recording.Session?.RoomName;

        if (string.IsNullOrWhiteSpace(roomName)) return false;

        var changed = false;

        foreach (var row in recording.Tracks
                     .Where(t => t.Status == RecordingStatus.Requested)
                     .ToList())
        {
            if (!row.CanRetry(options.MaxAttempts))
            {
                var reason = row.Error is { Length: > 0 } error
                    ? error
                    : "Bo'lakni yozib olishni boshlab bo'lmadi (urinishlar tugadi).";

                row.MarkFailed(reason, now);

                RecordingLog.ReconcileTrackGaveUp(logger, row.Id, recording.Id, reason);

                changed = true;

                continue;
            }

            if (row.LastAttemptAt is { } last && now - last < options.RetryDelay)
                continue;

            RecordingLog.ReconcileTrackRetry(logger, row.Id, recording.Id, row.Attempts);

            await StartAsync(recording, row, roomName, now, ct).ConfigureAwait(false);

            changed = true;
        }

        return changed;
    }

    // ═════════════════════════════════════════════════════════ 2) mikser bormi

    /// <summary>
    /// 🔴 BU VAZIFANING ENG QIMMATLI QADAMI. Xona ovozi qatori yo'q bo'lsa
    /// — <c>room_started</c> ham, birinchi <c>track_published</c> ham
    /// yo'qolgan degani, ya'ni dars OVOZSIZ yozilmoqda.
    ///
    /// ⚠️ FAQAT <c>RoomComposite</c> OVOZ REJIMIDA. Zaxira rejimda
    /// (<c>TeacherTrack</c>) ustozning mikrofoni alohida trek sifatida
    /// yoziladi va u yerga mikser qo'shilsa ustozning ovozi IKKI marta,
    /// biroz siljigan holda eshitilardi (§2.3).
    /// </summary>
    private async Task<bool> EnsureRoomAudioAsync(
        SessionRecording recording, bool roomAudioMode, DateTimeOffset now, CancellationToken ct)
    {
        if (!roomAudioMode) return false;

        if (recording.Tracks.Any(t => t.IsRoomAudio)) return false;

        var roomName = recording.Session?.RoomName;

        if (string.IsNullOrWhiteSpace(roomName)) return false;

        var row = NewRoomAudioRow(recording, RecordingTrack.RoomAudioSid, now);

        RecordingLog.ReconcileMixerEnsured(logger, recording.Id, row.TrackSid);

        await StartAllAsync(recording, [row], roomName, now, ct).ConfigureAwait(false);

        return true;
    }

    // ═════════════════════════════════════════════════════════ 3) LiveKit holati

    /// <summary>
    /// LiveKit HOZIR nima ko'rayotganini so'raydi: bizga noma'lum treklar
    /// va mikserning tirikligi.
    ///
    /// ★ BU YO'L API QAYTA ISHGA TUSHGANDAN KEYINGI TIKLASH: o'sha
    /// oraliqda kelgan webhook'lar butunlay yo'qolgan va boshqa manba
    /// yo'q.
    /// </summary>
    private async Task<bool> DiscoverAsync(
        SessionRecording recording, bool roomAudioMode, DateTimeOffset now, CancellationToken ct)
    {
        var session = recording.Session;
        var roomName = session?.RoomName;

        if (session is null || string.IsNullOrWhiteSpace(roomName)) return false;

        var changed = await DiscoverTracksAsync(recording, session, roomName, roomAudioMode, now, ct)
            .ConfigureAwait(false);

        changed |= await CheckMixerAsync(recording, roomName, roomAudioMode, now, ct)
            .ConfigureAwait(false);

        return changed;
    }

    private async Task<bool> DiscoverTracksAsync(
        SessionRecording recording,
        LiveSession session,
        string roomName,
        bool roomAudioMode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var listed = await rooms.ListParticipantsAsync(roomName, ct).ConfigureAwait(false);

        if (!listed.Succeeded)
        {
            RecordingLog.ReconcileLiveKitUnavailable(
                logger, recording.Id, listed.Error ?? "noma'lum");

            return false;
        }

        if (session.HostId is not { } hostId) return false;

        var host = hostId.ToString(CultureInfo.InvariantCulture);
        var rows = new List<RecordingTrack>();

        foreach (var published in listed.Tracks)
        {
            if (!string.Equals(published.ParticipantIdentity?.Trim(), host, StringComparison.Ordinal))
                continue;

            if (string.IsNullOrWhiteSpace(published.TrackSid)) continue;

            if (MapKind(published.Source, roomAudioMode) is not { } kind) continue;

            if (recording.Tracks.Any(t => string.Equals(t.TrackSid, published.TrackSid, StringComparison.Ordinal)))
                continue;

            if (rows.Any(t => string.Equals(t.TrackSid, published.TrackSid, StringComparison.Ordinal)))
                continue;

            RecordingLog.ReconcileTrackDiscovered(
                logger, recording.Id, published.TrackSid, kind.ToString());

            rows.Add(NewTrackRow(recording, published, kind, now));
        }

        if (rows.Count == 0) return false;

        await StartAllAsync(recording, rows, roomName, now, ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Mikser hali tirikmi.
    ///
    /// 🔴 FAQAT <c>Active</c> QATORLAR TEKSHIRILADI. <c>Starting</c> qator
    /// LiveKit ro'yxatida hali ko'rinmasligi mumkin va uni "o'lgan" deb
    /// hisoblash har dars boshida ikkinchi mikserni yoqardi.
    /// </summary>
    private async Task<bool> CheckMixerAsync(
        SessionRecording recording,
        string roomName,
        bool roomAudioMode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var active = recording.Tracks
            .Where(t => t.IsRoomAudio
                     && t.Status == RecordingStatus.Active
                     && !string.IsNullOrWhiteSpace(t.EgressId))
            .ToList();

        if (active.Count == 0) return false;

        var listed = await rooms.ListEgressAsync(roomName, ct).ConfigureAwait(false);

        if (!listed.Succeeded)
        {
            RecordingLog.ReconcileLiveKitUnavailable(
                logger, recording.Id, listed.Error ?? "noma'lum");

            return false;
        }

        var changed = false;

        foreach (var mixer in active)
        {
            if (listed.Items.Any(e => string.Equals(e.EgressId, mixer.EgressId, StringComparison.Ordinal)))
                continue;

            RecordingLog.ReconcileMixerDead(logger, recording.Id, mixer.Id, mixer.EgressId!);

            mixer.MarkFailed("Dars ovozi mikseri to'xtab qoldi.", now);

            changed = true;

            if (!roomAudioMode) continue;

            // ★ O'RNIGA YANGISI. Ikkinchi ovoz fayli paydo bo'ladi va
            //   sentinel tartib raqami bilan yoziladi (`ROOM2`, `ROOM3`…).
            //   Tungi yig'ish ularni `StartedAt` bo'yicha KETMA-KET
            //   ulaydi; oradagi bo'shliq — haqiqiy jimlik (§4.5-4).
            var replacement = NewRoomAudioRow(recording, NextRoomAudioSid(recording), now);

            RecordingLog.ReconcileMixerEnsured(logger, recording.Id, replacement.TrackSid);

            await StartAllAsync(recording, [replacement], roomName, now, ct).ConfigureAwait(false);
        }

        return changed;
    }

    // ═════════════════════════════════════════════════════════ 4) unutilgan xona

    /// <summary>
    /// To'rt soatdan ortiq ishlayotgan mikserni to'xtatadi.
    ///
    /// ★ Unutilgan xona (dars yopilmagan) mikserni kunlab yurgizardi:
    /// egress resursi ham, ombor ham, pul ham. Watchdog eski quvurda AYNI
    /// qo'riqni allaqachon bajaradi.
    /// </summary>
    private async Task<bool> StopOverlongAsync(
        SessionRecording recording, DateTimeOffset now, CancellationToken ct)
    {
        var changed = false;

        foreach (var mixer in recording.Tracks
                     .Where(t => t.IsRoomAudio
                              && t.Status is RecordingStatus.Starting or RecordingStatus.Active
                              && t.StopRequestedAt is null
                              && !string.IsNullOrWhiteSpace(t.EgressId))
                     .ToList())
        {
            var startedAt = mixer.StartedAt ?? mixer.LastAttemptAt ?? mixer.CreatedAt;

            if (now - startedAt < options.MaxDuration) continue;

            await egress.StopRecordingAsync(mixer.EgressId!, ct).ConfigureAwait(false);

            // Belgi HAR HOLDA qo'yiladi (watchdog'dagi AYNI qoida): LiveKit
            // allaqachon to'xtagan egress uchun rad javobini qaytaradi va
            // belgisiz qator har yurishda qayta to'xtatilardi.
            mixer.MarkStopRequested(now);

            RecordingLog.ReconcileMixerOverlong(logger, recording.Id, mixer.Id);

            changed = true;
        }

        return changed;
    }

    // ═════════════════════════════════════════════════════════ 5) yakunlash

    /// <summary>
    /// Dars tugadi, bo'laklar esa hali ochiq.
    ///
    /// Uch bosqich — <c>RecordingWatchdogJob.FinalizeAsync</c> dagi AYNI
    /// ketma-ketlik: (a) to'xtatish so'ralmagan bo'lsa so'raymiz;
    /// (b) muhlat kutamiz; (c) OMBORDAN so'raymiz.
    ///
    /// 🔴 HAQIQAT MANBAI — OMBORNING O'ZI, LiveKit hodisasi emas. Hodisa
    /// kelmagani faylning yo'qligini anglatmaydi.
    /// </summary>
    private async Task<bool> FinalizeAsync(
        SessionRecording recording, LiveSession? session, DateTimeOffset now, CancellationToken ct)
    {
        var endedAt = EndedAtOf(recording, session);
        var changed = false;

        foreach (var row in recording.Tracks.Where(t => !t.IsFinished).ToList())
        {
            if (row.StopRequestedAt is null && !string.IsNullOrWhiteSpace(row.EgressId))
            {
                await egress.StopRecordingAsync(row.EgressId, ct).ConfigureAwait(false);

                row.MarkStopRequested(now);

                changed = true;

                continue;       // fayl yuklanishini keyingi yurishda kutamiz
            }

            var waitingSince = row.StopRequestedAt ?? endedAt;

            if (now - waitingSince < options.FinalizeGrace) continue;

            StoredObjectInfo? stored;

            try
            {
                stored = await storage.HeadAsync(row.ObjectKey, ct).ConfigureAwait(false);
            }
            catch (ServiceUnavailableException ex)
            {
                // Ombor javob bermadi. 🔴 BU YERDA `Failed` QO'YIB BO'LMAYDI:
                // fayl bor bo'lishi ham mumkin, biz shunchaki ko'ra
                // olmadik. Keyingi yurishga qoldiramiz.
                RecordingLog.ReconcileStorageUnavailable(logger, ex, row.Id);

                continue;
            }

            if (stored is not null)
            {
                row.MarkCompleted(
                    objectKey: null,            // kalit BIZNIKI, u o'zgarmagan
                    sizeBytes: stored.SizeBytes,
                    durationSeconds: null,      // uzunlikni ombor bilmaydi
                    endedAt: now,
                    now: now);

                RecordingLog.ReconcileTrackRecovered(
                    logger, row.Id, recording.Id, stored.SizeBytes);
            }
            else
            {
                var reason = row.IsRoomAudio
                    ? "Dars ovozi omborga tushmadi."
                    : "Trek fayli omborga tushmadi.";

                row.MarkFailed(reason, now);

                RecordingLog.ReconcileTrackGaveUp(logger, row.Id, recording.Id, reason);
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Hamma bo'lak yakuniy holatga yetgan bo'lsa yozuvni TUNGI NAVBATGA
    /// qo'yadi.
    ///
    /// ⚠️ FAQAT OVOZ CHIQQANI — MUVAFFAQIYAT. FAQAT TASVIR CHIQQANI HAM.
    /// Ustoz kamerani umuman yoqmagan dars ham to'liq yozuv: o'quv bo'limi
    /// tushuntirish sifatini baholaydi va u OVOZDA. "Video majburiy"
    /// degan tekshiruv QO'SHILMAYDI (§4.1-6).
    ///
    /// ★ HECH NARSA CHIQMAGAN BO'LSA — MUHLAT KUTILADI: kechikkan
    /// <c>egress_ended</c> hali kelayotgan bo'lishi mumkin va yozuvni
    /// erta yopish uni QAYTMAS holga keltirardi.
    /// </summary>
    private bool TryQueue(SessionRecording recording, LiveSession? session, DateTimeOffset now)
    {
        if (recording.CompositionStatus != RecordingCompositionStatus.Collecting) return false;

        if (recording.Tracks.Any(t => !t.IsFinished)) return false;

        var completed = recording.Tracks.Count(t => t.Status == RecordingStatus.Completed);

        if (completed > 0)
        {
            recording.MarkRawCollected(now);

            RecordingLog.ReconcileQueued(logger, recording.Id, completed);

            return true;
        }

        if (now - EndedAtOf(recording, session) < options.FinalizeGrace) return false;

        recording.MarkCompositionFailed(RecordingCompositionPlanner.NoTracksReason, now);

        RecordingLog.ReconcileNoTracks(logger, recording.Id);

        return true;
    }

    // ═════════════════════════════════════════════════════════ boshlash

    /// <summary>
    /// Yangi qatorlarni saqlaydi va HAR BIRI uchun Egress'ni boshlaydi.
    ///
    /// 🔴 QATOR AVVAL SAQLANADI, EGRESS KEYIN BOSHLANADI — bu qoida
    /// <c>TrackRecordingWebhookHandler</c> da ham AYNAN shunday va sabab
    /// bir xil: teskari tartibda jarayon Twirp javobi bilan
    /// <c>SaveChanges</c> orasida uzilsa, LiveKit'da ISHLAYOTGAN mikser
    /// qolib, bizda uning qatori bo'lmasdi — keyingi yurish ikkinchi
    /// mikserni yoqib, darsning har ovozini ikki marta yozardi.
    ///
    /// Birinchi saqlashda unikal indeks (<c>RecordingId, TrackSid</c>)
    /// buzilsa istisno SHU YERDA chiqadi, ya'ni Egress'ga UMUMAN
    /// borilmaydi.
    ///
    /// ⚠️ USUL WEBHOOK ISHLOVCHISIDAN NUSXA KO'CHIRILGAN VA BU ONGLI:
    /// u yerdagi metod <c>private</c> va o'sha fayl boshqa modulga
    /// (M5) tegishli. Umumiy yordamchiga chiqarish ikkala modulga ham
    /// tegishni talab qilardi — qo'shimcha ish, additiv qoidadan
    /// tashqarida.
    /// </summary>
    private async Task StartAllAsync(
        SessionRecording recording,
        List<RecordingTrack> rows,
        string roomName,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var row in rows)
            recording.Tracks.Add(row);

        // 1-bosqich: JOYNI BAND QILAMIZ.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var row in rows)
            await StartAsync(recording, row, roomName, now, ct).ConfigureAwait(false);

        // 2-bosqich: Egress javoblari.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Bitta bo'lak uchun Egress'ni boshlaydi.
    ///
    /// ★ URINISH OLDIN SANALADI (<c>RecordingStarter</c> dagi AYNI
    /// mulohaza): Egress javobi umuman kelmasa ham urinish "bo'lmagan"
    /// deb qolmasin — aks holda vazifa cheksiz qayta urardi.
    /// </summary>
    private async Task StartAsync(
        SessionRecording recording,
        RecordingTrack row,
        string roomName,
        DateTimeOffset now,
        CancellationToken ct)
    {
        row.BeginAttempt(now);

        var result = row.IsRoomAudio
            ? await egress
                .StartRoomAudioRecordingAsync(
                    new RoomAudioEgressStartRequest(roomName, row.ObjectKey), ct)
                .ConfigureAwait(false)
            : await egress
                .StartTrackRecordingAsync(
                    new TrackEgressStartRequest(roomName, row.TrackSid, row.ObjectKey), ct)
                .ConfigureAwait(false);

        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.EgressId))
        {
            row.MarkStarting(result.EgressId, now);

            RecordingLog.TrackStarted(
                logger, row.Id, recording.Id, row.Kind.ToString(), result.EgressId);

            return;
        }

        var reason = string.IsNullOrWhiteSpace(result.Error)
            ? "Yozuv xizmati javob bermadi."
            : result.Error;

        // Holat `Requested` bo'lib QOLADI — yakuniy xato emas.
        row.RecordAttemptError(reason, now);

        RecordingLog.TrackStartFailed(
            logger, row.Id, recording.Id, row.Kind.ToString(), row.Attempts, reason);
    }

    // ═════════════════════════════════════════════════════════ yordamchilar

    private RecordingTrack NewRoomAudioRow(
        SessionRecording recording, string trackSid, DateTimeOffset now) =>
        new()
        {
            RecordingId = recording.Id,
            TrackSid = trackSid,
            ParticipantIdentity = null,     // aralashma HECH KIMGA tegishli emas
            Kind = RecordingTrackKind.RoomAudio,
            MimeType = RoomAudioMimeType,
            ObjectKey = storage.BuildRawObjectKey(
                recording.SessionId, recording.Id, trackSid, OggExtension),
            Status = RecordingStatus.Requested,
            CreatedAt = now,
        };

    private RecordingTrack NewTrackRow(
        SessionRecording recording,
        LiveKitPublishedTrackDto published,
        RecordingTrackKind kind,
        DateTimeOffset now) =>
        new()
        {
            RecordingId = recording.Id,
            TrackSid = published.TrackSid,
            ParticipantIdentity = published.ParticipantIdentity,
            Kind = kind,
            MimeType = TrimMime(published.MimeType),
            ObjectKey = storage.BuildRawObjectKey(
                recording.SessionId,
                recording.Id,
                published.TrackSid,
                ExtensionOf(published.MimeType, kind)),
            Status = RecordingStatus.Requested,
            CreatedAt = now,
        };

    /// <summary>
    /// Keyingi xona ovozi sentineli: <c>ROOM</c>, <c>ROOM2</c>, <c>ROOM3</c>…
    ///
    /// ⚠️ Unikal indeks <c>(RecordingId, TrackSid)</c> ni buzmaslik SHART,
    /// shuning uchun raqam MAVJUD qatorlar soniga qarab beriladi.
    /// </summary>
    private static string NextRoomAudioSid(SessionRecording recording)
    {
        var count = recording.Tracks.Count(t => t.IsRoomAudio);

        return count == 0
            ? RecordingTrack.RoomAudioSid
            : RecordingTrack.RoomAudioSid + (count + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Dars qachon tugagani. Bekor qilingan darsda <c>ActualEnd</c>
    /// bo'lmaydi, shuning uchun zaxira qiymatlar bor.
    /// </summary>
    private static DateTimeOffset EndedAtOf(SessionRecording recording, LiveSession? session) =>
        session?.ActualEnd ?? session?.UpdatedAt ?? session?.ScheduledEnd ?? recording.CreatedAt;

    /// <summary>
    /// LiveKit manbasini bo'lak turiga xaritalaydi —
    /// <c>TrackRecordingWebhookHandler.MapKind</c> BILAN AYNI QOIDA.
    ///
    /// 🔴 <c>RoomComposite</c> REJIMIDA MIKROFON VA EKRAN OVOZI QATOR
    /// YARATMAYDI: ular xona aralashmasida allaqachon bor va ikkinchi
    /// marta yozilsa taroqli filtrlash paydo bo'lardi (§2.3).
    ///
    /// ⚠️ SPEC §4.1-3 faqat VIDEO treklarni tiklashni so'raydi. Bu yerda
    /// zaxira rejimdagi OVOZ treklari ham tiklanadi va bu ONGLI
    /// kengaytma: o'sha rejimda ustozning mikrofoni yagona ovoz manbai,
    /// ya'ni uning yo'qolishi darsni butunlay jim qoldirardi — bu esa
    /// aynan shu vazifa oldini olishi kerak bo'lgan holat.
    /// </summary>
    private static RecordingTrackKind? MapKind(string? source, bool roomAudioMode) =>
        source?.Trim().ToUpperInvariant() switch
        {
            "CAMERA" => RecordingTrackKind.CameraVideo,
            "SCREEN_SHARE" => RecordingTrackKind.ScreenVideo,
            "MICROPHONE" => roomAudioMode ? null : RecordingTrackKind.MicAudio,
            "SCREEN_SHARE_AUDIO" => roomAudioMode ? null : RecordingTrackKind.ScreenAudio,

            _ => null,
        };

    /// <summary>
    /// Xom faylning kengaytmasini <c>mime_type</c> dan BASHORAT qiladi
    /// (§2.8) — webhook ishlovchisidagi AYNI jadval.
    ///
    /// ⚠️ BASHORATGA ISHONILMAYDI: haqiqiy nom <c>egress_ended</c> da
    /// keladi va farq qilsa kalit o'shanisi bilan almashtiriladi.
    /// </summary>
    private static string ExtensionOf(string? mimeType, RecordingTrackKind kind)
    {
        var mime = mimeType?.Trim().ToUpperInvariant().Split(';', 2)[0].Trim();

        return mime switch
        {
            "VIDEO/VP8" or "VIDEO/VP9" => WebmExtension,
            "VIDEO/H264" => "mp4",
            "AUDIO/OPUS" => OggExtension,

            _ => kind is RecordingTrackKind.CameraVideo or RecordingTrackKind.ScreenVideo
                ? WebmExtension
                : OggExtension,
        };
    }

    /// <summary>
    /// <c>MimeType</c> ustuni 64 belgi; uzun qiymat <c>SaveChanges</c> ni
    /// 22001 bilan yiqitib, BUTUN bo'lakni yo'qotardi. Maydon faqat
    /// tashxis uchun — kengaytma undan allaqachon hisoblangan.
    /// </summary>
    private static string? TrimMime(string? mimeType)
    {
        var mime = mimeType?.Trim();

        return mime is { Length: > MimeTypeMaxLength } ? mime[..MimeTypeMaxLength] : mime;
    }

    /// <summary>
    /// Xona ovozi rejimi yoqilganmi (<c>recordings.audio_capture_mode</c>).
    ///
    /// 🔴 TEKSHIRUV "<c>TeacherTrack</c> EMASMI" SHAKLIDA — webhook
    /// ishlovchisidagi AYNI mulohaza: sozlamada tushunarsiz qiymat tursa,
    /// teskari tekshiruv ikkala ovoz manbasini ham yoqib yuborishi mumkin
    /// edi. Bu shaklda noma'lum qiymat STANDART rejimga tushadi.
    ///
    /// ⚠️ SOZLAMA HALI REGISTRDA BO'LMASLIGI MUMKIN (uni M7 qo'shadi) —
    /// bunda SPEC dagi standart (<c>RoomComposite</c>) ishlatiladi.
    /// </summary>
    private async Task<bool> RoomAudioModeAsync(CancellationToken ct)
    {
        if (!SettingsRegistry.TryGet(AudioCaptureModeKey, out var definition))
            return true;

        var resolved = await settings.ResolveAsync(definition, ct).ConfigureAwait(false);

        return !string.Equals(
            resolved.Value?.Trim(), TeacherTrackMode, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- doimiylar

    private const int MimeTypeMaxLength = 64;

    private const string AudioCaptureModeKey = "recordings.audio_capture_mode";

    private const string TeacherTrackMode = "TeacherTrack";

    private const string RoomAudioMimeType = "audio/opus";

    private const string OggExtension = "ogg";

    private const string WebmExtension = "webm";
}

/// <summary>
/// Moslashtiruvchining chegaralari.
///
/// ★ NIMA UCHUN ALOHIDA YOZUV VA <c>IOptions</c> EMAS: Application
/// qatlami konfiguratsiya tizimini bilmaydi —
/// <c>RecordingWatchdogSettings</c> bilan AYNI naqsh.
/// </summary>
/// <param name="Interval">
/// Ikki yurish orasidagi masofa.
///
/// ⚠️ AMALDAGI ENG KICHIK QIYMAT <c>Jobs:TickSeconds</c> (30 s) —
/// rejalashtiruvchi undan tez uyg'onmaydi.
/// </param>
/// <param name="RetryDelay">
/// Bo'lakni qayta boshlashgacha kutish. Busiz LiveKit yiqilgan paytda
/// urinishlar chegarasi bir daqiqada tugab qolardi.
/// </param>
/// <param name="FinalizeGrace">
/// Dars tugagach (yoki to'xtatish so'ralgach) <c>egress_ended</c> ni
/// kutish muddati.
///
/// ⚠️ SAXOVATLI BO'LISHI SHART: uzun bo'lakni omborga yuklash daqiqalar
/// oladi va erta yakun TAYYOR faylni yo'qotardi.
/// </param>
/// <param name="MaxDuration">
/// Mikserning eng uzun umri. <c>RecordingWatchdogSettings.MaxDuration</c>
/// bilan AYNI qiymat va AYNI sabab: unutilgan xona kunlab yozib turmasin.
/// </param>
/// <param name="MaxAttempts">Bo'lakni boshlashga eng ko'p necha urinish.</param>
/// <param name="BatchSize">Bir yurishda ko'pi bilan nechta yozuv.</param>
public sealed record RecordingTrackReconcileSettings(
    TimeSpan Interval,
    TimeSpan RetryDelay,
    TimeSpan FinalizeGrace,
    TimeSpan MaxDuration,
    int MaxAttempts,
    int BatchSize)
{
    /// <summary>SPEC §4.1 dagi standart chegaralar.</summary>
    public static RecordingTrackReconcileSettings Default { get; } = new(
        Interval: TimeSpan.FromSeconds(60),
        RetryDelay: TimeSpan.FromSeconds(60),
        FinalizeGrace: TimeSpan.FromMinutes(10),
        MaxDuration: TimeSpan.FromHours(4),
        MaxAttempts: 5,
        BatchSize: 100);
}
