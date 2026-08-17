using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="TeacherDailyCheckin"/> bosqichma-bosqich holat mashinasi
/// (2026-08-17, ustoz kunlik tasdiqlash). Har bosqich BITTA yo'nalishda
/// o'tadi: <c>Pending → Confirmed</c> yoki
/// <c>Pending → SelectingSessions → AwaitingReason → AwaitingDays → Declined</c>.
///
/// ★ NIMA UCHUN BU MUHIM: bot suhbati stateless — har bosqich orasida
/// server qayta ishga tushishi mumkin. Holat FAQAT shu entity'da saqlanadi,
/// ya'ni noto'g'ri bosqichdan chaqirilgan metod (masalan ikkinchi marta
/// "Ha" bosilishi) `DomainException` bilan rad etilishi SHART — aks holda
/// ustoz ikki marta javob berib, ma'lumot buzilardi.
/// </summary>
public class TeacherDailyCheckinTests
{
    private static readonly DateTimeOffset SentAt =
        new(2026, 8, 17, 7, 5, 0, TimeSpan.Zero);

    private static TeacherDailyCheckin NewCheckin(TeacherCheckinStatus status = TeacherCheckinStatus.Pending) => new()
    {
        TeacherId = 11,
        CheckinDate = DateOnly.FromDateTime(SentAt.Date),
        Status = status,
        SentAt = SentAt,
    };

    // ------------------------------------------------------------------ Confirm ("Ha")

    [Fact]
    public void Confirm_WhenPending_SetsStatusConfirmed()
    {
        var checkin = NewCheckin();

        checkin.Confirm(SentAt.AddMinutes(2));

        checkin.Status.Should().Be(TeacherCheckinStatus.Confirmed);
    }

    [Fact]
    public void Confirm_WhenPending_SetsRespondedAt()
    {
        var checkin = NewCheckin();
        var respondedAt = SentAt.AddMinutes(2);

        checkin.Confirm(respondedAt);

        checkin.RespondedAt.Should().Be(respondedAt);
    }

    [Fact]
    public void Confirm_WhenPending_SetsUpdatedAt()
    {
        var checkin = NewCheckin();
        var respondedAt = SentAt.AddMinutes(2);

        checkin.Confirm(respondedAt);

        checkin.UpdatedAt.Should().Be(respondedAt);
    }

    [Theory]
    [InlineData(TeacherCheckinStatus.Confirmed)]
    [InlineData(TeacherCheckinStatus.SelectingSessions)]
    [InlineData(TeacherCheckinStatus.AwaitingReason)]
    [InlineData(TeacherCheckinStatus.AwaitingDays)]
    [InlineData(TeacherCheckinStatus.Declined)]
    public void Confirm_WhenNotPending_ThrowsDomainException(TeacherCheckinStatus status)
    {
        var checkin = NewCheckin(status);

        var act = () => checkin.Confirm(SentAt.AddMinutes(2));

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// ★ REGRESSIYA: agar tugma ikki marta bosilsa (masalan tarmoq
    /// kechikishi tufayli Telegram xabarni ikki marta yuborsa),
    /// ikkinchi urinish holatni O'ZGARTIRMASLIGI kerak.
    /// </summary>
    [Fact]
    public void Confirm_CalledTwice_KeepsFirstRespondedAt()
    {
        var checkin = NewCheckin();
        var firstResponse = SentAt.AddMinutes(2);
        checkin.Confirm(firstResponse);

        var act = () => checkin.Confirm(SentAt.AddMinutes(5));

        act.Should().Throw<DomainException>();
        checkin.RespondedAt.Should().Be(firstResponse);
    }

    // ------------------------------------------------------------------ StartDecline ("Yo'q")

    [Fact]
    public void StartDecline_WhenPending_SetsStatusSelectingSessions()
    {
        var checkin = NewCheckin();

        checkin.StartDecline(SentAt.AddMinutes(2));

        checkin.Status.Should().Be(TeacherCheckinStatus.SelectingSessions);
    }

    [Fact]
    public void StartDecline_WhenPending_SetsUpdatedAt()
    {
        var checkin = NewCheckin();
        var now = SentAt.AddMinutes(2);

        checkin.StartDecline(now);

        checkin.UpdatedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(TeacherCheckinStatus.Confirmed)]
    [InlineData(TeacherCheckinStatus.SelectingSessions)]
    [InlineData(TeacherCheckinStatus.AwaitingReason)]
    [InlineData(TeacherCheckinStatus.AwaitingDays)]
    [InlineData(TeacherCheckinStatus.Declined)]
    public void StartDecline_WhenNotPending_ThrowsDomainException(TeacherCheckinStatus status)
    {
        var checkin = NewCheckin(status);

        var act = () => checkin.StartDecline(SentAt.AddMinutes(2));

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ ConfirmSessionSelection

    [Fact]
    public void ConfirmSessionSelection_WhenSelectingSessions_SetsStatusAwaitingReason()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.SelectingSessions);

        checkin.ConfirmSessionSelection(SentAt.AddMinutes(3));

        checkin.Status.Should().Be(TeacherCheckinStatus.AwaitingReason);
    }

