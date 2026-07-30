using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="Attendance"/> qoidalari — Domain'dagi ENG NOZIK fayl.
///
/// Eski tizimda faqat bitta `joined_at` maydoni bor edi va u faqat birinchi
/// kirishda yozilardi. Chiqishda esa `duration += now - joined_at` qilinardi.
/// Zaif internetda qayta ulangan o'quvchining vaqti har safar dars BOSHIDAN
/// qayta qo'shilar va 80 daqiqalik darsda 125 daqiqa chiqib qolardi.
/// Shu sababli `FirstJoinAt` (tarix) va `LastJoinAt` (joriy seans) ajratilgan.
/// </summary>
public class AttendanceTests
{
    private static readonly DateTimeOffset At1900 =
        new(2026, 3, 10, 19, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset AtMinute(int minutesAfter1900) => At1900.AddMinutes(minutesAfter1900);

    private static Attendance NewAttendance() => new()
    {
        SessionId = 1,
        StudentId = 42,
    };

    // ------------------------------------------------------------------ ★ qayta ulanish stsenariysi

    /// <summary>
    /// ★ ENG MUHIM TEST — eski tizimning davomat "shishishi" bugini ushlaydi.
    ///
    /// Stsenariy: 19:00 kirdi → 19:10 chiqdi → 19:20 qayta kirdi → 19:30 chiqdi.
    /// Xonada HAQIQATDA 10 + 10 = 20 daqiqa (1200 soniya) bo'lgan.
    /// Eski mantiqda ikkinchi chiqishda `19:30 − 19:00 = 30 daqiqa` qo'shilar va
    /// jami 40 daqiqa (2400 soniya) chiqardi — ya'ni ikki barobar.
    /// </summary>
    [Fact]
    public void RegisterJoinAndLeave_WithReconnect_CountsOnlyTimeActuallyInsideTheRoom()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(AtMinute(0));    // 19:00
        attendance.RegisterLeave(AtMinute(10));  // 19:10
        attendance.RegisterJoin(AtMinute(20));   // 19:20 — qayta ulandi
        attendance.RegisterLeave(AtMinute(30));  // 19:30

        attendance.DurationSeconds.Should().Be(1200);
    }

