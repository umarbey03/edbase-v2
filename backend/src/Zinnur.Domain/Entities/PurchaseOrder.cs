using System.Security.Cryptography;
using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ONLAYN BUYURTMA (2026-10-29) — o'quvchi saytda tarif tanlagan
/// ════════════════════════════════════════════════════════════════════════
///
/// Markazda Payme va Click shartnomasi bor, lekin Merchant API kalitlari
/// hali yo'q: to'lov xabari faqat ularning Telegram guruhiga keladi. Shuning
/// uchun to'lovni XODIM tasdiqlaydi — bot xodimlar guruhiga tugmali xabar
/// yuboradi, kassir Payme/Click xabarini ko'rib «Tasdiqlash» ni bosadi.
/// API kalitlari kelganda xuddi shu yozuvni provayder tasdiqlaydi —
/// buyurtma modeli o'zgarmaydi.
///
/// ★ NARX BUYURTMADA MUZLATILADI (<see cref="Amount"/>, <see cref="Months"/>):
///   tarif narxi keyin o'zgarsa ham o'quvchi ko'rgan summa o'zgarmaydi va
///   kassir AYNAN shu summani qidiradi.
/// ★ O'CHIRILMAYDI, HOLATI O'ZGARADI — moliya izi.
/// </summary>
public class PurchaseOrder : BaseEntity
{
    public const int CodeLength = 8;
    public const int MaxNoteLength = 300;

    /// <summary>
    /// O'quvchi to'lov izohiga yozadigan va kassir qidiradigan kod —
    /// <c>ZN-48273</c>. Unikal indeks bilan himoyalangan.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public long PlanId { get; set; }

    public SubscriptionPlan? Plan { get; set; }

    public PlanKind Kind { get; set; }

    public long CourseId { get; set; }

    public decimal Amount { get; set; }

    public int Months { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Pending;

    /// <summary>Kim tasdiqladi yoki rad etdi. O'quvchi bekor qilsa — <c>null</c>.</summary>
    public long? DecidedById { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Rad etish sababi yoki tasdiqlash izohi.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>Mustaqil o'qish tarifi tasdiqlanganda ochilgan obuna davri.</summary>
    public long? SubscriptionPeriodId { get; set; }

    /// <summary>Guruh tarifi tasdiqlanganda yozilgan moliya jurnali yozuvi.</summary>
    public long? PaymentTransactionId { get; set; }

    public bool IsPending => Status == PurchaseOrderStatus.Pending;

    /// <summary>
    /// Yangi kod: <c>ZN-</c> + 5 raqam. Tasodif kriptografik — ketma-ket
    /// raqam bo'lsa, begona odam qo'shni buyurtma kodini taxmin qila olardi.
    /// </summary>
    public static string NewCode() =>
        "ZN-" + RandomNumberGenerator.GetInt32(10_000, 100_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void Confirm(long actorId, string? note, DateTimeOffset now)
    {
        EnsurePending();
        Status = PurchaseOrderStatus.Confirmed;
        Decide(actorId, note, now);
    }

    public void Reject(long actorId, string? reason, DateTimeOffset now)
    {
        EnsurePending();
        Status = PurchaseOrderStatus.Rejected;
        Decide(actorId, reason, now);
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsurePending();
        Status = PurchaseOrderStatus.Cancelled;
        DecidedAt = now;
        UpdatedAt = now;
    }

    private void Decide(long actorId, string? note, DateTimeOffset now)
    {
        DecidedById = actorId;
        DecidedAt = now;
        UpdatedAt = now;

        var trimmed = note?.Trim();
        DecisionNote = string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }

    private void EnsurePending()
    {
        if (!IsPending)
            throw new DomainException("Buyurtma allaqachon ko'rib chiqilgan.");
    }
}
