using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TREK MOSLASHTIRUVCHISI (SPEC-RECORDING-V2 §4.1)
/// ════════════════════════════════════════════════════════════════════════
///
/// Bu vazifa — YO'QOLGAN WEBHOOK'LARNING zaxira yo'li. API konteyneri
/// dars o'rtasida qayta ishga tushsa, o'sha oraliqdagi
/// <c>room_started</c> va <c>track_published</c> hodisalari BUTUNLAY
/// yo'qoladi va ularni hech kim qayta yubormaydi.
///
/// ── QO'RIQLANADIGAN UCHTA XATO ──────────────────────────────────────────
///
/// 🔴 1) OVOZSIZ DARS. Xona ovozi qatori yaratilmay qolsa, dars butunlay
///       jim yoziladi — va buni faqat ertasi kuni, faylni ochganda
///       bilishadi.
///
/// 🔴 2) IKKI MIKSER. LiveKit javob bermagan daqiqada "mikser o'lgan"
///       deb xulosa chiqarilsa, TIRIK mikser ustiga ikkinchisi yoqilardi:
///       bitta darsda ikki ovoz fayli va montajda har ovoz IKKI MARTA.
///
/// 🔴 3) NAVBATDA QOTIB QOLGAN YOZUV. Bitta yopilmagan bo'lak butun
///       yozuvni tungi navbatdan ushlab turardi va u HECH QACHON
///       yig'ilmasdi.
/// </summary>
public sealed class TrackReconcileTests(CompositionFactory factory)
    : IClassFixture<CompositionFactory>
{
    // ═══════════════════════════════════════════════════ 1) mikser bormi

    /// <summary>
    /// 🔴 BU VAZIFANING ENG QIMMATLI QADAMI: xona ovozi qatori yo'q
    /// bo'lsa mikser YOQILADI.
    ///
    /// Bu holat <c>room_started</c> ham, birinchi <c>track_published</c>
    /// ham yo'qolganda yuz beradi — ya'ni dars OVOZSIZ yozilmoqda.
    /// </summary>
    [Fact]
    public async Task LiveLessonWithoutAMixer_GetsOneStarted()
    {
        var lesson = await NewLessonAsync();

        var result = await factory.RunReconcileAsync();

        result.Processed.Should().Be(1);

        var tracks = await CompositionWorld.TracksAsync(factory, lesson.RecordingId);

        tracks.Should().ContainSingle();
        tracks[0].Kind.Should().Be(RecordingTrackKind.RoomAudio);
        tracks[0].TrackSid.Should().Be(RecordingTrack.RoomAudioSid);
        tracks[0].ParticipantIdentity.Should().BeNull("aralashma HECH KIMGA tegishli emas");
        tracks[0].Status.Should().Be(RecordingStatus.Starting);
        tracks[0].ObjectKey.Should().EndWith("ROOM.ogg");

        factory.Egress.StartedRoomAudio.Should().ContainSingle();
        factory.Egress.Started.Should().BeEmpty("eski, Chrome'li yo'l ISHGA TUSHMAYDI");
    }

    /// <summary>
    /// 🔴 IKKINCHI MIKSER YOQILMAYDI. Vazifa har daqiqada yuradi; qator
    /// borligini tekshirmasa, har yurishda yangi mikser qo'shilardi.
    /// </summary>
    [Fact]
    public async Task Mixer_IsNotStartedTwice()
    {
        await NewLessonAsync();

        await factory.RunReconcileAsync();
        await factory.RunReconcileAsync();
        await factory.RunReconcileAsync();

        factory.Egress.StartedRoomAudio.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════ 2) LiveKit holati

    /// <summary>
    /// Mikser LiveKit'da yo'q — u o'lgan. Qator yopiladi va O'RNIGA
    /// yangisi (<c>ROOM2</c>) yoqiladi; oradagi bo'shliq tungi montajda
    /// haqiqiy jimlik bo'ladi (§4.5-4).
    /// </summary>
    [Fact]
    public async Task DeadMixer_IsReplacedWithTheNextSentinel()
    {
        var lesson = await NewLessonAsync();

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.RoomAudio,
            status: RecordingStatus.Active,
            trackSid: RecordingTrack.RoomAudioSid,
            egressId: "EG_dead_mixer",
            startedAt: factory.Clock.GetUtcNow().AddMinutes(-20));

        // LiveKit javob berdi va xonada FAOL egress YO'Q.
        factory.Rooms.Egresses = LiveKitEgressListResult.Ok([]);

        await factory.RunReconcileAsync();

        var tracks = await CompositionWorld.TracksAsync(factory, lesson.RecordingId);

        tracks.Should().HaveCount(2);

        tracks[0].Status.Should().Be(RecordingStatus.Failed);
        tracks[0].Error.Should().Be("Dars ovozi mikseri to'xtab qoldi.");

        tracks[1].TrackSid.Should().Be("ROOM2");
        tracks[1].Kind.Should().Be(RecordingTrackKind.RoomAudio);
        tracks[1].Status.Should().Be(RecordingStatus.Starting);

        factory.Egress.StartedRoomAudio.Should().ContainSingle()
            .Which.ObjectKey.Should().EndWith("ROOM2.ogg");
    }

    /// <summary>
    /// 🔴 BU TO'PLAMDAGI ENG MUHIM TEST: LIVEKIT JAVOB BERMASA HECH
    /// QANDAY XULOSA CHIQARILMAYDI.
    ///
    /// "Xonada faol egress yo'q" va "LiveKit'ga yetib bo'lmadi" — IKKI
    /// BOSHQA javob. Ikkalasini bo'sh ro'yxat deb qabul qilsak, tarmoq
    /// uzilgan daqiqada TIRIK mikser ustiga ikkinchisi yoqilardi va
    /// darsning har bir ovozi montajda IKKI MARTA eshitilardi.
    /// </summary>
    [Fact]
    public async Task WhenLiveKitIsUnreachable_NoSecondMixerIsEverStarted()
    {
        var lesson = await NewLessonAsync();

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.RoomAudio,
            status: RecordingStatus.Active,
            trackSid: RecordingTrack.RoomAudioSid,
            egressId: "EG_alive_mixer",
            startedAt: factory.Clock.GetUtcNow().AddMinutes(-20));

        // Tarmoq uzildi: ro'yxat BO'SH emas — SO'ROV YIQILDI.
        factory.Rooms.Egresses = LiveKitEgressListResult.Fail("tarmoq uzildi");
        factory.Rooms.Participants = LiveKitTrackListResult.Fail("tarmoq uzildi");

        await factory.RunReconcileAsync();

        var tracks = await CompositionWorld.TracksAsync(factory, lesson.RecordingId);

        tracks.Should().ContainSingle("tirik mikser ustiga ikkinchisi qo'yilmaydi");
        tracks[0].Status.Should().Be(RecordingStatus.Active);

        factory.Egress.StartedRoomAudio.Should().BeEmpty();
    }

    /// <summary>
    /// LiveKit bizga NOMA'LUM host trekini ko'rsatdi — webhook yo'qolgan.
    /// Qator yaratiladi va yozuv boshlanadi.
    /// </summary>
    [Fact]
    public async Task UnknownHostTrack_IsDiscoveredAndStarted()
    {
        var lesson = await NewLessonAsync();

        factory.Rooms.Participants = LiveKitTrackListResult.Ok(
        [
            new LiveKitPublishedTrackDto(lesson.HostIdentity, "TR_lost_camera", "CAMERA", "video/vp8"),
        ]);

        await factory.RunReconcileAsync();

        var tracks = await CompositionWorld.TracksAsync(factory, lesson.RecordingId);

        var camera = tracks.Single(t => t.Kind == RecordingTrackKind.CameraVideo);

        camera.TrackSid.Should().Be("TR_lost_camera");
        camera.Status.Should().Be(RecordingStatus.Starting);
        camera.ObjectKey.Should().EndWith("TR_lost_camera.webm");

        factory.Egress.StartedTracks.Should().ContainSingle()
            .Which.TrackId.Should().Be("TR_lost_camera");
    }

    /// <summary>
    /// O'QUVCHINING treki YOZILMAYDI. Yozuvga faqat XOST tushadi —
    /// o'quvchilar ovozi xona aralashmasida, tasviri esa umuman yo'q.
    /// </summary>
    [Fact]
    public async Task StudentTrack_IsIgnored()
    {
        var lesson = await NewLessonAsync();

        factory.Rooms.Participants = LiveKitTrackListResult.Ok(
        [
            new LiveKitPublishedTrackDto(lesson.StudentIdentity, "TR_student_cam", "CAMERA", "video/vp8"),
        ]);

        await factory.RunReconcileAsync();

        (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Should().NotContain(t => t.Kind == RecordingTrackKind.CameraVideo);

        factory.Egress.StartedTracks.Should().BeEmpty();
    }

    /// <summary>
    /// 🔴 STANDART REJIMDA MIKROFON QATOR YARATMAYDI. U xona
    /// aralashmasida ALLAQACHON bor va ikkinchi marta yozilsa ustozning
    /// ovozi ikki marta, biroz siljigan holda eshitilardi (§2.3).
    /// </summary>
    [Fact]
    public async Task HostMicrophone_CreatesNoRowInRoomAudioMode()
    {
        var lesson = await NewLessonAsync();

        factory.Rooms.Participants = LiveKitTrackListResult.Ok(
        [
            new LiveKitPublishedTrackDto(lesson.HostIdentity, "TR_mic", "MICROPHONE", "audio/opus"),
        ]);

        await factory.RunReconcileAsync();

        (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Should().OnlyContain(t => t.Kind == RecordingTrackKind.RoomAudio);
    }

    /// <summary>Allaqachon ma'lum trek IKKINCHI marta yaratilmaydi.</summary>
    [Fact]
    public async Task KnownTrack_IsNotDuplicated()
    {
        var lesson = await NewLessonAsync();

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.CameraVideo,
            status: RecordingStatus.Active,
            trackSid: "TR_known",
            egressId: "EG_known");

        factory.Rooms.Participants = LiveKitTrackListResult.Ok(
        [
            new LiveKitPublishedTrackDto(lesson.HostIdentity, "TR_known", "CAMERA", "video/vp8"),
        ]);

        await factory.RunReconcileAsync();

        (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Count(t => t.TrackSid == "TR_known").Should().Be(1);

        factory.Egress.StartedTracks.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════ 3) qayta urinish

    /// <summary>
    /// <c>Requested</c> da qotib qolgan bo'lak qayta uriniladi — LiveKit
    /// bir daqiqa oldin javob bermagan bo'lishi mumkin.
    /// </summary>
    [Fact]
    public async Task StuckTrack_IsRetried()
    {
        var lesson = await NewLessonAsync();

        var trackId = await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.CameraVideo,
            status: RecordingStatus.Requested,
            trackSid: "TR_stuck",
            attempts: 1,
            lastAttemptAt: factory.Clock.GetUtcNow().AddMinutes(-5));

        await factory.RunReconcileAsync();

        var row = (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Single(t => t.Id == trackId);

        row.Status.Should().Be(RecordingStatus.Starting);
        row.Attempts.Should().Be(2);

        factory.Egress.StartedTracks.Should().ContainSingle()
            .Which.TrackId.Should().Be("TR_stuck");
    }

    /// <summary>
    /// ★ URINISHLAR ORASIDA KUTILADI. Busiz LiveKit yiqilgan paytda
    /// vazifa har yurishda urinaverib, chegarani bir daqiqada tugatardi
    /// va bo'lak SABABSIZ <c>Failed</c> bo'lardi.
    /// </summary>
    [Fact]
    public async Task StuckTrack_IsNotRetriedTooSoon()
    {
        var lesson = await NewLessonAsync();

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.CameraVideo,
            status: RecordingStatus.Requested,
            trackSid: "TR_fresh",
            attempts: 1,
            lastAttemptAt: factory.Clock.GetUtcNow().AddSeconds(-5));

        await factory.RunReconcileAsync();

        factory.Egress.StartedTracks.Should().BeEmpty();
    }

    /// <summary>Urinishlar tugagan bo'lak YAKUNIY xato bo'ladi.</summary>
    [Fact]
    public async Task StuckTrack_OutOfAttempts_IsFailed()
    {
        var lesson = await NewLessonAsync();

        var trackId = await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.CameraVideo,
            status: RecordingStatus.Requested,
            trackSid: "TR_hopeless",
            attempts: 5,
            lastAttemptAt: factory.Clock.GetUtcNow().AddMinutes(-30));

        await factory.RunReconcileAsync();

        (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Single(t => t.Id == trackId)
            .Status.Should().Be(RecordingStatus.Failed);

        factory.Egress.StartedTracks.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════ 4) unutilgan xona

    /// <summary>
    /// To'rt soatdan ortiq ishlayotgan mikser to'xtatiladi — unutilgan
    /// xona egress resursini va omborni kunlab yeb turardi.
    /// </summary>
    [Fact]
    public async Task OverlongMixer_IsStopped()
    {
        var lesson = await NewLessonAsync();

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.RoomAudio,
            status: RecordingStatus.Active,
            trackSid: RecordingTrack.RoomAudioSid,
            egressId: "EG_forever",
            startedAt: factory.Clock.GetUtcNow().AddHours(-5));

        factory.Rooms.Egresses = LiveKitEgressListResult.Ok(
            [new LiveKitEgressInfoDto("EG_forever", "EGRESS_ACTIVE")]);

        await factory.RunReconcileAsync();

        factory.Egress.Stopped.Should().Contain("EG_forever");

        (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Single(t => t.EgressId == "EG_forever")
            .StopRequestedAt.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════ 5) darsdan keyin

    /// <summary>
    /// Dars tugadi. Ochiq bo'lak avval TO'XTATILADI, muhlatdan keyin esa
    /// OMBORDAN so'raladi — haqiqat manbai LiveKit hodisasi emas,
    /// omborning O'ZI.
    ///
    /// Fayl topilgach yozuv TUNGI NAVBATGA tushadi.
    /// </summary>
    [Fact]
    public async Task EndedLesson_FinalizesFromStorageThenQueuesTheRecording()
    {
        var lesson = await NewLessonAsync(SessionStatus.Ended);

        var key = $"raw/itest/{Guid.NewGuid():N}.ogg";

        await PutAsync(key, 4096);

        var trackId = await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.RoomAudio,
            status: RecordingStatus.Active,
            trackSid: RecordingTrack.RoomAudioSid,
            egressId: "EG_ended",
            objectKey: key,
            startedAt: factory.Clock.GetUtcNow().AddHours(-2));

        // 1-yurish: to'xtatish so'raladi (hodisa kelmagan bo'lishi mumkin).
        await factory.RunReconcileAsync();

        factory.Egress.Stopped.Should().Contain("EG_ended");

        (await CompositionWorld.ReloadAsync(factory, lesson.RecordingId))
            .CompositionStatus.Should().Be(
                RecordingCompositionStatus.Collecting, "muhlat hali o'tmagan");

        // Muhlat o'tdi.
        factory.Clock.Set(factory.Clock.GetUtcNow().AddMinutes(15));

        await factory.RunReconcileAsync();

        var track = (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Single(t => t.Id == trackId);

        track.Status.Should().Be(RecordingStatus.Completed);
        track.SizeBytes.Should().Be(4096);

        (await CompositionWorld.ReloadAsync(factory, lesson.RecordingId))
            .CompositionStatus.Should().Be(RecordingCompositionStatus.Queued);
    }

    /// <summary>
    /// Fayl omborda YO'Q — bo'lak yiqiladi va yozuvda bironta ham tayyor
    /// bo'lak qolmagani uchun yozuvning O'ZI ham yiqiladi.
    /// </summary>
    [Fact]
    public async Task EndedLesson_WithoutAnyFile_FailsTheRecording()
    {
        var lesson = await NewLessonAsync(SessionStatus.Ended);

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.RoomAudio,
            status: RecordingStatus.Active,
            trackSid: RecordingTrack.RoomAudioSid,
            egressId: "EG_lost",
            objectKey: $"raw/itest/{Guid.NewGuid():N}.ogg",
            stopRequestedAt: factory.Clock.GetUtcNow().AddMinutes(-30),
            startedAt: factory.Clock.GetUtcNow().AddHours(-2));

        await factory.RunReconcileAsync();

        (await CompositionWorld.TracksAsync(factory, lesson.RecordingId))
            .Single().Error.Should().Be("Dars ovozi omborga tushmadi.");

        // ★ MUHLAT ATAYLAB KUTILADI. Trek yiqilgani "yozuvda hech narsa
        //   yo'q" degani EMAS: kechikkan `egress_ended` yoki kech kelgan
        //   `track_published` hali yo'lda bo'lishi mumkin. Shuning uchun
        //   vazifa yozuvni DARS TUGAGANIDAN `FinalizeGrace` o'tgachgina
        //   yopadi — birinchi yurish trekni yiqitadi, yopish esa keyingi
        //   yurishning ishi. Ishlab chiqarishda bu o'z-o'zidan bo'ladi
        //   (vazifa takrorlanadi), testda soatni qo'lda suramiz.
        //
        // ⚠️ NEGA 95 DAQIQA, 15 EMAS: `RecordingWorld.AddSessionAsync`
        //    darsni `hozir − 10 daqiqa` da boshlab, `ActualEnd` ni
        //    `+80 daqiqa` qilib qo'yadi — ya'ni dars KELAJAKDA tugaydi.
        //    `EndedAtOf` aynan `ActualEnd` ni oladi, shuning uchun soat
        //    70 + 10 = 80 daqiqadan ortiq surilishi SHART. 95 — shu
        //    chegaradan xotirjam o'tadigan qiymat.
        factory.Clock.Set(factory.Clock.GetUtcNow().AddMinutes(95));

        await factory.RunReconcileAsync();

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionStatus.Should().Be(RecordingCompositionStatus.Failed);
        row.Status.Should().Be(RecordingStatus.Failed);
        row.Error.Should().Be("Darsdan yozib olingan trek topilmadi.");
    }

    /// <summary>
    /// ⚠️ FAQAT OVOZ CHIQQANI — MUVAFFAQIYAT (§4.1-6). Ustoz kamerani
    /// yoqmagan dars ham to'liq yozuv: o'quv bo'limi tushuntirish
    /// sifatini baholaydi va u OVOZDA. "Video majburiy" degan tekshiruv
    /// QO'SHILMAYDI.
    /// </summary>
    [Fact]
    public async Task EndedLesson_WithAudioButNoVideo_IsQueuedNotFailed()
    {
        var lesson = await NewLessonAsync(SessionStatus.Ended);

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.RoomAudio,
            trackSid: RecordingTrack.RoomAudioSid,
            startedAt: factory.Clock.GetUtcNow().AddHours(-2),
            endedAt: factory.Clock.GetUtcNow().AddMinutes(-30));

        await factory.RunReconcileAsync();

        (await CompositionWorld.ReloadAsync(factory, lesson.RecordingId))
            .CompositionStatus.Should().Be(RecordingCompositionStatus.Queued);
    }

    /// <summary>
    /// Tugagan darsda mikser YOQILMAYDI — yoziladigan xona endi yo'q.
    /// </summary>
    [Fact]
    public async Task EndedLesson_DoesNotStartAMixer()
    {
        await NewLessonAsync(SessionStatus.Ended);

        await factory.RunReconcileAsync();

        factory.Egress.StartedRoomAudio.Should().BeEmpty();
        factory.Rooms.ParticipantCalls.Should().BeEmpty("tugagan xonani so'rashning ma'nosi yo'q");
    }

    /// <summary>
    /// Navbatga tushgan yozuvga BOSHQA TEGILMAYDI — u endi tungi
    /// kompozitorning ishi. Aks holda dars ikkinchi marta navbatga
    /// qo'yilardi.
    /// </summary>
    [Fact]
    public async Task QueuedRecording_IsLeftAlone()
    {
        var lesson = await NewLessonAsync(
            SessionStatus.Ended, RecordingCompositionStatus.Queued);

        (await factory.RunReconcileAsync()).Processed.Should().Be(0);

        factory.Egress.StartedRoomAudio.Should().BeEmpty();
    }

    /// <summary>
    /// 🔴 ESKI QUVURNING QATORIGA TEGILMAYDI. Uning bo'laklari yo'q va
    /// yig'ish bosqichi ham yo'q — bu vazifa uni ko'rmasligi kerak
    /// (watchdog esa AKSINCHA, faqat o'shani ko'radi — §5.9-1).
    /// </summary>
    [Fact]
    public async Task OldPipelineRecording_IsIgnored()
    {
        _world ??= await WorldBuilder.CreateAsync(factory, "rc");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, _world.GroupId, SessionStatus.Live, _world.Teacher.Id);

        await factory.WithDbAsync(async db =>
        {
            db.SessionRecordings.Add(new SessionRecording
            {
                SessionId = sessionId,
                Status = RecordingStatus.Active,
                Pipeline = RecordingPipeline.RoomComposite,
                ObjectKey = $"recordings/itest/{Guid.NewGuid():N}.mp4",
            });

            return await db.SaveChangesAsync();
        });

        (await factory.RunReconcileAsync()).Processed.Should().Be(0);

        factory.Egress.StartedRoomAudio.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════ yordamchilar

    private sealed record Lesson(
        long SessionId, long RecordingId, string HostIdentity, string StudentIdentity);

    /// <summary>
    /// Guruh + dars + <c>TrackComposition</c> yozuv qatori, VA butun
    /// holatni tozalash.
    ///
    /// 🔴 TOZALASH SHART: vazifa BUTUN bazadagi yig'ilayotgan yozuvlarni
    /// oladi, test sinfi esa bitta bazani baham ko'radi — qo'shni
    /// testning qatori natijani o'zgartirardi.
    /// </summary>
    private async Task<Lesson> NewLessonAsync(
        SessionStatus status = SessionStatus.Live,
        RecordingCompositionStatus composition = RecordingCompositionStatus.Collecting)
    {
        await factory.WithDbAsync(db => db.SessionRecordings.ExecuteDeleteAsync());

        factory.Clock.Set(DateTimeOffset.UtcNow);

        factory.Egress.Started.Clear();
        factory.Egress.StartedTracks.Clear();
        factory.Egress.StartedRoomAudio.Clear();
        factory.Egress.Stopped.Clear();
        factory.Egress.FailWith = null;

        factory.Rooms.Participants = LiveKitTrackListResult.Ok([]);
        factory.Rooms.Egresses = LiveKitEgressListResult.Ok([]);
        factory.Rooms.ParticipantCalls.Clear();
        factory.Rooms.EgressCalls.Clear();

        _world ??= await WorldBuilder.CreateAsync(factory, "rc");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, _world.GroupId, status, _world.Teacher.Id);

        var recordingId = await CompositionWorld.AddRecordingAsync(
            factory, sessionId, composition: composition);

        return new Lesson(
            sessionId,
            recordingId,
            _world.Teacher.Id.ToString(CultureInfo.InvariantCulture),
            _world.Student.Id.ToString(CultureInfo.InvariantCulture));
    }

    private async Task PutAsync(string objectKey, int size)
    {
        using var content = new MemoryStream(new byte[size]);

        await factory.Services.GetRequiredService<IRecordingStorage>()
            .PutAsync(objectKey, content, size, "application/octet-stream");
    }

    private StudentWorld? _world;
}
