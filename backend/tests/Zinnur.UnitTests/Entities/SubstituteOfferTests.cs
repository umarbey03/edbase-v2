using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="SubstituteOffer"/> — "bitta nomzod ustozga yuborilgan
/// taklif" (2026-08-17). Reyting YO'Q (MVP): birinchi rozi bo'lgan
/// darsni oladi, qolgan `Sent` takliflar <see cref="SubstituteOffer.Withdraw"/>
/// bilan bekor qilinadi — sabab <c>TeacherAvailabilityService.
/// HandleOfferCallbackAsync</c>.
/// </summary>
public class SubstituteOfferTests
{
    private static readonly DateTimeOffset SentAt =
        new(2026, 8, 17, 8, 5, 0, TimeSpan.Zero);

    private static SubstituteOffer NewOffer(SubstituteOfferStatus status = SubstituteOfferStatus.Sent) => new()
    {
        CoverageRequestId = 4,
        CandidateTeacherId = 22,
        Status = status,
        SentAt = SentAt,
    };

    // ------------------------------------------------------------------ Accept

    [Fact]
    public void Accept_WhenSent_SetsStatusAccepted()
    {
        var offer = NewOffer();

        offer.Accept(SentAt.AddMinutes(1));

        offer.Status.Should().Be(SubstituteOfferStatus.Accepted);
    }

    [Fact]
    public void Accept_WhenSent_SetsRespondedAt()
    {
        var offer = NewOffer();
        var respondedAt = SentAt.AddMinutes(1);

        offer.Accept(respondedAt);

        offer.RespondedAt.Should().Be(respondedAt);
    }

    [Fact]
    public void Accept_WhenSent_SetsUpdatedAt()
    {
        var offer = NewOffer();
        var now = SentAt.AddMinutes(1);

        offer.Accept(now);

        offer.UpdatedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(SubstituteOfferStatus.Accepted)]
    [InlineData(SubstituteOfferStatus.Declined)]
    [InlineData(SubstituteOfferStatus.Withdrawn)]
    public void Accept_WhenNotSent_ThrowsDomainException(SubstituteOfferStatus status)
    {
        var offer = NewOffer(status);

        var act = () => offer.Accept(SentAt.AddMinutes(1));

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ Decline

    [Fact]
    public void Decline_WhenSent_SetsStatusDeclined()
    {
        var offer = NewOffer();

        offer.Decline(SentAt.AddMinutes(1));

        offer.Status.Should().Be(SubstituteOfferStatus.Declined);
    }

    [Fact]
    public void Decline_WhenSent_SetsRespondedAt()
    {
        var offer = NewOffer();
        var respondedAt = SentAt.AddMinutes(1);

        offer.Decline(respondedAt);

        offer.RespondedAt.Should().Be(respondedAt);
    }

    [Theory]
    [InlineData(SubstituteOfferStatus.Accepted)]
    [InlineData(SubstituteOfferStatus.Declined)]
    [InlineData(SubstituteOfferStatus.Withdrawn)]
    public void Decline_WhenNotSent_ThrowsDomainException(SubstituteOfferStatus status)
    {
        var offer = NewOffer(status);

        var act = () => offer.Decline(SentAt.AddMinutes(1));

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ Withdraw

    [Fact]
    public void Withdraw_WhenSent_SetsStatusWithdrawn()
    {
        var offer = NewOffer();

        offer.Withdraw(SentAt.AddMinutes(1));

        offer.Status.Should().Be(SubstituteOfferStatus.Withdrawn);
    }

    [Fact]
    public void Withdraw_WhenSent_SetsUpdatedAt()
    {
        var offer = NewOffer();
        var now = SentAt.AddMinutes(1);

        offer.Withdraw(now);

        offer.UpdatedAt.Should().Be(now);
    }

    /// <summary>
    /// ★ IDEMPOTENT VA ATAYLAB SHU: `HandleOfferCallbackAsync` qolgan
    /// BARCHA <c>Sent</c> takliflarni bekor qiladi, lekin allaqachon
    /// javob berilgan (Accepted/Declined) taklifga TEGMASLIGI kerak —
    /// aks holda rozi bo'lgan ustozning holati "bekor qilingan"ga
    /// aylanib qolardi.
    /// </summary>
    [Fact]
    public void Withdraw_WhenAlreadyAccepted_DoesNotChangeStatus()
    {
        var offer = NewOffer();
        offer.Accept(SentAt.AddMinutes(1));

        offer.Withdraw(SentAt.AddMinutes(2));

        offer.Status.Should().Be(SubstituteOfferStatus.Accepted);
    }

    [Fact]
    public void Withdraw_WhenAlreadyDeclined_DoesNotChangeStatus()
    {
        var offer = NewOffer();
        offer.Decline(SentAt.AddMinutes(1));

        offer.Withdraw(SentAt.AddMinutes(2));

        offer.Status.Should().Be(SubstituteOfferStatus.Declined);
    }

    [Fact]
    public void Withdraw_CalledTwice_DoesNotThrow()
    {
        var offer = NewOffer();
        offer.Withdraw(SentAt.AddMinutes(1));

        var act = () => offer.Withdraw(SentAt.AddMinutes(2));

        act.Should().NotThrow();
    }

    [Fact]
    public void Withdraw_DoesNotThrowRegardlessOfStatus()
    {
        var offer = NewOffer();
        offer.Accept(SentAt.AddMinutes(1));

        var act = () => offer.Withdraw(SentAt.AddMinutes(2));

        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------ standart qiymat

    [Fact]
    public void Status_DefaultsToSent()
    {
        var offer = new SubstituteOffer();

        offer.Status.Should().Be(SubstituteOfferStatus.Sent);
    }
}
