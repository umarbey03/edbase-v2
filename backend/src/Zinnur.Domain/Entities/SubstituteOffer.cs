using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// "Bitta nomzod ustozga yuborilgan taklif" — bitta
/// <see cref="SessionCoverageRequest"/> uchun bo'sh topilgan har bir
/// ustozga bittadan yaratiladi. Reyting/saralash YO'Q (MVP) — birinchi
/// rozi bo'lgan darsni oladi, qolganlariga <see cref="SubstituteOfferStatus.Withdrawn"/>.
/// </summary>
public class SubstituteOffer : BaseEntity
{
    public long CoverageRequestId { get; set; }

    public SessionCoverageRequest? CoverageRequest { get; set; }

    public long CandidateTeacherId { get; set; }

    public User? CandidateTeacher { get; set; }

    public SubstituteOfferStatus Status { get; set; } = SubstituteOfferStatus.Sent;

    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset? RespondedAt { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    public void Accept(DateTimeOffset now)
    {
        EnsureSent();
        Status = SubstituteOfferStatus.Accepted;
        RespondedAt = now;
        UpdatedAt = now;
    }

    public void Decline(DateTimeOffset now)
    {
        EnsureSent();
        Status = SubstituteOfferStatus.Declined;
        RespondedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Boshqa nomzod allaqachon rozi bo'lgani uchun — bu taklif endi ma'nosiz.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        if (Status != SubstituteOfferStatus.Sent) return;   // idempotent — allaqachon javob berilgan bo'lsa tegilmaydi

        Status = SubstituteOfferStatus.Withdrawn;
        UpdatedAt = now;
    }

    private void EnsureSent()
    {
        if (Status != SubstituteOfferStatus.Sent)
            throw new DomainException("Bu taklifga allaqachon javob berilgan yoki u bekor qilingan.");
    }
}
