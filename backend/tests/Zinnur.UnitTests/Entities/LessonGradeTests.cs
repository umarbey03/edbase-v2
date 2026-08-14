using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// ========================================================================
/// DARS BAHOSI (R24) — INVARIANTNI QO'RIQLAYDIGAN TESTLAR
/// ========================================================================
///
/// Bu yerdagi eng muhim test — <see cref="Apply_WithScoreAboveMax_Throws"/>.
/// "Baho maxrajdan katta" bir qarashda bezarar ko'rinadi, lekin oqibati
/// reytingga chiqadi: foiz 100 dan oshadi va "har mezon 0..100" invarianti
/// buzilib, yakuniy ball 100 dan katta bo'lib qoladi.
/// </summary>
public class LessonGradeTests
{
    private static readonly DateTimeOffset At1900 =
        new(2026, 5, 14, 19, 0, 0, TimeSpan.Zero);

    private static LessonGrade NewGrade() => new() { SessionId = 1, StudentId = 42 };

    // ------------------------------------------------------------------ shkala

    /// <summary>
    /// ★ MAXRAJ KO'RSATILMASA standart shkala (5) ishlatiladi — ustoz
    /// oynada har safar "maksimal ball" tanlashi shart emas.
    /// </summary>
    [Fact]
    public void Apply_WithoutMaxScore_UsesDefaultScale()
    {
        var grade = NewGrade();

        grade.Apply(4m, maxScore: null, comment: null, graderId: 7, At1900);

        grade.MaxScore.Should().BeNull("tanlanmagan shkala qatorda saqlanmaydi");
        grade.EffectiveMaxScore.Should().Be(LessonGrade.DefaultMaxScore);
        grade.Percent.Should().Be(80m);
    }

    /// <summary>Imtihon darsi — 100 ballik shkala.</summary>
    [Fact]
    public void Apply_WithExplicitMaxScore_UsesIt()
    {
        var grade = NewGrade();

        grade.Apply(87m, maxScore: 100m, comment: null, graderId: 7, At1900);

        grade.EffectiveMaxScore.Should().Be(100m);
        grade.Percent.Should().Be(87m);
    }

    /// <summary>
    /// Foiz `Submission.ScorePercent` bilan AYNI yaxlitlash qoidasida —
    /// ikki mezon reytingda bir xil o'qilishi kerak.
    /// </summary>
    [Fact]
    public void Percent_RoundsToOneDecimal()
    {
        var grade = NewGrade();

        grade.Apply(2m, maxScore: 3m, comment: null, graderId: 7, At1900);

        grade.Percent.Should().Be(66.7m);
    }

    /// <summary>
    /// ★ 0 — HAQIQIY baho ("bajarmadi"), "baho yo'q" EMAS. "Baho yo'q"
    /// holati qatorning O'ZI bo'lmasligi bilan ifodalanadi.
    /// </summary>
    [Fact]
    public void Apply_WithZeroScore_IsAllowed_AndIsZeroPercent()
    {
        var grade = NewGrade();

        grade.Apply(0m, maxScore: null, comment: null, graderId: 7, At1900);

        grade.Score.Should().Be(0m);
        grade.Percent.Should().Be(0m);
    }

    // ------------------------------------------------------------------ ★ invariant

    /// <summary>
    /// ★ ENG MUHIM TEST: ball maxrajdan katta bo'lolmaydi.
    ///
    /// Himoyasiz 6/5 = 120% bo'lardi va reytingdagi yakuniy ball
    /// 100 dan oshib ketardi — jadval "0..100" shkalasida o'qilmay
    /// qolardi.
    /// </summary>
    [Fact]
    public void Apply_WithScoreAboveMax_Throws()
    {
        var grade = NewGrade();

        var act = () => grade.Apply(6m, maxScore: 5m, comment: null, graderId: 7, At1900);

        act.Should().Throw<DomainException>();
    }

    /// <summary>Maxraj berilmaganda ham chegara STANDART shkala bo'yicha.</summary>
    [Fact]
    public void Apply_WithScoreAboveDefaultScale_Throws()
    {
        var grade = NewGrade();

        var act = () => grade.Apply(10m, maxScore: null, comment: null, graderId: 7, At1900);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Apply_WithNegativeScore_Throws()
    {
        var grade = NewGrade();

        var act = () => grade.Apply(-1m, maxScore: null, comment: null, graderId: 7, At1900);

        act.Should().Throw<DomainException>();
    }

    /// <summary>Nol yoki manfiy maxraj — nolga bo'lishning oldini olish.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Apply_WithNonPositiveMaxScore_Throws(int maxScore)
    {
        var grade = NewGrade();

        var act = () => grade.Apply(0m, maxScore, comment: null, graderId: 7, At1900);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Apply_WithTooLongComment_Throws()
    {
        var grade = NewGrade();
        var comment = new string('x', LessonGrade.MaxCommentLength + 1);

        var act = () => grade.Apply(5m, null, comment, graderId: 7, At1900);

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ izoh va iz

    /// <summary>
    /// ★ TO'LIQ ALMASHTIRISH: izohsiz qayta baholash avvalgi izohni
    /// O'CHIRADI. "Saqlab qol" ma'nosi bo'lsa, noto'g'ri izohni olib
    /// tashlashning umuman yo'li bo'lmasdi.
    /// </summary>
    [Fact]
    public void Apply_WithoutComment_ClearsPreviousComment()
    {
        var grade = NewGrade();
        grade.Apply(3m, null, "uy ishini qilmagan", graderId: 7, At1900);

        grade.Apply(5m, null, comment: null, graderId: 7, At1900.AddDays(1));

        grade.Comment.Should().BeNull();
    }

    /// <summary>Bo'sh/probel izoh — izoh YO'Q (bazada bo'sh satr qolmasin).</summary>
    [Fact]
    public void Apply_WithWhitespaceComment_StoresNull()
    {
        var grade = NewGrade();

        grade.Apply(5m, null, "   ", graderId: 7, At1900);

        grade.Comment.Should().BeNull();
    }

    [Fact]
    public void Apply_TrimsComment()
    {
        var grade = NewGrade();

        grade.Apply(5m, null, "  faol  ", graderId: 7, At1900);

        grade.Comment.Should().Be("faol");
    }

    /// <summary>
    /// ★ QAYTA BAHOLASH IZI: `GradedById` va `GradedAt` OXIRGI qarorni
    /// ko'rsatadi. To'liq tarix `LessonGradeAudit` da — bu maydonlar
    /// "hozir kim javobgar" savoliga JOIN'siz javob beradi.
    /// </summary>
    [Fact]
    public void Apply_Twice_KeepsOnlyLastGraderAndTime()
    {
        var grade = NewGrade();
        grade.Apply(3m, null, null, graderId: 7, At1900);

        var later = At1900.AddDays(2);
        grade.Apply(5m, null, null, graderId: 9, later);

        grade.Score.Should().Be(5m);
        grade.GradedById.Should().Be(9);
        grade.GradedAt.Should().Be(later);
        grade.UpdatedAt.Should().Be(later);
    }
}
