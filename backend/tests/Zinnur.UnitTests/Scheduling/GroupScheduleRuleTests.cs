using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Scheduling;

/// <summary>
/// <see cref="Group.ValidateScheduleRule"/> va
/// <see cref="Group.ScheduleRuleDiffersFrom"/>.
///
/// Bu ikki metod jadval modulining ikki eng nozik qarorini ushlab turadi:
///  1) "bu jadval qoidasi umuman haqiqiymi" — noto'g'ri qoida bazaga tushsa
///     butun semestr jadvali xato bo'ladi;
///  2) "jadvalni QAYTA TUZISH kerakmi" — bu savolga noto'g'ri javob eski
///     tizimda har tahrirda butun kelajak jadvalni o'chirib yuborardi.
/// </summary>
public class GroupScheduleRuleTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly TimeOnly Seven = new(19, 0);

    private static readonly DayOfWeek[] TwoDays = [DayOfWeek.Monday, DayOfWeek.Wednesday];

    private static Group NewGroup(
        GroupType type = GroupType.Group,
        IEnumerable<DayOfWeek>? weekdays = null,
        int durationMinutes = 80,
        int courseMonths = 8,
        long? assistantId = 22,
        long? curatorGroupId = null) => new()
        {
            Name = "ATF-1",
            TeacherId = 11,
            AssistantId = assistantId,
            Type = type,
            CuratorGroupId = curatorGroupId,
            StartDate = Start,
            CourseMonths = courseMonths,
            Weekdays = [.. weekdays ?? TwoDays],
            StartTime = Seven,
            DurationMinutes = durationMinutes,
        };

    // ================================================================= VALIDATSIYA

    [Fact]
    public void Validate_ForAValidTeacherGroup_DoesNotThrow()
    {
        var validate = () => NewGroup().ValidateScheduleRule();

        validate.Should().NotThrow();
    }

    // ---------------------------------------------------------------- kun soni

    /// <summary>
    /// ESKI TIZIM BUGI: "haftada aniq 2 kun" sharti HAMMA turga qo'llanardi,
    /// shu jumladan kurator guruhiga ham. Kurator darslari haftada 3 kun
    /// bo'lgani uchun bunday guruhni SAQLASHNING UMUMAN IMKONI YO'Q EDI —
    /// panel 400 xato qaytarardi.
    ///
    /// Shuning uchun bir xil kun soni turga qarab BOSHQA natija berishi
    /// aynan shu yerda qulflab qo'yiladi.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Validate_ForPlainGroup_RequiresExactlyTwoWeekdays(int dayCount)
    {
        var group = NewGroup(GroupType.Group, FirstDays(dayCount));

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>()
            .WithMessage("*2 kun*");
    }

    [Fact]
    public void Validate_ForCuratorGroup_AcceptsThreeWeekdays()
    {
        var group = NewGroup(GroupType.Curator, FirstDays(3));

        var validate = () => group.ValidateScheduleRule();

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public void Validate_ForCuratorGroup_AcceptsAnyWeekdayCount(int dayCount)
    {
        var group = NewGroup(GroupType.Curator, FirstDays(dayCount));

        var validate = () => group.ValidateScheduleRule();

        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_ForIndividualGroup_AcceptsThreeWeekdays()
    {
        var group = NewGroup(GroupType.Individual, FirstDays(3));

        var validate = () => group.ValidateScheduleRule();

        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithNoWeekdays_Throws()
    {
        var group = NewGroup(weekdays: []);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*bitta dars kuni*");
    }

    /// <summary>
    /// Takroriy kun jadvalni JIMGINA buzadi: generator `HashSet` ishlatgani
    /// uchun dars soni kutilganidan kam chiqardi va nima uchunligi ko'rinmasdi.
    /// </summary>
    [Fact]
    public void Validate_WithDuplicateWeekdays_Throws()
    {
        var group = NewGroup(weekdays: [DayOfWeek.Monday, DayOfWeek.Monday]);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*takrorlanmasligi*");
    }

    // ---------------------------------------------------------------- chegaralar

    [Theory]
    [InlineData(0)]
    [InlineData(Group.MinDurationMinutes - 1)]
    [InlineData(Group.MaxDurationMinutes + 1)]
    [InlineData(10_000)]
    public void Validate_WithDurationOutOfRange_Throws(int durationMinutes)
    {
        var group = NewGroup(durationMinutes: durationMinutes);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*davomiyligi*");
    }

    [Theory]
    [InlineData(Group.MinDurationMinutes)]
    [InlineData(80)]
    [InlineData(Group.MaxDurationMinutes)]
    public void Validate_WithDurationAtTheBoundary_DoesNotThrow(int durationMinutes)
    {
        var group = NewGroup(durationMinutes: durationMinutes);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Group.MinCourseMonths - 1)]
    [InlineData(Group.MaxCourseMonths + 1)]
    [InlineData(240)]
    public void Validate_WithCourseMonthsOutOfRange_Throws(int courseMonths)
    {
        var group = NewGroup(courseMonths: courseMonths);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*Kurs davomiyligi*");
    }

    [Theory]
    [InlineData(Group.MinCourseMonths)]
    [InlineData(8)]
    [InlineData(Group.MaxCourseMonths)]
    public void Validate_WithCourseMonthsAtTheBoundary_DoesNotThrow(int courseMonths)
    {
        var group = NewGroup(courseMonths: courseMonths);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithoutStartDate_Throws()
    {
        var group = NewGroup();
        group.StartDate = default;

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*boshlanish sanasi*");
    }

    [Fact]
    public void Validate_WithBlankName_Throws()
    {
        var group = NewGroup();
        group.Name = "   ";

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*nomi*");
    }

    // ---------------------------------------------------------------- kurator qoidalari

    /// <summary>
    /// Kurator guruhida darsni USTOZ emas, KURATOR o'tadi
    /// (<c>Group.HostId</c> shunga tayanadi). Kurator biriktirilmagan bo'lsa
    /// jadval hostsiz darslar bilan tuzilib, ularni hech kim boshlay olmasdi.
    /// </summary>
    [Fact]
    public void Validate_ForCuratorGroupWithoutAnAssistant_Throws()
    {
        var group = NewGroup(GroupType.Curator, FirstDays(3), assistantId: null);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*kurator (yordamchi)*");
    }

    [Fact]
    public void Validate_ForCuratorGroupLinkedToAnotherCurator_Throws()
    {
        var group = NewGroup(GroupType.Curator, FirstDays(3), curatorGroupId: 99);

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*kurator guruhiga bog'lanmaydi*");
    }

    [Fact]
    public void Validate_ForGroupLinkedToItself_Throws()
    {
        var group = NewGroup();
        group.Id = 7;
        group.CuratorGroupId = 7;

        var validate = () => group.ValidateScheduleRule();

        validate.Should().Throw<DomainException>().WithMessage("*o'zini o'ziga*");
    }

    // ---------------------------------------------------------------- host

    [Fact]
    public void HostId_ForTeacherGroup_IsTheTeacher()
    {
        NewGroup().HostId.Should().Be(11);
    }

    [Fact]
    public void HostId_ForCuratorGroup_IsTheAssistant()
    {
        NewGroup(GroupType.Curator, FirstDays(3)).HostId.Should().Be(22);
    }

    // ================================================================= FARQ ANIQLASH

    /// <summary>
    /// ★ Bu test to'plami ESKI TIZIMNING ENG QIMMAT BUGINI qo'riqlaydi:
    /// guruh tahrirlanganda jadval SHARTSIZ qayta tuzilardi. Kursni yoki
    /// kuratorni almashtirsangiz ham butun kelajak jadval o'chib qayta
    /// yaratilardi — dars Id'lari va LiveKit xona nomlari o'zgarib, tarqatilgan
    /// havolalar jimgina ishlamay qolardi.
    /// </summary>
    [Fact]
    public void Differs_WhenNothingChanged_IsFalse()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 80, 8, GroupType.Group)
            .Should().BeFalse();
    }

    [Fact]
    public void Differs_WhenStartDateChanged_IsTrue()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(Start.AddDays(1), TwoDays, Seven, 80, 8, GroupType.Group)
            .Should().BeTrue();
    }

    [Fact]
    public void Differs_WhenWeekdaysChanged_IsTrue()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(
                Start, [DayOfWeek.Tuesday, DayOfWeek.Thursday], Seven, 80, 8, GroupType.Group)
            .Should().BeTrue();
    }

    [Fact]
    public void Differs_WhenStartTimeChanged_IsTrue()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(Start, TwoDays, new TimeOnly(18, 0), 80, 8, GroupType.Group)
            .Should().BeTrue();
    }

    [Fact]
    public void Differs_WhenDurationChanged_IsTrue()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 90, 8, GroupType.Group)
            .Should().BeTrue();
    }

    [Fact]
    public void Differs_WhenCourseMonthsChanged_IsTrue()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 80, 9, GroupType.Group)
            .Should().BeTrue();
    }

    [Fact]
    public void Differs_WhenTypeChanged_IsTrue()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 80, 8, GroupType.Individual)
            .Should().BeTrue();
    }

    /// <summary>
    /// Kunlar TARTIBI ahamiyatsiz: frontend checkbox'larni bosilish tartibida
    /// yuboradi. Tartibga sezgir taqqoslash "hech narsa o'zgarmagan"
    /// tahrirda ham butun jadvalni qayta tuzib yuborardi.
    /// </summary>
    [Fact]
    public void Differs_WhenWeekdayOrderDiffers_IsFalse()
    {
        var group = NewGroup();

        group.ScheduleRuleDiffersFrom(
                Start, [DayOfWeek.Wednesday, DayOfWeek.Monday], Seven, 80, 8, GroupType.Group)
            .Should().BeFalse();
    }

    /// <summary>
    /// Nom va ustoz jadval QOIDASI emas: ular o'zgarganda darslar O'RNIDA
    /// tahrirlanadi (Id, xona nomi, davomat va chat saqlanadi).
    /// </summary>
    [Fact]
    public void Differs_WhenOnlyNameChanged_IsFalse()
    {
        var group = NewGroup();
        group.Name = "ATF-2 (yangi nom)";

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 80, 8, GroupType.Group)
            .Should().BeFalse();
    }

    [Fact]
    public void Differs_WhenOnlyTeacherChanged_IsFalse()
    {
        var group = NewGroup();
        group.TeacherId = 999;

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 80, 8, GroupType.Group)
            .Should().BeFalse();
    }

    [Fact]
    public void Differs_WhenOnlyAssistantChanged_IsFalse()
    {
        var group = NewGroup();
        group.AssistantId = 999;

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 80, 8, GroupType.Group)
            .Should().BeFalse();
    }

    [Fact]
    public void Differs_WhenOnlyCourseOrCuratorLinkChanged_IsFalse()
    {
        var group = NewGroup();
        group.CourseId = 42;
        group.CuratorGroupId = 43;
        group.RecordEnabled = true;
        group.IsActive = false;

        group.ScheduleRuleDiffersFrom(Start, TwoDays, Seven, 80, 8, GroupType.Group)
            .Should().BeFalse();
    }

    [Fact]
    public void Differs_WithNullWeekdays_Throws()
    {
        var group = NewGroup();

        var compare = () => group.ScheduleRuleDiffersFrom(
            Start, null!, Seven, 80, 8, GroupType.Group);

        compare.Should().Throw<ArgumentNullException>();
    }

    // ================================================================= yordamchi

    /// <summary>Dushanbadan boshlab <paramref name="count"/> ta ketma-ket kun.</summary>
    private static DayOfWeek[] FirstDays(int count) =>
    [
        .. new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        }.Take(count),
    ];
}
