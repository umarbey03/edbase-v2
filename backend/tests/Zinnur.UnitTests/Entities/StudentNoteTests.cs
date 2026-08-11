using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="StudentNote"/> — matn qoidalari.
///
/// Chegara ENTITY ichida (servis ichida emas): izohni yaratishning ikkinchi
/// yo'li paydo bo'lsa ham 2000 belgi cheklovi va bo'sh matn taqiqi
/// avtomatik amal qiladi.
/// </summary>
public class StudentNoteTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsBodyAndStoresContext()
    {
        var note = StudentNote.Create(
            studentId: 5, authorId: 7, groupId: 9, body: "  Kech qoladi  ", Now);

        note.StudentId.Should().Be(5);
        note.AuthorId.Should().Be(7);
        note.GroupId.Should().Be(9);
        note.Body.Should().Be("Kech qoladi");
        note.CreatedAt.Should().Be(Now);
        note.UpdatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyBody_Throws(string? body)
    {
        var act = () => StudentNote.Create(1, 2, null, body, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithTooLongBody_Throws()
    {
        var act = () => StudentNote.Create(
            1, 2, null, new string('x', StudentNote.MaxBodyLength + 1), Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithExactlyMaxBody_IsAllowed()
    {
        var note = StudentNote.Create(
            1, 2, null, new string('x', StudentNote.MaxBodyLength), Now);

        note.Body.Should().HaveLength(StudentNote.MaxBodyLength);
    }

    /// <summary>
    /// Tahrirlash muallifni va guruh kontekstini O'ZGARTIRMAYDI — ruxsat
    /// qoidasi aynan muallifga tayanadi.
    /// </summary>
    [Fact]
    public void Edit_ChangesOnlyBodyAndStampsUpdatedAt()
    {
        var note = StudentNote.Create(1, 2, 3, "eski", Now);
        var later = Now.AddHours(1);

        note.Edit("yangi", later);

        note.Body.Should().Be("yangi");
        note.AuthorId.Should().Be(2);
        note.GroupId.Should().Be(3);
        note.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Edit_WithEmptyBody_Throws()
    {
        var note = StudentNote.Create(1, 2, null, "eski", Now);

        var act = () => note.Edit("  ", Now);

        act.Should().Throw<DomainException>();
        note.Body.Should().Be("eski", "yiqilgan tahrir matnni buzmasligi kerak");
    }
}
