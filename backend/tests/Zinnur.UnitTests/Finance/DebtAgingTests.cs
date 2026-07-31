using Zinnur.Application.Payments;
using Zinnur.Domain.Finance;

namespace Zinnur.UnitTests.Finance;

/// <summary>
/// QARZ YOSHI GURUHLARINING CHEGARALARI.
///
/// NIMA UCHUN ALOHIDA UNIT TEST: chegara — bu qoidaning eng oson buziladigan
/// joyi. <c>&lt;</c> va <c>&lt;=</c> ni almashtirib qo'yish har guruhning
/// oxirgi kunini keyingi guruhga surib yuborardi, hisobot esa baribir
/// "chiroyli" ko'rinardi — xato faqat kassir ma'lum bir qarzni
/// qidirmaganda sezilmasdi.
///
/// Bu yerda haqiqiy baza kerak emas: qoida — sof funksiya.
/// </summary>
public class DebtAgingTests
{
    /// <summary>Hisobot qaysi kunga olinayotgani. Barcha yoshlar shundan sanaladi.</summary>
    private static readonly DateOnly AsOf = new(2026, 7, 31);

    // ------------------------------------------------------- ★ chegaralar

    /// <summary>
    /// ★ 30 kun — HALI birinchi guruhda, 31 kun — ikkinchisida.
    /// 60/61 va 90/91 ham xuddi shunday.
    /// </summary>
    [Theory]
    [InlineData(0, "0-30")]
    [InlineData(1, "0-30")]
    [InlineData(30, "0-30")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "61-90")]
    [InlineData(90, "61-90")]
    [InlineData(91, "90+")]
    [InlineData(365, "90+")]
    public void IndexOf_PutsBoundaryDaysIntoTheExpectedBucket(int days, string expected)
    {
        var index = DebtAging.IndexOf(days);

        DebtAging.Buckets[index].Key.Should().Be(expected);
    }

    /// <summary>
    /// KELAJAK OYI (yoshi manfiy) eng yangi guruhga tushadi.
    ///
    /// Bu ataylab: hisob oldindan ochilgan bo'lsa ham qator hisobdan
    /// TUSHIB QOLMASLIGI kerak — aks holda guruhlar yig'indisi umumiy
    /// qarzga teng bo'lmay qolardi va ikki raqamning farqi tushuntirib
    /// bo'lmas bo'lardi.
    /// </summary>
    [Fact]
    public void IndexOf_WithNegativeAge_FallsIntoTheNewestBucket()
    {
        DebtAging.Buckets[DebtAging.IndexOf(-15)].Key.Should().Be("0-30");
    }

    // ------------------------------------------------------- yosh hisobi

    /// <summary>Yosh HISOB OYINING BIRINCHI KUNIDAN sanaladi, oxiridan emas.</summary>
    [Fact]
    public void AgeInDays_CountsFromTheFirstDayOfTheBillingMonth()
    {
        DebtAging.AgeInDays(AsOf, BillingPeriod.Parse("2026-07")).Should().Be(30);
        DebtAging.AgeInDays(AsOf, BillingPeriod.Parse("2026-06")).Should().Be(60);
        DebtAging.AgeInDays(AsOf, BillingPeriod.Parse("2026-05")).Should().Be(91);
    }

    /// <summary>
    /// 2026-07-31 sanasida iyul 30 kunlik (0-30), iyun 60 kunlik (31-60),
    /// may esa 91 kunlik (90+) bo'ladi — ya'ni davr satridan guruhga
    /// o'tish ham chegaraga rioya qiladi.
    /// </summary>
    [Theory]
    [InlineData("2026-07", "0-30")]
    [InlineData("2026-06", "31-60")]
    [InlineData("2026-05", "90+")]
    [InlineData("2025-01", "90+")]
    public void IndexOf_FromPeriodText_MatchesTheDayBoundaries(string period, string expected)
    {
        DebtAging.Buckets[DebtAging.IndexOf(AsOf, period)].Key.Should().Be(expected);
    }

    // ------------------------------------------------------- guruhlar ro'yxati

    /// <summary>
    /// Guruhlar KESISHMAYDI va oraliqda BO'SHLIQ qoldirmaydi: birining
    /// yuqori chegarasidan keyingi kun — keyingisining quyi chegarasi.
    /// Aks holda ba'zi qarz hech qaysi guruhga tushmasdi.
    /// </summary>
    [Fact]
    public void Buckets_AreContiguousAndDoNotOverlap()
    {
        DebtAging.Buckets.Should().HaveCount(4);
        DebtAging.Buckets[0].MinDays.Should().Be(0);
        DebtAging.Buckets[^1].MaxDays.Should().BeNull("oxirgi guruh cheksiz (90+)");

        for (var i = 1; i < DebtAging.Buckets.Count; i++)
        {
            DebtAging.Buckets[i].MinDays
                .Should().Be(DebtAging.Buckets[i - 1].MaxDays!.Value + 1);
        }
    }
}
