using Zinnur.Domain.Progress;

namespace Zinnur.UnitTests.Progress;

/// <summary>
/// ========================================================================
/// REYTING BALI — ADOLAT QOIDASINI QO'RIQLAYDIGAN TESTLAR
/// ========================================================================
///
/// Bu yerdagi eng muhim test — <see cref="MissingCriterion_IsExcluded_NotCountedAsZero"/>.
/// Bo'sh mezonni 0 deb hisoblash "bir qatorlik xato" bo'lib ko'rinadi,
/// lekin oqibati katta: oy boshida hech kimga hali vazifa berilmagan
/// bo'lsa BUTUN guruh bali 33 ga tushib qolardi va reyting ma'nosiz
/// bo'lardi. Shuning uchun qoida test bilan qotirilgan.
/// </summary>
public class LeaderboardScoreTests
{
    [Fact]
    public void AllThreeCriteria_AveragedEqually()
    {
        LeaderboardScore.Combine(90m, 60m, 30m).Should().Be(60m);
    }

    /// <summary>
    /// ★ ASOSIY QOIDA: mavjud bo'lmagan mezon o'rtachaga KIRMAYDI.
    /// Faqat davomat bor va u 80% bo'lsa — yakuniy ball ham 80,
    /// 26.7 (80/3) EMAS.
    /// </summary>
    [Fact]
    public void MissingCriterion_IsExcluded_NotCountedAsZero()
    {
        LeaderboardScore.Combine(80m, null, null).Should().Be(80m);
        LeaderboardScore.Combine(80m, 60m, null).Should().Be(70m);

        // Nazorat: agar bo'sh mezon 0 deb olinsa quyidagi 26.7 bo'lardi.
        LeaderboardScore.Combine(80m, null, null).Should().NotBe(26.7m);
    }

    /// <summary>
    /// HAQIQIY nol ham to'g'ri ishlashi kerak: "vazifa topshirmadim" (0%)
    /// va "vazifa umuman berilmagan" (null) — BOSHQA-BOSHQA holat.
    /// </summary>
    [Fact]
    public void ExplicitZero_DiffersFromNull()
    {
        LeaderboardScore.Combine(90m, 0m, null).Should().Be(45m);
        LeaderboardScore.Combine(90m, null, null).Should().Be(90m);
    }

    [Fact]
    public void NoCriteriaAtAll_IsZero()
    {
        LeaderboardScore.Combine(null, null, null).Should().Be(0m);
    }

    /// <summary>Yaxlitlash bir xonali kasrgacha va "yarmi — yuqoriga".</summary>
    [Theory]
    [InlineData(100, 100, null, 100.0)]
    [InlineData(33.3, 33.3, 33.4, 33.3)]
    [InlineData(82.2, 82.3, null, 82.3)]        // 82.25 -> 82.3 (banker's bo'lsa 82.2 bo'lardi)
    public void Combine_RoundsToOneDecimal_AwayFromZero(
        double attendance, double assignment, double? test, double expected)
    {
        var result = LeaderboardScore.Combine(
            (decimal)attendance, (decimal)assignment, (decimal?)test);

        result.Should().Be((decimal)expected);
    }

    /// <summary>
    /// ★ NOLGA BO'LISH HIMOYASI: shu oyda dars o'tilmagan bo'lsa maxraj 0.
    /// Himoyasiz bu `DivideByZeroException` yoki `NaN` berardi va butun
    /// reyting so'rovi 500 bilan yiqilardi.
    /// </summary>
    [Fact]
    public void Percent_WithZeroMax_IsZero_NotCrash()
    {
        LeaderboardScore.Percent(0m, 0m).Should().Be(0m);
        LeaderboardScore.Percent(5m, 0m).Should().Be(0m);
    }

    [Fact]
    public void Percent_ComputesShare()
    {
        LeaderboardScore.Percent(7m, 9m).Should().Be(77.8m);
        LeaderboardScore.PercentFromRatio(0.8345m).Should().Be(83.5m);
    }

    /// <summary>Yozuv (record) darajasida ham bir xil qoida ishlashi kerak.</summary>
    [Fact]
    public void Total_UsesSameRuleAsCombine()
    {
        var score = new LeaderboardScore(1, "Ali", 100m, null, 50m);

        score.Total.Should().Be(75m);
    }
}
