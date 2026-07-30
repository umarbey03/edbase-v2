using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// Uy vazifasiga topshirilgan javob: bir marta topshirish qoidasi,
/// qayta topshirish ruxsati, baholash.
/// </summary>
public class SubmissionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Allaqachon topshirilgan javob (birinchi urinish).</summary>
    private static Submission ExistingSubmission() =>
        Submission.Create(assignmentId: 1, studentId: 7, "Birinchi javob", isLate: false, Now);

    // ------------------------------------------------------------------ birinchi topshirish

    [Fact]
    public void Submit_FirstTime_SetsAttemptNumberToOne()
    {
        var s = Submission.Create(1, 7, "Javobim", isLate: false, Now);

        s.AttemptNumber.Should().Be(1);
        s.Status.Should().Be(SubmissionStatus.Submitted);
        s.SubmittedAt.Should().Be(Now);
    }

    [Fact]
    public void Submit_TrimsText()
    {
        var s = Submission.Create(1, 7, "   javob   ", isLate: false, Now);

        s.Text.Should().Be("javob");
    }

    [Fact]
    public void Submit_WithWhitespaceOnlyText_StoresNull()
    {
        var s = Submission.Create(1, 7, "   ", isLate: false, Now);

        s.Text.Should().BeNull("faqat bo'shliq — matn yo'q hisoblanadi (fayl bo'lishi mumkin)");
    }

    [Fact]
    public void Submit_WithTooLongText_Throws()
    {
        var tooLong = new string('a', Submission.MaxTextLength + 1);

        var act = () => Submission.Create(1, 7, tooLong, isLate: false, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Submit_RecordsLateFlag()
    {
        var s = Submission.Create(1, 7, "kech", isLate: true, Now);

        s.IsLate.Should().BeTrue();
    }

    // ------------------------------------------------------------------ ★ qayta topshirish qoidasi

    /// <summary>
    /// ★ Ruxsatsiz qayta topshirish TAQIQLANADI. Aks holda o'quvchi
    /// baholangandan keyin javobini almashtira olardi.
    /// </summary>
    [Fact]
    public void Submit_SecondTimeWithoutPermission_Throws()
    {
        var s = ExistingSubmission();

        var act = () => s.Resubmit("Ikkinchi javob", isLate: false, Now.AddHours(1));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Submit_SecondTimeWithPermission_IncrementsAttemptNumber()
    {
        var s = ExistingSubmission();
        s.ReopenForResubmit("Xattingiz o'qilmadi", Now.AddMinutes(30));

        s.Resubmit("Ikkinchi javob", isLate: false, Now.AddHours(1));

        s.AttemptNumber.Should().Be(2);
        s.Text.Should().Be("Ikkinchi javob");
    }

    /// <summary>
    /// ★ Qayta topshirilgach ruxsat AVTOMATIK yopiladi — aks holda o'quvchi
    /// cheksiz marta yubora olardi.
    /// </summary>
    [Fact]
    public void Submit_AfterResubmit_ClosesThePermission()
    {
        var s = ExistingSubmission();
        s.ReopenForResubmit("qayta yozing", Now);

        s.Resubmit("Ikkinchi", isLate: false, Now.AddHours(1));

        s.AllowResubmit.Should().BeFalse();
        s.ResubmitNote.Should().BeNull();

        // Uchinchi marta yuborishga urinish rad etiladi
        var act = () => s.Resubmit("Uchinchi", isLate: false, Now.AddHours(2));
        act.Should().Throw<DomainException>();
    }

    /// <summary>Yangi javob kelgach eski baho HAQIQIY EMAS — tozalanadi.</summary>
    [Fact]
    public void Submit_AfterResubmit_ClearsPreviousGrade()
    {
        var s = ExistingSubmission();
        s.Grade(4m, 5m, "yaxshi", graderId: 3, Now.AddMinutes(10));
        s.ReopenForResubmit("qayta", Now.AddMinutes(20));

        s.Resubmit("Ikkinchi", isLate: false, Now.AddMinutes(30));

        s.Score.Should().BeNull();
        s.Feedback.Should().BeNull();
        s.GradedById.Should().BeNull();
        s.GradedAt.Should().BeNull();
        s.Status.Should().Be(SubmissionStatus.Submitted);
    }

    // ------------------------------------------------------------------ baholash

    [Fact]
    public void Grade_SetsScoreStatusAndGrader()
    {
        var s = ExistingSubmission();
        var gradedAt = Now.AddHours(2);

        s.Grade(4.5m, 5m, "  Yaxshi ish  ", graderId: 3, gradedAt);

        s.Score.Should().Be(4.5m);
        s.Feedback.Should().Be("Yaxshi ish");
        s.Status.Should().Be(SubmissionStatus.Graded);
        s.GradedById.Should().Be(3);
        s.GradedAt.Should().Be(gradedAt);
        s.IsGraded.Should().BeTrue();
    }

    [Fact]
    public void Grade_AboveMaxScore_Throws()
    {
        var s = ExistingSubmission();

        var act = () => s.Grade(6m, 5m, null, 3, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Grade_WithNegativeScore_Throws()
    {
        var s = ExistingSubmission();

        var act = () => s.Grade(-1m, 5m, null, 3, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Grade_AtExactlyMaxScore_IsAllowed()
    {
        var s = ExistingSubmission();

        var act = () => s.Grade(5m, 5m, null, 3, Now);

        act.Should().NotThrow();
    }

    [Fact]
    public void Grade_WithTooLongFeedback_Throws()
    {
        var s = ExistingSubmission();
        var tooLong = new string('a', Submission.MaxFeedbackLength + 1);

        var act = () => s.Grade(4m, 5m, tooLong, 3, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ScorePercent_ComputesRoundedPercentage()
    {
        var s = ExistingSubmission();
        s.Grade(4m, 5m, null, 3, Now);

        s.ScorePercent(5m).Should().Be(80m);
    }

    [Fact]
    public void ScorePercent_WhenNotGraded_IsNull()
    {
        ExistingSubmission().ScorePercent(5m).Should().BeNull();
    }

    [Fact]
    public void ScorePercent_WithZeroMaxScore_IsNull()
    {
        var s = ExistingSubmission();
        s.Grade(0m, 5m, null, 3, Now);

        s.ScorePercent(0m).Should().BeNull("nolga bo'linish bo'lmasligi kerak");
    }
}
