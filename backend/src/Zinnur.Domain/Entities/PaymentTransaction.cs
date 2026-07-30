using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Moliya JURNALI — pul harakatining o'zgarmas yozuvi.
///
/// ★ NIMA UCHUN <see cref="Payment"/> DAN AYRIM: `Payment` — oyning JORIY
/// HOLATI (qancha qoldi), jurnal esa TARIX (kim, qachon, qancha keltirdi).
/// Eski tizimda to'lovni kiritishning ikki xil yo'li bor edi: biri
/// `paid_amount` ni yangilardi, ikkinchisi jurnalga yozardi — natijada ikki
/// manba bir-biriga mos kelmay qolardi va qaysi biri to'g'riligini hech kim
/// bilmasdi. v2 da yozuv YAGONA yo'l bilan kiritiladi va har ikkalasi
/// bitta amalda yangilanadi.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Qaysi guruh bo'yicha. Balansga oid amalda <c>null</c> bo'lishi mumkin.</summary>
    public long? GroupId { get; set; }

    public Group? Group { get; set; }

    public PaymentTransactionKind Kind { get; set; } = PaymentTransactionKind.Payment;

    /// <summary>Summa DOIM MUSBAT — yo'nalishni <see cref="Kind"/> aytadi.</summary>
    public decimal Amount { get; set; }

    /// <summary>Kvitansiya raqami (<c>ZN-2026-07-000123</c>). UNIKAL.</summary>
    public string? ReceiptNo { get; set; }

    /// <summary>
    /// To'lov usuli. Balansdan yopish va kechirimda <c>null</c> —
    /// u yerda kassaga pul TUSHMAGAN.
    /// </summary>
    public PaymentMethod? Method { get; set; }

    public string? Note { get; set; }

    /// <summary>Amalni bajargan xodim.</summary>
    public long? ActorId { get; set; }

    public void Validate()
    {
        if (Amount <= 0)
        {
            // Manfiy summa bilan "teskari" yozuv YOZILMAYDI: qaytarish alohida
            // `Kind` bilan yoziladi. Aks holda jamini hisoblashda ba'zi
            // so'rovlar manfiylarni qo'shib, ba'zilari ayirib yuborardi.
            throw new DomainException("Jurnal summasi musbat bo'lishi kerak.");
        }
    }
}
