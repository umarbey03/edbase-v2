using System.Globalization;
using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Moliya audit izi: KIM, QACHON, NIMANI, NIMADAN-NIMAGA o'zgartirdi.
///
/// NIMA UCHUN ALOHIDA JADVAL: pul bo'yicha nizo bo'lganda "kim bu oyni
/// to'langan qilib qo'ygan?" degan savolga javob kerak. Jurnal
/// (<see cref="PaymentTransaction"/>) faqat PUL harakatini yozadi; kechirim,
/// chegirma berish, summani qo'lda tuzatish kabi amallarda esa pul harakati
/// YO'Q, lekin ular hisobga ta'sir qiladi.
///
/// Yozuv asosiy amal bilan BIR tranzaksiyada saqlanadi: amal bekor bo'lsa
/// audit ham qolmaydi (aks holda bo'lmagan o'zgarish haqida yozuv qolardi).
/// </summary>
public class PaymentAudit : BaseEntity
{
    /// <summary>Qaysi obyekt: <c>payment</c>, <c>balance</c>, <c>discount</c>, <c>tariff</c>.</summary>
    public required string Entity { get; set; }

    public long? EntityId { get; set; }

    public long? StudentId { get; set; }

    /// <summary>Amal: <c>create</c>, <c>update</c>, <c>allocate</c>, <c>reverse</c>, <c>waive</c>.</summary>
    public required string Action { get; set; }

    /// <summary>O'zgargan maydon nomi (bo'lsa).</summary>
    public string? Field { get; set; }

    /// <summary>Eski va yangi qiymat — SATR sifatida (tur bo'yicha universal).</summary>
    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? Note { get; set; }

    public long? ActorId { get; set; }

    /// <summary>Pul o'zgarishini yozish uchun qulay fabrika.</summary>
    public static PaymentAudit Money(
        string entity,
        string action,
        long? entityId,
        long? studentId,
        decimal oldValue,
        decimal newValue,
        DateTimeOffset now,
        long? actorId = null,
        string? note = null) =>
        new()
        {
            Entity = entity,
            Action = action,
            EntityId = entityId,
            StudentId = studentId,
            Field = "amount",
            OldValue = oldValue.ToString(CultureInfo.InvariantCulture),
            NewValue = newValue.ToString(CultureInfo.InvariantCulture),
            Note = note,
            ActorId = actorId,
            CreatedAt = now,
        };
}
