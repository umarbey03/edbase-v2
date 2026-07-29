using System.Security.Cryptography;
using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Jonli (video) dars. LiveKit xonasi bilan bir-biriga mos keladi.
/// Biznes qoidalari shu yerda — servis qatlamida takrorlanmaydi (DRY).
/// </summary>
public class LiveSession : BaseEntity
{
    /// <summary>Darsni rejadan necha daqiqa oldin ochish mumkin.</summary>
    public const int StartLeadMinutes = 5;

    /// <summary>Uzaytirish JAMI chegarasi (daqiqa).</summary>
    public const int MaxExtendMinutes = 10;

    public long GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>Darsni o'tuvchi (ustoz yoki kurator).</summary>
    public long? HostId { get; set; }

    public string? Title { get; set; }

    public SessionType Type { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;

    public DateTimeOffset ScheduledStart { get; set; }

    public DateTimeOffset ScheduledEnd { get; set; }

    public DateTimeOffset? ActualStart { get; set; }

    public DateTimeOffset? ActualEnd { get; set; }

    /// <summary>
    /// LiveKit xona nomi. UNIKAL bo'lishi SHART.
    ///
    /// NIMA UCHUN: eski tizimda xona nomi `g{guruh}-l{tartib}` edi va jadval
    /// qayta tuzilganda tartib noldan sanalardi — ikki dars bir xil nom olib,
    /// webhook `MultipleResultsFound` bilan yiqilardi va davomat butunlay to'xtardi.
    /// </summary>
    public required string RoomName { get; set; }

    public string? RecordingUrl { get; set; }

    /// <summary>Uzaytirilgan daqiqalar (jami, maksimum <see cref="MaxExtendMinutes"/>).</summary>
    public int ExtendedMin { get; set; }

    // ---------------------------------------------------------------- hisoblanuvchi

    public int PlannedDurationMinutes =>
        Math.Max(1, (int)(ScheduledEnd - ScheduledStart).TotalMinutes);

    /// <summary>Dars qachon avtomatik yakunlanishi kerak. Faqat jonli darsda mavjud.</summary>
    public DateTimeOffset? EndsAt =>
        Status != SessionStatus.Live || ActualStart is null
            ? null
            : ActualStart.Value.AddMinutes(PlannedDurationMinutes + ExtendedMin);

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>Takrorlanmas LiveKit xona nomini yaratadi.</summary>
    public static string GenerateRoomName() =>
        $"s-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";

    /// <summary>Darsni boshlaydi (idempotent).</summary>
    public void Start(DateTimeOffset now)
    {
        if (Status == SessionStatus.Ended)
            throw new DomainException("Dars allaqachon yakunlangan.");

        if (Status == SessionStatus.Cancelled)
            throw new DomainException("Bekor qilingan darsni boshlab bo'lmaydi.");

        if (Status == SessionStatus.Scheduled && now < ScheduledStart.AddMinutes(-StartLeadMinutes))
            throw new DomainException(
                $"Darsni boshlanishidan {StartLeadMinutes} daqiqa oldin boshlash mumkin.");

        Status = SessionStatus.Live;

        // MUHIM: faqat BIRINCHI boshlashda yoziladi.
        // Eski tizimda bu shartsiz qayta yozilardi va ustoz "Boshlash"ni qayta
        // bosса dars muddati yana 80 daqiqaga surilib, uzaytirish chegarasi
        // (10 daqiqa) ma'nosiz bo'lib qolardi.
        ActualStart ??= now;

        UpdatedAt = now;
    }

    public void End(DateTimeOffset now)
    {
        if (Status == SessionStatus.Ended) return;   // idempotent

        Status = SessionStatus.Ended;
        ActualEnd = now;
        UpdatedAt = now;
    }

    /// <summary>Darsni uzaytiradi. Haqiqatan qo'shilgan daqiqalarni qaytaradi.</summary>
    public int Extend(int minutes, DateTimeOffset now)
    {
        if (Status != SessionStatus.Live)
            throw new DomainException("Faqat jonli darsni uzaytirish mumkin.");

        if (minutes <= 0)
            throw new DomainException("Uzaytirish musbat bo'lishi kerak.");

        var added = Math.Min(minutes, MaxExtendMinutes - ExtendedMin);
        if (added <= 0)
            throw new DomainException($"Uzaytirish limiti tugagan (maksimum {MaxExtendMinutes} daqiqa).");

        ExtendedMin += added;
        UpdatedAt = now;
        return added;
    }

    /// <summary>Muddati o'tganmi (fon vazifasi avto-yakunlash uchun).</summary>
    public bool IsOverdue(DateTimeOffset now) => EndsAt is { } end && now >= end;
}
