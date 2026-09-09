using Zinnur.Application.Payroll;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.Payroll;

/// <summary>
/// OYLIK DVIGATELI (<see cref="PayrollCalculator"/>) — sof funksiya,
/// bazasiz sinaladi (<c>BillingSelectionTests</c> bilan bir xil naqsh).
///
/// Aynan shu hisob xodimning oyligini belgilaydi: xato jimgina noto'g'ri
/// summa to'laydi va buni faqat ustoz shikoyat qilganda sezish mumkin.
/// </summary>
public class PayrollCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);
    private static readonly DateOnly Saturday = new(2026, 9, 5);

    private const long TeacherId = 7;
    private const long GroupId = 10;
    private const long CourseId = 3;
    private const long CategoryId = 4;

    // ================================================================= dars

    /// <summary>★ TURLI TURLAR QO'SHILADI: dars stavkasi + o'quvchi bonusi.</summary>
    [Fact]
    public void ComputeSession_SumsDifferentKinds()
    {
        var perSession = Rule(1, PayrollRuleKind.PerSession, 40_000m);
        var perStudent = Rule(2, PayrollRuleKind.PerAttendedStudent, 5_000m);

        var result = PayrollCalculator.ComputeSession(
            [perSession, perStudent], Context(attended: 6));

        result.BaseAmount.Should().Be(40_000m);
        result.BonusAmount.Should().Be(30_000m);
        result.Total.Should().Be(70_000m);
        result.Lines.Should().HaveCount(2);
        result.RuleMissing.Should().BeFalse();
    }

    /// <summary>
    /// 🔴 BIR XIL TUR QO'SHILMAYDI — faqat ENG ANIQI. Bu dvigatelning eng
    /// muhim qoidasi: aks holda umumiy ("barcha ustozlar") va shaxsiy
    /// qoida birga mos kelib, jimgina IKKI BARAVAR to'lanardi.
    /// </summary>
    [Fact]
    public void ComputeSession_SameKind_PicksOnlyMostSpecific()
    {
        var general = Rule(1, PayrollRuleKind.PerSession, 40_000m);
        var personal = Rule(2, PayrollRuleKind.PerSession, 55_000m, userId: TeacherId);

        var result = PayrollCalculator.ComputeSession([general, personal], Context());

        result.BaseAmount.Should().Be(55_000m);
        result.Lines.Should().ContainSingle();
    }

    /// <summary>★ GURUH XODIMDAN USTUN — "aynan shu guruh uchun" eng tor gap.</summary>
    [Fact]
    public void ComputeSession_GroupRuleBeatsPersonalRule()
    {
        var personal = Rule(1, PayrollRuleKind.PerSession, 55_000m, userId: TeacherId);
        var byGroup = Rule(2, PayrollRuleKind.PerSession, 70_000m, groupId: GroupId);

        PayrollCalculator.ComputeSession([personal, byGroup], Context())
            .BaseAmount.Should().Be(70_000m);
    }

    /// <summary>Kurs bo'yicha ajratish: boshqa kursning qoidasi tushmaydi.</summary>
    [Fact]
    public void ComputeSession_CourseTargeting_FiltersOutOtherCourses()
    {
        var otherCourse = Rule(1, PayrollRuleKind.PerSession, 90_000m, courseId: CourseId + 1);

        PayrollCalculator.ComputeSession([otherCourse], Context())
            .RuleMissing.Should().BeTrue();
    }

    /// <summary>Soatbay: 80 daqiqalik dars / 45 daqiqalik akademik soat.</summary>
    [Fact]
    public void ComputeSession_PerAcademicHour_DividesDurationByAcademicHour()
    {
        var rule = Rule(1, PayrollRuleKind.PerAcademicHour, 30_000m);
        rule.AcademicHourMinutes = 45;

        var result = PayrollCalculator.ComputeSession(
            [rule], Context(durationMinutes: 80));

        // 80 / 45 = 1.78 (2 xonagacha) -> 1.78 * 30 000 = 53 400
        result.BaseAmount.Should().Be(53_400m);
        result.Lines.Single().Basis.Should().Contain("akademik soat");
    }

    /// <summary>
    /// ★ HOLLIHOP FORMULASI (2026-09-09): stavka × o'quvchi × soat, ASOSIY
    /// stavka sifatida (ustama qo'llanadi, «Bonus» emas). Sonlar markazning
    /// 2026-avgust hisobotidan: HolliHop'da 1.5 soatlik dars × 9 901 =
    /// 14 851.5 so'm / o'quvchi / dars; markazda dars 80 daqiqa va akademik
    /// soat 80 — ya'ni bitta dars = 1 soat, 10 o'quvchi → 148 515.
    /// </summary>
    [Fact]
    public void ComputeSession_PerStudentAcademicHour_MultipliesStudentsByHours()
    {
        var rule = Rule(1, PayrollRuleKind.PerStudentAcademicHour, 14_851.5m);
        rule.AcademicHourMinutes = 80;
        rule.WeekendHolidayMultiplier = 1.5m;

        var weekday = PayrollCalculator.ComputeSession(
            [rule], Context(attended: 10, durationMinutes: 80));

        weekday.BaseAmount.Should().Be(148_515m);
        weekday.BonusAmount.Should().Be(0m);
        weekday.Lines.Single().Basis.Should().Contain("10 o'quvchi × 1 akademik soat");

        // Hech kim kelmasa — 0 (HolliHop'dagi 0.01 «minimal» qoldiq YO'Q).
        PayrollCalculator.ComputeSession([rule], Context(attended: 0, durationMinutes: 80))
            .Total.Should().Be(0m);

        // Dam olish kuni ustama ASOSIY stavkaga tushadi: 148 515 × 1.5.
        PayrollCalculator.ComputeSession(
                [rule], Context(attended: 10, durationMinutes: 80, date: Saturday, isWeekendOrHoliday: true))
            .BaseAmount.Should().Be(222_772.5m);
    }

    /// <summary>★ BOSQICHLI: sonidan KICHIK yoki TENG eng katta bosqich olinadi.</summary>
    [Theory]
    [InlineData(0, 20_000)]
    [InlineData(1, 35_000)]
    [InlineData(2, 35_000)]
    [InlineData(3, 60_000)]
    [InlineData(9, 60_000)] // eng yuqori bosqichdan ko'p -> eng yuqori summa
    public void ComputeSession_Tiered_PicksTierAtOrBelowCount(int attended, decimal expected)
    {
        var rule = Rule(1, PayrollRuleKind.TieredByAttendance, 0m);
        rule.Tiers.Add(new PayrollRuleTier { StudentCount = 0, Amount = 20_000m });
        rule.Tiers.Add(new PayrollRuleTier { StudentCount = 1, Amount = 35_000m });
        rule.Tiers.Add(new PayrollRuleTier { StudentCount = 3, Amount = 60_000m });

        PayrollCalculator.ComputeSession([rule], Context(attended: attended))
            .BaseAmount.Should().Be(expected);
    }

    /// <summary>Asos: sababli kelmaganlar ham sanaladigan qoida ularni QO'SHADI.</summary>
    [Fact]
    public void ComputeSession_Basis_AttendedAndExcused_CountsExcused()
    {
        var rule = Rule(1, PayrollRuleKind.PerAttendedStudent, 5_000m);
        rule.Basis = PayrollBasis.AttendedAndExcused;

        PayrollCalculator.ComputeSession([rule], Context(attended: 4, excused: 2))
            .BonusAmount.Should().Be(30_000m);
    }

    /// <summary>Shartlar: o'quvchi soni chegaradan tashqarida bo'lsa qoida qo'llanmaydi.</summary>
    [Fact]
    public void ComputeSession_MinStudents_ExcludesRuleBelowThreshold()
    {
        var rule = Rule(1, PayrollRuleKind.PerSession, 40_000m);
        rule.MinStudents = 3;

        PayrollCalculator.ComputeSession([rule], Context(attended: 2))
            .RuleMissing.Should().BeTrue();

        PayrollCalculator.ComputeSession([rule], Context(attended: 3))
            .BaseAmount.Should().Be(40_000m);
    }

    /// <summary>
    /// ★ USTAMA FAQAT ASOSIY STAVKAGA: dam olish kuni ustozning MEHNATI
    /// qimmatroq, o'quvchi soni emas.
    /// </summary>
    [Fact]
    public void ComputeSession_WeekendMultiplier_AppliesToBaseOnly()
    {
        var perSession = Rule(1, PayrollRuleKind.PerSession, 40_000m);
        perSession.WeekendHolidayMultiplier = 1.5m;

        var perStudent = Rule(2, PayrollRuleKind.PerAttendedStudent, 5_000m);
        perStudent.WeekendHolidayMultiplier = 1.5m;

        var result = PayrollCalculator.ComputeSession(
            [perSession, perStudent],
            Context(attended: 4, date: Saturday, isWeekendOrHoliday: true));

        result.BaseAmount.Should().Be(60_000m);
        result.BonusAmount.Should().Be(20_000m);
        result.MultiplierApplied.Should().Be(1.5m);
    }

    /// <summary>
    /// 🔴 "OKLAD ICHIDA" — haq 0, lekin bu XATO EMAS: <c>RuleMissing</c>
    /// yoqilmaydi, aks holda hisobot har oy soxta "stavka yo'q"
    /// ogohlantirishi berardi.
    /// </summary>
    [Fact]
    public void ComputeSession_IncludedInSalary_ReturnsZeroWithoutMissingFlag()
    {
        var rule = Rule(1, PayrollRuleKind.PerSession, 40_000m);

        var result = PayrollCalculator.ComputeSession(
            [rule], Context(mode: GroupPayrollMode.IncludedInSalary));

        result.Total.Should().Be(0m);
        result.IncludedInSalary.Should().BeTrue();
        result.RuleMissing.Should().BeFalse();
        result.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// QO'LDA MAJBURLASH: maqsad mos kelmasa ham tanlangan qoida ishlaydi
    /// (admin ataylab shuni tanlagan).
    /// </summary>
    [Fact]
    public void ComputeSession_FixedRule_UsesChosenRuleIgnoringTargeting()
    {
        var mine = Rule(1, PayrollRuleKind.PerSession, 40_000m);
        var otherCourse = Rule(2, PayrollRuleKind.PerSession, 90_000m, courseId: CourseId + 1);

        var result = PayrollCalculator.ComputeSession(
            [mine, otherCourse],
            Context(mode: GroupPayrollMode.FixedRule, forcedRuleId: 2));

        result.BaseAmount.Should().Be(90_000m);
    }

    /// <summary>★ Majburlangan qoidada ham SHARTLAR kuchda qoladi.</summary>
    [Fact]
    public void ComputeSession_FixedRule_StillHonoursConditions()
    {
        var rule = Rule(1, PayrollRuleKind.PerSession, 40_000m);
        rule.MinDurationMinutes = 60;

        PayrollCalculator.ComputeSession(
                [rule],
                Context(durationMinutes: 30, mode: GroupPayrollMode.FixedRule, forcedRuleId: 1))
            .RuleMissing.Should().BeTrue();
    }

    /// <summary>Muddati tugagan qoida (<c>ActiveTo</c>) hisobga olinmaydi.</summary>
    [Fact]
    public void ComputeSession_ExpiredRule_IsIgnored()
    {
        var rule = Rule(1, PayrollRuleKind.PerSession, 40_000m);
        rule.ActiveTo = Monday.AddDays(-1);

        PayrollCalculator.ComputeSession([rule], Context())
            .RuleMissing.Should().BeTrue();
    }

    // ================================================================= davr

    /// <summary>Oklad davrga BIR MARTA qo'shiladi.</summary>
    [Fact]
    public void ComputePeriod_FixedMonthly_AddsSalaryOnce()
    {
        var rule = Rule(1, PayrollRuleKind.FixedMonthly, 3_000_000m);

        var lines = PayrollCalculator.ComputePeriod([rule], PeriodContext());

        lines.Should().ContainSingle();
        lines[0].Amount.Should().Be(3_000_000m);
    }

    /// <summary>
    /// ★ KOEFFITSIENT: oy o'rtasida qo'shilgan o'quvchi to'liq emas, ULUSH
    /// bilan sanaladi (2.5 ulush × 100 000).
    /// </summary>
    [Fact]
    public void ComputePeriod_MonthlyPerActiveStudent_UsesWeightedUnits()
    {
        var rule = Rule(1, PayrollRuleKind.MonthlyPerActiveStudent, 100_000m);

        var lines = PayrollCalculator.ComputePeriod(
            [rule], PeriodContext(activeStudents: 3, weightedUnits: 2.5m));

        lines[0].Amount.Should().Be(250_000m);
    }

    /// <summary>Reja bajarilmagan: past foiz qo'llanadi.</summary>
    [Fact]
    public void ComputePeriod_PercentOfRevenue_BelowPlan_UsesLowPercent()
    {
        var rule = PercentRule(plan: 10_000_000m, belowPercent: 5m, reachedPercent: 10m);

        var lines = PayrollCalculator.ComputePeriod(
            [rule], PeriodContext(revenue: 4_000_000m));

        lines[0].Amount.Should().Be(200_000m);
    }

    /// <summary>Reja bajarilgan: yuqori foiz qo'llanadi.</summary>
    [Fact]
    public void ComputePeriod_PercentOfRevenue_AtPlan_UsesHighPercent()
    {
        var rule = PercentRule(plan: 10_000_000m, belowPercent: 5m, reachedPercent: 10m);

        var lines = PayrollCalculator.ComputePeriod(
            [rule], PeriodContext(revenue: 12_000_000m));

        lines[0].Amount.Should().Be(1_200_000m);
    }

    /// <summary>
    /// ★ FOIZ GURUHMA-GURUH moslanadi: bitta ustozda ikki xil foiz qoidasi
    /// bo'lishi mumkin (arab tilida 10%, boshqa kurslarda 5%).
    /// </summary>
    [Fact]
    public void ComputePeriod_PercentOfRevenue_MatchesRulePerGroup()
    {
        var general = PercentRule(id: 1, percent: 5m);
        var arabic = PercentRule(id: 2, percent: 10m, courseId: CourseId);

        var context = PeriodContext(revenue: 3_000_000m, groupRevenues:
        [
            new PayrollGroupRevenue(GroupId, CourseId, null, GroupType.Group, 1_000_000m),
            new PayrollGroupRevenue(GroupId + 1, CourseId + 1, null, GroupType.Group, 2_000_000m),
        ]);

        var lines = PayrollCalculator.ComputePeriod([general, arabic], context);

        lines.Should().HaveCount(2);
        lines.Single(l => l.RuleId == arabic.Id).Amount.Should().Be(100_000m);
        lines.Single(l => l.RuleId == general.Id).Amount.Should().Be(100_000m);
    }

    /// <summary>Dars qamrovidagi qoida davr hisobiga ARALASHMAYDI.</summary>
    [Fact]
    public void ComputePeriod_IgnoresSessionScopedRules()
    {
        var perSession = Rule(1, PayrollRuleKind.PerSession, 40_000m);

        PayrollCalculator.ComputePeriod([perSession], PeriodContext())
            .Should().BeEmpty();
    }

    // ================================================================= yordamchi

    private static PayrollRule Rule(
        long id,
        PayrollRuleKind kind,
        decimal amount,
        long? userId = null,
        long? groupId = null,
        long? courseId = null,
        long? categoryId = null) =>
        new()
        {
            Id = id,
            Name = $"Qoida {id}",
            Kind = kind,
            Amount = amount,
            Role = UserRole.Teacher,
            UserId = userId,
            GroupId = groupId,
            CourseId = courseId,
            CategoryId = categoryId,
            Basis = PayrollBasis.Attended,
            AcademicHourMinutes = 45,
            ActiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
        };

    private static PayrollRule PercentRule(
        long id = 1,
        decimal percent = 5m,
        decimal? plan = null,
        decimal? belowPercent = null,
        decimal? reachedPercent = null,
        long? courseId = null)
    {
        var rule = Rule(id, PayrollRuleKind.PercentOfRevenue, belowPercent ?? percent,
            courseId: courseId);

        rule.PlanAmount = plan;
        rule.PlanReachedPercent = reachedPercent;

        return rule;
    }

    private static PayrollSessionContext Context(
        int attended = 5,
        int excused = 0,
        int enrolled = 8,
        int durationMinutes = 80,
        DateOnly? date = null,
        bool isWeekendOrHoliday = false,
        GroupPayrollMode mode = GroupPayrollMode.Auto,
        long? forcedRuleId = null) =>
        new(
            TeacherId,
            UserRole.Teacher,
            GroupId,
            CourseId,
            CategoryId,
            GroupType.Group,
            mode,
            forcedRuleId,
            durationMinutes,
            date ?? Monday,
            isWeekendOrHoliday,
            attended,
            excused,
            enrolled);

    private static PayrollPeriodContext PeriodContext(
        int activeStudents = 0,
        decimal weightedUnits = 0m,
        decimal revenue = 0m,
        IReadOnlyList<PayrollGroupRevenue>? groupRevenues = null) =>
        new(
            TeacherId,
            UserRole.Teacher,
            new DateOnly(2026, 9, 30),
            activeStudents,
            weightedUnits,
            revenue,
            groupRevenues ?? (revenue > 0
                ? [new PayrollGroupRevenue(GroupId, CourseId, CategoryId, GroupType.Group, revenue)]
                : []));
}
