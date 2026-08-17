using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// "Bitta <see cref="LiveSession"/>ga o'rinbosar ustoz kerak" — ustoz "yo'q"
/// deb javob bergan har bir ta'sirlangan dars uchun bittadan yaratiladi
/// (<c>TeacherDailyCheckin.SubmitDays</c> yakunidan keyin).
///
/// ★ <see cref="SessionId"/> BAZA DARAJASIDA UNIKAL EMAS (faqat servis
/// darajasida bitta OCHIQ so'rov cheklanadi): tarixiy qayta-so'rovlarga
/// (masalan dars boshqa sababdan yana bekor bo'lsa) joy qoldiriladi.
/// </summary>
public class SessionCoverageRequest : BaseEntity
{
    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }

    public long CheckinId { get; set; }

    public TeacherDailyCheckin? Checkin { get; set; }

    /// <summary>So'rov yaratilgan paytdagi asl ustoz (audit uchun — <c>Session.HostId</c> keyin o'zgarishi mumkin).</summary>
    public long OriginalHostId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public CoverageRequestStatus Status { get; set; } = CoverageRequestStatus.Open;

    public long? ResolvedByUserId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public ICollection<SubstituteOffer> Offers { get; set; } = new List<SubstituteOffer>();

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>Bir nomzod rozi bo'lganda — so'rov yopiladi.</summary>
    public void Resolve(long substituteTeacherId, DateTimeOffset now)
    {
        if (Status != CoverageRequestStatus.Open)
            throw new DomainException("Bu so'rov allaqachon yopilgan.");

        Status = CoverageRequestStatus.Resolved;
        ResolvedByUserId = substituteTeacherId;
        ResolvedAt = now;
        UpdatedAt = now;
    }
}
