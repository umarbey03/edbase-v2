using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// QO'LDA TUZATISH — BONUS YOKI USHLAB QOLISH (2026-08-16)
/// ============================================================================
///
/// <see cref="SessionPayout"/> FAQAT dars asosidagi haqni bildiradi — amalda
/// bundan tashqari holatlar chiqadi (bir martalik rag'batlantirish, intizom
/// jarimasi, texnik xato tuzatishi). Sanoat standarti (Tutorbase: "Manual
/// Adjustments... reason tracking for transparency") — har bir tuzatish
/// SABAB bilan yoziladi va KIM/QACHON qo'shgani saqlanadi (audit).
///
/// <see cref="Amount"/> ISHORASI ma'noni bildiradi: musbat — bonus, manfiy —
/// ushlab qolish. Alohida "Kind" enum shart emas — bitta son ikkalasini ham
/// ifodalaydi va yig'indi hisobida ishorasi bilan qo'shiladi.
/// </summary>
public class PayrollAdjustment : BaseEntity
{
    public const int MaxReasonLength = 500;

    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Oyning 1-kuni (mahalliy kalendar) — qaysi davrga tegishli.</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>Musbat — bonus, manfiy — ushlab qolish.</summary>
    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    public long CreatedById { get; set; }

    public User? CreatedBy { get; set; }

    public void Validate()
    {
        if (Amount == 0)
            throw new DomainException("Tuzatish summasi nolga teng bo'lmaydi.");

        if (string.IsNullOrWhiteSpace(Reason))
            throw new DomainException("Tuzatish sababini kiriting.");

        if (Reason.Length > MaxReasonLength)
            throw new DomainException("Tuzatish sababi juda uzun.");
    }
}
