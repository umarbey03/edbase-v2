using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Mustaqil o'quvchining BITTA sotib olingan davri (2026-10-23).
///
/// ★ HAR TO'LOV — ALOHIDA QATOR: uzaytirish mavjud qatorni o'zgartirmaydi,
///   yangisini avvalgisining oxiridan boshlab qo'shadi. Shu tufayli to'lovlar
///   tarixi va "kim, qachon, qancha qabul qildi" o'z-o'zidan audit izi.
/// ★ KIRISH HUQUQI — <see cref="StartsAt"/> ≤ hozir &lt; <see cref="EndsAt"/>
///   va bekor qilinmagan qator bor bo'lsa.
/// </summary>
public class SubscriptionPeriod : BaseEntity
{
    public const int MaxNoteLength = 500;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Qaysi rejadan; reja keyin o'zgarsa ham shu qatordagi narx va kurs saqlanadi.</summary>
    public long? PlanId { get; set; }

    public SubscriptionPlan? Plan { get; set; }

    public long CourseId { get; set; }

    public Course? Course { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Qabul qilingan summa (so'm).</summary>
    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public string? Note { get; set; }

    public long CreatedById { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public long? CancelledById { get; set; }

    public string? CancelReason { get; set; }

    public bool IsCancelled => CancelledAt is not null;

    public bool CoversNow(DateTimeOffset now) => !IsCancelled && StartsAt <= now && now < EndsAt;
}
