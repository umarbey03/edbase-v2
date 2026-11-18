using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>Mustaqil o'quvchining kuratori — yaratish, tugatish va mas'uliyat ifodasi.</summary>
public class SelfStudyCuratorAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_an_active_assignment()
    {
        var a = SelfStudyCuratorAssignment.Create(10, 20, 30, 1, Now);

        a.IsActive.Should().BeTrue();
        a.CuratorId.Should().Be(20);
        a.BackupCuratorId.Should().Be(30);
        a.AssignedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(10, 10, null)]
    [InlineData(10, 20, 10L)]
    [InlineData(10, 20, 20L)]
    [InlineData(0, 20, null)]
    public void Create_rejects_impossible_pairs(long student, long curator, long? backup)
    {
        var act = () => SelfStudyCuratorAssignment.Create(student, curator, backup, 1, Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void End_is_idempotent_and_keeps_the_first_moment()
    {
        var a = SelfStudyCuratorAssignment.Create(10, 20, null, 1, Now);

        a.End(2, Now.AddHours(1));
        a.End(3, Now.AddHours(2));

        a.IsActive.Should().BeFalse();
        a.EndedAt.Should().Be(Now.AddHours(1));
        a.EndedById.Should().Be(2);
    }

    [Fact]
    public void ActiveFor_covers_primary_and_backup_but_not_ended_rows()
    {
        var rows = new[]
        {
            SelfStudyCuratorAssignment.Create(1, 20, 30, 1, Now),
            SelfStudyCuratorAssignment.Create(2, 30, null, 1, Now),
            SelfStudyCuratorAssignment.Create(3, 20, null, 1, Now),
        };
        rows[2].End(1, Now);

        var withBackup = SelfStudyCuratorAssignment.ActiveFor(30).Compile();
        var primaryOnly = SelfStudyCuratorAssignment.ActiveFor(30, includeBackup: false).Compile();
        var ofTwenty = SelfStudyCuratorAssignment.ActiveFor(20).Compile();

        rows.Where(withBackup).Select(r => r.StudentId).Should().BeEquivalentTo([1L, 2L]);
        rows.Where(primaryOnly).Select(r => r.StudentId).Should().BeEquivalentTo([2L]);
        rows.Where(ofTwenty).Select(r => r.StudentId).Should().BeEquivalentTo([1L]);
    }

    [Fact]
    public void IsSameAs_compares_both_seats()
    {
        var a = SelfStudyCuratorAssignment.Create(10, 20, 30, 1, Now);

        a.IsSameAs(20, 30).Should().BeTrue();
        a.IsSameAs(20, null).Should().BeFalse();
        a.IsSameAs(30, 20).Should().BeFalse();
    }
}
