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

    /// <summary>
    /// Nomzod rozi bo'ldi — o'quv bo'limi tasdig'i kutiladi. Ustoz hali
    /// ALMASHMAYDI (loyiha egasi qarori, 2026-10-22).
    /// </summary>
    public void AwaitApproval(DateTimeOffset now)
    {
        if (Status != CoverageRequestStatus.Open)
            throw new DomainException("Bu so'rov allaqachon yopilgan.");

        Status = CoverageRequestStatus.AwaitingApproval;
        UpdatedAt = now;
    }

    /// <summary>O'quv bo'limi tasdiqladi — o'rinbosar darsni oladi.</summary>
    public void Resolve(long substituteTeacherId, DateTimeOffset now)
    {
        if (Status is not (CoverageRequestStatus.Open or CoverageRequestStatus.AwaitingApproval))
            throw new DomainException("Bu so'rov allaqachon yopilgan.");

        Status = CoverageRequestStatus.Resolved;
        ResolvedByUserId = substituteTeacherId;
        ResolvedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// O'quv bo'limi rad etdi — dars asl ustozda qoladi. So'rov yopiladi:
    /// boshqa nomzodlarga takliflar allaqachon qaytarib olingan, keyingi
    /// qadam (boshqa ustoz qo'yish yoki darsni bekor qilish) — o'quv
    /// bo'limining qo'lida.
    /// </summary>
    public void Reject(DateTimeOffset now)
    {
        if (Status != CoverageRequestStatus.AwaitingApproval)
            throw new DomainException("Bu so'rov tasdiq kutmayapti.");

        Status = CoverageRequestStatus.Cancelled;
        ResolvedAt = now;
        UpdatedAt = now;
    }
}
