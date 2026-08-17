using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="SessionCoverageRequest"/> — "bitta darsga o'rinbosar kerak"
/// so'rovi (2026-08-17, ustoz kunlik tasdiqlash). Bir nomzod rozi
/// bo'lganda <see cref="SessionCoverageRequest.Resolve"/> chaqiriladi va
/// so'rov YAKUNIY holatga o'tadi — poyga holatida (ikki ustoz bir vaqtda
/// "Ha" bossa) ikkinchi urinish RAD ETILISHI SHART, aks holda ikkala
/// ustoz ham "men oldim" deb o'ylab qolardi.
/// </summary>
public class SessionCoverageRequestTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    private static SessionCoverageRequest NewRequest(CoverageRequestStatus status = CoverageRequestStatus.Open) => new()
    {
        SessionId = 501,
        CheckinId = 9,
        OriginalHostId = 11,
        Reason = "Kasal bo'lib qoldim",
        Status = status,
    };

    [Fact]
    public void Resolve_WhenOpen_SetsStatusResolved()
    {
        var request = NewRequest();

        request.Resolve(substituteTeacherId: 22, Now);

        request.Status.Should().Be(CoverageRequestStatus.Resolved);
    }

    [Fact]
    public void Resolve_WhenOpen_SetsResolvedByUserId()
    {
        var request = NewRequest();

        request.Resolve(substituteTeacherId: 22, Now);

        request.ResolvedByUserId.Should().Be(22);
    }

    [Fact]
    public void Resolve_WhenOpen_SetsResolvedAt()
    {
        var request = NewRequest();

        request.Resolve(substituteTeacherId: 22, Now);

        request.ResolvedAt.Should().Be(Now);
    }

    [Fact]
    public void Resolve_WhenOpen_SetsUpdatedAt()
    {
        var request = NewRequest();

        request.Resolve(substituteTeacherId: 22, Now);

        request.UpdatedAt.Should().Be(Now);
    }

    /// <summary>
    /// ★ POYGA HOLATI: ikkinchi nomzod ham "Ha" bossa, ikkinchi `Resolve`
    /// chaqiruvi RAD ETILADI — birinchi rozi bo'lgan ustoz o'zgarmasdan qoladi.
    /// </summary>
    [Fact]
    public void Resolve_WhenAlreadyResolved_ThrowsDomainException()
    {
        var request = NewRequest();
        request.Resolve(substituteTeacherId: 22, Now);

        var act = () => request.Resolve(substituteTeacherId: 33, Now.AddSeconds(5));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Resolve_WhenAlreadyResolved_KeepsFirstResolver()
    {
        var request = NewRequest();
        request.Resolve(substituteTeacherId: 22, Now);

        var act = () => request.Resolve(substituteTeacherId: 33, Now.AddSeconds(5));

        act.Should().Throw<DomainException>();
        request.ResolvedByUserId.Should().Be(22);
    }

    [Fact]
    public void Resolve_WhenCancelled_ThrowsDomainException()
    {
        var request = NewRequest(CoverageRequestStatus.Cancelled);

        var act = () => request.Resolve(substituteTeacherId: 22, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Status_DefaultsToOpen()
    {
        var request = new SessionCoverageRequest();

        request.Status.Should().Be(CoverageRequestStatus.Open);
    }

    [Fact]
    public void Offers_DefaultsToEmptyCollection()
    {
        var request = new SessionCoverageRequest();

        request.Offers.Should().BeEmpty();
    }
}