    [Fact]
    public void ConfirmSessionSelection_WhenSelectingSessions_SetsUpdatedAt()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.SelectingSessions);
        var now = SentAt.AddMinutes(3);

        checkin.ConfirmSessionSelection(now);

        checkin.UpdatedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(TeacherCheckinStatus.Pending)]
    [InlineData(TeacherCheckinStatus.AwaitingReason)]
    [InlineData(TeacherCheckinStatus.AwaitingDays)]
    [InlineData(TeacherCheckinStatus.Declined)]
    public void ConfirmSessionSelection_WhenNotSelectingSessions_ThrowsDomainException(TeacherCheckinStatus status)
    {
        var checkin = NewCheckin(status);

        var act = () => checkin.ConfirmSessionSelection(SentAt.AddMinutes(3));

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ SubmitReason

    [Fact]
    public void SubmitReason_WhenAwaitingReason_SetsStatusAwaitingDays()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingReason);

        checkin.SubmitReason("Kasal bo'lib qoldim", SentAt.AddMinutes(4));

        checkin.Status.Should().Be(TeacherCheckinStatus.AwaitingDays);
    }

    [Fact]
    public void SubmitReason_TrimsWhitespace()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingReason);

        checkin.SubmitReason("   Kasal bo'lib qoldim   ", SentAt.AddMinutes(4));

        checkin.DeclineReason.Should().Be("Kasal bo'lib qoldim");
    }

    [Fact]
    public void SubmitReason_WhenEmpty_ThrowsDomainException()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingReason);

        var act = () => checkin.SubmitReason(string.Empty, SentAt.AddMinutes(4));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SubmitReason_WhenWhitespaceOnly_ThrowsDomainException()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingReason);

        var act = () => checkin.SubmitReason("   ", SentAt.AddMinutes(4));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SubmitReason_TruncatesOverlyLongReason()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingReason);
        var reason = new string('a', TeacherDailyCheckin.MaxReasonLength + 200);

        checkin.SubmitReason(reason, SentAt.AddMinutes(4));

        checkin.DeclineReason.Should().HaveLength(TeacherDailyCheckin.MaxReasonLength);
    }

    [Fact]
    public void SubmitReason_SetsUpdatedAt()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingReason);
        var now = SentAt.AddMinutes(4);

        checkin.SubmitReason("Kasal bo'lib qoldim", now);

        checkin.UpdatedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(TeacherCheckinStatus.Pending)]
    [InlineData(TeacherCheckinStatus.SelectingSessions)]
    [InlineData(TeacherCheckinStatus.AwaitingDays)]
    [InlineData(TeacherCheckinStatus.Declined)]
    public void SubmitReason_WhenNotAwaitingReason_ThrowsDomainException(TeacherCheckinStatus status)
    {
        var checkin = NewCheckin(status);

        var act = () => checkin.SubmitReason("Kasal", SentAt.AddMinutes(4));

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ SubmitDays (yakuniy qadam)

    [Fact]
    public void SubmitDays_WhenAwaitingDays_SetsStatusDeclined()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingDays);

        checkin.SubmitDays(3, SentAt.AddMinutes(5));

        checkin.Status.Should().Be(TeacherCheckinStatus.Declined);
    }

    [Fact]
    public void SubmitDays_WhenAwaitingDays_SetsUnavailableDays()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingDays);

        checkin.SubmitDays(3, SentAt.AddMinutes(5));

        checkin.UnavailableDays.Should().Be(3);
    }

    [Fact]
    public void SubmitDays_SetsRespondedAt()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingDays);
        var respondedAt = SentAt.AddMinutes(5);

        checkin.SubmitDays(1, respondedAt);

        checkin.RespondedAt.Should().Be(respondedAt);
    }

    [Fact]
    public void SubmitDays_SetsUpdatedAt()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingDays);
        var now = SentAt.AddMinutes(5);

        checkin.SubmitDays(1, now);

        checkin.UpdatedAt.Should().Be(now);
    }

    /// <summary>Pastki chegara — "faqat bugun" (1 kun) qabul qilinishi kerak.</summary>
    [Fact]
    public void SubmitDays_AtMinimumBoundary_Succeeds()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingDays);

        var act = () => checkin.SubmitDays(1, SentAt.AddMinutes(5));

        act.Should().NotThrow();
    }

    [Fact]
    public void SubmitDays_AtMaximumBoundary_Succeeds()
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingDays);

        var act = () => checkin.SubmitDays(30, SentAt.AddMinutes(5));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    [InlineData(1000)]
    public void SubmitDays_OutOfRange_ThrowsDomainException(int days)
    {
        var checkin = NewCheckin(TeacherCheckinStatus.AwaitingDays);

        var act = () => checkin.SubmitDays(days, SentAt.AddMinutes(5));

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(TeacherCheckinStatus.Pending)]
    [InlineData(TeacherCheckinStatus.SelectingSessions)]
    [InlineData(TeacherCheckinStatus.AwaitingReason)]
    [InlineData(TeacherCheckinStatus.Declined)]
    public void SubmitDays_WhenNotAwaitingDays_ThrowsDomainException(TeacherCheckinStatus status)
    {
        var checkin = NewCheckin(status);

        var act = () => checkin.SubmitDays(2, SentAt.AddMinutes(5));

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ standart qiymatlar

    [Fact]
    public void Status_DefaultsToPending()
    {
        var checkin = new TeacherDailyCheckin();

        checkin.Status.Should().Be(TeacherCheckinStatus.Pending);
    }

    [Fact]
    public void AffectedSessions_DefaultsToEmptyCollection()
    {
        var checkin = new TeacherDailyCheckin();

        checkin.AffectedSessions.Should().BeEmpty();
    }

    [Fact]
    public void MaxReasonLength_IsFiveHundred()
    {
        // Konstanta shartnoma qismi: EF konfiguratsiyasidagi `HasMaxLength` shu bilan MOS bo'lishi kerak.
        TeacherDailyCheckin.MaxReasonLength.Should().Be(500);
    }
}
