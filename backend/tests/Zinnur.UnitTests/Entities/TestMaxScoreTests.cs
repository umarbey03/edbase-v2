using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <c>Test.MaxScore</c> — maksimal ball.
///
/// ★ Bu fayl JIMGINA NOL tuzog'ini qo'riqlaydi.
///
/// `Questions` — navigatsiya to'plami. Test uni `Include` qilmasdan
/// o'qilsa to'plam bo'sh keladi va eski hisob `0` qaytarardi. Ya'ni
/// "savollar yuklanmagan" va "testda savol yo'q" holatlari bir xil
/// qiymat berardi, chaqiruvchi esa foizni `Score / 0` deb hisoblab
/// nolga bo'linish yoki har doim "o'tdi" natijasini olardi.
///
/// Endi bo'sh to'plam BALAND OVOZDA yiqiladi — bug baho bosqichida emas,
/// aynan sababi yonida ko'rinadi.
/// </summary>
public class TestMaxScoreTests
{
    private static Test WithPoints(params decimal[] points)
    {
        var test = new Test { Id = 1, Title = "Nazorat ishi" };

        for (var i = 0; i < points.Length; i++)
        {
            var question = new TestQuestion
            {
                Id = 100 + i,
                TestId = test.Id,
                Body = $"Savol {i + 1}",
                Points = points[i],
                Position = i,
            };

            question.Options.Add(new TestOption
            {
                Id = 200 + i, QuestionId = question.Id, Body = "A", IsCorrect = true,
            });
            question.Options.Add(new TestOption
            {
                Id = 300 + i, QuestionId = question.Id, Body = "B",
            });

            test.Questions.Add(question);
        }

        return test;
    }

    [Fact]
    public void MaxScore_SumsQuestionPoints()
    {
        WithPoints(2m, 3m, 1.5m).MaxScore.Should().Be(6.5m);
    }

    [Fact]
    public void MaxScore_WithSingleQuestion_IsThatQuestionsPoints()
    {
        WithPoints(4m).MaxScore.Should().Be(4m);
    }

    /// <summary>
    /// ★ ASOSIY HIMOYA: savolsiz testda 0 EMAS, xato.
    ///
    /// Amalda bu deyarli har doim "Include unutildi" degani — e'lon
    /// qilingan testda savol bo'lishi <see cref="Test.Publish"/> tomonidan
    /// kafolatlangan.
    /// </summary>
    [Fact]
    public void MaxScore_WithoutQuestions_ThrowsInsteadOfReturningZero()
    {
        var test = new Test { Id = 1, Title = "Savolsiz" };

        test.Invoking(t => t.MaxScore)
            .Should().Throw<DomainException>()
            .WithMessage("*savolsiz*");
    }

    /// <summary>
    /// Xato xabari SABABNI aytadi: "0 chiqdi" emas, "savollar yuklanmagan
    /// bo'lishi mumkin". Log'ni o'qigan odam nima qilishni darhol biladi.
    /// </summary>
    [Fact]
    public void MaxScore_WithoutQuestions_ErrorPointsAtTheLikelyCause()
    {
        var test = new Test { Id = 1, Title = "Savolsiz" };

        test.Invoking(t => t.MaxScore)
            .Should().Throw<DomainException>()
            .WithMessage("*Include*");
    }
}
