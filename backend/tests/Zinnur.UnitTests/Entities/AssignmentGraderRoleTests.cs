using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Staffing;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// R33 — vazifa darajasidagi tekshiruvchi istisnosining Domain qoidasi.
///
/// ★ NIMA UCHUN ALOHIDA TEST FAYLI: qoidaning O'ZI bitta shart, lekin u
/// eng oson unutiladigan turdagi cheklov — "bu maydon faqat MA'LUM
/// nishonda ma'noli". Bunday shart tekshirilmasa xato JIMGINA bo'ladi:
/// tanlov saqlanadi, ekranda ko'rinadi va HECH NIMAGA ta'sir qilmaydi.
/// </summary>
public class AssignmentGraderRoleTests
{
    private static Assignment GroupAssignment(GroupStaffRole? graderRole) =>
        new() { Title = "Uy vazifasi", GroupId = 5, GraderRole = graderRole };

    private static Assignment CourseAssignment(GroupStaffRole? graderRole) =>
        new() { Title = "Kurs vazifasi", ModuleLessonId = 9, GraderRole = graderRole };

    [Fact]
    public void Default_IsNull_MeaningGroupSettingApplies()
    {
        new Assignment { Title = "Vazifa", GroupId = 5 }.GraderRole.Should().BeNull();
    }

    [Theory]
    [InlineData(GroupStaffRole.Both)]
    [InlineData(GroupStaffRole.Teacher)]
    [InlineData(GroupStaffRole.Assistant)]
    public void GroupAssignment_AcceptsOverride(GroupStaffRole role)
    {
        var act = () => GroupAssignment(role).Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void CourseAssignment_WithoutOverride_IsValid()
    {
        var act = () => CourseAssignment(null).Validate();

        act.Should().NotThrow();
    }

    /// <summary>
    /// 🔴 KURS VAZIFASI o'nlab guruhga taalluqli va ularning har birida
    /// boshqa-boshqa xodim ishlaydi. Bitta bayroq HAMMASINI birdan hal
    /// qilib qo'yardi — ya'ni o'quv bo'limi guruhlarga qo'ygan tanlovni
    /// bexosdan bekor qilardi va buni hech qayerda ko'rmasdi.
    /// </summary>
    [Fact]
    public void CourseAssignment_WithOverride_IsRejected()
    {
        var act = () => CourseAssignment(GroupStaffRole.Assistant).Validate();

        act.Should().Throw<DomainException>()
            .WithMessage("*GURUH sozlamasidan*");
    }
}
