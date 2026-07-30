using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="LiveSession"/> biznes qoidalari.
///
/// Barcha testlar QAT'IY (fixed) vaqt bilan ishlaydi. `DateTimeOffset.UtcNow`
/// tekshiruvda ishlatilmaydi — aks holda test "flaky" bo'lib qoladi va CI'da
/// tasodifan qizarib turadi.
/// </summary>
public class LiveSessionTests
{
    private static readonly DateTimeOffset Scheduled =
        new(2026, 3, 10, 19, 0, 0, TimeSpan.Zero);

    private const int PlannedMinutes = 80;

    private static LiveSession NewSession(SessionStatus status = SessionStatus.Scheduled) => new()
    {
        GroupId = 1,
        HostId = 7,
        Title = "ATF — 12-dars",
        Type = SessionType.Teacher,
        Status = status,
        RoomName = "s-20260310185500-0a1b2c3d",
        ScheduledStart = Scheduled,
        ScheduledEnd = Scheduled.AddMinutes(PlannedMinutes),
    };

    /// <summary>Boshlangan (Live) dars: 19:00 da boshlangan.</summary>
    private static LiveSession LiveSessionStartedAtScheduledTime()
    {
        var session = NewSession();
        session.Start(Scheduled);
        return session;
    }

    // ------------------------------------------------------------------ Start: vaqt oynasi

