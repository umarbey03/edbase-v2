using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI YIG'ISH AYLANISHI (SPEC-RECORDING-V2 §4.5)
/// ════════════════════════════════════════════════════════════════════════
///
/// ffmpeg bu yerda SOXTA (<see cref="ComposerSpy"/>) — tekshirilayotgan
/// narsa kodlash emas, uning ATROFIDAGI qarorlar: nosozlikda nima
/// bo'ladi, uzilishda nima bo'ladi, qulagan ishchidan qolgan izlar
/// qanday tozalanadi. Haqiqiy ffmpeg alohida faylda
/// (<c>FfmpegCompositionTests</c>).
///
/// ── QO'RIQLANADIGAN TO'RTTA XATO ────────────────────────────────────────
///
/// 🔴 1) YARIM QOLGAN ISHNI "DAVOM ETTIRISH". Yarim yozilgan mp4 da
///       <c>moov</c> atomi yo'q; unga qo'shib yozilgan fayl uch soniya
///       o'ynab to'xtaydi — faylsizlikdan HAM YOMON.
///
/// 🔴 2) UZILISHNI NOSOZLIK DEB SANASH. Tungi oyna tugashi "navbat
///       kechadan uzun bo'ldi" degani. Uni urinish deb sanasak, mutlaqo
///       sog'lom yozuv beshta band kechadan keyin o'lardi.
///
/// 🔴 3) UZILGAN QATORNI <c>Running</c> HOLIDA QOLDIRISH. U ijara
///       muddati tugaguncha ko'rinmasdi va keyin "qulagan ishchi" deb
///       olinib, URINISH sarflardi.
///
/// 🔴 4) YAKUNLANGAN YOZUVNI XOM FAYLLAR O'CHIRILMAGANI UCHUN ORQAGA
///       QAYTARISH. Yetim xom fayl PUL turadi, orqaga qaytarilgan sog'lom
///       yozuv esa BUTUN DARSNI.
/// </summary>
public sealed class CompositionRunnerTests(FakeComposerFactory factory)
    : IClassFixture<FakeComposerFactory>
{
    // ═══════════════════════════════════════════════════ 1) muvaffaqiyat

    /// <summary>
    /// Odatiy kecha: qator egallanadi, kodlanadi, yakunlanadi va XOM
    /// fayllar ombordan o'chiriladi.
    /// </summary>
    [Fact]
    public async Task Compose_Success_CompletesTheRecordingAndPurgesRaw()
    {
        var lesson = await NewLessonAsync();

        var audioKey = RawKey();
        var videoKey = RawKey();

        await PutAsync(audioKey, 2048);
        await PutAsync(videoKey, 4096);

        await AddTracksAsync(lesson, audioKey, videoKey);

        factory.Composer.Result = CompositionResult.Ok(9_000_000, 5400, []);

        var result = await factory.RunCompositionAsync();

        result.Outcome.Should().Be(CompositionCycleOutcome.Completed);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionStatus.Should().Be(RecordingCompositionStatus.Completed);
        row.Status.Should().Be(RecordingStatus.Completed);
        row.SizeBytes.Should().Be(9_000_000);
        row.DurationSeconds.Should().Be(5400);
        row.CompositionFinishedAt.Should().NotBeNull();
        row.CompositionLeaseUntil.Should().BeNull();
        row.RawPurgedAt.Should().NotBeNull();
        row.Error.Should().BeNull();

        (await HeadAsync(audioKey)).Should().BeNull("xom ovoz o'chirilishi kerak");
        (await HeadAsync(videoKey)).Should().BeNull("xom video o'chirilishi kerak");
    }

    /// <summary>
    /// Yakuniy kalit REJADA — yig'ish MAVJUD kalitga yozadi, yangisini
    /// o'ylab topmaydi. Aks holda qator bir joyni, fayl boshqa joyni
    /// ko'rsatardi va o'quvchining havolasi bo'shliqqa qarab qolardi.
    /// </summary>
    [Fact]
    public async Task Compose_TargetsTheExistingObjectKey()
    {
        var lesson = await NewLessonAsync();

        await AddTracksAsync(lesson, RawKey(), RawKey());

        await factory.RunCompositionAsync();

        factory.Composer.Plans[^1].TargetObjectKey.Should().Be(lesson.ObjectKey);
    }

    /// <summary>
    /// O'lchangan uzunliklar qatorlarga yoziladi — bu §9.1 ning YAGONA
    /// avtomatik o'lchovi. Egress aytgan uzunlik esa TEGILMAYDI: qimmatlisi
    /// ularning FARQI.
    /// </summary>
    [Fact]
    public async Task Compose_StoresTheProbedDurations()
    {
        var lesson = await NewLessonAsync();
        var tracks = await AddTracksAsync(lesson, RawKey(), RawKey());

        factory.Composer.OnCompose = (plan, _) => Task.FromResult(
            CompositionResult.Ok(1024, 5400,
            [
                new ProbedTrackDuration(tracks.AudioId, 5_399_100),
                new ProbedTrackDuration(tracks.VideoId, 1_795_000),
            ]));

        await factory.RunCompositionAsync();

        var rows = await CompositionWorld.TracksAsync(factory, lesson.RecordingId);

        rows.Single(t => t.Id == tracks.AudioId).ProbedDurationMs.Should().Be(5_399_100);
        rows.Single(t => t.Id == tracks.VideoId).ProbedDurationMs.Should().Be(1_795_000);
    }

    /// <summary>
    /// Mikser yiqilgan dars: fayl TAYYOR, lekin JIM. Xodim buni OCHMASDAN
    /// bilishi kerak (§4.6), aks holda "yozuv buzuq" degan xabar keladi.
    /// </summary>
    [Fact]
    public async Task Compose_SilentLesson_LeavesAWarningForStaff()
    {
        var lesson = await NewLessonAsync();

        await CompositionWorld.AddTrackAsync(
            factory, lesson.RecordingId, RecordingTrackKind.CameraVideo,
            startedAt: Start, endedAt: Start.AddMinutes(30), objectKey: RawKey());

        await factory.RunCompositionAsync();

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionStatus.Should().Be(RecordingCompositionStatus.Completed);
        row.CompositionError.Should().Be("Dars ovozi yozib olinmadi.");
    }

    // ═══════════════════════════════════════════════════ 2) 09:00 — qattiq to'xtash

    /// <summary>
    /// 🔴 BU TO'PLAMDAGI ENG MUHIM TEST: TUNGI OYNA TUGADI.
    ///
    /// Kutilgan natija — qator BUZUQ EMAS, NAVBATDA:
    ///
    ///   • <c>CompositionStatus = Queued</c> (osilib qolmaydi);
    ///   • <c>CompositionInterruptions</c> oshadi;
    ///   • 🔴 <c>CompositionAttempts</c> O'ZGARMAYDI — uzilish nosozlik
    ///     EMAS;
    ///   • ijara bo'shatiladi;
    ///   • 🔴 YAKUNIY KALITDA HECH NARSA YO'Q: yuklash — eng oxirgi qadam
    ///     va u faqat tekshiruvdan o'tgan fayl uchun bajariladi.
    /// </summary>
    [Fact]
    public async Task HardStop_LeavesTheRowQueuedAndResumable_NotCorrupt()
    {
        var lesson = await NewLessonAsync();

        await AddTracksAsync(lesson, RawKey(), RawKey());

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        factory.Composer.OnCompose = async (_, ct) =>
        {
            entered.TrySetResult();

            // Kodlash ketmoqda… 09:00 keldi.
            await Task.Delay(Timeout.Infinite, ct);

            return CompositionResult.Ok(0, 0, []);
        };

        using var window = new CancellationTokenSource();

        var run = factory.RunCompositionAsync(window.Token);

        await entered.Task;
        await window.CancelAsync();

        (await run).Outcome.Should().Be(CompositionCycleOutcome.Interrupted);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionStatus.Should().Be(
            RecordingCompositionStatus.Queued, "ish keyingi kechaga qoldirildi");

        row.CompositionInterruptions.Should().Be(1);
        row.CompositionAttempts.Should().Be(0, "uzilish NOSOZLIK EMAS");
        row.CompositionLeaseUntil.Should().BeNull();
        row.Status.Should().NotBe(RecordingStatus.Failed);

        row.CompositionError.Should().Be("Tungi oyna tugadi — keyingi kechada davom etadi.");

        (await HeadAsync(lesson.ObjectKey)).Should().BeNull(
            "yarim natija HECH QACHON yakuniy kalitga yozilmaydi");
    }

    /// <summary>
    /// 🔴 UZILGAN ISH KEYINGI KECHADA BIRINCHI BO'LIB OLINADI.
    ///
    /// Bu loyiha egasining oshkor talabi. Test uzilgan (eski) qator
    /// bilan birga YANGI qator yaratadi va keyingi egallash AYNAN
    /// eskisini olishini tekshiradi.
    /// </summary>
    [Fact]
    public async Task InterruptedWork_IsPickedUpFirstOnTheNextNight()
    {
        var interrupted = await NewLessonAsync(createdAt: DateTimeOffset.UtcNow.AddDays(-2));

        await AddTracksAsync(interrupted, RawKey(), RawKey());

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        factory.Composer.OnCompose = async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);

            return CompositionResult.Ok(0, 0, []);
        };

        using var window = new CancellationTokenSource();

        var run = factory.RunCompositionAsync(window.Token);

        await entered.Task;
        await window.CancelAsync();
        await run;

        // Keyingi kecha: navbatda YANGIROQ dars ham bor.
        var fresh = await NewLessonAsync(createdAt: DateTimeOffset.UtcNow);

        await AddTracksAsync(fresh, RawKey(), RawKey());

        (await factory.ClaimAsync(TimeSpan.FromMinutes(5)))!
            .RecordingId.Should().Be(
                interrupted.RecordingId,
                "sig'magan ish keyingi kechada BIRINCHI olinadi");
    }

    // ═══════════════════════════════════════════════════ 3) qulagan ishchi

    /// <summary>
    /// 🔴 QULAGAN ISHCHIDAN QOLGAN ISH BOSHIDAN BOSHLANADI VA UNGACHA
    /// IZLARI O'CHIRILADI.
    ///
    /// Test yakuniy kalitga YARIM fayl qo'yadi (oldingi urinish yuklash
    /// paytida qulagan), qatorni ijarasi eskirgan <c>Running</c> holiga
    /// keltiradi va aylanishni yurgizadi. Kutilgan natija:
    ///
    ///   • yarim fayl kodlash BOSHLANISHIDAN OLDIN o'chirilgan;
    ///   • urinish sanalgan;
    ///   • yig'uvchiga TO'LIQ reja berilgan — "davom ettirish" degan
    ///     tushuncha umuman yo'q.
    /// </summary>
    [Fact]
    public async Task CrashRecovery_DeletesTheLeftoverOutputBeforeStartingOver()
    {
        var lesson = await NewLessonAsync();

        await AddTracksAsync(lesson, RawKey(), RawKey());

        // Oldingi urinishdan qolgan YARIM mp4.
        await PutAsync(lesson.ObjectKey, 4096);

        (await HeadAsync(lesson.ObjectKey)).Should().NotBeNull();

        await ExpireLeaseAsync(lesson.RecordingId);

        var leftoverAtComposeTime = -1L;

        factory.Composer.OnCompose = async (_, _) =>
        {
            leftoverAtComposeTime = (await HeadAsync(lesson.ObjectKey))?.SizeBytes ?? -1;

            return CompositionResult.Ok(5_000_000, 5400, []);
        };

        var result = await factory.RunCompositionAsync();

        result.Outcome.Should().Be(CompositionCycleOutcome.Completed);

        leftoverAtComposeTime.Should().Be(
            -1, "yarim fayl kodlash boshlanishidan OLDIN o'chirilishi kerak");

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionAttempts.Should().Be(1, "uzilib qolgan urinish HAQIQIY nosozlik");

        factory.Composer.Plans[^1].Inputs.Should().HaveCount(
            2, "reja HAR SAFAR to'liq quriladi — yarim natija davom ettirilmaydi");
    }

    // ═══════════════════════════════════════════════════ 4) nosozliklar

    /// <summary>
    /// ffmpeg yiqilsa qator NAVBATGA qaytadi va urinish sanaladi; uchinchi
    /// urinishdan keyin yozuv YAKUNIY xato bo'ladi.
    /// </summary>
    [Fact]
    public async Task Compose_Failure_RetriesTwiceThenGivesUp()
    {
        var lesson = await NewLessonAsync();

        await AddTracksAsync(lesson, RawKey(), RawKey());

        factory.Composer.OnCompose = (_, _) =>
            Task.FromResult(CompositionResult.Fail("Video yig'ilmadi (ffmpeg kodi 1)."));

        (await factory.RunCompositionAsync()).Outcome.Should().Be(CompositionCycleOutcome.Retrying);
        (await factory.RunCompositionAsync()).Outcome.Should().Be(CompositionCycleOutcome.Retrying);

        var last = await factory.RunCompositionAsync();

        last.Outcome.Should().Be(CompositionCycleOutcome.Failed);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionAttempts.Should().Be(3);
        row.CompositionInterruptions.Should().Be(0, "bular nosozlik, uzilish emas");
        row.CompositionStatus.Should().Be(RecordingCompositionStatus.Failed);
        row.Status.Should().Be(RecordingStatus.Failed);
        row.Error.Should().Be("Video yig'ilmadi (ffmpeg kodi 1).");
    }

    /// <summary>
    /// Bironta ham tayyor bo'lak yo'q — qayta urinishning MA'NOSI yo'q,
    /// yozuv DARHOL yopiladi.
    /// </summary>
    [Fact]
    public async Task Compose_WithoutAnyCompletedTrack_FailsImmediately()
    {
        var lesson = await NewLessonAsync();

        await CompositionWorld.AddTrackAsync(
            factory, lesson.RecordingId, RecordingTrackKind.RoomAudio,
            status: RecordingStatus.Failed, objectKey: RawKey());

        var result = await factory.RunCompositionAsync();

        result.Outcome.Should().Be(CompositionCycleOutcome.Failed);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionAttempts.Should().Be(0, "urinish sarflanmadi — yig'ish umuman boshlanmadi");
        row.Status.Should().Be(RecordingStatus.Failed);
        row.Error.Should().Be("Darsdan yozib olingan trek topilmadi.");
    }

    /// <summary>
    /// 🔴 YO'QOLGAN XOM BO'LAK BUTUN DARSNI YIQITMAYDI.
    ///
    /// Bo'lak <c>Completed</c> bo'lgan, ya'ni fayl bir paytlar OMBORDA
    /// EDI. Endi yo'q bo'lsa, reja AYNI KECHADA, o'sha bo'laksiz qayta
    /// quriladi: dars bir bo'lagini yo'qotadi, HAMMASINI emas. Aks holda
    /// har urinish AYNAN shu joyda yiqilib, uch kechadan keyin butun dars
    /// yo'qolardi.
    ///
    /// ⚠️ BO'LAKNING QATORI <c>Completed</c> BO'LIB QOLADI va bu ATAYLAB:
    /// <c>RecordingTrack.MarkFailed</c> tayyor bo'lakni ORQAGA
    /// QAYTARMAYDI (kech kelgan hodisa tayyor faylni ro'yxatdan
    /// o'chirmasin — M1 qarori). Yo'qotish LOGDA qoladi.
    /// </summary>
    [Fact]
    public async Task Compose_WithAMissingRawObject_RebuildsThePlanWithoutIt()
    {
        var lesson = await NewLessonAsync();
        var tracks = await AddTracksAsync(lesson, RawKey(), RawKey());

        var attempt = 0;

        factory.Composer.OnCompose = (plan, _) =>
        {
            attempt++;

            return Task.FromResult(attempt == 1
                ? CompositionResult.Fail(
                    "Ba'zi xom bo'laklar omborda topilmadi.",
                    missingTrackIds: [tracks.VideoId])
                : CompositionResult.Ok(1024, 5400, []));
        };

        var result = await factory.RunCompositionAsync();

        result.Outcome.Should().Be(
            CompositionCycleOutcome.Completed, "dars ovozsiz emas, videosiz qoldi");

        attempt.Should().Be(2, "reja bir marta, AYNI kechada qayta quriladi");

        factory.Composer.Plans[0].Inputs.Should().HaveCount(2);

        factory.Composer.Plans[1].Inputs.Should().ContainSingle()
            .Which.TrackId.Should().Be(tracks.AudioId);

        (await CompositionWorld.ReloadAsync(factory, lesson.RecordingId))
            .CompositionStatus.Should().Be(RecordingCompositionStatus.Completed);
    }

    /// <summary>
    /// Hamma xom bo'lak yo'qolgan bo'lsa qayta qurishning ma'nosi yo'q —
    /// dastlabki nosozlik o'z kuchida qoladi.
    /// </summary>
    [Fact]
    public async Task Compose_WithEveryRawObjectMissing_KeepsTheOriginalFailure()
    {
        var lesson = await NewLessonAsync();
        var tracks = await AddTracksAsync(lesson, RawKey(), RawKey());

        var attempts = 0;

        factory.Composer.OnCompose = (_, _) =>
        {
            attempts++;

            return Task.FromResult(CompositionResult.Fail(
                "Ba'zi xom bo'laklar omborda topilmadi.",
                missingTrackIds: [tracks.AudioId, tracks.VideoId]));
        };

        (await factory.RunCompositionAsync()).Outcome.Should().Be(CompositionCycleOutcome.Retrying);

        attempts.Should().Be(1, "chiqarib tashlangach reja umuman qurilmaydi");

        (await CompositionWorld.ReloadAsync(factory, lesson.RecordingId))
            .CompositionError.Should().Be("Ba'zi xom bo'laklar omborda topilmadi.");
    }

    // ═══════════════════════════════════════════════════ 5) tozalash qoldig'i

    /// <summary>
    /// O'chirish yiqilgan kechadan qolgan xom fayllar KEYINGI kecha,
    /// navbat bo'sh bo'lgan lahzada yig'ishtiriladi (§4.5-9).
    /// </summary>
    [Fact]
    public async Task IdleCycle_PurgesTheRawBacklog()
    {
        var lesson = await NewLessonAsync(composition: RecordingCompositionStatus.Completed);

        var key = RawKey();

        await PutAsync(key, 1024);

        await CompositionWorld.AddTrackAsync(
            factory, lesson.RecordingId, RecordingTrackKind.RoomAudio,
            startedAt: Start, endedAt: Start.AddHours(1), objectKey: key);

        var result = await factory.RunCompositionAsync();

        result.Outcome.Should().Be(CompositionCycleOutcome.Idle);
        result.PurgedRecordings.Should().Be(1);

        (await HeadAsync(key)).Should().BeNull();

        (await CompositionWorld.ReloadAsync(factory, lesson.RecordingId))
            .RawPurgedAt.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════ yordamchilar

    private static readonly DateTimeOffset Start =
        new(2026, 9, 5, 5, 0, 0, TimeSpan.Zero);

    private sealed record Lesson(long SessionId, long RecordingId, string ObjectKey);

    private sealed record Tracks(long AudioId, long VideoId);

    /// <summary>
    /// Guruh + dars + <c>TrackComposition</c> yozuv qatori, VA navbatni
    /// tozalash.
    ///
    /// 🔴 TOZALASH SHART: egallash butun BAZA bo'yicha ishlaydi, test
    /// sinfi esa bitta bazani baham ko'radi — qo'shni testning qoldig'i
    /// natijani o'zgartirardi.
    /// </summary>
    private async Task<Lesson> NewLessonAsync(
        DateTimeOffset? createdAt = null,
        RecordingCompositionStatus composition = RecordingCompositionStatus.Queued)
    {
        if (createdAt is null)
        {
            await factory.WithDbAsync(db => db.SessionRecordings.ExecuteDeleteAsync());

            factory.Clock.Set(DateTimeOffset.UtcNow);
            factory.Composer.OnCompose = null;
            factory.Composer.Plans.Clear();
            factory.Composer.Result = CompositionResult.Ok(1024, 60, []);
        }

        _world ??= await WorldBuilder.CreateAsync(factory, "cr");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, _world.GroupId, SessionStatus.Ended, _world.Teacher.Id);

        var objectKey = $"recordings/itest/{Guid.NewGuid():N}.mp4";

        var recordingId = await CompositionWorld.AddRecordingAsync(
            factory,
            sessionId,
            composition: composition,
            objectKey: objectKey,
            createdAt: createdAt);

        return new Lesson(sessionId, recordingId, objectKey);
    }

    /// <summary>Bitta uzluksiz ovoz + bitta kamera bo'lagi — odatiy dars.</summary>
    private async Task<Tracks> AddTracksAsync(Lesson lesson, string audioKey, string videoKey)
    {
        var audioId = await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.RoomAudio,
            startedAt: Start,
            endedAt: Start.AddMinutes(90),
            trackSid: "ROOM",
            objectKey: audioKey);

        var videoId = await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            RecordingTrackKind.CameraVideo,
            startedAt: Start.AddSeconds(5),
            endedAt: Start.AddMinutes(30),
            objectKey: videoKey);

        return new Tracks(audioId, videoId);
    }

    private async Task ExpireLeaseAsync(long recordingId) =>
        await factory.WithDbAsync(async db =>
        {
            var row = await db.SessionRecordings.FirstAsync(r => r.Id == recordingId);

            row.CompositionStatus = RecordingCompositionStatus.Running;
            row.CompositionStartedAt = factory.Clock.GetUtcNow().AddHours(-2);
            row.CompositionLeaseUntil = factory.Clock.GetUtcNow().AddHours(-1);

            return await db.SaveChangesAsync();
        });

    private static string RawKey() => $"raw/itest/{Guid.NewGuid():N}.bin";

    private async Task PutAsync(string objectKey, int size)
    {
        using var content = new MemoryStream(new byte[size]);

        await factory.Services.GetRequiredService<IRecordingStorage>()
            .PutAsync(objectKey, content, size, "application/octet-stream");
    }

    private Task<StoredObjectInfo?> HeadAsync(string objectKey) =>
        factory.Services.GetRequiredService<IRecordingStorage>().HeadAsync(objectKey);

    private StudentWorld? _world;
}
