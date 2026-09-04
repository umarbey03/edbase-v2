using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Recordings.Jobs;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// WATCHDOG — "Starting/Active" YO'LI (2026-09-04 REGRESSIYASI)
/// ════════════════════════════════════════════════════════════════════════
///
/// WHAT BROKE IN PRODUCTION. LiveKit webhooks are disabled, so
/// <c>egress_started</c> never arrives and <c>SessionRecording.StartedAt</c>
/// stays <c>null</c>. <c>FinalizeAsync</c> then measured the deadline as
/// <c>StartTimeout</c> (10 minutes) from <c>LastAttemptAt</c> and, once it
/// expired, asked storage whether the object was already there. Egress,
/// however, uploads the mp4 only when the lesson ENDS (the moov atom is
/// written last) — so the object was never there yet and the row was marked
/// <c>Failed</c>, which is TERMINAL, while the recording was still running.
///
/// 🔴 EVERY LESSON LONGER THAN 10 MINUTES WAS SILENTLY LOST. Measured on
///    2026-09-04: recording 135 was marked <c>Failed</c> at 14:24, the
///    finished 1.75 GB file landed in R2 at 15:48. The only successful
///    recording that month was a 14 MB short lesson that happened to upload
///    inside the 10-minute window — which is why the failure looked random.
///
/// ── WHAT THESE TESTS LOCK ───────────────────────────────────────────────
///
///  1) While the lesson is <c>Live</c> and no stop was requested, the
///     watchdog WAITS — no matter how much time passed and no matter what
///     storage answers. This is the test the fix was written for.
///  2) The wait only DELAYS the path, it does not close it: once the lesson
///     ends the watchdog requests the stop and, after <c>FinalizeGrace</c>,
///     completes the row from the object that finally landed.
///  3) 🔴 THE FAILURE PATH IS STILL ALIVE (negative control): stop requested,
///     grace expired, nothing in storage → <c>Failed</c>. Without this test
///     the first one could be satisfied by deleting <c>MarkFailed</c>
///     altogether — a green suite proving nothing.
///  4) The runaway brake still works: a recording older than
///     <c>MaxDuration</c> is stopped even while the lesson is <c>Live</c>.
///     Otherwise the guard would have turned a forgotten room into an
///     endless egress.
///
/// ★ TIME IS NOT MOCKED. The job reads the real <c>TimeProvider</c>, so the
///   tests age the ROW instead of the clock (<c>LastAttemptAt</c>,
///   <c>StopRequestedAt</c> are written in the past). The thresholds come
///   from <see cref="RecordingWatchdogSettings.Default"/> — the values that
///   actually run in production; hard-coded minutes would keep passing after
///   somebody changed them.
///
/// ⚠️ Storage is REAL (MinIO), like everywhere else in this folder: the
///    watchdog's source of truth is a HEAD request, and a fake would answer
///    "found" to anything.
/// </summary>
public sealed class RecordingWatchdogTests(RecordingFactory factory)
    : IClassFixture<RecordingFactory>
{
    /// <summary>The limits the job really runs with (Program.cs passes exactly these).</summary>
    private static RecordingWatchdogSettings Limits => RecordingWatchdogSettings.Default;

    // ================================================================= 🔴 regressiya

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 THE REGRESSION: A RUNNING LESSON IS NEVER GIVEN UP ON
    /// ══════════════════════════════════════════════════════════════════
    ///
    /// The row is in exactly the production state: <c>Starting</c>,
    /// <c>StartedAt</c> null (no webhook ever came), the last attempt is
    /// older than <c>StartTimeout</c>, the object is NOT in storage yet —
    /// and the lesson is still <c>Live</c>.
    ///
    /// Before the fix this run ended in <c>Failed</c> and the lesson's
    /// recording was gone for good. Now the row must be untouched.
    /// </summary>
    [Fact]
    public async Task Watchdog_WhenLessonIsLiveAndStartEventNeverArrived_KeepsRecordingWaiting()
    {
        var world = await WorldBuilder.CreateAsync(factory, "wdlive");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, world.GroupId, SessionStatus.Live);

        var egressId = NewEgressId();

        var recordingId = await RecordingWorld.AddRecordingAsync(
            factory,
            sessionId,
            RecordingStatus.Starting,
            egressId: egressId,
            // ⚠️ `StartedAt` stays null — that is the whole point: the row is
            //    aged through `LastAttemptAt`, which is what the broken
            //    deadline was measured from.
            lastAttemptAt: DateTimeOffset.UtcNow - (Limits.StartTimeout + TimeSpan.FromMinutes(5)));

        // Two passes: the scheduler runs every 15 seconds, so the bug had
        // hundreds of chances per lesson. One run proves too little.
        await factory.RunRecordingWatchdogAsync();
        await factory.RunRecordingWatchdogAsync();

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.IsFinished.Should().BeFalse(
            "the lesson is still Live — a terminal state here loses the recording forever");

        recording.Status.Should().Be(
            RecordingStatus.Starting,
            "the row must stay exactly as it was: the watchdog can only WAIT while the lesson runs");

        recording.Error.Should().BeNull(
            "nothing failed — the file simply is not uploaded until the lesson ends");

        recording.EndedAt.Should().BeNull("the recording has not ended");

        recording.StopRequestedAt.Should().BeNull(
            "the lesson is Live and short of MaxDuration — there is nothing to stop yet");

        factory.Egress.Stopped.Should().NotContain(
            egressId, "stopping a healthy egress mid-lesson would cut the recording short");
    }

    // ================================================================= yakunlash yo'li

    /// <summary>
    /// The wait is a DELAY, not a dead end — the whole lifecycle in one test:
    /// waiting while Live → stop requested when the lesson ends → completed
    /// from storage once the object lands and the grace expires.
    ///
    /// ★ IF THE GUARD WERE TOO BROAD (say, "never finalize"), step 3 would
    ///   fail here — so the two tests hold each other in place.
    /// </summary>
    [Fact]
    public async Task Watchdog_AfterLessonEnds_RequestsStopAndCompletesFromStorage()
    {
        var world = await WorldBuilder.CreateAsync(factory, "wdfinish");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, world.GroupId, SessionStatus.Live);

        var egressId = NewEgressId();

        var recordingId = await RecordingWorld.AddRecordingAsync(
            factory,
            sessionId,
            RecordingStatus.Starting,
            egressId: egressId,
            lastAttemptAt: DateTimeOffset.UtcNow - (Limits.StartTimeout + TimeSpan.FromMinutes(5)));

        // ── 1. lesson still running → the watchdog waits ──────────────
        await factory.RunRecordingWatchdogAsync();

        (await RecordingWorld.ReloadAsync(factory, recordingId)).Status.Should().Be(
            RecordingStatus.Starting, "a Live lesson is never given up on");

        // ── 2. lesson ended → stop requested, row still NOT terminal ──
        await EndSessionAsync(sessionId);
        await factory.RunRecordingWatchdogAsync();

        var stopping = await RecordingWorld.ReloadAsync(factory, recordingId);

        stopping.StopRequestedAt.Should().NotBeNull(
            "the end of the lesson is what starts the FinalizeGrace countdown");

        stopping.Status.Should().Be(
            RecordingStatus.Starting,
            "the grace has not elapsed — the upload may still be running");

        stopping.Error.Should().BeNull();

        factory.Egress.Stopped.Should().Contain(
            egressId, "the egress of an ended lesson must actually be told to stop");

        // ── 3. the file landed and the grace expired → Completed ──────
        //
        // ⚠️ Egress writes the object itself; the test cannot make the
        //    upload port choose a key, so instead of moving the object we
        //    point the row at it. For the watchdog the two are
        //    indistinguishable: `HeadAsync(recording.ObjectKey)` starts
        //    finding an object at exactly the moment the real file would
        //    have landed.
        var payload = Encoding.UTF8.GetBytes(new string('m', 4096));
        var objectKey = await UploadAsync(payload);

        await LandFileAsync(
            recordingId,
            objectKey,
            stopRequestedAt: DateTimeOffset.UtcNow - (Limits.FinalizeGrace + TimeSpan.FromMinutes(1)));

        await factory.RunRecordingWatchdogAsync();

        var completed = await RecordingWorld.ReloadAsync(factory, recordingId);

        completed.Status.Should().Be(
            RecordingStatus.Completed,
            "the object is in storage — the missing webhook must not hide a finished file");

        completed.SizeBytes.Should().Be(
            payload.Length,
            "the size comes from the storage HEAD response, not from a guess");

        completed.ObjectKey.Should().Be(objectKey, "the key is ours and it does not change");
        completed.EndedAt.Should().NotBeNull();
        completed.Error.Should().BeNull("a completed recording carries no error");
    }

    // ================================================================= 🔴 salbiy nazorat

    /// <summary>
    /// 🔴 NEGATIVE CONTROL — THE FAILURE PATH IS STILL THERE.
    ///
    /// Lesson ended, stop requested, <c>FinalizeGrace</c> expired and storage
    /// still has nothing: the row MUST become <c>Failed</c>. Without this
    /// test the regression test above would also pass on a watchdog that
    /// never fails anything, and a stuck row would sit in the queue forever.
    /// </summary>
    [Fact]
    public async Task Watchdog_WhenGraceExpiredAndNothingLanded_FailsTheRecording()
    {
        var world = await WorldBuilder.CreateAsync(factory, "wdmissing");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, world.GroupId, SessionStatus.Ended);

        // ⚠️ The object key is a fresh random one — nothing was ever uploaded
        //    under it, so the real MinIO answers "no such object".
        var recordingId = await RecordingWorld.AddRecordingAsync(
            factory,
            sessionId,
            RecordingStatus.Starting,
            egressId: NewEgressId(),
            lastAttemptAt: DateTimeOffset.UtcNow.AddHours(-2),
            stopRequestedAt: DateTimeOffset.UtcNow - (Limits.FinalizeGrace + TimeSpan.FromMinutes(1)));

        await factory.RunRecordingWatchdogAsync();

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.Status.Should().Be(
            RecordingStatus.Failed,
            "the lesson is over, the grace expired and the file never arrived");

        // Uzbek — this string reaches the staff room screen.
        recording.Error.Should().Be("Yozuv fayli omborga tushmadi.");

        recording.EndedAt.Should().NotBeNull("a terminal row carries the moment it ended");
        recording.SizeBytes.Should().BeNull("there is no file to have a size");
    }

    // ================================================================= uzoq yozuv

    /// <summary>
    /// The guard must not disable the runaway brake: a recording older than
    /// <c>MaxDuration</c> is stopped even though the lesson is still
    /// <c>Live</c>. A forgotten room would otherwise record for days.
    /// </summary>
    [Fact]
    public async Task Watchdog_WhenRecordingOutlivesMaxDuration_RequestsStopEvenWhileLessonIsLive()
    {
        var world = await WorldBuilder.CreateAsync(factory, "wdlong");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, world.GroupId, SessionStatus.Live);

        var egressId = NewEgressId();

        var recordingId = await RecordingWorld.AddRecordingAsync(
            factory,
            sessionId,
            RecordingStatus.Starting,
            egressId: egressId,
            lastAttemptAt: DateTimeOffset.UtcNow - (Limits.MaxDuration + TimeSpan.FromMinutes(10)));

        await factory.RunRecordingWatchdogAsync();

        var recording = await RecordingWorld.ReloadAsync(factory, recordingId);

        recording.StopRequestedAt.Should().NotBeNull(
            "MaxDuration is the ceiling that keeps a forgotten room from recording forever");

        factory.Egress.Stopped.Should().Contain(
            egressId, "the brake means an actual StopEgress call, not just a column");

        recording.IsFinished.Should().BeFalse(
            "stopping is not failing — the file may still be uploading");

        recording.Status.Should().Be(RecordingStatus.Starting);
        recording.Error.Should().BeNull();
    }

    // ================================================================= yordamchilar

    /// <summary>
    /// ⚠️ UNIQUE PER ROW: `UX_SessionRecordings_EgressId` is unique, so a
    /// repeated value would blow up `SaveChanges` with 23505 in whichever
    /// test happens to run second.
    /// </summary>
    private static string NewEgressId() => "EG_" + Guid.NewGuid().ToString("N")[..20];

    /// <summary>The lesson ends — written straight to the database (no API involved).</summary>
    /// <returns>Rows written (CA1859: hiding the type buys nothing here).</returns>
    private Task<int> EndSessionAsync(long sessionId) =>
        factory.WithDbAsync(async db =>
        {
            var session = await db.LiveSessions.FirstAsync(s => s.Id == sessionId);

            session.Status = SessionStatus.Ended;
            session.ActualEnd = DateTimeOffset.UtcNow;

            return await db.SaveChangesAsync();
        });

    /// <summary>
    /// "The file has landed": the row now points at an object that really is
    /// in storage, and the stop request is aged past <c>FinalizeGrace</c>.
    /// </summary>
    private Task<int> LandFileAsync(
        long recordingId, string objectKey, DateTimeOffset stopRequestedAt) =>
        factory.WithDbAsync(async db =>
        {
            var recording = await db.SessionRecordings.FirstAsync(r => r.Id == recordingId);

            recording.ObjectKey = objectKey;
            recording.StopRequestedAt = stopRequestedAt;

            return await db.SaveChangesAsync();
        });

    /// <summary>
    /// Puts an object into the REAL bucket and returns its key.
    ///
    /// ⚠️ The upload port is the submissions one (<see cref="ISubmissionStorage"/>):
    /// the recording port is READ-ONLY on purpose — Egress writes the file
    /// without us, so a "save" method there could not exist
    /// (same reasoning as <c>RecordingStorageTests.UploadAsync</c>).
    /// </summary>
    private async Task<string> UploadAsync(byte[] payload)
    {
        var uploads = factory.Services.GetRequiredService<ISubmissionStorage>();

        return await uploads.SaveAsync(new SubmissionUpload(
            StudentId: 1,
            Kind: AttachmentKind.Document,
            Extension: "mp4",
            ContentType: "video/mp4",
            Content: payload));
    }
}