    [Fact]
    public void Start_OneSecondBeforeLeadWindow_ThrowsDomainException()
    {
        var session = NewSession();
        var tooEarly = Scheduled.AddMinutes(-LiveSession.StartLeadMinutes).AddSeconds(-1);

        var act = () => session.Start(tooEarly);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Start_ExactlyAtLeadWindowBoundary_SetsStatusToLive()
    {
        var session = NewSession();
        var boundary = Scheduled.AddMinutes(-LiveSession.StartLeadMinutes);

        session.Start(boundary);

        session.Status.Should().Be(SessionStatus.Live);
    }

    [Fact]
    public void Start_ExactlyAtLeadWindowBoundary_SetsActualStartToThatInstant()
    {
        var session = NewSession();
        var boundary = Scheduled.AddMinutes(-LiveSession.StartLeadMinutes);

        session.Start(boundary);

        session.ActualStart.Should().Be(boundary);
    }

    [Fact]
    public void Start_LongAfterScheduledStart_IsAllowed()
    {
        var session = NewSession();

        var act = () => session.Start(Scheduled.AddHours(3));

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_OnScheduledSession_SetsUpdatedAt()
    {
        var session = NewSession();

        session.Start(Scheduled);

        session.UpdatedAt.Should().Be(Scheduled);
    }

    // ------------------------------------------------------------------ Start: ★ ActualStart qayta yozilmasligi

    /// <summary>
    /// ★ ENG MUHIM TEST. Eski tizimda `actual_start = now` shartsiz yozilardi.
    /// Ustoz "Boshlash" tugmasini ikkinchi marta bosса (masalan brauzer qotib
    /// qolgani uchun sahifani yangilagach), dars muddati yana 80 daqiqaga
    /// surilardi. Natijada 10 daqiqalik uzaytirish chegarasi butunlay ma'nosiz
    /// bo'lib qolar va dars xohlagancha cho'zilaverardi.
    /// Endi qoida: `ActualStart ??= now` — faqat BIRINCHI boshlashda yoziladi.
    /// </summary>
    [Fact]
    public void Start_CalledTwice_DoesNotOverwriteActualStart()
    {
        var session = NewSession();
        var firstStart = Scheduled;
        var secondStart = Scheduled.AddMinutes(45);

        session.Start(firstStart);
        session.Start(secondStart);

        session.ActualStart.Should().Be(firstStart);
    }

    /// <summary>
    /// ★ Yuqoridagi bugning bevosita oqibati: agar `ActualStart` surilsa,
    /// `EndsAt` ham suriladi va avto-yakunlash hech qachon ishlamaydi.
    /// </summary>
    [Fact]
    public void Start_CalledTwice_DoesNotMoveEndsAt()
    {
        var session = NewSession();
        session.Start(Scheduled);
        var endsAtAfterFirstStart = session.EndsAt;

        session.Start(Scheduled.AddMinutes(45));

        session.EndsAt.Should().Be(endsAtAfterFirstStart);
    }

    /// <summary>
    /// ★ Uzaytirish chegarasi qayta boshlash orqali aylanib o'tilmasligi kerak:
    /// 10 daqiqa to'liq ishlatilgandan keyin qayta "Boshlash" bosilsa ham
    /// yakunlanish vaqti 80 + 10 daqiqadan nariga o'tmaydi.
    /// </summary>
    [Fact]
    public void Start_CalledAgainAfterFullExtension_KeepsEndsAtWithinCap()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.Extend(LiveSession.MaxExtendMinutes, Scheduled.AddMinutes(70));

        session.Start(Scheduled.AddMinutes(75));

        session.EndsAt.Should().Be(
            Scheduled.AddMinutes(PlannedMinutes + LiveSession.MaxExtendMinutes));
    }

    [Fact]
    public void Start_WhenAlreadyLive_KeepsStatusLive()
    {
        var session = LiveSessionStartedAtScheduledTime();

        session.Start(Scheduled.AddMinutes(10));

        session.Status.Should().Be(SessionStatus.Live);
    }

    // ------------------------------------------------------------------ Start: taqiqlangan holatlar

    [Fact]
    public void Start_OnEndedSession_ThrowsDomainException()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.End(Scheduled.AddMinutes(PlannedMinutes));

        var act = () => session.Start(Scheduled.AddMinutes(PlannedMinutes + 1));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Start_OnCancelledSession_ThrowsDomainException()
    {
        var session = NewSession(SessionStatus.Cancelled);

        var act = () => session.Start(Scheduled);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Start_OnEndedSession_DoesNotChangeStatus()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.End(Scheduled.AddMinutes(PlannedMinutes));

        var act = () => session.Start(Scheduled.AddMinutes(PlannedMinutes + 1));
        act.Should().Throw<DomainException>();

        session.Status.Should().Be(SessionStatus.Ended);
    }

    // ------------------------------------------------------------------ End

    [Fact]
    public void End_OnLiveSession_SetsStatusToEnded()
    {
        var session = LiveSessionStartedAtScheduledTime();

        session.End(Scheduled.AddMinutes(PlannedMinutes));

        session.Status.Should().Be(SessionStatus.Ended);
    }

    [Fact]
    public void End_OnLiveSession_SetsActualEnd()
    {
        var session = LiveSessionStartedAtScheduledTime();
        var endedAt = Scheduled.AddMinutes(PlannedMinutes);

        session.End(endedAt);

        session.ActualEnd.Should().Be(endedAt);
    }

    /// <summary>
    /// `End()` idempotent: fon vazifasi (avto-yakunlash) va ustozning
    /// "Yakunlash" tugmasi bir vaqtda ishlashi mumkin — ikkinchisi birinchisining
    /// yozuvini buzmasligi kerak.
    /// </summary>
    [Fact]
    public void End_CalledTwice_KeepsActualEndFromFirstCall()
    {
        var session = LiveSessionStartedAtScheduledTime();
        var firstEnd = Scheduled.AddMinutes(PlannedMinutes);

        session.End(firstEnd);
        session.End(Scheduled.AddMinutes(PlannedMinutes + 30));

        session.ActualEnd.Should().Be(firstEnd);
    }

    [Fact]
    public void End_CalledTwice_DoesNotThrow()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.End(Scheduled.AddMinutes(PlannedMinutes));

        var act = () => session.End(Scheduled.AddMinutes(PlannedMinutes + 30));

        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------ End: ★ bekor qilingan dars (regressiya)

    /// <summary>
    /// ★ REGRESSIYA. `Start()` bekor qilingan darsni rad etardi, `End()` esa yo'q.
    /// Natijada `POST /live-sessions/{id}/end` bekor qilingan darsni jimgina
    /// "Ended" ga o'tkazib, bekor qilish yozuvini yo'q qilardi va `Finalize()`
    /// umuman bo'lmagan dars uchun davomat yozardi. Xuddi shu xavf avto-yakunlash
    /// fon vazifasida ham bor edi.
    /// </summary>
    [Fact]
    public void End_OnCancelledSession_ThrowsDomainException()
    {
        var session = NewSession(SessionStatus.Cancelled);

        var act = () => session.End(Scheduled.AddMinutes(PlannedMinutes));

        act.Should().Throw<DomainException>();
    }

    /// <summary>★ Bekor qilingan holat SAQLANIB qolishi kerak — asosiy maqsad shu.</summary>
    [Fact]
    public void End_OnCancelledSession_KeepsStatusCancelled()
    {
        var session = NewSession(SessionStatus.Cancelled);

        var act = () => session.End(Scheduled.AddMinutes(PlannedMinutes));
        act.Should().Throw<DomainException>();

        session.Status.Should().Be(SessionStatus.Cancelled);
    }

    /// <summary>★ Bo'lmagan dars uchun yakunlanish vaqti yozilmasligi kerak.</summary>
    [Fact]
    public void End_OnCancelledSession_DoesNotSetActualEnd()
    {
        var session = NewSession(SessionStatus.Cancelled);

        var act = () => session.End(Scheduled.AddMinutes(PlannedMinutes));
        act.Should().Throw<DomainException>();

        session.ActualEnd.Should().BeNull();
    }

    [Fact]
    public void End_OnCancelledSession_DoesNotSetUpdatedAt()
    {
        var session = NewSession(SessionStatus.Cancelled);

        var act = () => session.End(Scheduled.AddMinutes(PlannedMinutes));
        act.Should().Throw<DomainException>();

        session.UpdatedAt.Should().BeNull();
    }

    // ------------------------------------------------------------------ End: boshqa holatlar hamon ishlaydi

    /// <summary>
    /// Boshlanmagan (Scheduled) darsni yakunlash MUMKIN bo'lib qolishi kerak:
    /// ustoz kelmagan darsni o'quv bo'limi yopadi, avto-yakunlash ham shunga tayanadi.
    /// </summary>
    [Fact]
    public void End_OnScheduledSession_SetsStatusToEnded()
    {
        var session = NewSession();

        session.End(Scheduled.AddMinutes(PlannedMinutes));

        session.Status.Should().Be(SessionStatus.Ended);
    }

    [Fact]
    public void End_OnScheduledSession_DoesNotThrow()
    {
        var session = NewSession();

        var act = () => session.End(Scheduled.AddMinutes(PlannedMinutes));

        act.Should().NotThrow();
    }

    [Fact]
    public void End_OnScheduledSession_SetsActualEnd()
    {
        var session = NewSession();
        var endedAt = Scheduled.AddMinutes(PlannedMinutes);

        session.End(endedAt);

        session.ActualEnd.Should().Be(endedAt);
    }

    [Fact]
    public void End_OnLiveSession_SetsUpdatedAt()
    {
        var session = LiveSessionStartedAtScheduledTime();
        var endedAt = Scheduled.AddMinutes(PlannedMinutes);

        session.End(endedAt);

        session.UpdatedAt.Should().Be(endedAt);
    }

    // ------------------------------------------------------------------ Extend

    [Theory]
    [InlineData(SessionStatus.Scheduled)]
    [InlineData(SessionStatus.Ended)]
    [InlineData(SessionStatus.Cancelled)]
    public void Extend_WhenSessionIsNotLive_ThrowsDomainException(SessionStatus status)
    {
        var session = NewSession(status);

        var act = () => session.Extend(5, Scheduled);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public void Extend_WithNonPositiveMinutes_ThrowsDomainException(int minutes)
    {
        var session = LiveSessionStartedAtScheduledTime();

        var act = () => session.Extend(minutes, Scheduled.AddMinutes(70));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Extend_WithinCap_ReturnsRequestedMinutes()
    {
        var session = LiveSessionStartedAtScheduledTime();

        var added = session.Extend(7, Scheduled.AddMinutes(70));

        added.Should().Be(7);
    }

    [Fact]
    public void Extend_WithinCap_AccumulatesExtendedMinutes()
    {
        var session = LiveSessionStartedAtScheduledTime();

        session.Extend(3, Scheduled.AddMinutes(70));
        session.Extend(4, Scheduled.AddMinutes(75));

        session.ExtendedMin.Should().Be(7);
    }

    [Fact]
    public void Extend_AboveCapInSingleCall_ReturnsOnlyTheAllowedRemainder()
    {
        var session = LiveSessionStartedAtScheduledTime();

        var added = session.Extend(30, Scheduled.AddMinutes(70));

        added.Should().Be(LiveSession.MaxExtendMinutes);
    }

    /// <summary>
    /// Chegara JAMI bo'yicha, bitta chaqiruv bo'yicha emas — aks holda ustoz
    /// 10 daqiqadan har safar qayta so'rab, darsni cheksiz cho'zishi mumkin edi.
    /// </summary>
    [Fact]
    public void Extend_AcrossMultipleCalls_CapsTotalAtMaxExtendMinutes()
    {
        var session = LiveSessionStartedAtScheduledTime();

        session.Extend(6, Scheduled.AddMinutes(70));
        session.Extend(6, Scheduled.AddMinutes(75));

        session.ExtendedMin.Should().Be(LiveSession.MaxExtendMinutes);
    }

    [Fact]
    public void Extend_SecondCallCrossingTheCap_ReturnsOnlyRemainingMinutes()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.Extend(6, Scheduled.AddMinutes(70));

        var added = session.Extend(6, Scheduled.AddMinutes(75));

        added.Should().Be(LiveSession.MaxExtendMinutes - 6);
    }

    [Fact]
    public void Extend_WhenCapAlreadyReached_ThrowsDomainException()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.Extend(LiveSession.MaxExtendMinutes, Scheduled.AddMinutes(70));

        var act = () => session.Extend(1, Scheduled.AddMinutes(80));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Extend_MovesEndsAtByTheAddedMinutes()
    {
        var session = LiveSessionStartedAtScheduledTime();

        session.Extend(7, Scheduled.AddMinutes(70));

        session.EndsAt.Should().Be(Scheduled.AddMinutes(PlannedMinutes + 7));
    }

    [Fact]
    public void Extend_SetsUpdatedAt()
    {
        var session = LiveSessionStartedAtScheduledTime();
        var now = Scheduled.AddMinutes(70);

        session.Extend(5, now);

        session.UpdatedAt.Should().Be(now);
    }

    // ------------------------------------------------------------------ EndsAt

    [Fact]
    public void EndsAt_WhenSessionIsScheduled_IsNull()
    {
        var session = NewSession();

        session.EndsAt.Should().BeNull();
    }

    [Fact]
    public void EndsAt_WhenSessionIsEnded_IsNull()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.End(Scheduled.AddMinutes(PlannedMinutes));

        session.EndsAt.Should().BeNull();
    }

    [Fact]
    public void EndsAt_WhenLiveButActualStartIsMissing_IsNull()
    {
        // Bazadan buzuq yozuv o'qilishi mumkin: Status=Live, lekin ActualStart bo'sh.
        var session = NewSession(SessionStatus.Live);

        session.EndsAt.Should().BeNull();
    }

    [Fact]
    public void EndsAt_WhenLive_EqualsActualStartPlusPlannedDuration()
    {
        var session = NewSession();
        var actualStart = Scheduled.AddMinutes(12);

        session.Start(actualStart);

        session.EndsAt.Should().Be(actualStart.AddMinutes(PlannedMinutes));
    }

    [Fact]
    public void EndsAt_WhenLiveAndExtended_IncludesExtendedMinutes()
    {
        var session = NewSession();
        var actualStart = Scheduled.AddMinutes(12);
        session.Start(actualStart);

        session.Extend(4, actualStart.AddMinutes(70));

        session.EndsAt.Should().Be(actualStart.AddMinutes(PlannedMinutes + 4));
    }

    // ------------------------------------------------------------------ PlannedDurationMinutes

    [Fact]
    public void PlannedDurationMinutes_ForEightyMinuteSlot_ReturnsEighty()
    {
        var session = NewSession();

        session.PlannedDurationMinutes.Should().Be(PlannedMinutes);
    }

    [Fact]
    public void PlannedDurationMinutes_WhenScheduledEndIsNotAfterStart_ReturnsOne()
    {
        // Jadval xato tuzilgan bo'lsa ham dars 0 yoki manfiy davom etmasligi kerak.
        var session = NewSession();
        session.ScheduledEnd = session.ScheduledStart;

        session.PlannedDurationMinutes.Should().Be(1);
    }

    // ------------------------------------------------------------------ IsOverdue

    [Fact]
    public void IsOverdue_OneSecondBeforeEndsAt_ReturnsFalse()
    {
        var session = LiveSessionStartedAtScheduledTime();
        var justBefore = Scheduled.AddMinutes(PlannedMinutes).AddSeconds(-1);

        session.IsOverdue(justBefore).Should().BeFalse();
    }

    [Fact]
    public void IsOverdue_ExactlyAtEndsAt_ReturnsTrue()
    {
        var session = LiveSessionStartedAtScheduledTime();

        session.IsOverdue(Scheduled.AddMinutes(PlannedMinutes)).Should().BeTrue();
    }

    [Fact]
    public void IsOverdue_AfterExtension_ReturnsFalseUntilTheNewDeadline()
    {
        var session = LiveSessionStartedAtScheduledTime();
        session.Extend(10, Scheduled.AddMinutes(70));

        session.IsOverdue(Scheduled.AddMinutes(PlannedMinutes + 9)).Should().BeFalse();
    }

    [Fact]
    public void IsOverdue_WhenSessionIsNotLive_ReturnsFalse()
    {
        var session = NewSession();

        session.IsOverdue(Scheduled.AddYears(1)).Should().BeFalse();
    }

    // ------------------------------------------------------------------ GenerateRoomName

    /// <summary>
    /// Eski tizimning B-4 bugi: xona nomi `g{guruh}-l{tartib}` edi va jadval
    /// qayta tuzilganda tartib noldan sanalardi. Ikki dars bir xil nom olgach,
    /// LiveKit webhook'i `MultipleResultsFound` bilan yiqilib, davomat butunlay
    /// to'xtab qolardi.
    /// </summary>
    [Fact]
    public void GenerateRoomName_CalledOneThousandTimes_ReturnsDistinctValues()
    {
        var names = new List<string>(1000);
        for (var i = 0; i < 1000; i++)
        {
            names.Add(LiveSession.GenerateRoomName());
        }

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GenerateRoomName_ReturnsDocumentedShape()
    {
        var name = LiveSession.GenerateRoomName();

        // s-<yyyyMMddHHmmss>-<16 ta kichik hex belgi> = 8 tasodifiy bayt
        //
        // 4 BAYT EMAS, 8 (2026-07-30 da o'zgartirildi): jadval generatsiyasi bir
        // guruhga 8 oylik darslarni BITTA paketda yaratadi — bir sekundda minglab
        // nom. 4 bayt bilan 10 000 nomda to'qnashuv ehtimoli ~1.2% edi va
        // `UX_LiveSessions_RoomName` unikal indeksi bunday INSERT'ni yiqitardi.
        name.Should().MatchRegex("^s-[0-9]{14}-[0-9a-f]{16}$");
    }

    /// <summary>
    /// ★ REGRESSIYA — entropiya "byudjeti" alohida qo'riqlanadi.
    ///
    /// Yuqoridagi shakl testi butun formatni tekshiradi; bu test esa AYNAN
    /// tasodifiy qism uzunligini qulflaydi, chunki xatarning o'zi shunda:
    /// vaqt qismi faqat SEKUND aniqligida, shuning uchun bir sekund ichidagi
    /// barcha nomlar faqat shu tasodifiy qismga tayanadi. Qisqartirilsa,
    /// paketli jadval generatsiyasi yana to'qnashuvga uchraydi.
    /// </summary>
    [Fact]
    public void GenerateRoomName_RandomSuffix_HasSixteenHexCharacters()
    {
        var name = LiveSession.GenerateRoomName();

        var randomSuffix = name.Split('-')[2];

        randomSuffix.Should().HaveLength(16);
    }

    // ------------------------------------------------------------------ konstantalar

    [Fact]
    public void StartLeadMinutes_IsFive()
    {
        // Konstanta shartnoma qismi: frontend ham shu oynani ko'rsatadi.
        LiveSession.StartLeadMinutes.Should().Be(5);
    }

    [Fact]
    public void MaxExtendMinutes_IsTen()
    {
        LiveSession.MaxExtendMinutes.Should().Be(10);
    }
}
