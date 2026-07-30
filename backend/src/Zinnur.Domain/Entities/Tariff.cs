using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Oylik narx. Narx TARIXI saqlanadi: eski qatorlar o'zgartirilmaydi, yangi
/// narx yangi qator sifatida <see cref="ActiveFrom"/> bilan kiritiladi.
///
/// NIMA UCHUN: narx qatorini joyida tahrirlash o'tmishdagi hisobotlarni
/// jimgina qayta yozardi — yanvarda 400 000 bo'lgan oy, iyulda narx
/// ko'tarilgach, 540 000 bo'lib ko'rinardi. Endi o'tmish o'zgarmaydi va
/// narxni OLDINDAN kiritib qo'yish mumkin (kelasi oydan kuchga kiradi).
/// </summary>
public class Tariff : BaseEntity
{
    public required string Name { get; set; }

    /// <summary>Oylik summa (so'm). Pul DOIM <c>decimal</c>.</summary>
    public decimal Amount { get; set; }

    /// <summary>Oyiga necha dars — chek/kvitansiyada ko'rsatiladi.</summary>
    public int LessonsCount { get; set; } = 8;

    /// <summary>Aniq kursga atalgan bo'lsa. <c>null</c> — barcha kurslar.</summary>
    public long? CourseId { get; set; }

    public Course? Course { get; set; }

    /// <summary>Aniq guruhga atalgan bo'lsa. <c>null</c> — barcha guruhlar.</summary>
    public long? GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>Shu sanadan kuchga kiradi (mahalliy kalendar).</summary>
    public DateOnly ActiveFrom { get; set; }

    public bool IsActive { get; set; } = true;

    // ---------------------------------------------------------------- qoidalar

    /// <summary>
    /// Tanlash ANIQLIKDAN UMUMIYGA: guruh &gt; kurs &gt; umumiy.
    ///
    /// Bu raqam Domain'da turadi, chunki tartib QOIDA — uni SQL <c>ORDER BY</c>
    /// ichida yozib qo'yish tanlov mantig'ini repozitoriyga yashirardi va
    /// test qilib bo'lmasdi.
    /// </summary>
    public int Specificity => GroupId is not null ? 2 : CourseId is not null ? 1 : 0;

    /// <summary>Shu guruh/kurs uchun nomzod bo'la oladimi (sana ham hisobga olinadi).</summary>
    public bool AppliesTo(long groupId, long? courseId, DateOnly on)
    {
        if (!IsActive || ActiveFrom > on) return false;
        if (GroupId is not null) return GroupId == groupId;
        if (CourseId is not null) return courseId is not null && CourseId == courseId;
        return true;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("Tarif nomi kiritilishi shart.");

        if (Amount < 0)
            throw new DomainException("Tarif summasi manfiy bo'lmaydi.");

        if (LessonsCount is < 1 or > 60)
            throw new DomainException("Darslar soni 1..60 oralig'ida bo'lishi kerak.");
    }
}
