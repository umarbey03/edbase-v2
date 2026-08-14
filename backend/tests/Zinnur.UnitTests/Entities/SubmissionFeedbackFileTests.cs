using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// R37 — ustozning tekshiruv faylining Domain qoidalari.
///
/// ★ QULFLANADIGAN ENG MUHIM NARSA — TUZILISH QARORI: tekshiruv fayli
/// o'quvchining javob fayllari (<see cref="Submission.Files"/>) ichiga
/// TUSHMAYDI. Aynan shu chalkashlik `SubmissionFile` ga "yo'nalish" ustuni
/// qo'shilganda yuz berardi va u ikki joyda BIZNES qarorini buzardi:
/// javob formati tekshiruvi (`AnswerFormats`) va 5 ta fayl chegarasi.
/// </summary>
public class SubmissionFeedbackFileTests
{
    /// <summary>
    /// 🔴 IKKI KOLLEKSIYA ARALASHMAYDI. Test arzon ko'rinadi, lekin u
    /// aynan o'sha "bir jadval, ikki ma'no" dizayniga qaytishni bloklaydi:
    /// birlashtirilsa bu tasdiq qizarardi.
    /// </summary>
    [Fact]
    public void FeedbackFiles_AreSeparateFromStudentFiles()
    {
        var submission = new Submission { AssignmentId = 1, StudentId = 2 };

        submission.Files.Add(new SubmissionFile
        {
            ObjectKey = "submissions/2/a.jpg",
            Kind = AttachmentKind.Image,
            SizeBytes = 100,
        });

        submission.FeedbackFiles.Add(new SubmissionFeedbackFile
        {
            SubmissionId = 1,
            ObjectKey = "submission-feedback/b.pdf",
            ContentType = "application/pdf",
            Kind = AttachmentKind.Document,
            SizeBytes = 200,
        });

        submission.Files.Should().HaveCount(1);
        submission.FeedbackFiles.Should().HaveCount(1);
    }

    /// <summary>
    /// Chegara o'quvchi tomonidagi bilan AYNI — ikki tomon uchun bir xil
    /// qoidani tushuntirish oson.
    /// </summary>
    [Fact]
    public void MaxPerSubmission_MatchesStudentSide()
    {
        SubmissionFeedbackFile.MaxPerSubmission.Should().Be(Submission.MaxAttachments);
    }

    [Fact]
    public void Validate_WithoutSubmission_Throws()
    {
        var file = new SubmissionFeedbackFile
        {
            ObjectKey = "submission-feedback/a.pdf",
            ContentType = "application/pdf",
            SizeBytes = 10,
        };

        var act = file.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithoutContentType_Throws()
    {
        var file = new SubmissionFeedbackFile
        {
            SubmissionId = 1,
            ObjectKey = "submission-feedback/a.pdf",
            ContentType = "   ",
            SizeBytes = 10,
        };

        var act = file.Validate;

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Validate_WithNonPositiveSize_Throws(long size)
    {
        var file = new SubmissionFeedbackFile
        {
            SubmissionId = 1,
            ObjectKey = "submission-feedback/a.pdf",
            ContentType = "application/pdf",
            SizeBytes = size,
        };

        var act = file.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithValidRow_DoesNotThrow()
    {
        var file = new SubmissionFeedbackFile
        {
            SubmissionId = 1,
            Kind = AttachmentKind.Document,
            ObjectKey = "submission-feedback/a.pdf",
            ContentType = "application/pdf",
            FileName = "tuzatilgan-varaq.pdf",
            SizeBytes = 4096,
            CreatedById = 9,
        };

        var act = file.Validate;

        act.Should().NotThrow();
    }

    /// <summary>
    /// ⚠️ QAYTA TOPSHIRISH TEKSHIRUV FAYLLARIGA TEGMAYDI.
    ///
    /// `Resubmit` bahoni va izohni tozalaydi (eski baho endi haqiqiy emas),
    /// lekin fayllarni O'CHIRMAYDI: ular ombordagi obyektlarga bog'langan
    /// va ularni tozalash faqat servis yo'lidan (ombor bilan birga) borishi
    /// mumkin. Test bu xatti-harakatni ATAYLAB qulflaydi — kelajakda
    /// `Resubmit` ichiga `FeedbackFiles.Clear()` qo'shilsa, u qatorlarni
    /// yo'qotib, R2'da yetim obyekt qoldirardi.
    /// </summary>
    [Fact]
    public void Resubmit_DoesNotDropFeedbackFiles()
    {
        var now = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

        var submission = Submission.Create(1, 2, "javob", isLate: false, now);
        submission.FeedbackFiles.Add(new SubmissionFeedbackFile
        {
            SubmissionId = submission.Id,
            ObjectKey = "submission-feedback/a.pdf",
            ContentType = "application/pdf",
            SizeBytes = 10,
        });

        submission.ReopenForResubmit("xatingiz o'qilmadi", now);
        submission.Resubmit("yangi javob", isLate: false, now);

        submission.FeedbackFiles.Should().HaveCount(1);
        submission.Score.Should().BeNull();
    }
}
