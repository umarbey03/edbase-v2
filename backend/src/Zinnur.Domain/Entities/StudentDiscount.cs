using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchi chegirmasi: foizda yoki qat'iy summada, muddatli yoki muddatsiz.
///
/// ★ CHEGIRMALAR QO'SHILMAYDI. Bir nechta amaldagi chegirma bo'lsa BITTASI
/// tanlanadi (guruhga atalgani ustunroq, so'ng eng yangisi). Bu ataylab:
/// qo'shilib ketsa summa manfiyga tushib qolishi va nazorat qilib
/// bo'lmasligi mumkin — eski tizim ham shu qoidani ushlagan.
/// </summary>
public class StudentDiscount : BaseEntity
{
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Aniq guruhga atalgan bo'lsa. <c>null</c> — barcha guruhlarga.</summary>
    public long? GroupId { get; set; }

    public Group? Group { get; set; }

    public DiscountKind Kind { get; set; } = DiscountKind.Percent;

    /// <summary>Foiz (0..100) yoki summa (so'm) — <see cref="Kind"/> ga qarab.</summary>
    public decimal Value { get; set; }

    public DateOnly ValidFrom { get; set; }

    /// <summary><c>null</c> — muddatsiz.</summary>
    public DateOnly? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Reason { get; set; }

    // ---------------------------------------------------------------- qoidalar

    /// <summary>Guruhga atalgani umumiydan ustun — tanlashda shu raqam ishlatiladi.</summary>
    public int Specificity => GroupId is not null ? 1 : 0;

    public bool IsActiveOn(DateOnly date) =>
        IsActive && ValidFrom <= date && (ValidTo is null || ValidTo >= date);

    /// <summary>
    /// Chegirmani qo'llaydi va (yakuniy summa, chegirma summasi) juftini qaytaradi.
    ///
    /// ★ CHEGIRMA HECH QACHON NARXDAN OSHMAYDI va natija MANFIY bo'lmaydi:
    /// "200 000 so'm chegirma" 150 000 lik oyga qo'llanganda tizim markazga
    /// 50 000 qarzdor bo'lib qolmasin. Eski tizim ham shu yerda
    /// <c>max(0, min(cut, base))</c> qilgan.
    ///
    /// YAXLITLASH: natija butun so'mga yaxlitlanadi (tiyin amalda ishlatilmaydi)
    /// va yaxlitlash FAQAT shu yerda bo'ladi — hisob-kitobning boshqa
    /// bosqichlarida yaxlitlansa, oylar yig'indisi jamiga teng bo'lmay qolardi.
    /// </summary>
    public (decimal Final, decimal Cut) Apply(decimal baseAmount)
    {
        if (baseAmount < 0)
            throw new DomainException("Asosiy summa manfiy bo'lmaydi.");

        var raw = Kind == DiscountKind.Percent
            ? baseAmount * Value / 100m
            : Value;

        var cut = Math.Round(Math.Clamp(raw, 0m, baseAmount), 0, MidpointRounding.AwayFromZero);
        return (baseAmount - cut, cut);
    }

    /// <summary>Chegirmasiz hisob — chaqiruvchi shart yozmasin uchun.</summary>
    public static (decimal Final, decimal Cut) ApplyOrNone(StudentDiscount? discount, decimal baseAmount)
    {
        if (baseAmount < 0)
            throw new DomainException("Asosiy summa manfiy bo'lmaydi.");

        return discount is null ? (baseAmount, 0m) : discount.Apply(baseAmount);
    }

    public void Validate()
    {
        if (Value <= 0)
            throw new DomainException("Chegirma qiymati musbat bo'lishi kerak.");

        if (Kind == DiscountKind.Percent && Value > 100)
            throw new DomainException("Foizli chegirma 100 dan oshmaydi.");

        if (ValidTo is not null && ValidTo < ValidFrom)
            throw new DomainException("Chegirma tugash sanasi boshlanish sanasidan oldin bo'lmaydi.");
    }
}
