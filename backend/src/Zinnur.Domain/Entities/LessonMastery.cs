using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// KURATORNING DARS BO'YICHA QARORI (Mustaqil, 2026-11-18)
/// ========================================================================
///
/// Loyiha egasi: «yordamchi birinchi darsni yaxshi o'zlashtirdi deb
/// belgilasa, ikkinchi dars ochilsin … shu qat'iy qoida bo'lishi kerak».
/// Bu jadval o'quvchi×dars juftligi uchun JORIY qarorni saqlaydi; har bir
/// o'zgarish esa alohida, o'chirilmaydigan <see cref="LessonMasteryEvent"/>
/// sifatida yoziladi — "kim, qachon, nima uchun" hech qachon yo'qolmaydi.
///
/// ★ JORIY HOLAT ALOHIDA JADVALDA, chunki gating har so'rovda uni o'qiydi:
///   jurnaldan "oxirgi hodisa"ni topish har dars uchun saralash bo'lardi.
/// </summary>
public class LessonMastery : BaseEntity
{
    public const int MaxNoteLength = 1000;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public long ModuleLessonId { get; set; }

    public ModuleLesson? ModuleLesson { get; set; }

    public MasteryStatus Status { get; set; }

    /// <summary>Kurator izohi. «Qayta ishlasin» da MAJBURIY — o'quvchi nima qilishini bilsin.</summary>
    public string? Note { get; set; }

    public long DecidedById { get; set; }

    public DateTimeOffset DecidedAt { get; set; }

    public static LessonMastery Create(
        long studentId, long moduleLessonId, MasteryStatus status, string? note, long actorId, DateTimeOffset now)
    {
        var mastery = new LessonMastery
        {
            StudentId = studentId,
            ModuleLessonId = moduleLessonId,
            CreatedAt = now,
        };
        mastery.Decide(status, note, actorId, now);
        return mastery;
    }

    public void Decide(MasteryStatus status, string? note, long actorId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status))
            throw new DomainException("Qaror turi noma'lum.");

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
            throw new DomainException($"Izoh {MaxNoteLength} belgidan oshmasin.");
        if (status == MasteryStatus.NeedsWork && trimmed is null)
            throw new DomainException("«Qayta ishlasin» deganda izoh yozing — o'quvchi nimani tuzatishini bilsin.");

        Status = status;
        Note = trimmed;
        DecidedById = actorId;
        DecidedAt = now;
        UpdatedAt = now;
    }
}

/// <summary>
/// O'zlashtirish JURNALI — faqat qo'shiladi, hech qachon o'zgartirilmaydi
/// yoki o'chirilmaydi. Kurator qarorlari ham, administratorning istisno
/// ochishlari ham (sababi bilan) shu yerda.
/// </summary>
public class LessonMasteryEvent : BaseEntity
{
    public const int MaxNoteLength = LessonMastery.MaxNoteLength;

    public long StudentId { get; set; }

    public long ModuleLessonId { get; set; }

    public MasteryEventKind Kind { get; set; }

    public string? Note { get; set; }

    public long ActorId { get; set; }

    public DateTimeOffset At { get; set; }

    public static LessonMasteryEvent Record(
        long studentId, long moduleLessonId, MasteryEventKind kind, string? note, long actorId, DateTimeOffset now) =>
        new()
        {
            StudentId = studentId,
            ModuleLessonId = moduleLessonId,
            Kind = kind,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorId = actorId,
            At = now,
            CreatedAt = now,
        };
}
