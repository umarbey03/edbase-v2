using Zinnur.Application.Payments;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.Finance;

/// <summary>
/// TARIF VA CHEGIRMANI TANLASH QOIDASI (<see cref="BillingSelection"/>).
///
/// Sof funksiya — bazasiz sinaladi (gating qoidasi bilan bir xil naqsh).
/// Aynan shu tanlov o'quvchining oylik summasini belgilaydi: xato tanlov
/// jimgina noto'g'ri narx qo'yadi va buni faqat ota-ona shikoyat qilganda
/// sezish mumkin bo'lardi.
/// </summary>
public class BillingSelectionTests
{
    private static readonly DateOnly Today = new(2026, 7, 1);

    private const long GroupId = 10;
    private const long OtherGroupId = 11;
    private const long CourseId = 5;

    // ---------------------------------------------------------------- tarif

    /// <summary>★ ANIQLIKDAN UMUMIYGA: guruh &gt; kurs &gt; umumiy.</summary>
    [Fact]
    public void PickTariff_PrefersGroupOverCourseOverGeneral()
    {
        var general = Tariff(1, 400_000m);
        var byCourse = Tariff(2, 500_000m, courseId: CourseId);
        var byGroup = Tariff(3, 540_000m, groupId: GroupId);

        BillingSelection.PickTariff([general, byCourse, byGroup], GroupId, CourseId, Today)
            .Should().Be(byGroup);

        BillingSelection.PickTariff([general, byCourse], GroupId, CourseId, Today)
            .Should().Be(byCourse);

        BillingSelection.PickTariff([general], GroupId, CourseId, Today)
            .Should().Be(general);
    }

    /// <summary>
    /// Bir xil aniqlikda ENG YANGI narx tanlanadi — narx tarixi shunday
    /// ishlaydi: eski qator o'zgartirilmaydi, yangisi qo'shiladi.
    /// </summary>
    [Fact]
    public void PickTariff_WithSameSpecificity_TakesTheLatestActiveFrom()
    {
        var older = Tariff(1, 400_000m, groupId: GroupId, activeFrom: new DateOnly(2026, 1, 1));
        var newer = Tariff(2, 540_000m, groupId: GroupId, activeFrom: new DateOnly(2026, 6, 1));

        BillingSelection.PickTariff([older, newer], GroupId, CourseId, Today)
            .Should().Be(newer);
    }

    /// <summary>
    /// ★ Bir KUNDA kiritilgan ikki tarif orasida OXIRGISI tanlanadi.
    /// Bu qoida bo'lmasa tanlov ro'yxat tartibiga tayanardi va bir xil
    /// o'quvchiga har oy boshqa narx tushishi mumkin edi.
    /// </summary>
    [Fact]
    public void PickTariff_WithSameDate_TakesTheLastEntered()
    {
        var first = Tariff(1, 400_000m, groupId: GroupId);
        var second = Tariff(2, 540_000m, groupId: GroupId);

        BillingSelection.PickTariff([second, first], GroupId, CourseId, Today)
            .Should().Be(second);
    }

    /// <summary>Kelasi oydan kuchga kiradigan narx BUGUN tanlanmaydi.</summary>
    [Fact]
    public void PickTariff_IgnoresFutureAndInactiveRows()
    {
        var future = Tariff(1, 900_000m, groupId: GroupId, activeFrom: new DateOnly(2026, 8, 1));
        var disabled = Tariff(2, 800_000m, groupId: GroupId);
        disabled.IsActive = false;

        var current = Tariff(3, 540_000m, groupId: GroupId);

        BillingSelection.PickTariff([future, disabled, current], GroupId, CourseId, Today)
            .Should().Be(current);
    }

    /// <summary>Boshqa guruhga atalgan tarif NOMZOD EMAS.</summary>
    [Fact]
    public void PickTariff_WithNoCandidate_ReturnsNull()
    {
        var foreign = Tariff(1, 540_000m, groupId: OtherGroupId);

        BillingSelection.PickTariff([foreign], GroupId, CourseId, Today)
            .Should().BeNull("tarif topilmasligi XATO emas — chaqiruvchi buni hisobotga yozadi");
    }

    // ---------------------------------------------------------------- chegirma

    /// <summary>Guruhga atalgan chegirma umumiysidan ustun (qo'shilmaydi).</summary>
    [Fact]
    public void PickDiscount_PrefersGroupSpecific()
    {
        var general = Discount(1, 10m);
        var byGroup = Discount(2, 25m, groupId: GroupId);

        BillingSelection.PickDiscount([general, byGroup], GroupId, Today)
            .Should().Be(byGroup);
    }

    /// <summary>Muddati tugagan va boshqa guruhniki — nomzod emas.</summary>
    [Fact]
    public void PickDiscount_IgnoresExpiredAndForeignRows()
    {
        var expired = Discount(1, 50m, validTo: new DateOnly(2026, 6, 30));
        var foreign = Discount(2, 40m, groupId: OtherGroupId);
        var valid = Discount(3, 10m);

        BillingSelection.PickDiscount([expired, foreign, valid], GroupId, Today)
            .Should().Be(valid);
    }

    /// <summary>Chegirma bo'lmasa summa o'zgarmaydi (Domain bilan birga).</summary>
    [Fact]
    public void PickDiscount_WithNoCandidate_LeavesAmountUntouched()
    {
        var picked = BillingSelection.PickDiscount([], GroupId, Today);

        picked.Should().BeNull();

        StudentDiscount.ApplyOrNone(picked, 540_000m).Should().Be((540_000m, 0m));
    }

    // ---------------------------------------------------------------- fabrikalar

    private static Tariff Tariff(
        long id,
        decimal amount,
        long? courseId = null,
        long? groupId = null,
        DateOnly? activeFrom = null) =>
        new()
        {
            Id = id,
            Name = "T" + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Amount = amount,
            CourseId = courseId,
            GroupId = groupId,
            ActiveFrom = activeFrom ?? new DateOnly(2026, 1, 1),
            IsActive = true,
        };

    private static StudentDiscount Discount(
        long id,
        decimal value,
        long? groupId = null,
        DateOnly? validTo = null) =>
        new()
        {
            Id = id,
            StudentId = 1,
            GroupId = groupId,
            Kind = DiscountKind.Percent,
            Value = value,
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = validTo,
            IsActive = true,
        };
}
