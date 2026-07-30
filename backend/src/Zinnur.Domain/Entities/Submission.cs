using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchining vazifaga topshirgan javobi.
/// <c>(AssignmentId, StudentId)</c> — UNIKAL: bir vazifaga bitta javob.
///
/// QAYTA TOPSHIRISH QOIDASI: o'quvchi bir marta topshiradi. Kurator/ustoz
/// <see cref="AllowResubmit"/> bilan ruxsat bersagina qayta yuborishi mumkin.
/// Qayta topshirilgach ruxsat AVTOMATIK yopiladi — aks holda o'quvchi
/// cheksiz marta yubora olardi.
/// </summary>
public class Submission : BaseEntity
{
    public const int MaxTextLength = 10_000;
    public const int MaxFeedbackLength = 2000;
    public const int MaxAttachments = 5;

    public long AssignmentId { get; set; }

    public Assignment? Assignment { get; set; }

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public string? Text { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Submitted;

    /// <summary>Qo'yilgan baho. <c>null</c> — hali baholanmagan.</summary>
    public decimal? Score { get; set; }

    public string? Feedback { get; set; }

    public long? GradedById { get; set; }

    public DateTimeOffset? GradedAt { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>Nechanchi urinish (1 dan boshlanadi).</summary>
    public int AttemptNumber { get; set; } = 1;

    /// <summary>Kurator qayta topshirishga ruxsat berdimi.</summary>
    public bool AllowResubmit { get; set; }

    /// <summary>Qayta topshirish sababi (o'quvchiga ko'rinadi).</summary>
    public string? ResubmitNote { get; set; }

    /// <summary>Muddatdan keyin topshirilganmi (baholashda hisobga olinadi).</summary>
    public bool IsLate { get; set; }

    public ICollection<SubmissionFile> Files { get; set; } = new List<SubmissionFile>();

    // ---------------------------------------------------------------- hisoblanuvchi

    public bool IsGraded => Status == SubmissionStatus.Graded && Score is not null;

    /// <summary>Baho foizi (reyting uchun). Baholanmagan bo'lsa <c>null</c>.</summary>
    public decimal? ScorePercent(decimal maxScore) =>
        Score is { } score && maxScore > 0
            ? Math.Round(score / maxScore * 100m, 1)
            : null;

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// BIRINCHI topshirish. Yangi obyekt yaratadi.
    ///
    /// NIMA UCHUN ALOHIDA METOD: ilgari bitta `Submit()` bor edi va u
    /// "allaqachon topshirilgan"ni `Id != 0` bilan aniqlardi — ya'ni
    /// SAQLASH holatini (bazada bormi) BIZNES holati (topshirilganmi) bilan
    /// chalkashtirardi. Endi ikki niyat ikki metod bilan ifodalanadi va
    /// chaqiruvchi nima qilayotganini aniq bildiradi.
    /// </summary>
    public static Submission Create(
        long assignmentId, long studentId, string? text, bool isLate, DateTimeOffset now)
    {
        EnsureTextLength(text);

        return new Submission
        {
            AssignmentId = assignmentId,
            StudentId = studentId,
            Text = Normalize(text),
            Status = SubmissionStatus.Submitted,
            SubmittedAt = now,
            IsLate = isLate,
            AttemptNumber = 1,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// QAYTA topshirish. Faqat kurator ruxsat bergan bo'lsa
    /// (<see cref="AllowResubmit"/>) mumkin.
    /// </summary>
    public void Resubmit(string? text, bool isLate, DateTimeOffset now)
    {
        if (!AllowResubmit)
            throw new DomainException(
                "Bu vazifaga javob allaqachon yuborilgan. Qayta yuborish uchun "
                + "kuratoringiz ruxsat berishi kerak.");

        EnsureTextLength(text);

        AttemptNumber++;
        Text = Normalize(text);
        Status = SubmissionStatus.Submitted;
        SubmittedAt = now;
        IsLate = isLate;
        UpdatedAt = now;

        // Qayta topshirilgach ruxsat YOPILADI — aks holda cheksiz yuborish mumkin
        AllowResubmit = false;
        ResubmitNote = null;

        // Yangi javob keldi — eski baho endi haqiqiy emas
        Score = null;
        Feedback = null;
        GradedById = null;
        GradedAt = null;
    }

    private static void EnsureTextLength(string? text)
    {
        if (text?.Length > MaxTextLength)
            throw new DomainException($"Javob matni {MaxTextLength} belgidan oshmasin.");
    }

    private static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Ustoz/kurator baho qo'yadi.</summary>
    public void Grade(decimal score, decimal maxScore, string? feedback, long graderId, DateTimeOffset now)
    {
        if (score < 0 || score > maxScore)
            throw new DomainException($"Baho 0..{maxScore} oralig'ida bo'lishi kerak.");

        if (feedback?.Length > MaxFeedbackLength)
            throw new DomainException($"Izoh {MaxFeedbackLength} belgidan oshmasin.");

        Score = score;
        Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();
        Status = SubmissionStatus.Graded;
        GradedById = graderId;
        GradedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Qayta topshirishga ruxsat beradi (baho tozalanmaydi — tarix qoladi).</summary>
    public void ReopenForResubmit(string? note, DateTimeOffset now)
    {
        AllowResubmit = true;
        ResubmitNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAt = now;
    }
}

/// <summary>Javobga ilova qilingan fayl (rasm yoki ovoz).</summary>
public class SubmissionFile : BaseEntity
{
    public long SubmissionId { get; set; }

    public Submission? Submission { get; set; }

    /// <summary>Obyekt ombori kaliti (R2). To'liq URL SAQLANMAYDI —
    /// u vaqtinchalik (presigned) va o'zgaradi.</summary>
    public required string ObjectKey { get; set; }

    public AttachmentKind Kind { get; set; } = AttachmentKind.Image;

    public long SizeBytes { get; set; }

    public string? ContentType { get; set; }
}
