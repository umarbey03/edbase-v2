using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// KUNLIK "DARSGA O'TA OLASIZMI?" TASDIQLASH (2026-08-17)
/// ════════════════════════════════════════════════════════════════════════
///
/// Har ustoz, har KUN uchun bitta yozuv — ertalabki fon vazifasi
/// (<c>TeacherMorningCheckinJob</c>) yaratadi va Telegram orqali savol
/// yuboradi. Javob TO'LIQ shu yerda saqlanadi, chunki bot suhbati
/// bosqichma-bosqich (Ha/Yo'q → dars(lar) → sabab → necha kun) va har
/// bosqich orasida server qayta ishga tushishi mumkin — holat Redis'da
/// EMAS, bazada, aks holda konteyner qayta yuklanganda o'quvchi... ya'ni
/// ustoz suhbatning o'rtasida "unutilgan" bo'lib qolardi.
///
/// ★ UNIKAL <c>(TeacherId, CheckinDate)</c>: bir kunga bir savol — vazifa
/// tez-tez yursa ham ikkinchi xabar yubormaydi (idempotentlik,
/// <c>TelegramUpdateHandler</c> dagi bilan AYNI falsafa).
/// </summary>
public class TeacherDailyCheckin : BaseEntity
{
    /// <summary>Sabab uchun eng ko'p belgi.</summary>
    public const int MaxReasonLength = 500;

    public long TeacherId { get; set; }

    public User? Teacher { get; set; }

    /// <summary>Mahalliy sana (markaz vaqt zonasi) — bitta ustoz uchun bir kunga bitta savol.</summary>
    public DateOnly CheckinDate { get; set; }

    public TeacherCheckinStatus Status { get; set; } = TeacherCheckinStatus.Pending;

    public DateTimeOffset SentAt { get; set; }

    /// <summary>Yakuniy javob qachon berilgan (<see cref="TeacherCheckinStatus.Confirmed"/>/<see cref="TeacherCheckinStatus.Declined"/>).</summary>
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>Faqat <see cref="TeacherCheckinStatus.AwaitingDays"/> dan keyin, yakunda to'ldiriladi.</summary>
    public string? DeclineReason { get; set; }

    /// <summary>Necha kunga o'ta olmaydi (1 — faqat bugun). Faqat yakunda to'ldiriladi.</summary>
    public int? UnavailableDays { get; set; }

    /// <summary>
    /// "Yo'q" bosqichida tanlab borilayotgan/tanlangan darslar — bosqichlar
    /// orasida (bot xabarlari orasida) saqlanadigan holat.
    /// </summary>
    public ICollection<TeacherCheckinAffectedSession> AffectedSessions { get; set; } =
        new List<TeacherCheckinAffectedSession>();

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>"Ha, o'taman" — yakuniy holat, boshqa bosqich yo'q.</summary>
    public void Confirm(DateTimeOffset now)
    {
        EnsurePending();
        Status = TeacherCheckinStatus.Confirmed;
        RespondedAt = now;
        UpdatedAt = now;
    }

    /// <summary>"Yo'q" — keyingi bosqichga (dars tanlash) o'tadi.</summary>
    public void StartDecline(DateTimeOffset now)
    {
        EnsurePending();
        Status = TeacherCheckinStatus.SelectingSessions;
        UpdatedAt = now;
    }

    /// <summary>Dars(lar) tanlandi — sabab so'raladi.</summary>
    public void ConfirmSessionSelection(DateTimeOffset now)
    {
        if (Status != TeacherCheckinStatus.SelectingSessions)
            throw new DomainException("Bu bosqichda emas.");

        Status = TeacherCheckinStatus.AwaitingReason;
        UpdatedAt = now;
    }

    /// <summary>Sabab yozildi — necha kunga so'raladi.</summary>
    public void SubmitReason(string reason, DateTimeOffset now)
    {
        if (Status != TeacherCheckinStatus.AwaitingReason)
            throw new DomainException("Hozir sabab kutilmayapti.");

        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
            throw new DomainException("Sababni yozing.");

        DeclineReason = trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed;
        Status = TeacherCheckinStatus.AwaitingDays;
        UpdatedAt = now;
    }

    /// <summary>Necha kunga — YAKUNIY qadam.</summary>
    public void SubmitDays(int days, DateTimeOffset now)
    {
        if (Status != TeacherCheckinStatus.AwaitingDays)
            throw new DomainException("Hozir kun soni kutilmayapti.");

        if (days is < 1 or > 30)
            throw new DomainException("Kun soni 1..30 oralig'ida bo'lishi kerak.");

        UnavailableDays = days;
        Status = TeacherCheckinStatus.Declined;
        RespondedAt = now;
        UpdatedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != TeacherCheckinStatus.Pending)
            throw new DomainException("Bu savolga allaqachon javob berilgan.");
    }
}

/// <summary>
/// "Yo'q" javobida tanlangan bitta dars — <see cref="TeacherDailyCheckin"/>
/// bilan bir vaqtda YASHAYDI (bosqichlar orasidagi vaqtinchalik holat, lekin
/// yakunda ham AYNI ro'yxat "qaysi darslarga o'rinbosar kerak"ni bildiradi).
/// </summary>
public class TeacherCheckinAffectedSession : BaseEntity
{
    public long CheckinId { get; set; }

    public TeacherDailyCheckin? Checkin { get; set; }

    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }
}
