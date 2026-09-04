using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Recordings;

/// <summary>
/// TUNGI YIG'ISHNING HOLAT MASHINASI (yozuv quvuri v2).
///
/// ★ NIMA UCHUN BU TESTLAR MUHIM: yig'ish holatini uchta mustaqil manba
/// o'zgartiradi — moslashtiruvchi vazifa (navbatga qo'yadi), kompozitor
/// ishchisi (egallaydi, uzaytiradi, yakunlaydi) va bekor qilish signali
/// (tungi oyna tugadi). Ular BOSHQA-BOSHQA konteynerlarda ishlaydi, ya'ni
/// "qaysi biri oxirgi yozdi" savoli hech qachon nazoratda emas.
///
/// ★ IKKI HISOBLAGICHNING FARQI ALOHIDA TEKSHIRILADI: uzilish nosozlik
/// EMAS, va bu ikkisini chalkashtirish sog'lom yozuvni beshta band
/// kechadan keyin o'ldirardi.
/// </summary>
public sealed class SessionRecordingCompositionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 1, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private const string FinalKey = "recordings/2026-09/7/0123456789abcdef.mp4";

    /// <summary>Yangi yo'l qatori — scheduler qanday yaratsa, shundayligicha.</summary>
    private static SessionRecording NewTrackPipeline()
    {
        var recording = new SessionRecording
        {
            SessionId = 7,
            ObjectKey = FinalKey,
            Pipeline = RecordingPipeline.TrackComposition,
        };
        recording.BeginComposition(Now);
        return recording;
    }

    /// <summary>Eski yo'l qatori — bugungi ishlab chiqarish holati.</summary>
    private static SessionRecording NewRoomComposite() =>
        new() { SessionId = 7, ObjectKey = FinalKey };

    private static SessionRecording Queued()
    {
        var recording = NewTrackPipeline();
        recording.MarkRawCollected(Now);
        return recording;
    }

    // ================================================================= ochilish

    [Fact]
    public void New_RoomCompositeRow_HasNoCompositionState()
    {
        var recording = NewRoomComposite();

        recording.Pipeline.Should().Be(RecordingPipeline.RoomComposite);
        recording.CompositionStatus.Should().BeNull(
            "eski yo'lda yig'ish bosqichi umuman yo'q");
    }

    [Fact]
    public void BeginComposition_OpensTheCollectingPhase()
    {
        var recording = NewTrackPipeline();

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Collecting);
    }

    /// <summary>
    /// 🔴 Eski yo'l qatorida bo'sh bo'lmagan <c>CompositionStatus</c> —
    /// XATO, "hali boshlanmagan" emas. Shuning uchun bu yagona metod
    /// jimgina qaytmaydi, balki baqiradi.
    /// </summary>
    [Fact]
    public void BeginComposition_OnRoomCompositeRow_Throws()
    {
        var recording = NewRoomComposite();

        var act = () => recording.BeginComposition(Now);

        act.Should().Throw<DomainException>();
        recording.CompositionStatus.Should().BeNull();
    }

    [Fact]
    public void BeginComposition_IsIdempotent()
    {
        var recording = Queued();

        recording.BeginComposition(Now.AddMinutes(1));

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Queued,
            "takroriy ochilish navbatdagi qatorni orqaga surmaydi");
    }

    // ================================================================= navbat

    [Fact]
    public void MarkRawCollected_MovesCollectingToQueued()
    {
        var recording = NewTrackPipeline();

        recording.MarkRawCollected(Now);

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Queued);
    }

    /// <summary>
    /// Kechikkan vazifa ishlanayotgan qatorni navbatga QAYTARMASLIGI
    /// kerak — aks holda ikkinchi ishchi uni ffmpeg ishlab turganda
    /// egallab olardi.
    /// </summary>
    [Fact]
    public void MarkRawCollected_OnRunningRow_IsIgnored()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);

        recording.MarkRawCollected(Now.AddMinutes(1));

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Running);
    }

    // ================================================================= egallash

    [Fact]
    public void TryClaimComposition_TakesAQueuedRow_AndSetsTheLease()
    {
        var recording = Queued();

        var claimed = recording.TryClaimComposition(Now, Lease);

        claimed.Should().BeTrue();
        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Running);
        recording.CompositionStartedAt.Should().Be(Now);
        recording.CompositionLeaseUntil.Should().Be(Now + Lease);
        recording.CompositionAttempts.Should().Be(0, "bu birinchi, sog'lom urinish");
    }

    /// <summary>Ijara TIRIK ekan, ikkinchi ishchi qatorni ololmaydi.</summary>
    [Fact]
    public void TryClaimComposition_OnALiveLease_IsRefused()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);

        var second = recording.TryClaimComposition(Now.AddMinutes(1), Lease);

        second.Should().BeFalse();
        recording.CompositionLeaseUntil.Should().Be(Now + Lease, "ijara uzaymadi");
    }

    /// <summary>
    /// 🔴 MUDDATI O'TGAN ijara "ish ketyapti" emas, "ishchi qulagan"
    /// degani — shuning uchun uni egallash HAQIQIY urinish sifatida
    /// sanaladi. Aks holda o'sha joyda qulaydigan ish abadiy aylanardi.
    /// </summary>
    [Fact]
    public void TryClaimComposition_OnAnExpiredLease_IsCrashRecovery_AndCountsAsAnAttempt()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);
        var later = Now + Lease + TimeSpan.FromMinutes(1);

        var reclaimed = recording.TryClaimComposition(later, Lease);

        reclaimed.Should().BeTrue();
        recording.CompositionAttempts.Should().Be(1);
        recording.CompositionInterruptions.Should().Be(0);
        recording.CompositionStartedAt.Should().Be(later);
        recording.CompositionLeaseUntil.Should().Be(later + Lease);
        recording.CompositionError.Should().NotBeNull();
    }

    [Fact]
    public void TryClaimComposition_OnACollectingRow_IsRefused()
    {
        var recording = NewTrackPipeline();

        recording.TryClaimComposition(Now, Lease).Should().BeFalse(
            "dars hali tugamagan — yig'iladigan narsa to'liq emas");
    }

    [Fact]
    public void TryClaimComposition_OnARoomCompositeRow_IsRefused()
    {
        NewRoomComposite().TryClaimComposition(Now, Lease).Should().BeFalse();
    }

    [Fact]
    public void RenewCompositionLease_ExtendsOnlyARunningRow()
    {
        var running = Queued();
        running.TryClaimComposition(Now, Lease);
        var queued = Queued();

        running.RenewCompositionLease(Now.AddMinutes(1), Lease);
        queued.RenewCompositionLease(Now.AddMinutes(1), Lease);

        running.CompositionLeaseUntil.Should().Be(Now.AddMinutes(1) + Lease);
        queued.CompositionLeaseUntil.Should().BeNull();
    }

    // ================================================================= nosozlik va uzilish

    [Fact]
    public void ReleaseCompositionForRetry_QueuesAgain_AndCountsARealFailure()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);

        recording.ReleaseCompositionForRetry("ffmpeg 1 kodi bilan chiqdi.", Now.AddHours(1));

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Queued);
        recording.CompositionAttempts.Should().Be(1);
        recording.CompositionInterruptions.Should().Be(0);
        recording.CompositionLeaseUntil.Should().BeNull("ijara bo'shatildi");
        recording.CompositionError.Should().Be("ffmpeg 1 kodi bilan chiqdi.");
        recording.Status.Should().Be(RecordingStatus.Requested, "yozuvning o'zi hali tirik");
    }

    /// <summary>
    /// 🔴 SHU FAYLDAGI ENG MUHIM FARQ: uzilish NOSOZLIK EMAS.
    /// Ikkalasini bitta hisoblagichga qo'shsak, mutlaqo sog'lom yozuv
    /// beshta band kechadan keyin <c>Failed</c> bo'lib qolardi.
    /// </summary>
    [Fact]
    public void InterruptComposition_DoesNotCountAsAFailure()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);

        recording.InterruptComposition(Now.AddHours(8));

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Queued);
        recording.CompositionInterruptions.Should().Be(1);
        recording.CompositionAttempts.Should().Be(0);
        recording.CompositionLeaseUntil.Should().BeNull();
        recording.CompositionError.Should().Contain("keyingi kechada");
    }

    [Fact]
    public void CanRetryComposition_IsFalse_OnlyAfterTheThirdRealFailure()
    {
        var recording = Queued();

        for (var i = 1; i <= SessionRecording.MaxCompositionAttempts; i++)
        {
            recording.TryClaimComposition(Now, Lease);
            recording.ReleaseCompositionForRetry($"{i}-urinish yiqildi.", Now);

            recording.CanRetryComposition.Should().Be(
                i < SessionRecording.MaxCompositionAttempts);
        }
    }

    [Fact]
    public void CanResumeComposition_SurvivesFarMoreNightsThanFailures()
    {
        var recording = Queued();

        for (var i = 1; i <= SessionRecording.MaxCompositionInterruptions; i++)
        {
            recording.TryClaimComposition(Now, Lease);
            recording.InterruptComposition(Now);

            recording.CanResumeComposition.Should().Be(
                i < SessionRecording.MaxCompositionInterruptions);
        }

        recording.CompositionAttempts.Should().Be(0,
            "o'nta uzilish ham bitta nosozlik emas");
    }

    // ================================================================= yakunlash

    [Fact]
    public void MarkCompositionCompleted_FinishesTheRecordingAtTheSameKey()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);
        var endedAt = Now.AddHours(1);

        recording.MarkCompositionCompleted(1_500_000_000, 4_800, endedAt, endedAt);

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Completed);
        recording.CompositionFinishedAt.Should().Be(endedAt);
        recording.CompositionLeaseUntil.Should().BeNull();
        recording.CompositionError.Should().BeNull();

        recording.Status.Should().Be(RecordingStatus.Completed);
        recording.ObjectKey.Should().Be(FinalKey, "yig'ish MAVJUD kalitga yozadi");
        recording.SizeBytes.Should().Be(1_500_000_000);
        recording.DurationSeconds.Should().Be(4_800);
        recording.IsPlayable.Should().BeTrue();
    }

    [Fact]
    public void MarkCompositionFailed_FailsTheRecordingToo_WithTheSameReason()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);

        recording.MarkCompositionFailed("Darsdan yozib olingan trek topilmadi.", Now);

        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Failed);
        recording.CompositionFinishedAt.Should().Be(Now);
        recording.CompositionLeaseUntil.Should().BeNull();

        recording.Status.Should().Be(RecordingStatus.Failed);
        recording.Error.Should().Be("Darsdan yozib olingan trek topilmadi.");
    }

    /// <summary>
    /// Ijara muddati o'tib ketgan ishchi keyinroq o'z natijasini yozib
    /// qo'yishi mumkin — tayyor faylni bu YO'Q QILMASLIGI kerak.
    /// </summary>
    [Fact]
    public void CompositionMethods_AreNoOps_OnceTheRecordingIsCompleted()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);
        recording.MarkCompositionCompleted(10, 20, Now, Now);

        recording.MarkCompositionFailed("Kech kelgan xato.", Now.AddMinutes(5));
        recording.ReleaseCompositionForRetry("Kech kelgan nosozlik.", Now.AddMinutes(5));
        recording.InterruptComposition(Now.AddMinutes(5));
        recording.TryClaimComposition(Now.AddHours(6), Lease).Should().BeFalse();

        recording.Status.Should().Be(RecordingStatus.Completed);
        recording.CompositionStatus.Should().Be(RecordingCompositionStatus.Completed);
        recording.CompositionAttempts.Should().Be(0);
        recording.CompositionInterruptions.Should().Be(0);
    }

    [Fact]
    public void CompositionMethods_NeverTouchARoomCompositeRow()
    {
        var recording = NewRoomComposite();

        recording.MarkRawCollected(Now);
        recording.TryClaimComposition(Now, Lease);
        recording.RenewCompositionLease(Now, Lease);
        recording.ReleaseCompositionForRetry("Nosozlik.", Now);
        recording.InterruptComposition(Now);
        recording.MarkCompositionFailed("Nosozlik.", Now);
        recording.MarkCompositionCompleted(1, 1, Now, Now);

        recording.CompositionStatus.Should().BeNull();
        recording.CompositionAttempts.Should().Be(0);
        recording.CompositionInterruptions.Should().Be(0);
        recording.Status.Should().Be(RecordingStatus.Requested,
            "eski yo'l o'z watchdog'i bilan yashaydi");
    }

    // ================================================================= xom fayllarni tozalash

    /// <summary>
    /// Tozalash aynan MUVAFFAQIYATLI yig'ishdan KEYIN bo'ladi, shuning
    /// uchun bu metodda <c>IsFinished</c> darvozasi ATAYLAB yo'q.
    /// </summary>
    [Fact]
    public void MarkRawPurged_WorksAfterCompletion_AndKeepsTheFirstMoment()
    {
        var recording = Queued();
        recording.TryClaimComposition(Now, Lease);
        recording.MarkCompositionCompleted(10, 20, Now, Now);

        recording.MarkRawPurged(Now.AddMinutes(2));
        recording.MarkRawPurged(Now.AddDays(1));

        recording.RawPurgedAt.Should().Be(Now.AddMinutes(2));
    }
}
