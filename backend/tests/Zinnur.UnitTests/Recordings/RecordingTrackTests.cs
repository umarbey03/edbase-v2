using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Recordings;

/// <summary>
/// XOM BO'LAKNING HOLAT MASHINASI (yozuv quvuri v2).
///
/// ★ NIMA UCHUN AYRIM TEST FAYLI, <c>SessionRecordingTests</c> ICHIDA
/// EMAS: bo'lak <see cref="RecordingStatus"/> ni QAYTA ISHLATADI, lekin
/// uning qoidalari bir joyda ATAYLAB QAT'IYROQ (kech kelgan
/// <c>egress_ended</c> yiqilgan bo'lakni tiriltirmaydi). Aynan shu farq
/// eng oson yo'qoladigan narsa, shuning uchun u alohida, ko'rinadigan
/// joyda tekshiriladi.
///
/// ★ Bo'lak holatini UCHTA mustaqil manba o'zgartiradi — webhook
/// (<c>track_published</c>, <c>egress_*</c>), moslashtiruvchi vazifa va
/// tungi yig'ish. Ularning har biri KECHIKKAN yoki TAKRORIY hodisa bilan
/// keladi (<c>SessionRecordingTests</c> dagi AYNI sabab).
/// </summary>
public sealed class RecordingTrackTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    private static RecordingTrack NewVideo() =>
        new()
        {
            RecordingId = 1,
            TrackSid = "TR_VCabc123",
            ParticipantIdentity = "42",
            Kind = RecordingTrackKind.CameraVideo,
            MimeType = "video/vp8",
            ObjectKey = "raw/7/1/TR_VCabc123.webm",
        };

    private static RecordingTrack NewRoomAudio() =>
        new()
        {
            RecordingId = 1,
            TrackSid = RecordingTrack.RoomAudioSid,
            ParticipantIdentity = null,
            Kind = RecordingTrackKind.RoomAudio,
            ObjectKey = "raw/7/1/ROOM.ogg",
        };

    // ================================================================= boshlash

    [Fact]
    public void New_StartsAsRequested_SoTheReconcileJobCanSeeIt()
    {
        var track = NewVideo();

        track.Status.Should().Be(RecordingStatus.Requested);
        track.IsFinished.Should().BeFalse();
        track.CanRetry(5).Should().BeTrue();
    }

    [Fact]
    public void BeginAttempt_CountsTheAttempt_BeforeTheExternalCall()
    {
        var track = NewVideo();

        track.BeginAttempt(Now);

        track.Attempts.Should().Be(1);
        track.LastAttemptAt.Should().Be(Now);
    }

    /// <summary>
    /// Yakunlangan bo'lakni qayta boshlash — DASTURCHI XATOSI:
    /// u omborda turgan xom faylning kalitini ustidan yozib yuborardi.
    /// </summary>
    [Fact]
    public void BeginAttempt_OnFinishedTrack_Throws()
    {
        var track = NewVideo();
        track.MarkCompleted(null, 10, 20, Now, Now);

        var act = () => track.BeginAttempt(Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkStarting_ClearsThePreviousAttemptError()
    {
        var track = NewVideo();
        track.RecordAttemptError("Egress javob bermadi.", Now);

        track.MarkStarting("EG_track_1", Now);

        track.Status.Should().Be(RecordingStatus.Starting);
        track.EgressId.Should().Be("EG_track_1");
        track.Error.Should().BeNull("oldingi urinishning sababi eskirdi");
    }

    [Fact]
    public void RecordAttemptError_KeepsTheRowRetryable()
    {
        var track = NewVideo();
        track.BeginAttempt(Now);

        track.RecordAttemptError("Egress so'rovi yiqildi.", Now);

        track.Status.Should().Be(RecordingStatus.Requested);
        track.CanRetry(5).Should().BeTrue();
    }

    [Fact]
    public void CanRetry_StopsAtTheLimit()
    {
        var track = NewVideo();
        for (var i = 0; i < 5; i++) track.BeginAttempt(Now);

        track.CanRetry(5).Should().BeFalse();
    }

    // ================================================================= vaqt o'qi

    /// <summary>
    /// 🔴 <c>StartedAt</c> — YIG'ISHNING VAQT O'QI, shunchaki qayd emas.
    /// Takroriy <c>egress_started</c> uni surib yuborsa, bo'lak tayyor
    /// videoda noto'g'ri lahzaga tushardi.
    /// </summary>
    [Fact]
    public void MarkActive_KeepsTheFirstStartedAt_BecauseItIsTheTimelineAnchor()
    {
        var track = NewVideo();
        var first = Now;
        var duplicate = Now.AddSeconds(30);

        track.MarkActive(first, Now);
        track.MarkActive(duplicate, Now);

        track.Status.Should().Be(RecordingStatus.Active);
        track.StartedAt.Should().Be(first);
    }

    // ================================================================= yakunlash

    /// <summary>
    /// Taxmin qilingan kengaytma ISHONCHSIZ: <c>egress_ended</c> haqiqiy
    /// nomni qaytaradi va u ustunlik qiladi.
    /// </summary>
    [Fact]
    public void MarkCompleted_OverwritesTheGuessedKey_WithWhatEgressActuallyWrote()
    {
        var track = NewVideo();
        track.MarkActive(Now, Now);

        track.MarkCompleted("raw/7/1/TR_VCabc123.mp4", 2048, 90, Now.AddMinutes(80), Now);

        track.ObjectKey.Should().Be("raw/7/1/TR_VCabc123.mp4");
        track.SizeBytes.Should().Be(2048);
        track.DurationSeconds.Should().Be(90);
        track.Status.Should().Be(RecordingStatus.Completed);
    }

    [Fact]
    public void MarkCompleted_WithoutAKey_KeepsTheGuessedOne()
    {
        var track = NewVideo();

        track.MarkCompleted(null, 2048, 90, Now, Now);

        track.ObjectKey.Should().Be("raw/7/1/TR_VCabc123.webm");
    }

    /// <summary>
    /// 🔴 SHU FAYLDAGI ENG MUHIM TEST — va aynan shu yerda bo'lak
    /// <see cref="SessionRecording"/> DAN FARQ QILADI.
    ///
    /// Bo'lak <c>Failed</c> bo'lishi uchun uni OMBORDA QIDIRIB TOPMAGAN
    /// bo'lishimiz kerak. Kech kelgan <c>egress_ended</c> uni tiriltirsa,
    /// tungi yig'ish MAVJUD BO'LMAGAN faylni yuklab olishga urinardi va
    /// butun yozuv yiqilardi — bitta bo'lakni yo'qotish o'rniga.
    /// </summary>
    [Fact]
    public void MarkCompleted_AfterFailure_DoesNotResurrectTheRow()
    {
        var track = NewVideo();
        track.MarkFailed("Trek fayli omborga tushmadi.", Now);

        track.MarkCompleted("raw/7/1/TR_VCabc123.webm", 999, 12, Now.AddMinutes(5), Now);

        track.Status.Should().Be(RecordingStatus.Failed);
        track.SizeBytes.Should().BeNull();
        track.Error.Should().Be("Trek fayli omborga tushmadi.");
    }

    [Fact]
    public void MarkFailed_AfterCompletion_IsIgnored()
    {
        var track = NewVideo();
        track.MarkCompleted(null, 2048, 90, Now, Now);

        track.MarkFailed("Kech kelgan xato.", Now);

        track.Status.Should().Be(RecordingStatus.Completed);
        track.Error.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_TrimsALongReason_ToTheColumnLimit()
    {
        var track = NewVideo();

        track.MarkFailed(new string('x', RecordingTrack.MaxErrorLength + 200), Now);

        track.Error.Should().HaveLength(RecordingTrack.MaxErrorLength);
    }

    /// <summary>
    /// Takroriy <c>StopEgress</c> LiveKit'da xato beradi va log'ni
    /// bekorga to'ldiradi — shuning uchun payt BIR MARTA yoziladi.
    /// </summary>
    [Fact]
    public void MarkStopRequested_KeepsTheFirstMoment()
    {
        var track = NewVideo();

        track.MarkStopRequested(Now);
        track.MarkStopRequested(Now.AddMinutes(3));

        track.StopRequestedAt.Should().Be(Now);
    }

    // ================================================================= xona ovozi

    /// <summary>
    /// Xona ovozi qatorini turi ANIQLAYDI, sentinel emas: mikser
    /// almashtirilganda sentinel <c>ROOM2</c>, <c>ROOM3</c> bo'lib ketadi.
    /// </summary>
    [Fact]
    public void IsRoomAudio_LooksAtTheKind_NotTheSentinel()
    {
        var replacement = NewRoomAudio();
        replacement.TrackSid = RecordingTrack.RoomAudioSid + "2";

        replacement.IsRoomAudio.Should().BeTrue();
        NewVideo().IsRoomAudio.Should().BeFalse();
    }

    /// <summary>
    /// Aralashma HECH KIMGA tegishli emas — bo'sh satr u yerda
    /// kelajakdagi biror <c>WHERE</c> ishonadigan yolg'on bo'lardi.
    /// </summary>
    [Fact]
    public void RoomAudio_HasNoParticipant()
    {
        NewRoomAudio().ParticipantIdentity.Should().BeNull();
    }

    /// <summary>
    /// Sentinel haqiqiy trek bilan TO'QNASHA OLMAYDI — LiveKit trek
    /// identifikatorlari doim <c>TR_</c> bilan boshlanadi. Aynan shu
    /// sabab <c>(RecordingId, TrackSid)</c> unikal indeksi "bitta darsga
    /// bitta mikser" kafolatini ham beradi.
    /// </summary>
    [Fact]
    public void RoomAudioSid_CannotCollideWithALiveKitTrackSid()
    {
        RecordingTrack.RoomAudioSid.Should().NotStartWith("TR_");
    }
}
