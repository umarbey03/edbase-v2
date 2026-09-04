using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TREK QUVURI — WEBHOOK ORQALI BO'LAK TOPISH (SPEC-RECORDING-V2 §3.3)
/// ════════════════════════════════════════════════════════════════════════
///
/// Yangi yozuv quvurida darsning tasviri BITTA fayl EMAS: kamera, ekran
/// ulashish va ustozning har uzilib-ulanishi ALOHIDA bo'lak. Ularni
/// topishning YAGONA real vaqtli manbai — webhook. Shuning uchun bu
/// fayl darsning HAQIQIY ketma-ketligini boshdan oxirigacha o'ynatadi va
/// natijadagi qatorlarni tekshiradi.
///
/// ── QO'RIQLANADIGAN UCHTA XATO ──────────────────────────────────────────
///
/// 🔴 1) IKKI MIKSER. Xona ovozi qatori darsga BITTA bo'lishi shart. Ikki
///       marta yoqilsa tungi yig'ishda har ovoz IKKI marta eshitiladi va
///       buni faylni tinglamasdan bilib bo'lmaydi.
///
/// 🔴 2) YO'QOLGAN BO'LAK. Dars o'rtasida yoqilgan ekran ulashish yoki
///       ustozning qayta ulanishi YANGI trek — u topilmasa darsning
///       o'sha qismi tasvirsiz qoladi.
///
/// 🔴 3) ESKI QUVURNING BUZILISHI. Controller endi ikki ishlovchini
///       ketma-ket chaqiradi. Trek ishlovchisi begona hodisaga tegib
///       qo'ysa (masalan takror jurnalini band qilsa), eski quvurning
///       <c>egress_ended</c> hodisasi yo'qolardi va dars yozuvi abadiy
///       "Active" bo'lib qolardi — bu SPEC §5.9 ta'qiqlagan yagona
///       narsa.
/// </summary>
public sealed class TrackWebhookTests(RecordingFactory factory)
    : IClassFixture<RecordingFactory>
{
    // ═══════════════════════════════════════════════════ 1) darsning to'liq oqimi

    /// <summary>
    /// 🔴 BU TO'PLAMNING ASOSIY TESTI: PRODUKSIYA AYNAN SHU KETMA-KETLIKNI
    /// yuboradi.
    ///
    /// <code>
    ///   dars boshlandi          -> room_started
    ///   ustoz kamera + mikrofon -> track_published × 2
    ///   mikser ishga tushdi     -> egress_started
    ///   o'rtada EKRAN ulashildi -> track_published
    ///   ekran o'chirildi        -> track_unpublished
    ///   ustoz uzildi            -> participant_left
    ///   ustoz qaytdi            -> track_published (YANGI sid)
    ///   dars tugadi             -> room_finished
    /// </code>
    ///
    /// Kutilgan natija: 4 ta qator — 1 ta xona ovozi va 3 ta video
    /// (kamera, ekran, qayta ulangan kamera). Mikrofon qator YARATMAYDI:
    /// u xona aralashmasida allaqachon bor (§2.3).
    /// </summary>
    [Fact]
    public async Task Lesson_FromStartToEnd_ProducesTheExpectedTrackRows()
    {
        var lesson = await NewLessonAsync();

        var t0 = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

        // ── dars boshlandi: xona ovozi mikseri ────────────────────────
        (await AckAsync(TrackEvent("room_started", lesson.RoomName))).Should().Be("Started");

        var mixer = await SingleTrackAsync(lesson, RecordingTrackKind.RoomAudio);

        mixer.TrackSid.Should().Be(RecordingTrack.RoomAudioSid);
        mixer.ParticipantIdentity.Should().BeNull("butun xonaning aralashmasi HECH KIMGA tegishli emas");
        mixer.ObjectKey.Should().Be($"raw/{lesson.SessionId}/{lesson.RecordingId}/ROOM.ogg");
        mixer.Status.Should().Be(RecordingStatus.Starting);

        factory.Egress.StartedRoomAudio
            .Count(r => r.RoomName == lesson.RoomName)
            .Should().Be(1, "darsga BITTA mikser");

        await ActivateAsync(mixer.EgressId!, t0);

        // ── ustoz kamerani va mikrofonni e'lon qildi ──────────────────
        (await AckAsync(Published(lesson, "TR_cam1", "CAMERA", "video/vp8"))).Should().Be("Started");

        // 🔴 MIKROFON — QATOR YARATMAYDI (`RoomComposite` rejimi).
        (await AckAsync(Published(lesson, "TR_mic1", "MICROPHONE", "audio/opus")))
            .Should().Be("Ignored");

        var camera1 = await SingleTrackAsync(lesson, RecordingTrackKind.CameraVideo);

        camera1.ObjectKey.Should().Be($"raw/{lesson.SessionId}/{lesson.RecordingId}/TR_cam1.webm");
        camera1.ParticipantIdentity.Should().Be(lesson.HostIdentity);

        await ActivateAsync(camera1.EgressId!, t0.AddSeconds(10));

        // ── dars o'rtasida EKRAN ulashildi ────────────────────────────
        (await AckAsync(Published(lesson, "TR_scr1", "SCREEN_SHARE", "video/vp8")))
            .Should().Be("Started");

        var screen = await SingleTrackAsync(lesson, RecordingTrackKind.ScreenVideo);

        await ActivateAsync(screen.EgressId!, t0.AddMinutes(15));

        // ── ekran o'chirildi ──────────────────────────────────────────
        (await AckAsync(TrackEvent(
            "track_unpublished", lesson.RoomName,
            identity: lesson.HostIdentity, trackSid: "TR_scr1", source: "SCREEN_SHARE")))
            .Should().Be("Handled");

        factory.Egress.Stopped.Should().Contain(screen.EgressId!);

        await CompleteAsync(screen.EgressId!, screen.ObjectKey, 11_000, t0.AddMinutes(15), t0.AddMinutes(25));

        // ── ustoz uzildi: VIDEO yopiladi, MIKSER ishlashda davom etadi ─
        (await AckAsync(TrackEvent(
            "participant_left", lesson.RoomName, identity: lesson.HostIdentity)))
            .Should().Be("Handled");

        factory.Egress.Stopped.Should().Contain(camera1.EgressId!);

        // 🔴 BUTUN OVOZ SXEMASINING MOHIYATI: ustoz uzilgani mikserni
        //    to'xtatmaydi — o'quvchilar shu paytda ham gapiradi.
        factory.Egress.Stopped.Should().NotContain(mixer.EgressId!);

        await CompleteAsync(
            camera1.EgressId!, camera1.ObjectKey, 22_000, t0.AddSeconds(10), t0.AddMinutes(30));

        // ── ustoz qaytdi: YANGI trek, YANGI bo'lak ────────────────────
        (await AckAsync(Published(lesson, "TR_cam2", "CAMERA", "video/vp8"))).Should().Be("Started");

        var camera2 = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_cam2");

        await ActivateAsync(camera2.EgressId!, t0.AddMinutes(31));

        // ── dars tugadi ──────────────────────────────────────────────
        (await AckAsync(TrackEvent("room_finished", lesson.RoomName))).Should().Be("Handled");

        factory.Egress.Stopped.Should().Contain(mixer.EgressId!, "xona yopilgach mikser ham to'xtaydi");
        factory.Egress.Stopped.Should().Contain(camera2.EgressId!);

        await CompleteAsync(
            camera2.EgressId!, camera2.ObjectKey, 33_000, t0.AddMinutes(31), t0.AddMinutes(60));
        await CompleteAsync(mixer.EgressId!, mixer.ObjectKey, 5_000, t0, t0.AddMinutes(60));

        // ── natija ────────────────────────────────────────────────────
        var tracks = await TracksAsync(lesson);

        tracks.Should().HaveCount(4, "1 ta xona ovozi + 3 ta video bo'lak");

        tracks.Count(t => t.Kind == RecordingTrackKind.RoomAudio)
            .Should().Be(1, "🔴 darsga BITTA ovoz fayli");

        tracks.Select(t => t.Kind).Should().BeEquivalentTo(new[]
        {
            RecordingTrackKind.RoomAudio,
            RecordingTrackKind.CameraVideo,
            RecordingTrackKind.ScreenVideo,
            RecordingTrackKind.CameraVideo,
        });

        tracks.Should().OnlyContain(
            t => t.Status == RecordingStatus.Completed, "hamma bo'lak YAKUNIY holatda");

        tracks.Should().OnlyContain(t => t.SizeBytes > 0);

        tracks.Should().OnlyContain(
            t => t.DurationSeconds > 0, "tungi yig'ish uzunlikka qarab joylashtiradi");

        // Vaqt o'qi: mikser butun darsni qoplaydi, video bo'laklar esa
        // o'z oraliqlarida turadi — tungi yig'ish AYNAN shularga qarab
        // joylashtiradi (§4.5).
        AssertSpan(tracks, RecordingTrack.RoomAudioSid, t0, t0.AddMinutes(60));
        AssertSpan(tracks, "TR_cam1", t0.AddSeconds(10), t0.AddMinutes(30));
        AssertSpan(tracks, "TR_scr1", t0.AddMinutes(15), t0.AddMinutes(25));
        AssertSpan(tracks, "TR_cam2", t0.AddMinutes(31), t0.AddMinutes(60));
    }

    // ═══════════════════════════════════════════════════ 2) mikser bitta bo'lishi

    /// <summary>
    /// 🔴 <c>room_started</c> VA undan keyingi <c>track_published</c> —
    /// IKKALASI ham mikser qatorini "kafolatlaydi", lekin qator BITTA
    /// bo'lib qoladi. Bu ikkilanish ATAYLAB (§3.3): <c>room_started</c>
    /// yo'qolsa mikser millisekundlar ichida, tiklash vazifasini
    /// kutmasdan yoqiladi.
    /// </summary>
    [Fact]
    public async Task RoomStartedThenTrackPublished_CreatesExactlyOneRoomAudioRow()
    {
        var lesson = await NewLessonAsync();

        (await AckAsync(TrackEvent("room_started", lesson.RoomName))).Should().Be("Started");

        // Takroriy `room_started` (LiveKit qayta yuborishi mumkin).
        (await AckAsync(TrackEvent("room_started", lesson.RoomName))).Should().Be("Duplicate");

        (await AckAsync(Published(lesson, "TR_c", "CAMERA", "video/vp8"))).Should().Be("Started");

        (await TracksAsync(lesson))
            .Count(t => t.Kind == RecordingTrackKind.RoomAudio)
            .Should().Be(1);

        factory.Egress.StartedRoomAudio
            .Count(r => r.RoomName == lesson.RoomName)
            .Should().Be(1, "🔴 ikkinchi mikser = darsda ikki karra ovoz");
    }

    /// <summary>
    /// <c>room_started</c> umuman kelmasa ham birinchi
    /// <c>track_published</c> mikserni yoqadi.
    /// </summary>
    [Fact]
    public async Task TrackPublished_WithoutRoomStarted_StillStartsTheMixer()
    {
        var lesson = await NewLessonAsync();

        (await AckAsync(Published(lesson, "TR_only", "CAMERA", "video/vp8"))).Should().Be("Started");

        var tracks = await TracksAsync(lesson);

        tracks.Should().HaveCount(2);
        tracks.Should().ContainSingle(t => t.Kind == RecordingTrackKind.RoomAudio);
        tracks.Should().ContainSingle(t => t.Kind == RecordingTrackKind.CameraVideo);
    }

    // ═══════════════════════════════════════════════════ 3) chetlanadigan hodisalar

    /// <summary>O'quvchining treki YOZILMAYDI — faqat xost (§3.1).</summary>
    [Fact]
    public async Task TrackPublished_ForAStudent_CreatesNothing()
    {
        var lesson = await NewLessonAsync();

        var body = TrackEvent(
            "track_published", lesson.RoomName,
            identity: lesson.StudentIdentity, trackSid: "TR_stud", source: "CAMERA",
            mimeType: "video/vp8");

        (await AckAsync(body)).Should().Be("Started", "mikser baribir kafolatlanadi");

        var tracks = await TracksAsync(lesson);

        tracks.Should().ContainSingle().Which.Kind.Should().Be(RecordingTrackKind.RoomAudio);
    }

    /// <summary>
    /// LiveKit hodisani QAYTA yuboradi. Ikkinchi marta yangi qator ham,
    /// yangi egress ham paydo bo'lmasligi kerak.
    /// </summary>
    [Fact]
    public async Task TrackPublished_Repeated_CreatesNothingTheSecondTime()
    {
        var lesson = await NewLessonAsync();

        var body = Published(lesson, "TR_dup", "CAMERA", "video/vp8", eventId: "EV_track_dup");

        (await AckAsync(body)).Should().Be("Started");
        (await AckAsync(body)).Should().Be("Duplicate");

        // Boshqa hodisa Id'si, AYNI trek — u ham qator yaratmaydi
        // (himoya faqat jurnalda emas, `(RecordingId, TrackSid)` da ham).
        (await AckAsync(Published(lesson, "TR_dup", "CAMERA", "video/vp8"))).Should().Be("Duplicate");

        (await TracksAsync(lesson))
            .Count(t => t.TrackSid == "TR_dup")
            .Should().Be(1);

        factory.Egress.StartedTracks
            .Count(r => r.TrackId == "TR_dup")
            .Should().Be(1);
    }

    /// <summary>
    /// Noma'lum xona — bizga aloqasi yo'q (bitta LiveKit'ni dev va
    /// staging baham ko'rishi mumkin). Javob 200, hech qanday qator yo'q.
    /// </summary>
    [Fact]
    public async Task Webhook_ForAnUnknownRoom_IsIgnored()
    {
        var lesson = await NewLessonAsync();

        var body = TrackEvent(
            "track_published", "bunday-xona-yoq",
            identity: lesson.HostIdentity, trackSid: "TR_x", source: "CAMERA");

        (await AckAsync(body)).Should().Be("Ignored");

        (await TracksAsync(lesson)).Should().BeEmpty();
    }

    /// <summary>
    /// Xaritalanmaydigan manba (<c>UNKNOWN</c> yoki kelajakdagi qiymat) —
    /// qator YARATILMAYDI (§2.3).
    /// </summary>
    [Fact]
    public async Task TrackPublished_WithAnUnknownSource_CreatesNoVideoRow()
    {
        var lesson = await NewLessonAsync();

        await AckAsync(TrackEvent("room_started", lesson.RoomName));

        (await AckAsync(Published(lesson, "TR_?", "SOMETHING_NEW", "video/av1")))
            .Should().Be("Ignored");

        (await TracksAsync(lesson)).Should().ContainSingle()
            .Which.Kind.Should().Be(RecordingTrackKind.RoomAudio);
    }

    // ═══════════════════════════════════════════════════ 4) to'xtatish

    /// <summary>
    /// <c>track_unpublished</c> to'xtatishni AYNAN BIR MARTA so'raydi:
    /// takroriy <c>StopEgress</c> LiveKit'da xato beradi va log'ni
    /// bekorga to'ldiradi.
    /// </summary>
    [Fact]
    public async Task TrackUnpublished_RequestsTheStopExactlyOnce()
    {
        var lesson = await NewLessonAsync();

        await AckAsync(Published(lesson, "TR_stop", "CAMERA", "video/vp8"));

        var camera = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_stop");

        await ActivateAsync(camera.EgressId!, DateTimeOffset.UtcNow.AddMinutes(-5));

        var unpublished = TrackEvent(
            "track_unpublished", lesson.RoomName,
            identity: lesson.HostIdentity, trackSid: "TR_stop", source: "CAMERA");

        (await AckAsync(unpublished)).Should().Be("Handled");

        // Yangi hodisa Id'si bilan TAKROR — `StopRequestedAt` allaqachon
        // qo'yilgan, ya'ni ikkinchi so'rov yuborilmaydi.
        (await AckAsync(TrackEvent(
            "track_unpublished", lesson.RoomName,
            identity: lesson.HostIdentity, trackSid: "TR_stop", source: "CAMERA")))
            .Should().Be("Ignored");

        factory.Egress.Stopped.Count(id => id == camera.EgressId).Should().Be(1);

        var reloaded = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_stop");

        reloaded.StopRequestedAt.Should().NotBeNull();
        reloaded.Status.Should().Be(RecordingStatus.Active, "to'xtatish so'rovi YAKUN emas");
    }

    // ═══════════════════════════════════════════════════ 5) egress hodisalari

    /// <summary>
    /// Oraliq holat (<c>EGRESS_ENDING</c>) qatorga TEGMAYDI, lekin hodisa
    /// BIZNIKI — eski ishlovchiga o'tkazilmaydi (aks holda u har bo'lak
    /// uchun "noma'lum egress" ogohlantirishini yozardi).
    /// </summary>
    [Fact]
    public async Task EgressUpdated_WithAnIntermediateStatus_IsHandledWithoutChangingTheRow()
    {
        var lesson = await NewLessonAsync();

        await AckAsync(Published(lesson, "TR_mid", "CAMERA", "video/vp8"));

        var camera = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_mid");

        await ActivateAsync(camera.EgressId!, DateTimeOffset.UtcNow.AddMinutes(-2));

        (await AckAsync(EgressEvent("egress_updated", camera.EgressId!, "EGRESS_ENDING")))
            .Should().Be("Handled");

        (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_mid")
            .Status.Should().Be(RecordingStatus.Active);
    }

    /// <summary>
    /// ⚠️ KENGAYTMA — BASHORAT VA UNGA ISHONILMAYDI (§2.8). LiveKit
    /// haqiqiy fayl nomini qaytaradi va u boshqa bo'lsa kalit O'SHANI
    /// bilan almashtiriladi: aks holda tungi yig'ish mavjud bo'lmagan
    /// obyektni qidirardi.
    /// </summary>
    [Fact]
    public async Task EgressEnded_WithADifferentFilename_OverwritesTheObjectKey()
    {
        var lesson = await NewLessonAsync();

        await AckAsync(Published(lesson, "TR_key", "CAMERA", "video/vp8"));

        var camera = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_key");

        camera.ObjectKey.Should().EndWith(".webm", "bashorat: `video/vp8` -> `.webm`");

        var actual = $"raw/{lesson.SessionId}/{lesson.RecordingId}/TR_key.mp4";

        await CompleteAsync(
            camera.EgressId!, actual, 777,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);

        var reloaded = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_key");

        reloaded.ObjectKey.Should().Be(actual);
        reloaded.SizeBytes.Should().Be(777);
        reloaded.Status.Should().Be(RecordingStatus.Completed);
    }

    /// <summary>
    /// 🔴 KECH KELGAN XATO HODISASI TAYYOR BO'LAKNI BUZMAYDI — fayl
    /// allaqachon omborda va tungi yig'ish unga tayanadi
    /// (<c>RecordingTrack.MarkCompleted</c> izohi).
    /// </summary>
    [Fact]
    public async Task EgressEnded_LateFailureAfterCompletion_DoesNotResurrectTheRow()
    {
        var lesson = await NewLessonAsync();

        await AckAsync(Published(lesson, "TR_late", "CAMERA", "video/vp8"));

        var camera = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_late");

        await CompleteAsync(
            camera.EgressId!, camera.ObjectKey, 4242,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);

        (await AckAsync(EgressEvent(
            "egress_ended", camera.EgressId!, "EGRESS_FAILED", error: "kech kelgan xato")))
            .Should().Be("Failed");

        var reloaded = (await TracksAsync(lesson)).Single(t => t.TrackSid == "TR_late");

        reloaded.Status.Should().Be(RecordingStatus.Completed);
        reloaded.SizeBytes.Should().Be(4242);
    }

    // ═══════════════════════════════════════════════════ 6) eski quvur buzilmadi

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 ENG MUHIM REGRESSIYA QO'RIQCHISI (SPEC §5.9)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Controller endi AVVAL trek ishlovchisini chaqiradi. Bu test
    /// SOYA (A/B) rejimini takrorlaydi: bitta darsda IKKI yozuv qatori —
    /// eski <c>RoomComposite</c> va yangi <c>TrackComposition</c>.
    /// Eski quvurning <c>egress_ended</c> hodisasi baribir o'z qatorini
    /// topib, uni yakunlashi SHART.
    ///
    /// Bu buzilsa nosozlik jimgina bo'lardi: dars yozuvi abadiy "Active"
    /// bo'lib qolib, watchdog uni 10 daqiqadan keyin `Failed` qilardi.
    /// </summary>
    [Fact]
    public async Task OldPipelineEvent_IsStillHandledByTheOldHandler()
    {
        var lesson = await NewLessonAsync();

        var egressId = "EG_eski_" + Guid.NewGuid().ToString("N")[..12];

        var oldRecordingId = await factory.WithDbAsync(async db =>
        {
            var recording = new SessionRecording
            {
                SessionId = lesson.SessionId,
                Status = RecordingStatus.Active,
                EgressId = egressId,
                ObjectKey = $"recordings/eski/{Guid.NewGuid():N}.mp4",
                Pipeline = RecordingPipeline.RoomComposite,
            };

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();

            return recording.Id;
        });

        // Yangi quvur ham ishlab tursin — hodisalar aralashib ketmasin.
        await AckAsync(TrackEvent("room_started", lesson.RoomName));

        (await AckAsync(EgressEvent(
            "egress_ended", egressId, "EGRESS_COMPLETE",
            objectKey: "recordings/eski/tayyor.mp4", sizeBytes: 999)))
            .Should().Be("Completed");

        var oldRecording = await RecordingWorld.ReloadAsync(factory, oldRecordingId);

        oldRecording.Status.Should().Be(RecordingStatus.Completed);
        oldRecording.ObjectKey.Should().Be("recordings/eski/tayyor.mp4");
        oldRecording.SizeBytes.Should().Be(999);

        // Trek qatorlariga TEGILMAGAN.
        (await TracksAsync(lesson)).Should().OnlyContain(
            t => t.Status == RecordingStatus.Starting);
    }

    /// <summary>
    /// Bizda umuman yo'q egress — eski ishlovchining "Unknown" javobi
    /// SAQLANIB QOLADI (trek ishlovchisi uni o'zlashtirib olmaydi).
    /// </summary>
    [Fact]
    public async Task UnknownEgressEvent_StillReportsUnknown()
    {
        var body = EgressEvent(
            "egress_ended", "EG_yoq_" + Guid.NewGuid().ToString("N")[..8], "EGRESS_COMPLETE",
            objectKey: "recordings/x.mp4");

        (await AckAsync(body)).Should().Be("Unknown");
    }

    /// <summary>Bizga aloqasi yo'q hodisa jimgina chetlanadi.</summary>
    [Fact]
    public async Task UnrelatedEvent_IsIgnored()
    {
        var lesson = await NewLessonAsync();

        (await AckAsync(TrackEvent(
            "participant_joined", lesson.RoomName, identity: lesson.HostIdentity)))
            .Should().Be("Ignored");

        (await TracksAsync(lesson)).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════ yordamchilar

    /// <summary>Dars konteksti: xona, yozuv qatori va ishtirokchi identifikatorlari.</summary>
    private sealed record Lesson(
        long SessionId,
        long RecordingId,
        string RoomName,
        string HostIdentity,
        string StudentIdentity);

    /// <summary>
    /// Guruh + JONLI dars + <c>TrackComposition</c> yozuv qatori.
    ///
    /// ⚠️ Qator TO'G'RIDAN-TO'G'RI bazaga yoziladi, `AutoRecordingScheduler`
    /// orqali EMAS: uni quvurga sezgir qilish M7 ning ishi (§5.9-2) va u
    /// hali yozilmagan. Bu testlar tekshiradigan narsa — WEBHOOK, qator
    /// esa uning kirish ma'lumoti.
    /// </summary>
    private async Task<Lesson> NewLessonAsync()
    {
        var world = await WorldBuilder.CreateAsync(factory, "trk");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, world.GroupId, hostId: world.Teacher.Id);

        return await factory.WithDbAsync(async db =>
        {
            var session = await db.LiveSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);

            var recording = new SessionRecording
            {
                SessionId = sessionId,
                ObjectKey = $"recordings/test/{Guid.NewGuid():N}.mp4",
                Pipeline = RecordingPipeline.TrackComposition,
                CompositionStatus = RecordingCompositionStatus.Collecting,
            };

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();

            return new Lesson(
                sessionId,
                recording.Id,
                session.RoomName,
                world.Teacher.Id.ToString(CultureInfo.InvariantCulture),
                world.Student.Id.ToString(CultureInfo.InvariantCulture));
        });
    }

    private Task<List<RecordingTrack>> TracksAsync(Lesson lesson) =>
        factory.WithDbAsync(db => db.RecordingTracks
            .AsNoTracking()
            .Where(t => t.RecordingId == lesson.RecordingId)
            .OrderBy(t => t.Id)
            .ToListAsync());

    private async Task<RecordingTrack> SingleTrackAsync(Lesson lesson, RecordingTrackKind kind) =>
        (await TracksAsync(lesson)).Single(t => t.Kind == kind);

    /// <summary>
    /// Bo'lakning vaqt oralig'i — tungi yig'ish uchun BU IKKI QIYMAT
    /// hal qiluvchi: video bo'lak vaqt o'qiga aynan shular bo'yicha
    /// qo'yiladi (§4.5). Ular noto'g'ri bo'lsa tasvir ovozdan siljiydi.
    /// </summary>
    private static void AssertSpan(
        IEnumerable<RecordingTrack> tracks,
        string trackSid,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        var track = tracks.Single(t => t.TrackSid == trackSid);

        track.StartedAt.Should().Be(startedAt, "bo'lak {0} boshlanishi", trackSid);
        track.EndedAt.Should().Be(endedAt, "bo'lak {0} tugashi", trackSid);
    }

    /// <summary>Imzolangan so'rov yuboradi va javobdagi natija nomini qaytaradi.</summary>
    private async Task<string> AckAsync(string body)
    {
        var keys = RecordingWorld.LiveKitOf(factory);
        var token = RecordingWorld.SignToken(keys, body);

        using var client = factory.CreateClient();
        using var request = RecordingWorld.Request(body, token);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<WebhookAckDto>())!.Outcome;
    }

    private Task<string> ActivateAsync(string egressId, DateTimeOffset startedAt) =>
        AckAsync(EgressEvent("egress_started", egressId, "EGRESS_ACTIVE", startedAt: startedAt));

    /// <summary>
    /// <c>egress_ended</c> — HAQIQIY LiveKit tanasida <c>started_at</c> ham,
    /// <c>ended_at</c> ham bo'ladi va uzunlik AYNAN shulardan hisoblanadi
    /// (fayl <c>duration</c> maydoni bo'lmasa).
    /// </summary>
    private Task<string> CompleteAsync(
        string egressId,
        string objectKey,
        long sizeBytes,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt) =>
        AckAsync(EgressEvent(
            "egress_ended", egressId, "EGRESS_COMPLETE",
            objectKey: objectKey, sizeBytes: sizeBytes,
            startedAt: startedAt, endedAt: endedAt));

    private static string Published(
        Lesson lesson, string trackSid, string source, string? mimeType = null, string? eventId = null) =>
        TrackEvent(
            "track_published", lesson.RoomName,
            eventId: eventId,
            identity: lesson.HostIdentity,
            trackSid: trackSid,
            source: source,
            mimeType: mimeType);

    /// <summary>
    /// Trek/xona hodisasining tanasi — LiveKit protojson shakli.
    ///
    /// ★ <c>JsonObject</c> bilan quriladi, satr shabloni bilan emas:
    /// tana ichma-ich obyektlardan iborat va qo'lda qavs sanash aynan
    /// shu turdagi testlarni "sababsiz" yiqitadigan xato manbai.
    /// </summary>
    private static string TrackEvent(
        string eventName,
        string roomName,
        string? eventId = null,
        string? identity = null,
        string? trackSid = null,
        string? source = null,
        string? mimeType = null)
    {
        var root = new JsonObject
        {
            ["event"] = eventName,
            ["id"] = eventId ?? NewEventId(),
            ["room"] = new JsonObject { ["sid"] = "RM_test", ["name"] = roomName },
        };

        if (identity is not null)
            root["participant"] = new JsonObject { ["sid"] = "PA_test", ["identity"] = identity };

        if (trackSid is not null)
        {
            root["track"] = new JsonObject
            {
                ["sid"] = trackSid,
                ["source"] = source,
                ["mimeType"] = mimeType,
            };
        }

        return root.ToJsonString();
    }

    /// <summary>
    /// Egress hodisasining tanasi.
    ///
    /// ⚠️ <c>RecordingWorld.EgressEvent</c> QAYTA ISHLATILMADI: unda
    /// <c>started_at</c> / <c>ended_at</c> yo'q, bu testlar esa AYNAN
    /// bo'laklarning vaqt oralig'ini tekshiradi (tungi yig'ish ularga
    /// qarab joylashtiradi). Mavjud yordamchini kengaytirish esa eski
    /// quvur testlariga ham tegishli bo'lardi.
    ///
    /// ★ <c>int64</c> maydonlar SATR bo'lib yoziladi — protobuf JSON
    /// xaritalash qoidasi AYNAN shunday va tahlilchi ham shunga
    /// tayyorlangan.
    /// </summary>
    private static string EgressEvent(
        string eventName,
        string egressId,
        string status,
        string? eventId = null,
        string? objectKey = null,
        long? sizeBytes = null,
        string? error = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null)
    {
        var info = new JsonObject
        {
            ["egress_id"] = egressId,
            ["status"] = status,
        };

        if (error is not null)
            info["error"] = error;

        if (startedAt is { } start)
            info["started_at"] = Nanos(start);

        if (endedAt is { } end)
            info["ended_at"] = Nanos(end);

        if (objectKey is not null)
        {
            info["file_results"] = new JsonArray(
                new JsonObject
                {
                    ["filename"] = objectKey,
                    ["size"] = (sizeBytes ?? 0).ToString(CultureInfo.InvariantCulture),
                });
        }

        return new JsonObject
        {
            ["event"] = eventName,
            ["id"] = eventId ?? NewEventId(),
            ["egress_info"] = info,
        }.ToJsonString();
    }

    /// <summary>UNIX NANOSEKUND, SATR ko'rinishida (protobuf JSON qoidasi).</summary>
    private static string Nanos(DateTimeOffset value) =>
        (value.ToUnixTimeMilliseconds() * 1_000_000L).ToString(CultureInfo.InvariantCulture);

    private static string NewEventId() => "EV_" + Guid.NewGuid().ToString("N")[..16];

    private sealed record WebhookAckDto(bool Ok, string Outcome);
}
