using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Finance;

namespace Zinnur.UnitTests.Finance;

/// <summary>
/// Davr, tarif, chegirma, kvitansiya raqami va bloklash qoidalari.
/// </summary>
public class BillingRulesTests
{
    private static readonly DateOnly July = new(2026, 7, 15);

    // ------------------------------------------------------------------ davr

    /// <summary>
    /// ★ Eski tizimda davr oddiy satr edi va bir joyda oldiga nol qo'yilmasa
    /// (<c>"2026-7"</c>) tartib buzilardi: satr solishtiruvida
    /// <c>"2026-7" &gt; "2026-12"</c> bo'lib, "eng eski qarz" noto'g'ri
    /// tanlanardi. Endi format bitta joyda qulflangan.
    /// </summary>
    [Fact]
    public void BillingPeriod_FormatsMonthWithLeadingZero()
    {
        BillingPeriod.Create(2026, 7).ToString().Should().Be("2026-07");
    }

    [Fact]
    public void BillingPeriod_ComparesChronologicallyNotAlphabetically()
    {
        var july = BillingPeriod.Parse("2026-07");
        var december = BillingPeriod.Parse("2026-12");

        (july < december).Should().BeTrue("iyul dekabrdan oldin keladi");
    }

    [Fact]
    public void BillingPeriod_AddMonths_CrossesYearBoundary()
    {
        BillingPeriod.Create(2026, 11).AddMonths(3).ToString().Should().Be("2027-02");
        BillingPeriod.Create(2026, 1).AddMonths(-1).ToString().Should().Be("2025-12");
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026")]
    [InlineData("iyul")]
    public void BillingPeriod_Parse_RejectsBadInput(string value)
    {
        var act = () => BillingPeriod.Parse(value);

        act.Should().Throw<DomainException>();
    }

    // ----------------------------------------------------------------- tarif

    [Fact]
    public void Tariff_Specificity_PrefersGroupOverCourseOverGlobal()
    {
        var global = new Tariff { Name = "Umumiy", Amount = 500_000m, ActiveFrom = new DateOnly(2026, 1, 1) };
        var byCourse = new Tariff { Name = "Kurs", Amount = 540_000m, CourseId = 3, ActiveFrom = new DateOnly(2026, 1, 1) };
        var byGroup = new Tariff { Name = "Guruh", Amount = 600_000m, GroupId = 7, ActiveFrom = new DateOnly(2026, 1, 1) };

        byGroup.Specificity.Should().BeGreaterThan(byCourse.Specificity);
        byCourse.Specificity.Should().BeGreaterThan(global.Specificity);
    }

    /// <summary>Kelajakdagi narx bugungi hisobga TA'SIR QILMAYDI (narx tarixi).</summary>
    [Fact]
    public void Tariff_AppliesTo_IgnoresFuturePrice()
    {
        var future = new Tariff
        {
            Name = "Sentabrdan",
            Amount = 700_000m,
            GroupId = 7,
            ActiveFrom = new DateOnly(2026, 9, 1),
        };

        future.AppliesTo(groupId: 7, courseId: 3, on: July).Should().BeFalse();
        future.AppliesTo(groupId: 7, courseId: 3, on: new DateOnly(2026, 9, 1)).Should().BeTrue();
    }

    [Fact]
    public void Tariff_AppliesTo_MatchesOnlyItsOwnGroup()
    {
        var groupTariff = new Tariff
        {
            Name = "Guruh",
            Amount = 600_000m,
            GroupId = 7,
            ActiveFrom = new DateOnly(2026, 1, 1),
        };

        groupTariff.AppliesTo(7, 3, July).Should().BeTrue();
        groupTariff.AppliesTo(8, 3, July).Should().BeFalse();
    }

    // -------------------------------------------------------------- chegirma

    [Fact]
    public void Discount_Percent_ReducesAmount()
    {
        var discount = new StudentDiscount { Kind = DiscountKind.Percent, Value = 20m, StudentId = 42 };

        var (final, cut) = discount.Apply(540_000m);

        cut.Should().Be(108_000m);
        final.Should().Be(432_000m);
    }

    /// <summary>
    /// ★ Chegirma narxdan OSHMAYDI: "200 000 chegirma" 150 000 lik oyga
    /// qo'llanganda tizim markazga qarzdor bo'lib qolmasin.
    /// </summary>
    [Fact]
    public void Discount_LargerThanPrice_NeverProducesNegativeAmount()
    {
        var discount = new StudentDiscount { Kind = DiscountKind.Amount, Value = 200_000m, StudentId = 42 };

        var (final, cut) = discount.Apply(150_000m);

        cut.Should().Be(150_000m);
        final.Should().Be(0m);
    }

    [Fact]
    public void Discount_IsActiveOn_RespectsValidityWindow()
    {
        var discount = new StudentDiscount
        {
            StudentId = 42,
            Kind = DiscountKind.Percent,
            Value = 10m,
            ValidFrom = new DateOnly(2026, 7, 1),
            ValidTo = new DateOnly(2026, 7, 31),
        };

        discount.IsActiveOn(July).Should().BeTrue();
        discount.IsActiveOn(new DateOnly(2026, 8, 1)).Should().BeFalse();
        discount.IsActiveOn(new DateOnly(2026, 6, 30)).Should().BeFalse();
    }

    [Fact]
    public void Discount_ApplyOrNone_WithoutDiscount_KeepsPrice()
    {
        var (final, cut) = StudentDiscount.ApplyOrNone(null, 540_000m);

        final.Should().Be(540_000m);
        cut.Should().Be(0m);
    }

    [Fact]
    public void Discount_Validate_RejectsPercentAbove100()
    {
        var discount = new StudentDiscount { StudentId = 42, Kind = DiscountKind.Percent, Value = 120m };

        var act = discount.Validate;

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------- kvitansiya

    [Fact]
    public void ReceiptNumber_FormatsWithPaddedSequence()
    {
        var receipt = ReceiptNumber.Create(BillingPeriod.Create(2026, 7), 123);

        receipt.ToString().Should().Be("ZN-2026-07-000123");
    }

    [Fact]
    public void ReceiptNumber_Next_RestartsInNewPeriod()
    {
        var july = BillingPeriod.Create(2026, 7);
        var last = ReceiptNumber.Create(july, 42);

        ReceiptNumber.Next(july, last).Sequence.Should().Be(43);
        ReceiptNumber.Next(BillingPeriod.Create(2026, 8), last).Sequence
            .Should().Be(1, "yangi oyda raqam 1 dan boshlanadi");
    }

    [Fact]
    public void ReceiptNumber_Parse_RoundTrips()
    {
        var parsed = ReceiptNumber.Parse("ZN-2026-07-000123");

        parsed.Sequence.Should().Be(123);
        parsed.Period.ToString().Should().Be("2026-07");
    }

    // ---------------------------------------------------------------- bloklash

    /// <summary>Chegaraga TENG qarz bloklamaydi — faqat undan oshgani.</summary>
    [Fact]
    public void BlockPolicy_DebtEqualToThreshold_DoesNotBlock()
    {
        PaymentBlockPolicy.IsBlocked(
            debt: 540_000m, threshold: 540_000m,
            configured: PaymentBlockScope.Platform, requested: PaymentBlockScope.Video,
            exempt: false).Should().BeFalse();
    }

    [Fact]
    public void BlockPolicy_ScopeHierarchy_WorksTopDown()
    {
        // Sozlamada faqat video yopilgan: jonli dars hali ochiq.
        PaymentBlockPolicy.Covers(PaymentBlockScope.Video, PaymentBlockScope.Video).Should().BeTrue();
        PaymentBlockPolicy.Covers(PaymentBlockScope.Video, PaymentBlockScope.Live).Should().BeFalse();

        // Platforma bloki hammasini yopadi.
        PaymentBlockPolicy.Covers(PaymentBlockScope.Platform, PaymentBlockScope.Video).Should().BeTrue();
        PaymentBlockPolicy.Covers(PaymentBlockScope.Platform, PaymentBlockScope.Live).Should().BeTrue();
    }

    [Fact]
    public void BlockPolicy_ExemptStudent_IsNeverBlocked()
    {
        PaymentBlockPolicy.IsBlocked(
            debt: 5_000_000m, threshold: 540_000m,
            configured: PaymentBlockScope.Platform, requested: PaymentBlockScope.Platform,
            exempt: true).Should().BeFalse();
    }

    /// <summary>Yumshoq rejim (sinov muhiti): qarz bo'lsa ham hech kim bloklanmaydi.</summary>
    [Fact]
    public void BlockPolicy_WhenEnforcementIsOff_NobodyIsBlocked()
    {
        PaymentBlockPolicy.IsBlocked(
            debt: 5_000_000m, threshold: 540_000m,
            configured: PaymentBlockScope.Platform, requested: PaymentBlockScope.Platform,
            exempt: false, enforce: false).Should().BeFalse();
    }

    // ------------------------------------------------------------------ balans

    [Fact]
    public void StudentAccount_Withdraw_NeverGoesNegative()
    {
        var account = new StudentAccount { StudentId = 42, Balance = 100_000m };
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        var taken = account.Withdraw(300_000m, now);

        taken.Should().Be(100_000m, "yo'q pulni yechib bo'lmaydi");
        account.Balance.Should().Be(0m, "manfiy balans yashirin qarz bo'lib qolardi");
    }
}
