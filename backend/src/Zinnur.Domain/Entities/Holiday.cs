using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// BAYRAM KUNI (2026-08-16) — UMUMIY KALENDAR
/// ============================================================================
///
/// Talab (loyiha egasi): *"bizda ba'zi bayram kunlari e'lon qilinadi, bu
/// kunlari darslar ham o'tilmaydi ... 8 oylik dars qoldirilganiga qarab
/// surilishi kerak oldinga, va bundan tashqari o'tilmagan dars uchun
/// o'quvchidan pul yechib olinmasligi kerak"*.
///
/// ★ BITTA SANA = BUTUN PLATFORMA. Har guruh alohida emas — o'quv/admin
/// bo'limi bitta sanani e'lon qilganda BARCHA guruhlarning o'sha kundagi
/// darsi bekor qilinadi (<c>HolidayService.CreateAsync</c>). Bu qaror
/// so'zma-so'z talabga mos ("bayram kunlari e'lon qilinadi" — markazlashgan
/// e'lon, guruh-guruh emas).
///
/// ★ O'CHIRISH RETROAKTIV TIKLAMAYDI — allaqachon bekor qilingan darslar
/// bekor bo'lib qoladi (<c>Tariff</c> versiyalash falsafasi bilan AYNI:
/// tarix qayta yozilmaydi). Xato sana qo'shilgan bo'lsa, tegishli darsni
/// alohida <c>LiveSessionsController</c> orqali qo'lda bekor qilingan
/// holicha qoldirish yoki yangi guruh bilan murojaat qilish kerak bo'ladi.
/// </summary>
public class Holiday : BaseEntity
{
    /// <summary>Bayram sanasi (mahalliy kalendar kuni) — UNIKAL.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Nomi: "Mustaqillik kuni", "Navro'z" va h.k.</summary>
    public required string Label { get; set; }

    /// <summary>Kim e'lon qilgani (o'quv bo'limi/admin xodimi).</summary>
    public long CreatedById { get; set; }

    public const int MaxLabelLength = 150;

    /// <summary>
    /// Invariant. Servis buni undan OLDIN 400 bilan tutadi
    /// (<c>HolidayService.RequireLabel</c>); bu yerdagi tekshiruv
    /// servisdan tashqari yo'llarni qo'riqlaydi.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Label))
            throw new DomainException("Bayram nomi kiritilishi shart.");

        if (Label.Length > MaxLabelLength)
            throw new DomainException("Bayram nomi juda uzun.");
    }
}
