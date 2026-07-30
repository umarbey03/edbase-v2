using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Progress;

namespace Zinnur.UnitTests.Progress;

/// <summary>
/// O'rin berish qoidasi.
///
/// ★ NIMA UCHUN AYNAN BU TESTLAR: eski tizim o'rinni <c>i + 1</c> deb
/// yozardi va teng ballga ega ikki o'quvchi turli o'rin olardi — kim
/// yuqori turishi sort barqarorligiga, ya'ni TASODIFGA bog'liq edi.
/// Quyidagi ikki test aynan shu xatoni qaytib kelishiga yo'l qo'ymaydi.
/// </summary>
public class LeaderboardRankingTests
{
    private static LeaderboardScore Score(long id, string name, decimal attendance) =>
        new(id, name, attendance, null, null);

    [Fact]
    public void HighestTotal_GetsFirstPlace()
    {
        var ranked = LeaderboardRanking.Rank(
        [
            Score(1, "Ali", 40m),
            Score(2, "Vali", 90m),
            Score(3, "Gani", 70m),
        ]);

        ranked.Select(r => r.Score.StudentId).Should().Equal(2, 3, 1);
        ranked.Select(r => r.Rank).Should().Equal(1, 2, 3);
    }

    /// <summary>★ TENG BALL — TENG O'RIN, keyingisi SAKRAYDI (1, 2, 2, 4).</summary>
    [Fact]
    public void EqualTotals_ShareTheSameRank_AndNextRankSkips()
    {
        var ranked = LeaderboardRanking.Rank(
        [
            Score(1, "Ali", 90m),
            Score(2, "Vali", 70m),
            Score(3, "Gani", 70m),
            Score(4, "Dilnoza", 50m),
        ]);

        ranked.Select(r => r.Rank).Should().Equal(1, 2, 2, 4);
    }

    /// <summary>
    /// ★ TARTIB DETERMINISTIK: bir xil ma'lumot HAR DOIM bir xil jadval
    /// beradi. Kirish tartibi teskari bo'lsa ham natija o'zgarmaydi.
    /// </summary>
    [Fact]
    public void SameData_InAnyInputOrder_ProducesIdenticalTable()
    {
        LeaderboardScore[] scores =
        [
            Score(7, "Zebo", 70m),
            Score(3, "Anvar", 70m),
            Score(5, "Anvar", 70m),      // ayni ism, boshqa Id
        ];

        var forward = LeaderboardRanking.Rank(scores);
        var reversed = LeaderboardRanking.Rank(scores.Reverse().ToList());

        forward.Select(r => r.Score.StudentId).Should().Equal(3, 5, 7);
        reversed.Select(r => r.Score.StudentId).Should().Equal(3, 5, 7);
        forward.Select(r => r.Rank).Should().Equal(1, 1, 1);
    }

    [Fact]
    public void EmptyGroup_ProducesEmptyTable()
    {
        LeaderboardRanking.Rank([]).Should().BeEmpty();
    }

    /// <summary>Chegara — noto'g'ri ma'lumotdan kelgan ulkan jadvaldan himoya.</summary>
    [Fact]
    public void TooManyRows_IsRejected()
    {
        var scores = Enumerable
            .Range(1, LeaderboardRanking.MaxRows + 1)
            .Select(i => Score(i, "O'quvchi " + i, i))
            .ToList();

        var act = () => LeaderboardRanking.Rank(scores);

        act.Should().Throw<DomainException>();
    }
}
