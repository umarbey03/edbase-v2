using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Uy vazifasi. Ikki rejim bor:
///
///  1) KURS vazifasi — <see cref="ModuleLessonId"/> to'ldirilgan, <see cref="GroupId"/>
///     bo'sh. O'quv bo'limi kurs darsiga biriktiradi va u BARCHA guruhlarga
///     taalluqli bo'ladi.
///
///  2) GURUH vazifasi — <see cref="GroupId"/> to'ldirilgan. Ustoz/kurator
///     faqat o'z guruhiga beradi.
///
/// Ikkalasi ham bir vaqtda bo'sh yoki ikkalasi ham to'ldirilgan bo'lishi
/// mumkin emas — <see cref="Validate"/> shuni tekshiradi.
/// </summary>
public class Assignment : BaseEntity
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;

    /// <summary>Guruh vazifasi bo'lsa — guruh; kurs vazifasida <c>null</c>.</summary>
    public long? GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>Kurs vazifasi bo'lsa — kurs darsi; guruh vazifasida <c>null</c>.</summary>
    public long? ModuleLessonId { get; set; }

    public ModuleLesson? ModuleLesson { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Maksimal baho (masalan 5 yoki 100).</summary>
    public decimal MaxScore { get; set; } = 5;

    /// <summary>
    /// Topshirish muddati. <c>null</c> — muddatsiz.
    ///
    /// SERVERDA MAJBURIY tekshiriladi. Eski tizimda `due_at` ustuni bor edi,
    /// lekin uni HECH QAYERDA tekshirilmasdi — o'quvchi muddat tugagandan
    /// keyin ham topshira olardi.
    /// </summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>
    /// Qaysi formatda javob qabul qilinadi. Bayroqlar birlashmasi
    /// (masalan <c>Text | Audio</c> — arab tili talaffuzi uchun).
    /// </summary>
    public AnswerFormats AllowedFormats { get; set; } = AnswerFormats.Text | AnswerFormats.Image;

    /// <summary>Vazifa shartlari rasmi (obyekt kaliti).</summary>
    public string? ImageKey { get; set; }

    public long? CreatedById { get; set; }

    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();

    // ---------------------------------------------------------------- hisoblanuvchi

    public bool IsCourseAssignment => ModuleLessonId is not null;

    /// <summary>Muddat o'tganmi.</summary>
    public bool IsOverdue(DateTimeOffset now) => DueAt is { } due && now > due;

    // ---------------------------------------------------------------- xatti-harakat

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            throw new DomainException("Vazifa sarlavhasi kiritilishi shart.");

        if (Title.Length > MaxTitleLength)
            throw new DomainException($"Sarlavha {MaxTitleLength} belgidan oshmasin.");

        if (Description?.Length > MaxDescriptionLength)
            throw new DomainException($"Tavsif {MaxDescriptionLength} belgidan oshmasin.");

        if (MaxScore <= 0)
            throw new DomainException("Maksimal baho noldan katta bo'lishi kerak.");

        if (AllowedFormats == AnswerFormats.None)
            throw new DomainException("Kamida bitta javob formati tanlanishi kerak.");

        var hasGroup = GroupId is not null;
        var hasLesson = ModuleLessonId is not null;

        if (hasGroup == hasLesson)
            throw new DomainException(
                "Vazifa YOKI guruhga, YOKI kurs darsiga biriktirilishi kerak — ikkalasiga emas.");
    }

    /// <summary>
    /// Berilgan javob shakli ruxsat etilganmi.
    /// Server tomonda tekshiriladi — klient cheklovni chetlab o'ta olmasin.
    /// </summary>
    public void EnsureFormatAllowed(AnswerFormats provided)
    {
        if (provided == AnswerFormats.None)
            throw new DomainException("Javob bo'sh bo'lishi mumkin emas.");

        var extra = provided & ~AllowedFormats;
        if (extra != AnswerFormats.None)
            throw new DomainException($"Bu vazifaga {Describe(extra)} qabul qilinmaydi.");
    }

    private static string Describe(AnswerFormats formats)
    {
        var parts = new List<string>(3);
        if (formats.HasFlag(AnswerFormats.Text)) parts.Add("matn");
        if (formats.HasFlag(AnswerFormats.Image)) parts.Add("rasm");
        if (formats.HasFlag(AnswerFormats.Audio)) parts.Add("audio");
        return string.Join(", ", parts);
    }
}
