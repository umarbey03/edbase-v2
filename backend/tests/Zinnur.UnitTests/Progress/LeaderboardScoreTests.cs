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

    /* ═══════════════════════════════════════════════════════════════════
       R24 · TO'RTINCHI MEZON — DARS BAHOSI

       Qo'shilishning eng katta xavfi — ORQAGA MOSLIK: bu funksiyani
       ishlatmaydigan guruhning bali BIR ZARRA ham o'zgarmasligi kerak.
       Quyidagi birinchi ikkita test aynan shuni qotiradi.
       ═══════════════════════════════════════════════════════════════════ */

    /// <summary>
    /// ★ ORQAGA MOSLIK: dars bahosi yo'q oyda yakuniy ball AVVALGIDEK
    /// qoladi. Mezon `null` bo'lgani uchun o'rtachaga umuman kirmaydi.
    /// </summary>
    [Fact]
    public void LessonCriterion_WhenAbsent_DoesNotChangeExistingTotals()
    {
        LeaderboardScore.Combine(90m, 60m, 30m, lesson: null)
            .Should().Be(LeaderboardScore.Combine(90m, 60m, 30m));

        LeaderboardScore.Combine(80m, null, null, lesson: null).Should().Be(80m);
    }

    /// <summary>
    /// ★ ESKI CHAQIRUVLAR (uch argument) ham AYNAN avvalgi natijani
    /// beradi — to'rtinchi parametr standart `null`.
    /// </summary>
    [Fact]
    public void Combine_WithThreeArguments_StillAveragesThreeCriteria()
    {
        LeaderboardScore.Combine(90m, 60m, 30m).Should().Be(60m);
    }

    /// <summary>To'rtala mezon ham bor — TENG vaznda o'rtachalanadi.</summary>
    [Fact]
    public void AllFourCriteria_AveragedEqually()
    {
        LeaderboardScore.Combine(100m, 80m, 60m, 40m).Should().Be(70m);
    }

    /// <summary>
    /// ★ FAQAT DARS BAHOSI bor holat: oy boshida ustoz baho qo'ydi, lekin
    /// hali dars o'tilmagan, vazifa ham, test ham berilmagan. Yakuniy ball
    /// AYNAN o'sha foiz — 25 (100/4) EMAS.
    /// </summary>
    [Fact]
    public void OnlyLessonCriterion_IsTheWholeScore()
    {
        LeaderboardScore.Combine(null, null, null, 100m).Should().Be(100m);
    }

    /// <summary>
    /// HAQIQIY nol farqlanadi: "darsga 0 qo'yildi" (0%) va "dars bahosi
    /// umuman yo'q" (`null`) — boshqa-boshqa holat.
    /// </summary>
    [Fact]
    public void LessonCriterion_ExplicitZero_DiffersFromNull()
    {
        LeaderboardScore.Combine(90m, null, null, 0m).Should().Be(45m);
        LeaderboardScore.Combine(90m, null, null, null).Should().Be(90m);
    }

    /// <summary>Yozuv darajasida ham to'rtinchi mezon hisobga olinadi.</summary>
    [Fact]
    public void Total_IncludesLessonPercent()
    {
        var score = new LeaderboardScore(1, "Ali", 100m, null, 50m, LessonPercent: 30m);

        score.Total.Should().Be(60m);
    }

    /// <summary>
    /// ★ YOZUVDA MAYDON KO'RSATILMASA — mezon YO'Q (0 emas). Pozitsion
    /// standart qiymat aynan shu ma'noni beradi.
    /// </summary>
    [Fact]
    public void Total_WithoutLessonPercent_MatchesThreeCriteriaScore()
    {
        var score = new LeaderboardScore(1, "Ali", 100m, null, 50m);

        score.LessonPercent.Should().BeNull();
        score.Total.Should().Be(75m);
    }
}