    /// <summary>★ `FirstJoinAt` tarix uchun — qayta ulanish uni o'zgartirmaydi.</summary>
    [Fact]
    public void RegisterJoin_AfterReconnect_KeepsFirstJoinAtUnchanged()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));
        attendance.RegisterJoin(AtMinute(20));

        attendance.FirstJoinAt.Should().Be(AtMinute(0));
    }

    /// <summary>★ `LastJoinAt` esa har ulanishda eng yangi vaqtga suriladi.</summary>
    [Fact]
    public void RegisterJoin_AfterReconnect_UpdatesLastJoinAtToNewestJoin()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));
        attendance.RegisterJoin(AtMinute(20));

        attendance.LastJoinAt.Should().Be(AtMinute(20));
    }

    [Fact]
    public void RegisterJoinAndLeave_WithThreeReconnects_SumsOnlyClosedSessions()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(5));    // 5 daq
        attendance.RegisterJoin(AtMinute(15));
        attendance.RegisterLeave(AtMinute(25));   // 10 daq
        attendance.RegisterJoin(AtMinute(40));
        attendance.RegisterLeave(AtMinute(43));   // 3 daq

        attendance.DurationSeconds.Should().Be(18 * 60);
    }

    // ------------------------------------------------------------------ RegisterJoin

    [Fact]
    public void RegisterJoin_OnFreshRecord_SetsFirstJoinAt()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(At1900);

        attendance.FirstJoinAt.Should().Be(At1900);
    }

    [Fact]
    public void RegisterJoin_OnFreshRecord_ChangesStatusFromAbsentToPresent()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(At1900);

        attendance.Status.Should().Be(AttendanceStatus.Present);
    }

    [Fact]
    public void RegisterJoin_AfterLeaving_ClearsLeftAt()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));

        attendance.RegisterJoin(AtMinute(20));

        attendance.LeftAt.Should().BeNull();
    }

    [Fact]
    public void RegisterJoin_DoesNotAddTimeByItself()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(At1900);

        attendance.DurationSeconds.Should().Be(0);
    }

    // ------------------------------------------------------------------ RegisterLeave

    /// <summary>
    /// Chiqish hodisasi kirishsiz kelishi mumkin (webhook takrorlanishi,
    /// SignalR `OnDisconnected` ikki marta ishlashi). Bu jamini buzmasligi kerak.
    /// </summary>
    [Fact]
    public void RegisterLeave_WithoutAnyJoin_LeavesDurationAtZero()
    {
        var attendance = NewAttendance();

        attendance.RegisterLeave(AtMinute(30));

        attendance.DurationSeconds.Should().Be(0);
    }

    [Fact]
    public void RegisterLeave_WithoutOpenSession_DoesNotCorruptExistingTotal()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));   // jami = 600

        attendance.RegisterLeave(AtMinute(40));   // ochiq seans yo'q

        attendance.DurationSeconds.Should().Be(600);
    }

    /// <summary>Ikki marta chiqish signali kelsa vaqt ikki barobar qo'shilmasligi kerak.</summary>
    [Fact]
    public void RegisterLeave_CalledTwice_DoesNotDoubleCount()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));

        attendance.RegisterLeave(AtMinute(10));
        attendance.RegisterLeave(AtMinute(10));

        attendance.DurationSeconds.Should().Be(600);
    }

    [Fact]
    public void RegisterLeave_ClosesTheOpenSession()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));

        attendance.RegisterLeave(AtMinute(10));

        attendance.LastJoinAt.Should().BeNull();
    }

    [Fact]
    public void RegisterLeave_SetsLeftAt()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));

        attendance.RegisterLeave(AtMinute(10));

        attendance.LeftAt.Should().Be(AtMinute(10));
    }

    [Fact]
    public void RegisterLeave_AtTheSameInstantAsJoin_AddsNothing()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(At1900);

        attendance.RegisterLeave(At1900);

        attendance.DurationSeconds.Should().Be(0);
    }

    /// <summary>
    /// Server soati orqaga surilsa (NTP tuzatishi) manfiy vaqt qo'shilmasligi kerak.
    /// </summary>
    [Fact]
    public void RegisterLeave_WhenClockMovedBackwards_DoesNotSubtractTime()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(10));

        attendance.RegisterLeave(AtMinute(5));

        attendance.DurationSeconds.Should().Be(0);
    }

    // ------------------------------------------------------------------ Finalize: ochiq seans

    /// <summary>
    /// O'quvchi xonadan chiqmasdan dars tugadi — ochiq seans yopilishi va
    /// vaqti hisobga olinishi kerak, aks holda davomat kam ko'rsatiladi.
    /// </summary>
    [Fact]
    public void Finalize_WithStillOpenSession_AddsTheRemainingTime()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));

        attendance.Finalize(AtMinute(30));

        attendance.DurationSeconds.Should().Be(1800);
    }

    [Fact]
    public void Finalize_WithStillOpenSession_ClosesIt()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));

        attendance.Finalize(AtMinute(30));

        attendance.LastJoinAt.Should().BeNull();
    }

    [Fact]
    public void Finalize_AfterReconnectWithOpenSession_SumsAllSegments()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));   // 10 daq
        attendance.RegisterJoin(AtMinute(20));    // ochiq qoldi

        attendance.Finalize(AtMinute(35));        // + 15 daq

        attendance.DurationSeconds.Should().Be(25 * 60);
    }

    [Fact]
    public void Finalize_CalledTwice_DoesNotAddExtraTime()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.Finalize(AtMinute(30));

        attendance.Finalize(AtMinute(60));

        attendance.DurationSeconds.Should().Be(1800);
    }

    // ------------------------------------------------------------------ Finalize: holat chegaralari

    [Fact]
    public void Finalize_WithZeroSeconds_SetsAbsent()
    {
        var attendance = NewAttendance();

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Absent);
    }

    [Fact]
    public void Finalize_ForStudentWhoJoinedButNeverStayed_SetsAbsent()
    {
        // Kirdi va o'sha zahoti chiqdi → 0 soniya → Absent.
        var attendance = NewAttendance();
        attendance.RegisterJoin(At1900);
        attendance.RegisterLeave(At1900);

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Absent);
    }

    [Fact]
    public void Finalize_WithOneSecond_SetsPartial()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(At1900);
        attendance.RegisterLeave(At1900.AddSeconds(1));

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Partial);
    }

    /// <summary>Chegara: 899 soniya — hali "Partial".</summary>
    [Fact]
    public void Finalize_OneSecondBelowMinFullAttendance_SetsPartial()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(At1900);
        attendance.RegisterLeave(At1900.AddSeconds(Attendance.MinFullAttendanceSeconds - 1));

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Partial);
    }

    /// <summary>Chegara: aynan 900 soniya — allaqachon "Present" (>= qoidasi).</summary>
    [Fact]
    public void Finalize_ExactlyAtMinFullAttendance_SetsPresent()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(At1900);
        attendance.RegisterLeave(At1900.AddSeconds(Attendance.MinFullAttendanceSeconds));

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Present);
    }

    [Fact]
    public void Finalize_AboveMinFullAttendance_SetsPresent()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(80));

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Present);
    }

    /// <summary>
    /// Qayta ulanish bilan yig'ilgan vaqt ham chegara hisobiga to'g'ri kirishi kerak:
    /// 8 + 8 = 16 daqiqa > 15 daqiqa → Present.
    /// </summary>
    [Fact]
    public void Finalize_AfterReconnectCrossingTheThreshold_SetsPresent()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(8));
        attendance.RegisterJoin(AtMinute(20));
        attendance.RegisterLeave(AtMinute(28));

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Present);
    }

    [Fact]
    public void MinFullAttendanceSeconds_IsFifteenMinutes()
    {
        Attendance.MinFullAttendanceSeconds.Should().Be(900);
    }

    // ------------------------------------------------------------------ Finalize: qo'lda qo'yilgan holat

    /// <summary>
    /// Ustoz/o'quv bo'limi qo'lda "Late" qo'ygan bo'lsa, avto-hisob uni
    /// bosib ketmasligi kerak — aks holda qo'lda tuzatish ma'nosiz bo'ladi.
    /// </summary>
    [Fact]
    public void Finalize_WhenStatusIsManual_DoesNotOverwriteStatus()
    {
        var attendance = NewAttendance();
        attendance.Status = AttendanceStatus.Late;
        attendance.IsManual = true;

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Late);
    }

    [Fact]
    public void Finalize_WhenManualAndStudentWasPresent_StillDoesNotOverwriteStatus()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(80));
        attendance.Status = AttendanceStatus.Absent;   // ustoz qo'lda "kelmagan" deb belgiladi
        attendance.IsManual = true;

        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Absent);
    }

    /// <summary>
    /// Qo'lda qo'yilgan HOLAT tegilmaydi, lekin FAKTIK vaqt baribir yozilishi
    /// kerak — hisobotlarda "necha daqiqa xonada bo'ldi" ko'rsatiladi.
    /// </summary>
    [Fact]
    public void Finalize_WhenManual_StillClosesTheOpenSessionAndKeepsDuration()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.IsManual = true;

        attendance.Finalize(AtMinute(30));

        attendance.DurationSeconds.Should().Be(1800);
    }

    // ------------------------------------------------------------------ ★ RegisterJoin va IsManual (regressiya)

    /// <summary>
    /// ★ REGRESSIYA. Ilgari `RegisterJoin()` `IsManual` ni umuman tekshirmasdi,
    /// `Finalize()` esa tekshirardi — va aynan shu assimetriya tuzoq edi:
    ///
    ///   1. Ustoz o'quvchini qo'lda "Absent" deb belgilaydi (IsManual = true).
    ///   2. O'quvchi qayta ulanadi (yoki SignalR `JoinSession` takroran keladi).
    ///   3. `RegisterJoin()` statusni jimgina "Present" ga o'zgartiradi.
    ///   4. `Finalize()` esa AYNAN IsManual tufayli qayta hisoblamaydi.
    ///
    /// Natijada ustozning qarori bekor bo'lib, noto'g'ri "Present" abadiy
    /// qolib ketardi — `IsManual` bayrog'i esa aynan shundan himoya qilish
    /// uchun mavjud edi.
    /// </summary>
    [Fact]
    public void RegisterJoin_WhenStatusIsManual_DoesNotOverwriteStatus()
    {
        var attendance = NewAttendance();
        attendance.Status = AttendanceStatus.Absent;
        attendance.IsManual = true;

        attendance.RegisterJoin(AtMinute(20));

        attendance.Status.Should().Be(AttendanceStatus.Absent);
    }

    /// <summary>
    /// ★ Tuzatishning IKKINCHI yarmi: HOLAT tegilmaydi, lekin VAQT belgilari
    /// baribir yangilanadi — davomiylik va "qachon ulandi" tarixi faktik
    /// ma'lumot, ustozning bahosi emas.
    /// </summary>
    [Fact]
    public void RegisterJoin_WhenStatusIsManual_StillUpdatesLastJoinAt()
    {
        var attendance = NewAttendance();
        attendance.IsManual = true;

        attendance.RegisterJoin(AtMinute(20));

        attendance.LastJoinAt.Should().Be(AtMinute(20));
    }

    [Fact]
    public void RegisterJoin_WhenStatusIsManual_StillSetsFirstJoinAt()
    {
        var attendance = NewAttendance();
        attendance.IsManual = true;

        attendance.RegisterJoin(AtMinute(20));

        attendance.FirstJoinAt.Should().Be(AtMinute(20));
    }

    [Fact]
    public void RegisterJoin_WhenStatusIsManual_StillClearsLeftAt()
    {
        var attendance = NewAttendance();
        attendance.IsManual = true;
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));

        attendance.RegisterJoin(AtMinute(20));

        attendance.LeftAt.Should().BeNull();
    }

    [Fact]
    public void RegisterJoin_WhenStatusIsManual_StillAccumulatesDurationOnLeave()
    {
        var attendance = NewAttendance();
        attendance.IsManual = true;

        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));

        attendance.DurationSeconds.Should().Be(600);
    }

    /// <summary>
    /// ★ To'liq stsenariy — bug aynan shu ketma-ketlikda yuzaga kelardi:
    /// ustoz "Absent" qo'ydi → o'quvchi qayta ulanib 20 daqiqa (chegaradan ko'p)
    /// o'tirdi → dars yakunlandi. Avto-hisob "Present" degan bo'lardi, lekin
    /// qo'lda qo'yilgan baho ustun turishi kerak.
    /// </summary>
    [Fact]
    public void Finalize_AfterManualAbsentThenReconnect_KeepsStatusAbsent()
    {
        var attendance = NewAttendance();
        attendance.Status = AttendanceStatus.Absent;
        attendance.IsManual = true;

        attendance.RegisterJoin(AtMinute(10));
        attendance.RegisterLeave(AtMinute(30));
        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Absent);
    }

    /// <summary>
    /// Qarama-qarshi tomon: `IsManual = false` bo'lganda avvalgi xatti-harakat
    /// AYNAN o'zgarishsiz qolishi kerak — tuzatish oddiy yo'lni buzmadi.
    /// </summary>
    [Fact]
    public void RegisterJoin_WhenIsManualIsFalse_StillPromotesAbsentToPresent()
    {
        var attendance = NewAttendance();
        attendance.Status = AttendanceStatus.Absent;
        attendance.IsManual = false;

        attendance.RegisterJoin(AtMinute(20));

        attendance.Status.Should().Be(AttendanceStatus.Present);
    }

    /// <summary>
    /// Qo'lda "Absent" qo'yilgan o'quvchi qayta ulansa ham avto-hisob uni
    /// "Partial" ga ham surib qo'ymasligi kerak (chegaradan kam o'tirgan holat).
    /// </summary>
    [Fact]
    public void Finalize_AfterManualAbsentWithShortReconnect_KeepsStatusAbsent()
    {
        var attendance = NewAttendance();
        attendance.Status = AttendanceStatus.Absent;
        attendance.IsManual = true;

        attendance.RegisterJoin(AtMinute(10));
        attendance.RegisterLeave(AtMinute(12));
        attendance.Finalize(AtMinute(80));

        attendance.Status.Should().Be(AttendanceStatus.Absent);
    }

    // ------------------------------------------------------------------ UpdatedAt (regressiya)

    /// <summary>
    /// `Attendance` ilgari `UpdatedAt` ni HECH QACHON qo'ymasdi (`LiveSession`
    /// dan farqli o'laroq). Bu audit izini yo'qotardi: "bu yozuv qachon
    /// o'zgardi?" degan savolga javob yo'q edi — davomat nizolarida esa
    /// aynan shu savol so'raladi.
    /// </summary>
    [Fact]
    public void RegisterJoin_SetsUpdatedAt()
    {
        var attendance = NewAttendance();

        attendance.RegisterJoin(AtMinute(0));

        attendance.UpdatedAt.Should().Be(AtMinute(0));
    }

    [Fact]
    public void RegisterLeave_SetsUpdatedAt()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));

        attendance.RegisterLeave(AtMinute(10));

        attendance.UpdatedAt.Should().Be(AtMinute(10));
    }

    [Fact]
    public void Finalize_SetsUpdatedAt()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));

        attendance.Finalize(AtMinute(80));

        attendance.UpdatedAt.Should().Be(AtMinute(80));
    }

    [Fact]
    public void Finalize_WithStillOpenSession_SetsUpdatedAt()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));

        attendance.Finalize(AtMinute(30));

        attendance.UpdatedAt.Should().Be(AtMinute(30));
    }

    [Fact]
    public void RegisterJoin_OnReconnect_MovesUpdatedAtForward()
    {
        var attendance = NewAttendance();
        attendance.RegisterJoin(AtMinute(0));
        attendance.RegisterLeave(AtMinute(10));

        attendance.RegisterJoin(AtMinute(20));

        attendance.UpdatedAt.Should().Be(AtMinute(20));
    }

    [Fact]
    public void RegisterJoin_WhenStatusIsManual_StillSetsUpdatedAt()
    {
        var attendance = NewAttendance();
        attendance.IsManual = true;

        attendance.RegisterJoin(AtMinute(20));

        attendance.UpdatedAt.Should().Be(AtMinute(20));
    }

    [Fact]
    public void UpdatedAt_OnUntouchedRecord_IsNull()
    {
        var attendance = NewAttendance();

        attendance.UpdatedAt.Should().BeNull();
    }
}
