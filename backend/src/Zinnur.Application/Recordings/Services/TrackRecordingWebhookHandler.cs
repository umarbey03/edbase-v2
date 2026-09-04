using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// <see cref="ITrackRecordingWebhookHandler"/> ning amalga oshirilishi.
///
/// ── DARSNING HAQIQIY OQIMI (aynan shu ketma-ketlik ishlanadi) ───────────
///
///   room_started        -> xona ovozi mikserini yoqamiz (1 ta qator)
///   track_published     -> mikserni KAFOLATLAYMIZ + kamera treki
///   track_published     -> dars o'rtasida EKRAN ulashildi -> yangi bo'lak
///   track_unpublished   -> ekran o'chirildi -> bo'lak yopiladi
///   participant_left    -> ustoz uzildi -> ochiq VIDEO bo'laklar yopiladi
///   track_published     -> ustoz qaytdi  -> YANGI sid, yangi bo'lak
///   room_finished       -> dars tugadi   -> qolgan hamma bo'lak yopiladi
///
/// 🔴 BITTA DARSDA BIR NECHTA VIDEO BO'LAK — ODATIY HOL. Mikser esa
/// BITTA va u ustoz uzilganda ham TO'XTAMAYDI: o'quvchilar gapirishda
/// davom etadi va aynan shu uzluksiz ovoz fayli tungi yig'ishning VAQT
/// O'QI (SPEC-RECORDING-V2 §3.2, §9.1).
///
/// ── ⚠️ QATOR AVVAL SAQLANADI, EGRESS KEYIN BOSHLANADI ───────────────────
///
/// Har boshlash IKKI <c>SaveChanges</c> dan iborat:
///
///   1) qator yoziladi (<c>Requested</c>) — <c>SaveChanges</c>;
///   2) Egress chaqiriladi va javob qatorga yoziladi — <c>SaveChanges</c>.
///
/// ★ NIMA UCHUN AYNAN SHU TARTIBDA, VA NIMA UCHUN BU ESKI ISHLOVCHIDAGI
///   "BITTA TRANZAKSIYA" QOIDASIDAN CHEKINISHGA ARZIYDI:
///
///   Teskari tartibda (avval Egress, keyin saqlash) jarayon Twirp
///   javobidan KEYIN, <c>SaveChanges</c> dan OLDIN uzilsa, LiveKit'da
///   ISHLAYOTGAN mikser qoladi, bizda esa u haqda HECH QANDAY qator
///   yo'q. Tiklash vazifasi (§4.1) "mikser qatori yo'q" deb xulosa
///   chiqarib IKKINCHISINI ishga tushirardi — bitta darsda ikki ovoz
///   fayli va tungi yig'ishda HAR OVOZ IKKI MARTA. Bu nosozlikni
///   eshitib topish mumkin, tuzatib bo'lmaydi (fayl allaqachon shunday
///   yozilgan).
///
///   Bu tartibda esa eng yomon holat — <c>Requested</c> holatida qolgan
///   qator, uni tiklash vazifasi 60 soniyada qayta uradi. Ya'ni ikkita
///   nosozlikning ARZONI tanlangan.
///
///   Qo'shimcha foyda: <c>UX_RecordingTracks_RecordingId_TrackSid</c>
///   unikal indeksi endi HAQIQATAN qo'riqlaydi — ikkita bir vaqtda
///   kelgan hodisadan biri BIRINCHI <c>SaveChanges</c> da yiqiladi va
///   Egress'ga umuman bormaydi.
///
/// ── TAKROR JURNALI — ARZON FILTRLARDAN KEYIN ────────────────────────────
///
/// <see cref="ILiveKitWebhookLog"/> ga faqat holat O'ZGARADIGAN hodisa
/// yoziladi. 25 kishilik dars yuzlab <c>participant_*</c> hodisasi
/// yuboradi va ularning har birini jadvalga yozish uni tozalash
/// vazifasisiz cheksiz o'stirardi (§3.3).
///
/// 🔴 Bundan tashqari bu QAT'IY SHART: <c>Ignored</c> qaytadigan yo'lda
/// jurnalga tegilsa, eski ishlovchi hodisani takror deb ko'rardi —
/// batafsil sabab <see cref="ITrackRecordingWebhookHandler"/> izohida.
/// </summary>
public sealed class TrackRecordingWebhookHandler(
    IApplicationDbContext db,
    ILiveKitEgress egress,
    IRecordingStorage storage,
    ILiveKitWebhookLog log,
    ISettingsResolver settings,
    TimeProvider clock,
    ILogger<TrackRecordingWebhookHandler> logger) : ITrackRecordingWebhookHandler
{
    /// <inheritdoc />
    public async Task<RecordingWebhookOutcome> HandleAsync(
        ReadOnlyMemory<byte> body, CancellationToken ct = default)
    {
        var evt = LiveKitWebhookParser.ParseTrackEvent(body.Span);

        // ⚠️ BUZUQ TANA HAM `Ignored`: xabarni eski ishlovchi bersin
        //    (u `Malformed` qaytaradi va logga yozadi). Ikkalasi ham
        //    xabar bersa bitta nosozlik ikki qator bo'lib ko'rinardi.
        if (evt is null)
            return RecordingWebhookOutcome.Ignored;

        if (IsEgressEvent(evt.EventName))
            return await EgressEventAsync(body, ct).ConfigureAwait(false);

        return evt.EventName switch
        {
            RoomStarted => await RoomStartedAsync(evt, ct).ConfigureAwait(false),
            TrackPublished => await TrackPublishedAsync(evt, ct).ConfigureAwait(false),
            TrackUnpublished => await TrackUnpublishedAsync(evt, ct).ConfigureAwait(false),
            ParticipantLeft => await ParticipantLeftAsync(evt, ct).ConfigureAwait(false),
            RoomFinished => await RoomFinishedAsync(evt, ct).ConfigureAwait(false),

            _ => RecordingWebhookOutcome.Ignored,
        };
    }

    // ═════════════════════════════════════════════════════════ xona hodisalari

    /// <summary>
    /// <c>room_started</c> — xona ovozi mikserini yoqadi.
    ///
    /// ★ NIMA UCHUN AYNAN BU HODISADA: <c>LiveSessionService.StartAsync</c>
    /// yozuv qatorini ustoz "Darsni boshlash" ni bosgan zahoti yozadi,
    /// brauzer esa xonaga bir necha soniyadan keyin kiradi. Ya'ni
    /// <c>room_started</c> kelganda qator ODATDA allaqachon bor va mikser
    /// darsning BIRINCHI soniyasidan yoziladi.
    /// </summary>
    private async Task<RecordingWebhookOutcome> RoomStartedAsync(
        LiveKitTrackEventDto evt, CancellationToken ct)
    {
        var recording = await ResolveAsync(evt.RoomName, ct).ConfigureAwait(false);

        if (recording is null)
            return RecordingWebhookOutcome.Ignored;

        if (!await RoomAudioModeAsync(ct).ConfigureAwait(false))
            return RecordingWebhookOutcome.Ignored;     // `TeacherTrack` rejimi — mikser YO'Q

        if (recording.Tracks.Any(t => t.IsRoomAudio))
            return RecordingWebhookOutcome.Duplicate;

        if (!await log.TryBeginAsync(evt.EventId, ct).ConfigureAwait(false))
            return RecordingWebhookOutcome.Duplicate;

        var now = clock.GetUtcNow();

        await StartAllAsync(recording, [NewRoomAudioRow(recording, now)], now, ct).ConfigureAwait(false);

        return RecordingWebhookOutcome.Started;
    }

    /// <summary>
    /// <c>room_finished</c> — xona yopildi, demak HECH BIR bo'lak endi
    /// yozilayotgan bo'lishi mumkin emas.
    ///
    /// ⚠️ SPEC (§3.3) bu yerda faqat MIKSERNI to'xtatishni talab qiladi,
    /// chunki video bo'laklar odatda oldinroq kelgan
    /// <c>participant_left</c> bilan yopiladi. Bu yerda BARCHA ochiq
    /// bo'lak yopiladi va bu ATAYLAB kengroq: <c>participant_left</c>
    /// yetkazilmasligi mumkin (API qayta ishga tushgan bo'lsa), o'sha
    /// holatda video qatori tiklash vazifasining 4 soatlik chegarasigacha
    /// "ochiq" bo'lib turardi va tungi yig'ish o'sha darsni butun kecha
    /// kutardi. Xona yo'q bo'lgach to'xtatish so'rovi esa hech qanday
    /// zarar keltirmaydi — LiveKit uni rad etsa, bu NORMAL javob.
    /// </summary>
    private Task<RecordingWebhookOutcome> RoomFinishedAsync(
        LiveKitTrackEventDto evt, CancellationToken ct) =>
        StopManyAsync(evt, static _ => true, ct);

    /// <summary>
    /// <c>participant_left</c> — USTOZ uzildi (yoki darsni tark etdi).
    ///
    /// 🔴 MIKSER TO'XTAMAYDI. Bu butun ovoz sxemasining eng qimmatli
    /// xossasi: o'quvchilar ustoz qayta ulanayotgan paytda ham gapiradi
    /// va ularning ovozi yozuvda qoladi. Ustozning uzilishi TASVIRDA
    /// ko'rinadigan kesim bo'ladi, ovozda esa umuman bilinmaydi (§3.6).
    ///
    /// ⚠️ SPEC (§3.3) "video bo'laklar" deydi; bu yerda mikserdan
    /// TASHQARI hamma bo'lak yopiladi. Farq faqat `TeacherTrack`
    /// zaxira rejimida sezilarli: u yerda ustozning mikrofoni ham
    /// alohida `TrackEgress` bo'ladi va u ustoz ketgach yozadigan hech
    /// narsasi qolmaydi. "Faqat video" deb yozilsa, o'sha egress 4 soatlik
    /// chegaragacha ishlab turardi.
    /// </summary>
    private async Task<RecordingWebhookOutcome> ParticipantLeftAsync(
        LiveKitTrackEventDto evt, CancellationToken ct)
    {
        var recording = await ResolveAsync(evt.RoomName, ct).ConfigureAwait(false);

        if (recording is null || !IsHost(recording.Session, evt.ParticipantIdentity))
            return RecordingWebhookOutcome.Ignored;

        return await StopManyAsync(evt, t => !t.IsRoomAudio, recording, ct).ConfigureAwait(false);
    }

    // ═════════════════════════════════════════════════════════ trek hodisalari

    /// <summary>
    /// <c>track_published</c> — yangi trek e'lon qilindi.
    ///
    /// IKKI ISH BIR VAQTDA BAJARILADI:
    ///
    ///   1) MIKSER KAFOLATI. <c>room_started</c> yo'qolgan bo'lsa (yoki
    ///      yozuv qatori o'sha paytda hali yozilmagan bo'lsa) mikser
    ///      AYNAN shu yerda, millisekundlar ichida yoqiladi. Aks holda uni
    ///      tiklash vazifasi 60 soniyagacha kutardi — darsning boshidagi
    ///      60 soniya ovozsizlik esa eng ko'zga tashlanadigan yo'qotish.
    ///      Takroriy urinish BEPUL: <c>(RecordingId, TrackSid)</c> unikal
    ///      indeksi ikkinchi qatorni yozdirmaydi.
    ///
    ///   2) TREKNING O'ZI — faqat XOST e'lon qilgan va xaritalanadigan
    ///      manba bo'lsa (§3.1).
    /// </summary>
    private async Task<RecordingWebhookOutcome> TrackPublishedAsync(
        LiveKitTrackEventDto evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.TrackSid))
            return RecordingWebhookOutcome.Ignored;

        var recording = await ResolveAsync(evt.RoomName, ct).ConfigureAwait(false);

        if (recording is null)
            return RecordingWebhookOutcome.Ignored;

        var roomAudio = await RoomAudioModeAsync(ct).ConfigureAwait(false);

        var kind = MapKind(evt.TrackSource, roomAudio);
        var known = recording.Tracks.Any(t => t.TrackSid == evt.TrackSid);

        var needsRoomAudio = roomAudio && !recording.Tracks.Any(t => t.IsRoomAudio);
        var needsTrack = !known && kind is not null && IsHost(recording.Session, evt.ParticipantIdentity);

        if (!needsRoomAudio && !needsTrack)
        {
            // `known` — hodisa BIZNIKI edi va allaqachon ishlangan
            // (LiveKit takror yuborgan). Qolgan holatlar: o'quvchining
            // treki, `RoomComposite` rejimidagi mikrofon, noma'lum manba —
            // ular BIZGA UMUMAN tegishli emas.
            return known ? RecordingWebhookOutcome.Duplicate : RecordingWebhookOutcome.Ignored;
        }

        if (!await log.TryBeginAsync(evt.EventId, ct).ConfigureAwait(false))
            return RecordingWebhookOutcome.Duplicate;

        var now = clock.GetUtcNow();
        var rows = new List<RecordingTrack>(2);

        if (needsRoomAudio)
            rows.Add(NewRoomAudioRow(recording, now));

        if (needsTrack)
            rows.Add(NewTrackRow(recording, evt, kind!.Value, now));

        await StartAllAsync(recording, rows, now, ct).ConfigureAwait(false);

        return RecordingWebhookOutcome.Started;
    }

    /// <summary>
    /// <c>track_unpublished</c> — trek olib tashlandi (ekran o'chirildi,
    /// kamera yopildi).
    ///
    /// ⚠️ MIKSER QATORI BU YERGA HECH QACHON TUSHMAYDI: u LiveKit treki
    /// emas va uning sentineli (<c>ROOM</c>) haqiqiy <c>TR_…</c> bilan
    /// to'qnasha olmaydi. Shunga qaramay tekshiruv <c>IsRoomAudio</c>
    /// bo'yicha OSHKOR qo'yilgan — qoida kodda ko'rinib tursin.
    /// </summary>
    private async Task<RecordingWebhookOutcome> TrackUnpublishedAsync(
        LiveKitTrackEventDto evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.TrackSid))
            return RecordingWebhookOutcome.Ignored;

        return await StopManyAsync(
                evt, t => !t.IsRoomAudio && t.TrackSid == evt.TrackSid, ct)
            .ConfigureAwait(false);
    }

    // ═════════════════════════════════════════════════════════ egress hodisalari

    /// <summary>
    /// <c>egress_started</c> / <c>egress_updated</c> / <c>egress_ended</c>.
    ///
    /// 🔴 QATOR AVVAL TOPILADI, TAKROR JURNALIGA KEYIN TEGILADI. Bu
    /// tartib MAJBURIY: bu hodisalarning KO'PCHILIGI eski quvurga
    /// tegishli va ular <c>Ignored</c> bilan o'tkazib yuborilishi kerak.
    /// Jurnalni oldin band qilsak, eski ishlovchi o'z hodisasini takror
    /// deb ko'rib, dars yozuvini yakunlamay qo'yardi.
    ///
    /// ★ Qidiruv <c>EgressId</c> bo'yicha: u unikal va AYNAN BITTA
    /// urinishga tegishli — eski ishlovchidagi bilan bir xil mulohaza.
    /// Trek egress'i va xona egress'i identifikatorlari bir-biriga
    /// aralashmaydi, ya'ni ikki ishlovchi bir qatorga tegib qolmaydi.
    /// </summary>
    private async Task<RecordingWebhookOutcome> EgressEventAsync(
        ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var evt = LiveKitWebhookParser.Parse(body.Span);

        if (evt is null || string.IsNullOrWhiteSpace(evt.EgressId))
            return RecordingWebhookOutcome.Ignored;

        var track = await db.RecordingTracks
            .AsTracking()
            .FirstOrDefaultAsync(t => t.EgressId == evt.EgressId, ct)
            .ConfigureAwait(false);

        if (track is null)
            return RecordingWebhookOutcome.Ignored;     // eski quvurniki — tegmaymiz

        if (!await log.TryBeginAsync(evt.EventId, ct).ConfigureAwait(false))
            return RecordingWebhookOutcome.Duplicate;

        var outcome = Apply(track, evt, clock.GetUtcNow());

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        RecordingLog.TrackWebhookApplied(
            logger, evt.EventName, evt.EgressId!, track.Id, track.Status.ToString());

        return outcome;
    }

    /// <summary>
    /// Hodisani bo'lakning holat o'zgarishiga aylantiradi.
    ///
    /// ★ ESKI ISHLOVCHIDAGI QOIDA AYNAN TAKRORLANADI: qaror avval
    /// <c>status</c> maydoniga, keyin hodisa nomiga qaraydi. Sabab
    /// <see cref="RecordingWebhookHandler"/> izohida — <c>egress_updated</c>
    /// ichida ham <c>EGRESS_COMPLETE</c> kelishi mumkin, holat maydoni esa
    /// protokolning O'ZIDA aniqlangan qiymat.
    /// </summary>
    private RecordingWebhookOutcome Apply(
        RecordingTrack track, LiveKitWebhookEventDto evt, DateTimeOffset now)
    {
        var status = evt.EgressStatus?.Trim().ToUpperInvariant();

        return status switch
        {
            EgressActive => Activate(track, evt, now),
            EgressComplete => Complete(track, evt, now),
            EgressFailed or EgressAborted or EgressLimitReached => Fail(track, evt, now, status),

            // Oraliq holatlar — qatorga TEGILMAYDI (orqaga qaytarish faqat
            // chalkashlik bo'lardi), lekin hodisa BIZNIKI: `Ignored`
            // qaytarsak u eski ishlovchiga tushib, "noma'lum egress"
            // ogohlantirishini yozardi.
            EgressStarting or EgressEnding => RecordingWebhookOutcome.Handled,

            _ => ApplyByName(track, evt, now),
        };
    }

    private RecordingWebhookOutcome ApplyByName(
        RecordingTrack track, LiveKitWebhookEventDto evt, DateTimeOffset now) =>
        evt.EventName switch
        {
            EgressStartedEvent => Activate(track, evt, now),

            // Nomi "tugadi", holat esa noma'lum. Fayl kaliti kelmagan
            // bo'lsa bu XATO: "fayl yo'q, lekin tugadi" ni muvaffaqiyat
            // deb belgilash tungi yig'ishni MAVJUD BO'LMAGAN faylni
            // yuklab olishga yuborardi.
            EgressEndedEvent => string.IsNullOrWhiteSpace(evt.ObjectKey)
                ? Fail(track, evt, now, EgressEndedEvent)
                : Complete(track, evt, now),

            _ => RecordingWebhookOutcome.Handled,
        };

    private static RecordingWebhookOutcome Activate(
        RecordingTrack track, LiveKitWebhookEventDto evt, DateTimeOffset now)
    {
        track.MarkActive(evt.StartedAt ?? now, now);

        return RecordingWebhookOutcome.Started;
    }

    /// <summary>
    /// Xom fayl omborda.
    ///
    /// ⚠️ KENGAYTMA BASHORATI SHU YERDA TEKSHIRILADI. Kalit qator
    /// yaratilganda <c>mime_type</c> dan TAXMIN qilingan; LiveKit esa
    /// haqiqiy nomni qaytaradi. Farq bo'lsa qatordagi kalit ustidan
    /// yoziladi (<see cref="RecordingTrack.MarkCompleted"/> shuni qiladi)
    /// va farqning O'ZI logga tushadi: §2.8 dagi xaritalash jadvalini
    /// PRODUKSIYA DALILI bilan tuzatish uchun boshqa manba yo'q.
    /// </summary>
    private RecordingWebhookOutcome Complete(
        RecordingTrack track, LiveKitWebhookEventDto evt, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(evt.ObjectKey)
            && !string.Equals(evt.ObjectKey, track.ObjectKey, StringComparison.Ordinal))
        {
            RecordingLog.TrackObjectKeyDiffers(logger, track.Id, track.ObjectKey, evt.ObjectKey!);
        }

        track.MarkCompleted(
            evt.ObjectKey, evt.FileSizeBytes, evt.DurationSeconds, evt.EndedAt ?? now, now);

        return RecordingWebhookOutcome.Completed;
    }

    /// <summary>
    /// Bo'lak chiqmadi.
    ///
    /// ⚠️ BUTUN YOZUV YIQILMAYDI: yig'ish qolgan bo'laklardan davom etadi
    /// va yo'qolgan joy qora ekran (video) yoki jimlik (ovoz) bo'lib
    /// chiqadi (§4.1).
    /// </summary>
    private static RecordingWebhookOutcome Fail(
        RecordingTrack track, LiveKitWebhookEventDto evt, DateTimeOffset now, string? source)
    {
        var reason = string.IsNullOrWhiteSpace(evt.Error)
            ? $"LiveKit bo'lakni yakunlay olmadi ({source ?? evt.EventName})."
            : evt.Error;

        track.MarkFailed(reason, now);

        return RecordingWebhookOutcome.Failed;
    }

    // ═════════════════════════════════════════════════════════ boshlash

    /// <summary>
    /// Yangi qatorlarni saqlaydi va HAR BIRI uchun Egress'ni boshlaydi.
    ///
    /// Ikki bosqichli saqlashning sababi sinf izohida ("QATOR AVVAL
    /// SAQLANADI") — qisqasi: LiveKit'da ishlayotgan, bizda esa qatori
    /// yo'q egress ikkinchi mikserga olib keladi.
    /// </summary>
    private async Task StartAllAsync(
        SessionRecording recording,
        List<RecordingTrack> rows,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (rows.Count == 0) return;

        db.RecordingTracks.AddRange(rows);

        // 1-bosqich: JOYNI BAND QILAMIZ. Unikal indeks buzilsa istisno
        // bu yerda chiqadi — ya'ni Egress'ga UMUMAN borilmaydi va
        // ikkinchi mikser paydo bo'lmaydi. Controller istisnoni ushlab
        // LiveKit'ga baribir 200 qaytaradi.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var roomName = recording.Session?.RoomName;

        if (string.IsNullOrWhiteSpace(roomName))
            return;     // amalda bo'lmaydi: `Session` yuklangan (`ResolveAsync`)

        foreach (var row in rows)
        {
            // ★ URINISH OLDIN SANALADI (`RecordingStarter` dagi AYNI
            //   mulohaza): Egress javobi umuman kelmasa ham urinish
            //   "bo'lmagan" deb qolmasin — aks holda tiklash vazifasi
            //   cheksiz qayta urardi.
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

                continue;
            }

            var reason = string.IsNullOrWhiteSpace(result.Error)
                ? "Yozuv xizmati javob bermadi."
                : result.Error;

            // Holat `Requested` bo'lib QOLADI — bu yakuniy xato emas,
            // tiklash vazifasi qayta uradi (§4.1, 1-qadam).
            row.RecordAttemptError(reason, now);

            RecordingLog.TrackStartFailed(
                logger, row.Id, recording.Id, row.Kind.ToString(), row.Attempts, reason);
        }

        // 2-bosqich: Egress javoblari (`EgressId` yoki sabab).
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// XONA OVOZI qatori — butun darsga BITTA uzluksiz Opus fayl.
    ///
    /// ⚠️ <see cref="RecordingTrack.ParticipantIdentity"/> ATAYLAB
    /// <c>null</c>: aralashma HECH KIMGA tegishli emas (entity izohi).
    /// Kengaytma esa bashorat emas — so'rovda fayl turini <c>OGG</c> deb
    /// belgilaymiz, ya'ni <c>.ogg</c> — fakt (§3.4b).
    /// </summary>
    private RecordingTrack NewRoomAudioRow(SessionRecording recording, DateTimeOffset now) =>
        new()
        {
            RecordingId = recording.Id,
            TrackSid = RecordingTrack.RoomAudioSid,
            ParticipantIdentity = null,
            Kind = RecordingTrackKind.RoomAudio,
            MimeType = RoomAudioMimeType,
            ObjectKey = storage.BuildRawObjectKey(
                recording.SessionId, recording.Id, RecordingTrack.RoomAudioSid, OggExtension),
            Status = RecordingStatus.Requested,
            CreatedAt = now,
        };

    private RecordingTrack NewTrackRow(
        SessionRecording recording,
        LiveKitTrackEventDto evt,
        RecordingTrackKind kind,
        DateTimeOffset now) =>
        new()
        {
            RecordingId = recording.Id,
            TrackSid = evt.TrackSid!,
            ParticipantIdentity = evt.ParticipantIdentity,
            Kind = kind,
            MimeType = TrimMime(evt.MimeType),
            ObjectKey = storage.BuildRawObjectKey(
                recording.SessionId, recording.Id, evt.TrackSid!, ExtensionOf(evt.MimeType, kind)),
            Status = RecordingStatus.Requested,
            CreatedAt = now,
        };

    // ═════════════════════════════════════════════════════════ to'xtatish

    private Task<RecordingWebhookOutcome> StopManyAsync(
        LiveKitTrackEventDto evt, Func<RecordingTrack, bool> selector, CancellationToken ct) =>
        StopManyAsync(evt, selector, recording: null, ct);

    /// <summary>
    /// Tanlangan ochiq bo'laklarni yopadi: <c>StopEgress</c> + qatorga
    /// belgi.
    ///
    /// ★ NIMA UCHUN <c>StopRequestedAt</c> JAVOBGA QARAMASDAN QO'YILADI
    /// (watchdog'dagi AYNI qoida): LiveKit allaqachon to'xtagan egress
    /// uchun xato qaytaradi. Belgi qo'yilmasa, o'sha qator har hodisada
    /// yana va yana to'xtatilishga urinilardi.
    /// </summary>
    /// <param name="recording">
    /// Allaqachon yuklangan yozuv (chaqiruvchi uni xost tekshiruvi uchun
    /// o'qigan bo'lsa) — ikkinchi marta bazaga bormaslik uchun.
    /// </param>
    private async Task<RecordingWebhookOutcome> StopManyAsync(
        LiveKitTrackEventDto evt,
        Func<RecordingTrack, bool> selector,
        SessionRecording? recording,
        CancellationToken ct)
    {
        recording ??= await ResolveAsync(evt.RoomName, ct).ConfigureAwait(false);

        if (recording is null)
            return RecordingWebhookOutcome.Ignored;

        var open = recording.Tracks.Where(t => IsOpen(t) && selector(t)).ToList();

        if (open.Count == 0)
            return RecordingWebhookOutcome.Ignored;

        if (!await log.TryBeginAsync(evt.EventId, ct).ConfigureAwait(false))
            return RecordingWebhookOutcome.Duplicate;

        var now = clock.GetUtcNow();

        foreach (var row in open)
        {
            var accepted = await egress
                .StopRecordingAsync(row.EgressId!, ct)
                .ConfigureAwait(false);

            if (accepted)
                RecordingLog.TrackStopRequested(logger, row.Id, recording.Id, row.EgressId!);
            else
                RecordingLog.TrackStopRefused(logger, row.Id, recording.Id, row.EgressId!);

            row.MarkStopRequested(now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RecordingWebhookOutcome.Handled;
    }

    /// <summary>
    /// Bo'lak hozir yozilayotgan va uni to'xtatish MA'NOLI: Egress
    /// so'rovni qabul qilgan (<c>EgressId</c> bor), yakunlanmagan va
    /// to'xtatish so'rovi hali yuborilmagan.
    /// </summary>
    private static bool IsOpen(RecordingTrack track) =>
        track.Status is RecordingStatus.Starting or RecordingStatus.Active
        && !string.IsNullOrWhiteSpace(track.EgressId)
        && track.StopRequestedAt is null;

    // ═════════════════════════════════════════════════════════ yordamchilar

    /// <summary>
    /// Xona nomidan JONLI trek-quvur yozuvini topadi.
    ///
    /// ★ BITTA SO'ROV: dars (xost uchun) va mavjud bo'laklar birga
    /// yuklanadi. Ularsiz har hodisa uchun uch marta bazaga borilardi va
    /// bitta dars bir necha yuz hodisa yuboradi.
    ///
    /// ⚠️ <c>Status &lt; Completed</c> filtri
    /// <c>UX_SessionRecordings_SessionId_Pipeline_Active</c> indeksining
    /// filtri bilan AYNI — ya'ni javob ko'pi bilan bitta qator bo'lishi
    /// BAZA KAFOLATI, "amalda shunday" emas (§2.5).
    ///
    /// ★ SOYA (A/B) REJIMIDA bitta darsda ikkita yozuv qatori bo'ladi;
    /// bu so'rov ulardan AYNAN yangi quvurnikini oladi va eski qator
    /// o'z yo'lida, tegilmagan holda qoladi.
    /// </summary>
    private async Task<SessionRecording?> ResolveAsync(string? roomName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roomName))
            return null;

        return await db.SessionRecordings
            .AsTracking()
            .Include(r => r.Session)
            .Include(r => r.Tracks)
            .FirstOrDefaultAsync(
                r => r.Session!.RoomName == roomName
                  && r.Pipeline == RecordingPipeline.TrackComposition
                  && r.Status != RecordingStatus.Completed
                  && r.Status != RecordingStatus.Failed,
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Trekni XOST e'lon qildimi.
    ///
    /// LiveKit <c>identity</c> — <c>User.Id</c> ning invariant satri
    /// (<c>LiveSessionService.CreateJoinTokenAsync</c>). Xost belgilanmagan
    /// dars (<c>HostId is null</c>) yozilmaydi: kimning tasvirini
    /// yozayotganimizni bilmasak, yozmaganimiz ma'qul.
    /// </summary>
    private static bool IsHost(LiveSession? session, string? identity) =>
        session?.HostId is { } hostId
        && !string.IsNullOrWhiteSpace(identity)
        && string.Equals(
            identity.Trim(),
            hostId.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>
    /// LiveKit manbasini bo'lak turiga xaritalaydi (§2.3).
    ///
    /// 🔴 <c>RoomComposite</c> REJIMIDA MIKROFON VA EKRAN OVOZI QATOR
    /// YARATMAYDI. Ular xona aralashmasida ALLAQACHON bor va ikkinchi
    /// marta yozilsa, tungi yig'ishda ustozning ovozi ikki marta, biroz
    /// siljigan holda eshitilardi — bu "aks-sado" emas, mikrofon
    /// buzilganday tuyuladigan taroqli filtrlash (§2.3).
    ///
    /// Noma'lum yoki kelajakdagi manbalar — <c>null</c>, ya'ni qator YO'Q.
    /// </summary>
    private static RecordingTrackKind? MapKind(string? source, bool roomAudioMode) =>
        source?.Trim().ToUpperInvariant() switch
        {
            SourceCamera => RecordingTrackKind.CameraVideo,
            SourceScreenShare => RecordingTrackKind.ScreenVideo,
            SourceMicrophone => roomAudioMode ? null : RecordingTrackKind.MicAudio,
            SourceScreenShareAudio => roomAudioMode ? null : RecordingTrackKind.ScreenAudio,

            _ => null,
        };

    /// <summary>
    /// Xom faylning kengaytmasini <c>mime_type</c> dan BASHORAT qiladi
    /// (§2.8).
    ///
    /// ⚠️ BU TAXMIN VA UNGA ISHONILMAYDI: haqiqiy nom <c>egress_ended</c>
    /// javobida keladi va farq qilsa qatordagi kalit o'shanisi bilan
    /// almashtiriladi (<see cref="Complete"/>). Bashorat baribir kerak,
    /// chunki kalit Egress'ga <c>filepath</c> sifatida OLDINDAN beriladi.
    ///
    /// Zaxira qiymat TURGA qarab tanlanadi: video — <c>webm</c>, ovoz —
    /// <c>ogg</c>. Ya'ni <c>mime_type</c> umuman kelmasa ham kalit
    /// mazmunan to'g'ri qoladi.
    /// </summary>
    private static string ExtensionOf(string? mimeType, RecordingTrackKind kind)
    {
        // `;` dan keyin kodek parametrlari kelishi mumkin
        // (`video/vp8; profile=0`) — ular bizga kerak emas.
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
    /// Xona ovozi rejimi yoqilganmi (<c>recordings.audio_capture_mode</c>).
    ///
    /// 🔴 TEKSHIRUV "<c>TeacherTrack</c> EMASMI" SHAKLIDA, "<c>RoomComposite</c>
    /// MI" shaklida EMAS — va bu ataylab. Sozlamada tushunarsiz qiymat
    /// tursa (qo'lda tahrir, yarim ko'chirilgan migratsiya), teskari
    /// tekshiruv IKKALA ovoz manbasini ham o'chirib qo'yardi yoki, undan
    /// yomoni, ikkalasini ham yoqardi. Bu shaklda esa noma'lum qiymat
    /// STANDART rejimga tushadi va "hech qachon ikkalasi" qoidasi
    /// buzilmaydi (§2.3).
    ///
    /// ⚠️ SOZLAMA HALI REGISTRDA BO'LMASLIGI MUMKIN. Uni
    /// <c>SettingsRegistry</c> ga M7 qo'shadi (§5.7) va o'sha fayl
    /// ATAYLAB bitta modulga biriktirilgan — takroriy kalit ilovani
    /// ishga tushishda yiqitadi (§5.8). Registrda yo'q bo'lsa
    /// SPEC dagi standart (<c>RoomComposite</c>) ishlatiladi, ya'ni
    /// modul M7 dan OLDIN ham to'g'ri ishlaydi va M7 kelgach hech narsa
    /// o'zgartirilmasdan sozlamaga bo'ysunadi.
    /// </summary>
    private async Task<bool> RoomAudioModeAsync(CancellationToken ct)
    {
        if (!SettingsRegistry.TryGet(AudioCaptureModeKey, out var definition))
            return true;

        var resolved = await settings.ResolveAsync(definition, ct).ConfigureAwait(false);

        return !string.Equals(
            resolved.Value?.Trim(), TeacherTrackMode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>mime_type</c> ustuni 64 belgi
    /// (<c>RecordingTrackConfiguration.MimeTypeMaxLength</c>), LiveKit esa
    /// bu qiymatni KLIENTNING SDP'sidan oladi va u parametrlar bilan
    /// kelishi mumkin (<c>video/vp8; profile-id=0; …</c>).
    ///
    /// ⚠️ KESISH — ONGLI (<c>LiveKitWebhookLog</c> dagi AYNI mulohaza):
    /// uzun qiymat <c>SaveChanges</c> ni 22001 bilan yiqitardi, ya'ni
    /// BUTUN bo'lak yo'qolardi. Bu maydon esa faqat tashxis uchun —
    /// kengaytma undan ALLAQACHON hisoblangan
    /// (<see cref="ExtensionOf"/>), ya'ni kesilgan qiymat hech qanday
    /// qarorga ta'sir qilmaydi.
    ///
    /// ★ <c>TrackSid</c> VA <c>ParticipantIdentity</c> KESILMAYDI: ular
    /// IDENTIFIKATOR. Kesilgan sid boshqa egress'ni to'xtatishga yoki
    /// noto'g'ri kalitga yozishga olib kelardi — u yerda ochiq xato
    /// (va tiklash vazifasi) ancha xavfsiz.
    /// </summary>
    private static string? TrimMime(string? mimeType)
    {
        var mime = mimeType?.Trim();

        return mime is { Length: > MimeTypeMaxLength } ? mime[..MimeTypeMaxLength] : mime;
    }

    private static bool IsEgressEvent(string eventName) =>
        eventName is EgressStartedEvent or EgressUpdatedEvent or EgressEndedEvent;

    // ---------------------------------------------------------------- doimiylar

    /// <summary>
    /// Ovoz manbasi sozlamasining kaliti.
    ///
    /// ⚠️ SATR SIFATIDA, <c>SettingsRegistry.Keys</c> orqali EMAS: o'sha
    /// konstanta M7 bilan birga keladi (§5.7). M7 qo'shilgach bu qatorni
    /// <c>SettingsRegistry.Keys.RecordingsAudioCaptureMode</c> ga
    /// almashtirish kifoya — mantiq o'zgarmaydi.
    /// </summary>
    private const string AudioCaptureModeKey = "recordings.audio_capture_mode";

    /// <summary>Zaxira rejim: ustoz mikrofoni + ekran ovozi, o'quvchilarsiz (§3.4b).</summary>
    private const string TeacherTrackMode = "TeacherTrack";

    /// <summary>
    /// Mikser fayli AYNAN shu turda so'raladi (§3.4b), shuning uchun
    /// qatorda ham u bashorat emas, fakt.
    /// </summary>
    private const string RoomAudioMimeType = "audio/opus";

    private const string OggExtension = "ogg";
    private const string WebmExtension = "webm";

    /// <summary>
    /// <c>RecordingTrackConfiguration.MimeTypeMaxLength</c> bilan AYNI
    /// qiymat. Konfiguratsiya Infrastructure'da va Application uni
    /// ko'rmaydi, shuning uchun bu yerda takrorlanadi — sabab
    /// <see cref="TrimMime"/> izohida.
    /// </summary>
    private const int MimeTypeMaxLength = 64;

    // LiveKit hodisa nomlari.
    private const string RoomStarted = "room_started";
    private const string RoomFinished = "room_finished";
    private const string TrackPublished = "track_published";
    private const string TrackUnpublished = "track_unpublished";
    private const string ParticipantLeft = "participant_left";
    private const string EgressStartedEvent = "egress_started";
    private const string EgressUpdatedEvent = "egress_updated";
    private const string EgressEndedEvent = "egress_ended";

    // LiveKit `TrackSource` enum nomlari (protojson ularni SATR yuboradi).
    private const string SourceCamera = "CAMERA";
    private const string SourceScreenShare = "SCREEN_SHARE";
    private const string SourceMicrophone = "MICROPHONE";
    private const string SourceScreenShareAudio = "SCREEN_SHARE_AUDIO";

    // LiveKit `EgressStatus` enum nomlari.
    private const string EgressStarting = "EGRESS_STARTING";
    private const string EgressActive = "EGRESS_ACTIVE";
    private const string EgressEnding = "EGRESS_ENDING";
    private const string EgressComplete = "EGRESS_COMPLETE";
    private const string EgressFailed = "EGRESS_FAILED";
    private const string EgressAborted = "EGRESS_ABORTED";
    private const string EgressLimitReached = "EGRESS_LIMIT_REACHED";
}
