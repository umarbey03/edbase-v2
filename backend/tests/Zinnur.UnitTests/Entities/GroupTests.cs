using Zinnur.Domain.Entities;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="Group.IsStaff"/> — ruxsat matritsasining poydevori.
/// Xato natija darsni begona odam boshlashiga yoki ustozni o'z guruhidan
/// chiqarib qo'yishga olib keladi, shuning uchun `null` holatlar ham tekshiriladi.
/// </summary>
public class GroupTests
{
    private const long TeacherId = 11;
    private const long AssistantId = 22;
    private const long StudentId = 33;

    private static Group NewGroup(long? teacherId = TeacherId, long? assistantId = AssistantId) => new()
    {
        Name = "ATF-1",
        CourseId = 1,
        TeacherId = teacherId,
        AssistantId = assistantId,
        StartDate = new DateOnly(2026, 3, 1),
    };

    [Fact]
    public void IsStaff_ForTheTeacher_ReturnsTrue()
    {
        var group = NewGroup();

        group.IsStaff(TeacherId).Should().BeTrue();
    }

    [Fact]
    public void IsStaff_ForTheAssistant_ReturnsTrue()
    {
        var group = NewGroup();

        group.IsStaff(AssistantId).Should().BeTrue();
    }

    [Fact]
    public void IsStaff_ForAnUnrelatedUser_ReturnsFalse()
    {
        var group = NewGroup();

        group.IsStaff(StudentId).Should().BeFalse();
    }

    [Fact]
    public void IsStaff_WhenTeacherIdIsNull_StillRecognisesTheAssistant()
    {
        var group = NewGroup(teacherId: null);

        group.IsStaff(AssistantId).Should().BeTrue();
    }

    [Fact]
    public void IsStaff_WhenAssistantIdIsNull_StillRecognisesTheTeacher()
    {
        var group = NewGroup(assistantId: null);

        group.IsStaff(TeacherId).Should().BeTrue();
    }

    [Fact]
    public void IsStaff_WhenTeacherIdIsNull_DoesNotMatchTheOldTeacher()
    {
        var group = NewGroup(teacherId: null);

        group.IsStaff(TeacherId).Should().BeFalse();
    }

    [Fact]
    public void IsStaff_WhenBothIdsAreNull_ReturnsFalse()
    {
        var group = NewGroup(teacherId: null, assistantId: null);

        group.IsStaff(StudentId).Should().BeFalse();
    }

    /// <summary>
    /// Nozik joy: `long?` ni `long` bilan solishtirganda `null` HECH QACHON
    /// qiymatga teng bo'lmasligi kerak. Agar biror joyda `?? 0` ishlatilsa,
    /// `userId == 0` bo'lgan chaqiruv butun guruhga xodim huquqini berib yuborardi.
    /// </summary>
    [Fact]
    public void IsStaff_WithZeroUserId_WhenBothIdsAreNull_ReturnsFalse()
    {
        var group = NewGroup(teacherId: null, assistantId: null);

        group.IsStaff(0).Should().BeFalse();
    }

    [Fact]
    public void IsStaff_WhenTeacherAndAssistantAreTheSamePerson_ReturnsTrue()
    {
        var group = NewGroup(teacherId: TeacherId, assistantId: TeacherId);

        group.IsStaff(TeacherId).Should().BeTrue();
    }
}
