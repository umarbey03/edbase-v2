using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// Test baholash mantiqi.
///
/// ★ Bu fayl eski tizimning IKKI jiddiy bugini qo'riqlaydi:
///
///  1) Ko'p to'g'ri javobli savolda faqat OXIRGI to'g'ri variant hisobga
///     olinardi (`correct_opt[question_id] = option_id` — dict ustiga yozardi).
///     O'quvchi hamma to'g'ri javobni belgilab ham ball olmasligi mumkin edi.
///
///  2) Klient yuborgan variant ID'lari savolga tegishli ekani
///     TEKSHIRILMASDI — boshqa savolning yoki boshqa testning ID'sini
///     yuborib ball olish mumkin edi.
/// </summary>
public class TestQuestionScoringTests
{
    private static TestQuestion Question(decimal points, params bool[] correctFlags)
    {
        var q = new TestQuestion { Id = 100, TestId = 1, Body = "Savol", Points = points };

        for (var i = 0; i < correctFlags.Length; i++)
        {
            q.Options.Add(new TestOption
            {
                Id = 200 + i,
                QuestionId = q.Id,
                Body = $"Variant {i + 1}",
                IsCorrect = correctFlags[i],
                Position = i,
            });
        }

        return q;
    }

    // ------------------------------------------------------------------ bitta to'g'ri javob

    [Fact]
    public void Score_SingleCorrect_WhenChosen_ReturnsFullPoints()
    {
        var q = Question(3m, true, false, false);

        q.Score([200]).Should().Be(3m);
    }

    [Fact]
    public void Score_SingleCorrect_WhenWrongChosen_ReturnsZero()
    {
        var q = Question(3m, true, false, false);

        q.Score([201]).Should().Be(0m);
    }

    [Fact]
    public void Score_SingleCorrect_WhenNothingChosen_ReturnsZero()
    {
        var q = Question(3m, true, false, false);

        q.Score([]).Should().Be(0m);
    }

    // ------------------------------------------------------------------ ★ ko'p to'g'ri javob

    /// <summary>
    /// ★ Eski tizimda bu holat butunlay buzuq edi: uchta to'g'ri variantdan
    /// faqat oxirgisi "to'g'ri" deb saqlanardi va hammasini belgilagan
    /// o'quvchi 0 ball olardi.
    /// </summary>
    [Fact]
    public void Score_MultipleCorrect_WhenAllChosen_ReturnsFullPoints()
    {
        var q = Question(5m, true, true, false, true);

        q.Score([200, 201, 203]).Should().Be(5m);
    }

    [Fact]
    public void Score_MultipleCorrect_WhenOnlySomeChosen_ReturnsZero()
    {
        var q = Question(5m, true, true, false, true);

        // Qisman ball BERILMAYDI — aks holda tasodifiy tanlash rag'batlantiriladi
        q.Score([200, 201]).Should().Be(0m);
    }

    [Fact]
    public void Score_MultipleCorrect_WhenExtraWrongChosen_ReturnsZero()
    {
        var q = Question(5m, true, true, false, false);

        q.Score([200, 201, 202]).Should().Be(0m);
    }

    [Fact]
    public void Score_IgnoresDuplicateSelections()
    {
        var q = Question(4m, true, true, false);

        // Klient bir variantni ikki marta yuborsa ham natija o'zgarmaydi
        q.Score([200, 201, 200]).Should().Be(4m);
    }

    [Fact]
    public void Score_IsOrderInsensitive()
    {
        var q = Question(4m, true, true, false);

        q.Score([201, 200]).Should().Be(4m);
    }

    [Fact]
    public void IsMultipleChoice_WithTwoCorrect_IsTrue()
    {
        Question(1m, true, true, false).IsMultipleChoice.Should().BeTrue();
    }

    [Fact]
    public void IsMultipleChoice_WithOneCorrect_IsFalse()
    {
        Question(1m, true, false, false).IsMultipleChoice.Should().BeFalse();
    }

    [Fact]
    public void CorrectOptionIds_ReturnsEveryCorrectOption()
    {
        var q = Question(1m, true, false, true);

        q.CorrectOptionIds.Should().BeEquivalentTo([200L, 202L]);
    }

    // ------------------------------------------------------------------ validatsiya

    [Fact]
    public void Validate_WithFewerThanTwoOptions_Throws()
    {
        var q = Question(1m, true);

        var act = q.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithNoCorrectOption_Throws()
    {
        var q = Question(1m, false, false);

        var act = q.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithZeroPoints_Throws()
    {
        var q = Question(0m, true, false);

        var act = q.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithEmptyBody_Throws()
    {
        var q = Question(1m, true, false);
        q.Body = "   ";

        var act = q.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithValidQuestion_DoesNotThrow()
    {
        var q = Question(2m, true, false, true);

        var act = q.Validate;

        act.Should().NotThrow();
    }
}
