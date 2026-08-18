using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// JARIMA (2026-08-18) — yaratish va ko'rib chiqish qoidalari.
///
/// ★ ASOSIY DIQQAT: summa QAYERDAN kelishi. Tarif berilsa u tarifdan
/// HISOBLANADI (operator raqamni o'zi yozmaydi), tarifsiz esa qo'lda
/// kiritiladi. Ikki yo'l chalkashsa, bir xil qoidabuzarlikka har safar
/// boshqa summa yozilib, jarima tizimi ishonchini yo'qotardi.
/// </summary>
public class PenaltyTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 18, 9, 15, 0, TimeSpan.Zero);
    private static readonly DateOnly Period = new(2026, 8, 1);

    private static PenaltyCategory LateCategory(decimal rate = 5_000m)
    {
        var category = new PenaltyCategory { Id = 1, SystemKey = PenaltyCategory.LateStartKey };
        category.Apply("Darsga kechikish", rate, perUnit: true, unitLabel: "daqiqa");

        return category;
    }

    private static PenaltyCategory MissedCategory(decimal amount = 200_000m)
    {
        var category = new PenaltyCategory { Id = 2, SystemKey = PenaltyCategory.MissedLessonKey };
        category.Apply("Dars o'tilmadi", amount, perUnit: false, unitLabel: null);

        return category;
    }

    private static PenaltyCategory FlatCategory(decimal amount = 50_000m)
    {
        var category = new PenaltyCategory { Id = 3 };
        category.Apply("Ish kiyimi qoidasi", amount, perUnit: false, unitLabel: null);

        return category;
    }

    // ============================================================ kechikish

    [Fact]
    public void ForLateStart_ComputesFromCategoryRate()
    {
        var penalty = Penalty.ForLateStart(7, 42, lateMinutes: 9, LateCategory(), Moment, Period);

        penalty.Amount.Should().Be(45_000m);
        penalty.Kind.Should().Be(PenaltyKind.LateStart);
        penalty.CategoryId.Should().Be(1);
        penalty.Status.Should().Be(PenaltyStatus.Pending);
    }

    /// <summary>
    /// `LateMinutes` va `Quantity` IKKALASI to'ldiriladi: birinchisi —
    /// tiplangan isbot (hisobotda jamlanadi), ikkinchisi — jadvalda
    /// "9 daqiqa" deb ko'rsatish uchun umumiy maydon.
    /// </summary>
    [Fact]
    public void ForLateStart_FillsBothMinutesAndQuantity()
    {
        var penalty = Penalty.ForLateStart(7, 42, lateMinutes: 9, LateCategory(), Moment, Period);

        penalty.LateMinutes.Should().Be(9);
        penalty.Quantity.Should().Be(9m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void ForLateStart_WithoutPositiveMinutes_Throws(int minutes)
    {
        var act = () => Penalty.ForLateStart(7, 42, minutes, LateCategory(), Moment, Period);

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Tarif `0` — administrator avtomatik jarimani ATAYLAB o'chirgan.
    /// Domen bunday jarimani yaratishga yo'l qo'ymaydi (servis ham
    /// bunday tarifni umuman qaytarmaydi — ikki qatlamli himoya).
    /// </summary>
    [Fact]
    public void ForLateStart_WithZeroRate_Throws()
    {
        var act = () => Penalty.ForLateStart(7, 42, 9, LateCategory(rate: 0m), Moment, Period);

        act.Should().Throw<DomainException>();
    }

    // ============================================================ o'tilmagan dars

    [Fact]
    public void ForMissedLesson_UsesFlatCategoryAmount()
    {
        var penalty = Penalty.ForMissedLesson(7, 42, MissedCategory(), Moment, Period);

        penalty.Amount.Should().Be(200_000m);
        penalty.Kind.Should().Be(PenaltyKind.MissedLesson);
        penalty.CategoryId.Should().Be(2);
    }

    /// <summary>
    /// O'tilmagan darsda "necha daqiqa" o'lchovi MAVJUD EMAS — dars
    /// umuman boshlanmagan. Shuning uchun miqdor bo'sh qolishi kerak.
    /// </summary>
    [Fact]
    public void ForMissedLesson_LeavesQuantityEmpty()
    {
        var penalty = Penalty.ForMissedLesson(7, 42, MissedCategory(), Moment, Period);

        penalty.Quantity.Should().BeNull();
        penalty.LateMinutes.Should().BeNull();
    }

    [Fact]
    public void ForMissedLesson_WithZeroAmount_Throws()
    {
        var act = () => Penalty.ForMissedLesson(7, 42, MissedCategory(amount: 0m), Moment, Period);

        act.Should().Throw<DomainException>();
    }

    // ============================================================ qo'lda

    [Fact]
    public void Manual_WithoutCategory_UsesGivenAmount()
    {
        var penalty = Penalty.Manual(
            7, category: null, quantity: null, amount: 75_000m,
            "Bir martalik holat", createdById: 3, Moment, Period);

        penalty.Amount.Should().Be(75_000m);
        penalty.CategoryId.Should().BeNull();
        penalty.CreatedById.Should().Be(3);
    }

    [Fact]
    public void Manual_WithPerUnitCategory_ComputesFromRate()
    {
        var penalty = Penalty.Manual(
            7, LateCategory(), quantity: 12m, amount: null,
            "12 daqiqa kech", createdById: 3, Moment, Period);

        penalty.Amount.Should().Be(60_000m);
        penalty.Quantity.Should().Be(12m);
    }

    /// <summary>
    /// ★ TARIF USTUN: kategoriya berilganda yuborilgan `amount`
    /// E'TIBORGA OLINMAYDI. Aks holda operator tarifni chetlab o'tib
    /// istalgan summani yozib qo'ya olardi va tarifning ma'nosi
    /// qolmasdi.
    /// </summary>
    [Fact]
    public void Manual_WithCategory_IgnoresProvidedAmount()
    {
        var penalty = Penalty.Manual(
            7, FlatCategory(), quantity: null, amount: 999_999m,
            "Sabab", createdById: 3, Moment, Period);

        penalty.Amount.Should().Be(50_000m);
    }

    /// <summary>Qat'iy tarifda miqdor SAQLANMAYDI — u ma'nosiz.</summary>
    [Fact]
    public void Manual_WithFlatCategory_DoesNotStoreQuantity()
    {
        var penalty = Penalty.Manual(
            7, FlatCategory(), quantity: 5m, amount: null,
            "Sabab", createdById: 3, Moment, Period);

        penalty.Quantity.Should().BeNull();
    }

    [Fact]
    public void Manual_WithPerUnitCategoryButNoQuantity_Throws()
    {
        var act = () => Penalty.Manual(
            7, LateCategory(), quantity: null, amount: null,
            "Sabab", createdById: 3, Moment, Period);

        act.Should().Throw<DomainException>().WithMessage("*daqiqa*");
    }

    [Fact]
    public void Manual_WithZeroRateCategory_Throws()
    {
        var act = () => Penalty.Manual(
            7, FlatCategory(amount: 0m), quantity: null, amount: null,
            "Sabab", createdById: 3, Moment, Period);

        act.Should().Throw<DomainException>();
    }

    // `double?` — `decimal?` ni `InlineData` bevosita qabul qilmaydi.
    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-10d)]
    public void Manual_WithoutCategoryAndPositiveAmount_Throws(double? amount)
    {
        var act = () => Penalty.Manual(
            7, category: null, quantity: null, (decimal?)amount,
            "Sabab", createdById: 3, Moment, Period);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Manual_WithoutReason_Throws(string reason)
    {
        var act = () => Penalty.Manual(
            7, category: null, quantity: null, amount: 10_000m,
            reason, createdById: 3, Moment, Period);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Manual_TruncatesTooLongReason()
    {
        var penalty = Penalty.Manual(
            7, category: null, quantity: null, amount: 10_000m,
            new string('a', 900), createdById: 3, Moment, Period);

        penalty.Reason.Should().HaveLength(Penalty.MaxReasonLength);
    }

    // ============================================================ ko'rib chiqish

    [Fact]
    public void Approve_SetsReviewer()
    {
        var penalty = Penalty.ForMissedLesson(7, 42, MissedCategory(), Moment, Period);
        penalty.Approve(reviewerId: 1, Moment);

        penalty.Status.Should().Be(PenaltyStatus.Approved);
        penalty.ReviewedById.Should().Be(1);
        penalty.ReviewedAt.Should().Be(Moment);
    }

    [Fact]
    public void Cancel_AppendsReasonToText()
    {
        var penalty = Penalty.ForMissedLesson(7, 42, MissedCategory(), Moment, Period);
        penalty.Cancel(reviewerId: 1, "Internet uzilgan", Moment);

        penalty.Status.Should().Be(PenaltyStatus.Cancelled);
        penalty.Reason.Should().Contain("Internet uzilgan");
    }

    /// <summary>
    /// Ikki marta ko'rib chiqish TAQIQLANGAN: tasdiqlangan jarima
    /// allaqachon oylik tuzatmasiga aylangan, uni qayta tasdiqlash
    /// ikkinchi ushlanma yaratardi.
    /// </summary>
    [Fact]
    public void Approve_Twice_Throws()
    {
        var penalty = Penalty.ForMissedLesson(7, 42, MissedCategory(), Moment, Period);
        penalty.Approve(1, Moment);

        var act = () => penalty.Approve(1, Moment);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancel_AfterApprove_Throws()
    {
        var penalty = Penalty.ForMissedLesson(7, 42, MissedCategory(), Moment, Period);
        penalty.Approve(1, Moment);

        var act = () => penalty.Cancel(1, "Xato", Moment);

        act.Should().Throw<DomainException>();
    }
}
