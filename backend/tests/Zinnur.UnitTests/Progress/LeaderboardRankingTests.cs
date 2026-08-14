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

    /// <summary>
    /// Chegara — noto'g'ri ma'lumotdan kelgan ulkan jadvaldan himoya.
    /// ★ Bu chegara GURUH jadvaliga tegishli va SHUNDAY QOLADI (pastdagi
    /// markaz testlariga qarang).
    /// </summary>
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

    // ================================================================= markaz

    /// <summary>
    /// ★ MARKAZ JADVALI 500 QATORDAN OSHGANDA YIQILMAYDI (2026-08-13 qarori).
    ///
    /// Bu test QAROR QULFI: 500 dan ko'p faol o'quvchili o'quv markaz —
    /// mutlaqo normal holat, va u <c>DomainException</c> (409) bermasligi
    /// kerak. Kesish esa RUXSAT qatlamida emas, javob qurishda bo'ladi
    /// (<c>LeaderboardService.CenterTopRows</c>).
    ///
    /// Agar kimdir markaz yo'lini yana <see cref="LeaderboardRanking.Rank"/>
    /// ga ulab qo'ysa, shu test qizaradi.
    /// </summary>
    [Fact]
    public void RankAll_AboveGroupLimit_DoesNotThrow()
    {
        var scores = Enumerable
            .Range(1, LeaderboardRanking.MaxRows + 21)
            .Select(i => Score(i, "O'quvchi " + i, i % 100))
            .ToList();

        var ranked = LeaderboardRanking.RankAll(scores);

        ranked.Should().HaveCount(LeaderboardRanking.MaxRows + 21);
    }

    /// <summary>
    /// ★ O'RIN TO'LIQ RO'YXATDAN HISOBLANADI, KESILGANIDAN EMAS.
    ///
    /// Markaz jadvalida javob TOP-N gacha qisqartiriladi, lekin o'rin
    /// undan OLDIN beriladi. Aks holda 101-o'rindagi o'quvchi kesilgan
    /// ro'yxatning birinchisi bo'lib "1-o'rin" olardi.
    ///
    /// Bu yerda eng past balli o'quvchi 520 ta o'quvchidan OXIRGISI —
    /// ya'ni uning o'rni 520, kesish esa bunga TA'SIR QILMAYDI.
    /// </summary>
    [Fact]
    public void RankAll_KeepsTruePosition_ForRowsOutsideTheTop()
    {
        const int total = 520;

        // Ball i (1..520) — hammasi TURLICHA, ya'ni teng o'rin yo'q.
        var scores = Enumerable
            .Range(1, total)
            .Select(i => Score(i, "O'quvchi " + i, i))
            .ToList();

        var ranked = LeaderboardRanking.RankAll(scores);

        ranked[0].Rank.Should().Be(1);
        ranked[0].Score.StudentId.Should().Be(total, "eng yuqori ball — 1-o'rin");

        var last = ranked[^1];
        last.Rank.Should().Be(total);
        last.Score.StudentId.Should().Be(1);

        // Kesilgan ko'rinishda ham o'rinlar 1..100 bo'lib qoladi.
        ranked.Take(100).Select(r => r.Rank).Should().Equal(Enumerable.Range(1, 100));
    }

    /// <summary>Teng ball qoidasi chegarasiz yo'lda ham AYNI (1, 2, 2, 4).</summary>
    [Fact]
    public void RankAll_AppliesTheSameTieRule()
    {
        var ranked = LeaderboardRanking.RankAll(
        [
            Score(1, "Ali", 90m),
            Score(2, "Vali", 70m),
            Score(3, "Gani", 70m),
            Score(4, "Dilnoza", 50m),
        ]);

        ranked.Select(r => r.Rank).Should().Equal(1, 2, 2, 4);
    }
}
