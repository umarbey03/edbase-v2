using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Recordings;

/// <summary>
/// Dars yozuvining HOLAT MASHINASI.
///
/// ★ NIMA UCHUN BU TESTLAR ENG MUHIMLARIDAN: yozuv holatini uchta
/// mustaqil manba o'zgartiradi — ustoz tugmasi, LiveKit webhook'i va
/// watchdog. Ularning har biri KECHIKKAN yoki TAKRORIY hodisa bilan
/// kelishi mumkin. Agar qoida servis qatlamida bo'lganda, uchta yo'lning
/// birida u albatta buzilardi.
/// </summary>
public sealed class SessionRecordingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static SessionRecording New() =>
        new() { SessionId = 1, ObjectKey = "recordings/2026-07/1/abc.mp4" };

    // ================================================================= boshlash

    [Fact]
    public void New_StartsAsRequested_SoTheWatchdogCanSeeIt()
    {
        var recording = New();

        recording.Status.Should().Be(RecordingStatus.Requested);
        recording.IsPending.Should().BeTrue();
        recording.IsFinished.Should().BeFalse();
        recording.IsPlayable.Should().BeFalse();
    }

    [Fact]
    public void BeginAttempt_CountsTheAttempt_BeforeTheExternalCall()
    {
        var recording = New();

        recording.BeginAttempt(Now);

        recording.Attempts.Should().Be(1);
        recording.LastAttemptAt.Should().Be(Now);
    }

    /// <summary>
    /// ★ Yakunlangan yozuvni qayta boshlash — DASTURCHI XATOSI, jimgina
    /// e'tiborsizlik emas: u omborda tayyor turgan faylning kalitini
    /// ustidan yozib yuborardi.
    /// </summary>
    [Fact]
    public void BeginAttempt_OnFinishedRecording_Throws()
    {
        var recording = New();
        recording.MarkCompleted(null, 10, 20, Now, Now);

        var act = () => recording.BeginAttempt(Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkStarting_ClearsThePreviousAttemptError()
    {
        var recording = New();
        recording.RecordAttemptError("Egress javob bermadi.", Now);

        recording.MarkStarting("EG_1", Now);

        recording.Status.Should().Be(RecordingStatus.Starting);
        recording.EgressId.Should().Be("EG_1");
        recording.Error.Should().BeNull("oldingi urinishning sababi eskirdi");
    }

    /// <summary>Kech kelgan "boshlandi" javobi YAKUNNI buzmasligi kerak.</summary>
    [Fact]
    public void MarkStarting_AfterCompletion_IsIgnored()
    {
        var recording = New();
        recording.MarkCompleted(null, 1, 1, Now, Now);

        recording.MarkStarting("EG_late", Now);

        recording.Status.Should().Be(RecordingStatus.Completed);
        recording.EgressId.Should().BeNull();
    }

    // ================================================================= faollashish

    /// <summary>
    /// ★ IDEMPOTENTLIK: LiveKit <c>egress_started</c> ni qayta yuborishi
    /// mumkin — BIRINCHI boshlanish payti saqlanib qolishi shart, aks
    /// holda videoning davomiyligi qisqarib ko'rinardi.
    /// </summary>
    [Fact]
    public void MarkActive_Twice_KeepsTheFirstStartTime()
    {
        var recording = New();
        var later = Now.AddMinutes(3);

        recording.MarkActive(Now, Now);
        recording.MarkActive(later, later);

        recording.Status.Should().Be(RecordingStatus.Active);
        recording.StartedAt.Should().Be(Now);
    }

    // ================================================================= yakunlash

    [Fact]
    public void MarkCompleted_StoresTheKeySizeAndDuration()
    {
        var recording = New();
        var endedAt = Now.AddMinutes(80);

        recording.MarkCompleted("recordings/2026-07/1/real.mp4", 512_000, 4800, endedAt, endedAt);

        recording.Status.Should().Be(RecordingStatus.Completed);
        recording.ObjectKey.Should().Be("recordings/2026-07/1/real.mp4");
        recording.SizeBytes.Should().Be(512_000);
        recording.DurationSeconds.Should().Be(4800);
        recording.EndedAt.Should().Be(endedAt);
        recording.IsPlayable.Should().BeTrue();
        recording.IsFinished.Should().BeTrue();
    }

    /// <summary>
    /// Egress kalitni qaytarmasa BIZ tanlagan kalit qoladi — u Egress'ga
    /// <c>filepath</c> sifatida shablonsiz berilgan, ya'ni ular AYNI.
    /// </summary>
    [Fact]
    public void MarkCompleted_WithoutKey_KeepsOurOwnKey()
    {
        var recording = New();
        var original = recording.ObjectKey;

        recording.MarkCompleted(null, null, null, Now, Now);

        recording.ObjectKey.Should().Be(original);
    }

    /// <summary>
    /// 🔴 ENG MUHIM QOIDA: tugallangan yozuv HECH QANDAY hodisa bilan
    /// orqaga qaytmaydi. Fayl allaqachon omborda va uni o'quvchilar
    /// ochayotgan bo'lishi mumkin.
    /// </summary>
    [Fact]
    public void MarkFailed_AfterCompletion_DoesNothing()
    {
        var recording = New();
        recording.MarkCompleted("recordings/final.mp4", 100, 10, Now, Now);

        recording.MarkFailed("Kech kelgan xato hodisasi", Now.AddMinutes(5));

        recording.Status.Should().Be(RecordingStatus.Completed);
        recording.Error.Should().BeNull();
        recording.ObjectKey.Should().Be("recordings/final.mp4");
    }

    [Fact]
    public void MarkCompleted_Twice_KeepsTheFirstResult()
    {
        var recording = New();

        recording.MarkCompleted("recordings/first.mp4", 100, 10, Now, Now);
        recording.MarkCompleted("recordings/second.mp4", 999, 99, Now.AddMinutes(1), Now.AddMinutes(1));

        recording.ObjectKey.Should().Be("recordings/first.mp4");
        recording.SizeBytes.Should().Be(100);
    }

    // ================================================================= xato

    [Fact]
    public void MarkFailed_IsTerminalAndKeepsTheReason()
    {
        var recording = New();

        recording.MarkFailed("Xona topilmadi.", Now);

        recording.Status.Should().Be(RecordingStatus.Failed);
        recording.Error.Should().Be("Xona topilmadi.");
        recording.IsFinished.Should().BeTrue();
        recording.IsPlayable.Should().BeFalse();
    }

    /// <summary>
    /// Uzun javob (S3 XML yoki twirp steki) BAZAGA to'liq tushmasligi
    /// kerak — u LOGDA bo'ladi, bu yerda faqat qisqa sabab.
    /// </summary>
    [Fact]
    public void MarkFailed_TrimsAVeryLongReason()
    {
        var recording = New();

        recording.MarkFailed(new string('x', SessionRecording.MaxErrorLength + 500), Now);

        recording.Error.Should().HaveLength(SessionRecording.MaxErrorLength);
    }

    [Fact]
    public void MarkFailed_WithEmptyReason_StillExplainsSomething()
    {
        var recording = New();

        recording.MarkFailed("   ", Now);

        recording.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Urinish xatosi YAKUNIY EMAS: holat <c>Requested</c> bo'lib qoladi,
    /// ya'ni watchdog uni qayta uradi.
    /// </summary>
    [Fact]
    public void RecordAttemptError_KeepsTheRecordingRetryable()
    {
        var recording = New();
        recording.BeginAttempt(Now);

        recording.RecordAttemptError("Ulanib bo'lmadi.", Now);

        recording.Status.Should().Be(RecordingStatus.Requested);
        recording.IsFinished.Should().BeFalse();
        recording.CanRetry(maxAttempts: 5).Should().BeTrue();
    }

    // ================================================================= qayta urinish

    [Fact]
    public void CanRetry_IsFalse_WhenTheAttemptLimitIsReached()
    {
        var recording = New();

        for (var i = 0; i < 3; i++)
            recording.BeginAttempt(Now);

        recording.CanRetry(maxAttempts: 3).Should().BeFalse();
        recording.CanRetry(maxAttempts: 4).Should().BeTrue();
    }

    /// <summary>Faqat <c>Requested</c> qayta urinadi — `Active` ni qayta boshlash fayl dublikatini yasardi.</summary>
    [Fact]
    public void CanRetry_IsFalse_WhenTheRecordingIsAlreadyRunning()
    {
        var recording = New();
        recording.MarkStarting("EG_1", Now);
        recording.MarkActive(Now, Now);

        recording.CanRetry(maxAttempts: 5).Should().BeFalse();
    }

    // ================================================================= to'xtatish

    /// <summary>
    /// To'xtatish so'rovi FAQAT BIR MARTA belgilanadi: watchdog har
    /// yurishda `StopEgress` ni qayta yuborsa LiveKit xato berardi va log
    /// bekorga to'lardi.
    /// </summary>
    [Fact]
    public void MarkStopRequested_KeepsTheFirstTimestamp()
    {
        var recording = New();

        recording.MarkStopRequested(Now);
        recording.MarkStopRequested(Now.AddMinutes(5));

        recording.StopRequestedAt.Should().Be(Now);
    }

    // ================================================================= R5: ko'rinish

    /// <summary>
    /// 🔴 ENG MUHIM YAGONA TEST: standart qiymat <c>true</c>.
    ///
    /// U shunchaki xossani emas, MIGRATSIYA QARORINI qulflaydi. Bu qiymat
    /// <c>false</c> ga o'zgartirilsa, deploy kunida HAR BIR o'quvchining
    /// "Dars yozuvlari" bo'limi bo'shab qolardi va buni hech kim
    /// nosozlikdan ajrata olmasdi. Sabab batafsil
    /// <c>SessionRecording.IsVisibleToStudents</c> izohida.
    /// </summary>
    [Fact]
    public void New_IsVisibleToStudents_SoExistingBehaviourIsPreserved()
    {
        var recording = New();

        recording.IsVisibleToStudents.Should().BeTrue();
        recording.VisibilityChangedById.Should().BeNull();
        recording.VisibilityChangedAt.Should().BeNull();
    }

    /// <summary>
    /// Yashirish HAR QANDAY holatda ishlaydi — hali boshlanmagan yozuvda ham.
    /// Bu <see cref="ShowToStudents_OnUnfinishedRecording_Throws"/> bilan
    /// ATAYLAB assimetrik: "ko'rsatma" deyishning yo'li hech qachon yopiq
    /// bo'lmasligi kerak.
    /// </summary>
    [Fact]
    public void HideFromStudents_WorksInAnyState_BecauseItIsTheConsentEscapeHatch()
    {
        var recording = New();

        recording.HideFromStudents(actorId: 7, Now);

        recording.IsVisibleToStudents.Should().BeFalse();
        recording.VisibilityChangedById.Should().Be(7);
        recording.VisibilityChangedAt.Should().Be(Now);
        recording.UpdatedAt.Should().Be(Now);
    }

    /// <summary>
    /// ★ <c>Test.Publish()</c> DAGI AYNI NAQSH: u bo'sh testni, bu esa
    /// tayyor bo'lmagan yozuvni rad etadi. Aks holda ro'yxatda bosilganda
    /// DOIM xato beradigan qator paydo bo'lardi.
    /// </summary>
    [Fact]
    public void ShowToStudents_OnUnfinishedRecording_Throws()
    {
        var recording = New();
        recording.HideFromStudents(actorId: 7, Now);

        var act = () => recording.ShowToStudents(actorId: 7, Now);

        act.Should().Throw<DomainException>();
        recording.IsVisibleToStudents.Should().BeFalse();
    }

    [Fact]
    public void ShowToStudents_OnCompletedRecording_RecordsWhoReopenedIt()
    {
        var recording = New();
        recording.MarkCompleted("recordings/x.mp4", 10, 20, Now, Now);
        recording.HideFromStudents(actorId: 7, Now);

        recording.ShowToStudents(actorId: 9, Now.AddHours(1));

        recording.IsVisibleToStudents.Should().BeTrue();

        // Kim qayta ochgani SAQLANADI: "o'quv bo'limi yopganini faqat
        // o'quv bo'limi ochadi" qoidasi AYNAN shu maydonga tayanadi.
        recording.VisibilityChangedById.Should().Be(9);
        recording.VisibilityChangedAt.Should().Be(Now.AddHours(1));
    }

    /// <summary>
    /// Yashirilgan yozuv <c>IsPlayable</c> BO'LIB QOLADI.
    ///
    /// ★ BU ATAYLAB: <c>IsPlayable</c> — "fayl omborda bormi" degan TEXNIK
    /// savol, ko'rinish esa SIYOSAT. Ikkalasini bitta xossaga yig'ish
    /// yashirilgan yozuvni xodim uchun ham ochilmaydigan qilib qo'yardi —
    /// holbuki o'quv bo'limi aynan uni ko'rib, xulosa yozishi kerak.
    /// </summary>
    [Fact]
    public void HideFromStudents_DoesNotAffectIsPlayable_BecauseStaffStillNeedIt()
    {
        var recording = New();
        recording.MarkCompleted("recordings/x.mp4", 10, 20, Now, Now);

        recording.HideFromStudents(actorId: 7, Now);

        recording.IsPlayable.Should().BeTrue();
    }
}
