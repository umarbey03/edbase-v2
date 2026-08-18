using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// JARIMA TARIFLARI KATALOGI (2026-08-18) — hisoblash va tekshirish
/// qoidalari.
///
/// ★ NIMA UCHUN AYNAN DOMENDA SINALADI: summa hisobi ikki joydan
/// chaqiriladi (qo'lda kiritish va avtomatik aniqlash). Qoida servisda
/// takrorlansa, biri o'zgarib ikkinchisi eskirib qolardi — va bu
/// JIMGINA noto'g'ri pul ushlanishiga olib kelardi.
/// </summary>
public class PenaltyCategoryTests
{
    private static PenaltyCategory Flat(decimal amount = 50_000m)
    {
        var category = new PenaltyCategory();
        category.Apply("Ish kiyimi qoidasi", amount, perUnit: false, unitLabel: null);

        return category;
    }

    private static PenaltyCategory PerUnit(decimal rate = 5_000m, string unit = "daqiqa")
    {
        var category = new PenaltyCategory();
        category.Apply("Darsga kechikish", rate, perUnit: true, unitLabel: unit);

        return category;
    }

    // ============================================================ hisoblash

    [Fact]
    public void ComputeAmount_Flat_IgnoresQuantity()
    {
        // Qat'iy tarifda miqdor ma'nosiz — u tasodifan yuborilsa ham
        // summa o'zgarmasligi kerak.
        Flat().ComputeAmount(7m).Should().Be(50_000m);
    }

    [Fact]
    public void ComputeAmount_Flat_WithoutQuantity_ReturnsRate()
    {
        Flat().ComputeAmount(null).Should().Be(50_000m);
    }

    [Theory]
    [InlineData(1, 5_000)]
    [InlineData(15, 75_000)]
    [InlineData(2.5, 12_500)]
    public void ComputeAmount_PerUnit_MultipliesRate(decimal quantity, decimal expected)
    {
        PerUnit().ComputeAmount(quantity).Should().Be(expected);
    }

    // `decimal?` — `InlineData` uchun yaroqsiz tur (xUnit `int` ni unga
    // o'girolmaydi), shuning uchun `double?` orqali beriladi.
    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-3d)]
    public void ComputeAmount_PerUnit_WithoutPositiveQuantity_Throws(double? quantity)
    {
        var act = () => PerUnit().ComputeAmount((decimal?)quantity);

        // Xato matnida BIRLIK NOMI bo'lsin — operator "necha daqiqa"
        // deb so'ralayotganini tushunsin.
        act.Should().Throw<DomainException>().WithMessage("*daqiqa*");
    }

    [Fact]
    public void ComputeAmount_PerUnit_WithoutUnitLabel_FallsBackToDona()
    {
        // `Apply` birliksiz `perUnit` ga yo'l qo'ymaydi, lekin bazadan
        // o'qilgan eski yozuvda bo'sh bo'lishi mumkin — xabar baribir
        // o'qiladigan bo'lib qolsin.
        var category = new PenaltyCategory { PerUnit = true, Amount = 1_000m };

        var act = () => category.ComputeAmount(null);

        act.Should().Throw<DomainException>().WithMessage("*dona*");
    }

    // ============================================================ tekshirish

    [Fact]
    public void Apply_TrimsLabel()
    {
        var category = Flat();
        category.Apply("  Kechikish  ", 1_000m, perUnit: false, unitLabel: null);

        category.Label.Should().Be("Kechikish");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_WithoutLabel_Throws(string label)
    {
        var act = () => Flat().Apply(label, 1_000m, perUnit: false, unitLabel: null);

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// ★ `0` — XATO EMAS, O'CHIRGICH: tizim tarifida bu "bu jarima
    /// hozircha yozilmasin" degani. Taqiqlansa, administrator avtomatik
    /// jarimani vaqtincha to'xtata olmasdi.
    /// </summary>
    [Fact]
    public void Apply_ZeroAmount_IsAllowed()
    {
        var category = Flat();
        category.Apply("Kechikish", 0m, perUnit: false, unitLabel: null);

        category.Amount.Should().Be(0m);
    }

    [Fact]
    public void Apply_NegativeAmount_Throws()
    {
        var act = () => Flat().Apply("Kechikish", -1m, perUnit: false, unitLabel: null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Apply_PerUnitWithoutUnitLabel_Throws()
    {
        var act = () => Flat().Apply("Kechikish", 1_000m, perUnit: true, unitLabel: "  ");

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Qat'iy tarifga o'tilganda birlik nomi TOZALANADI — aks holda
    /// jadvalda "50 000 so'm / daqiqa" degan yolg'on yozuv qolardi.
    /// </summary>
    [Fact]
    public void Apply_SwitchingToFlat_ClearsUnitLabel()
    {
        var category = PerUnit();
        category.Apply("Darsga kechikish", 60_000m, perUnit: false, unitLabel: "daqiqa");

        category.PerUnit.Should().BeFalse();
        category.UnitLabel.Should().BeNull();
    }

    [Fact]
    public void Apply_TruncatesTooLongLabel()
    {
        var category = Flat();
        category.Apply(new string('a', 200), 1_000m, perUnit: false, unitLabel: null);

        category.Label.Should().HaveLength(PenaltyCategory.MaxLabelLength);
    }

    // ============================================================ tizim tarifi

    [Fact]
    public void IsSystem_WithoutKey_IsFalse()
    {
        Flat().IsSystem.Should().BeFalse();
    }

    [Theory]
    [InlineData(PenaltyCategory.LateStartKey)]
    [InlineData(PenaltyCategory.MissedLessonKey)]
    public void IsSystem_WithKey_IsTrue(string systemKey)
    {
        var category = Flat();
        category.SystemKey = systemKey;

        category.IsSystem.Should().BeTrue();
    }
}
