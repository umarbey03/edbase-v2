using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// BITTA XODIM — BITTA DAVR — TASDIQLASH/TO'LOV HOLATI (2026-08-16)
/// ============================================================================
///
/// Sanoat standarti (Tutorbase/GetCourse): Draft → Approved → Paid. Admin
/// hisobotni ko'rib chiqadi (<see cref="PayrollApprovalStatus.Approved"/>,
/// summa <see cref="SnapshotTotalAmount"/>ga SURATGA OLINADI), keyin
/// to'lovni amalga oshirgach <see cref="PayrollApprovalStatus.Paid"/> deb
/// belgilaydi. Yozuv topilmasa — davr <c>Draft</c> deb hisoblanadi
/// (`PayrollService` da), ya'ni HAR bir davr uchun oldindan qator yaratish
/// shart emas.
///
/// ★ SNAPSHOT — ULARNI TASDIQLAGANDAN KEYIN <see cref="SessionPayout"/>lar
/// o'zgarsa (masalan kechroq bepul deb belgilansa), joriy hisoblangan summa
/// suratga olingandan farq qilishi mumkin — bu ATAYLAB TAQIQLANMAYDI (davr
/// qulflab qo'yish keraksiz murakkablik), frontend farqni OGOHLANTIRISH
/// sifatida ko'rsatadi.
/// </summary>
public class PayrollApproval : BaseEntity
{
    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Oyning 1-kuni (mahalliy kalendar) — davr identifikatori.</summary>
    public DateOnly PeriodStart { get; set; }

    public PayrollApprovalStatus Status { get; set; } = PayrollApprovalStatus.Draft;

    /// <summary>Tasdiqlash paytidagi jami summa (audit uchun suratga olingan).</summary>
    public decimal SnapshotTotalAmount { get; set; }

    public long? ApprovedById { get; set; }

    public User? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public long? PaidById { get; set; }

    public User? PaidBy { get; set; }

    public DateTimeOffset? PaidAt { get; set; }
}
