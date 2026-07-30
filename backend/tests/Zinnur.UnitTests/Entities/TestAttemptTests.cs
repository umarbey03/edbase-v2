using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// Test urinishi: server tomonda baholash, vaqt chegarasi, begona ID filtri.
/// </summary>
public class TestAttemptTests
{
    private static readonly DateTimeOffset Started =
        new(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Ikki savol: 1-savolda bitta to'g'ri, 2-savolda ikkita.</summary>
    private static List<TestQuestion> TwoQuestions()
    {
        var q1 = new TestQuestion { Id = 1, TestId = 1, Body = "Birinchi", Points = 2 };
        q1.Options.Add(new TestOption { Id = 11, QuestionId = 1, Body = "A", IsCorrect = true });
        q1.Options.Add(new TestOption { Id = 12, QuestionId = 1, Body = "B" });

        var q2 = new TestQuestion { Id = 2, TestId = 1, Body = "Ikkinchi", Points = 3 };
        q2.Options.Add(new TestOption { Id = 21, QuestionId = 2, Body = "A", IsCorrect = true });
        q2.Options.Add(new TestOption { Id = 22, QuestionId = 2, Body = "B", IsCorrect = true });
        q2.Options.Add(new TestOption { Id = 23, QuestionId = 2, Body = "C" });

        return [q1, q2];
    }

    private static TestAttempt NewAttempt() => new()
    {
        Id = 500,
        TestId = 1,
        StudentId = 7,
        Status = AttemptStatus.InProgress,
        StartedAt = Started,
    };

    // ------------------------------------------------------------------ baholash

    [Fact]
    public void SubmitAnswers_WithAllCorrect_ScoresFullMarks()
    {
        var attempt = NewAttempt();

        attempt.SubmitAnswers(
            TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>>
            {
                [1] = [11],
                [2] = [21, 22],
            },
            Started.AddMinutes(5));

        attempt.Score.Should().Be(5m);
        attempt.MaxScore.Should().Be(5m);
        attempt.Percent.Should().Be(100m);
    }

    [Fact]
    public void SubmitAnswers_WithPartialMultiChoice_ScoresOnlyTheSingleQuestion()
    {
        var attempt = NewAttempt();

        attempt.SubmitAnswers(
            TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>>
            {
                [1] = [11],       // to'g'ri -> 2 ball
                [2] = [21],       // qisman -> 0 ball
            },
            Started.AddMinutes(5));

        attempt.Score.Should().Be(2m);
        attempt.MaxScore.Should().Be(5m);
    }

    [Fact]
    public void SubmitAnswers_SetsStatusAndTimestamp()
    {
        var attempt = NewAttempt();
        var submittedAt = Started.AddMinutes(12);

        attempt.SubmitAnswers(TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>>(), submittedAt);

        attempt.Status.Should().Be(AttemptStatus.Submitted);
        attempt.SubmittedAt.Should().Be(submittedAt);
        attempt.IsSubmitted.Should().BeTrue();
    }

    [Fact]
    public void SubmitAnswers_WithNoSelections_ScoresZeroButKeepsMaxScore()
    {
        var attempt = NewAttempt();

        attempt.SubmitAnswers(TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>>(), Started.AddMinutes(3));

        attempt.Score.Should().Be(0m);
        attempt.MaxScore.Should().Be(5m);
    }

    [Fact]
    public void SubmitAnswers_Twice_Throws()
    {
        var attempt = NewAttempt();
        var selections = new Dictionary<long, IReadOnlyCollection<long>> { [1] = [11] };

        attempt.SubmitAnswers(TwoQuestions(), selections, Started.AddMinutes(5));

        var act = () => attempt.SubmitAnswers(TwoQuestions(), selections, Started.AddMinutes(6));

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ ★ begona ID filtri

    /// <summary>
    /// ★ Klient boshqa savolning variant ID'sini yuborsa, u HISOBGA
    /// OLINMASLIGI kerak. Eski tizimda bunday tekshiruv yo'q edi.
    /// </summary>
    [Fact]
    public void SubmitAnswers_WithOptionIdFromAnotherQuestion_IgnoresIt()
    {
        var attempt = NewAttempt();

        attempt.SubmitAnswers(
            TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>>
            {
                // 1-savolga 2-savolning to'g'ri variantini yuboramiz
                [1] = [21],
            },
            Started.AddMinutes(5));

        attempt.Score.Should().Be(0m, "begona variant ball bermasligi kerak");
    }

    [Fact]
    public void SubmitAnswers_WithUnknownOptionId_IgnoresItAndDoesNotThrow()
    {
        var attempt = NewAttempt();

        var act = () => attempt.SubmitAnswers(
            TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>> { [1] = [999_999] },
            Started.AddMinutes(5));

        act.Should().NotThrow();
        attempt.Score.Should().Be(0m);
    }

    [Fact]
    public void SubmitAnswers_WithUnknownQuestionId_IgnoresIt()
    {
        var attempt = NewAttempt();

        attempt.SubmitAnswers(
            TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>>
            {
                [1] = [11],
                [777] = [11],     // mavjud bo'lmagan savol
            },
            Started.AddMinutes(5));

        attempt.Score.Should().Be(2m);
        attempt.Answers.Should().OnlyContain(a => a.QuestionId == 1);
    }

    [Fact]
    public void SubmitAnswers_RecordsOneAnswerRowPerSelectedOption()
    {
        var attempt = NewAttempt();

        attempt.SubmitAnswers(
            TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>>
            {
                [1] = [11],
                [2] = [21, 22],
            },
            Started.AddMinutes(5));

        // Ko'p tanlovda BIR savol uchun BIR NECHTA qator bo'ladi —
        // eski tizimda (attempt, question) unikal edi va bu imkonsiz edi.
        attempt.Answers.Should().HaveCount(3);
        attempt.Answers.Count(a => a.QuestionId == 2).Should().Be(2);
    }

    // ------------------------------------------------------------------ vaqt chegarasi

    /// <summary>
    /// Muddat hisobi uchun test: faqat ikkita maydon ahamiyatli —
    /// <c>TimeLimitMinutes</c> va <c>DueAt</c>.
    /// </summary>
    private static Test TestWith(int? timeLimitMinutes, DateTimeOffset? dueAt) => new()
    {
        Id = 1,
        Title = "Muddat testi",
        TimeLimitMinutes = timeLimitMinutes,
        DueAt = dueAt,
    };

    /// <summary>(e) Hech qanday chegara yo'q — urinish cheksiz ochiq.</summary>
    [Fact]
    public void Deadline_WithoutTimeLimitAndWithoutDueAt_IsNull()
    {
        NewAttempt().Deadline(TestWith(null, null)).Should().BeNull();
    }

    /// <summary>(a) Faqat vaqt chegarasi: boshlanish + limit + tolerantlik.</summary>
    [Fact]
    public void Deadline_WithTimeLimitOnly_IncludesGracePeriod()
    {
        var attempt = NewAttempt();

        attempt.Deadline(TestWith(30, dueAt: null)).Should()
            .Be(Started.AddMinutes(30).Add(Test.SubmitGracePeriod));
    }

    /// <summary>
    /// (b) Faqat muddat: vaqt chegarasi yo'q bo'lsa ham urinish CHEKSIZ EMAS —
    /// `DueAt` dan keyin `EnsureOpenForSubmission` baribir rad etadi, shuning
    /// uchun taymer ham o'sha onni ko'rsatishi kerak.
    /// </summary>
    [Fact]
    public void Deadline_WithDueAtOnly_UsesDueAt()
    {
        var due = Started.AddHours(2);

        NewAttempt().Deadline(TestWith(timeLimitMinutes: null, due)).Should()
            .Be(due.Add(Test.SubmitGracePeriod));
    }

    /// <summary>
    /// ★ (c) ASOSIY BUG. 30 daqiqalik test, muddati boshlanishdan 5 daqiqa
    /// keyin. Ilgari taymer 30 daqiqa ko'rsatardi, server esa 5-daqiqadan
    /// keyingi topshirishni RAD ETARDI — o'quvchi ishini yo'qotardi.
    /// </summary>
    [Fact]
    public void Deadline_WhenDueAtIsEarlierThanTimeLimit_UsesDueAt()
    {
        var due = Started.AddMinutes(5);

        NewAttempt().Deadline(TestWith(30, due)).Should()
            .Be(due.Add(Test.SubmitGracePeriod),
                "muddat vaqt chegarasidan oldin kelsa — test o'sha yerda tugaydi");
    }

    /// <summary>
    /// (d) Teskari holat: muddat uzoq, lekin o'quvchining shaxsiy vaqti
    /// tugaydi. Bu yerda muddat urinishni UZAYTIRMASLIGI kerak.
    /// </summary>
    [Fact]
    public void Deadline_WhenTimeLimitIsEarlierThanDueAt_UsesTimeLimit()
    {
        var due = Started.AddHours(5);

        NewAttempt().Deadline(TestWith(30, due)).Should()
            .Be(Started.AddMinutes(30).Add(Test.SubmitGracePeriod));
    }

    /// <summary>
    /// Taymer va serverning "kech bo'ldi" qarori BIR ONDA ishlashi shart.
    /// Aks holda o'quvchi taymerda vaqt bor deb turib 409 olardi.
    /// </summary>
    [Fact]
    public void Deadline_MatchesTheMomentSubmissionStopsBeingAccepted()
    {
        var due = Started.AddMinutes(5);
        var test = TestWith(30, due);
        test.IsPublished = true;   // aks holda boshqa sabab bilan yiqilardi

        var deadline = NewAttempt().Deadline(test)!.Value;

        // Aynan deadline'da — hali qabul qilinadi.
        test.Invoking(t => t.EnsureOpenForSubmission(deadline))
            .Should().NotThrow();

        // Bir soniya keyin — yopiq.
        test.Invoking(t => t.EnsureOpenForSubmission(deadline.AddSeconds(1)))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void CloseByTimeout_SetsZeroScoreAndMarksFlag()
    {
        var attempt = NewAttempt();
        var closedAt = Started.AddMinutes(45);

        attempt.CloseByTimeout(maxScore: 5m, closedAt);

        attempt.Score.Should().Be(0m);
        attempt.MaxScore.Should().Be(5m);
        attempt.Status.Should().Be(AttemptStatus.Submitted);
        attempt.ClosedByTimeout.Should().BeTrue();
        attempt.SubmittedAt.Should().Be(closedAt);
    }

    /// <summary>Fon vazifasi va o'quvchi bir vaqtda yopishga urinishi mumkin.</summary>
    [Fact]
    public void CloseByTimeout_OnAlreadySubmitted_IsIdempotentAndKeepsScore()
    {
        var attempt = NewAttempt();
        attempt.SubmitAnswers(TwoQuestions(),
            new Dictionary<long, IReadOnlyCollection<long>> { [1] = [11] },
            Started.AddMinutes(5));

        attempt.CloseByTimeout(5m, Started.AddMinutes(40));

        attempt.Score.Should().Be(2m, "topshirilgan urinishning bali o'zgarmaydi");
        attempt.ClosedByTimeout.Should().BeFalse();
    }

    [Fact]
    public void Percent_WhenNotSubmitted_IsNull()
    {
        NewAttempt().Percent.Should().BeNull();
    }
}
