using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// MUSTAQIL O'QISH REJASI (2026-10-23, loyiha egasi talabi): guruhsiz
/// o'quvchi ma'lum summaga N oy davomida bitta kursning video darslarini
/// ko'radi, vazifa va testlarni bajaradi va boshqa mustaqil o'quvchilar
/// orasidagi reytingini ko'radi. Jonli dars, chat va guruh ma'lumotlari
/// UMUMAN kirmaydi — ular guruh a'zoligiga bog'liq va mustaqil o'quvchi
/// hech qachon guruh a'zosi bo'lmaydi.
/// </summary>
public class SubscriptionPlan : BaseEntity
{
    public const int MaxNameLength = 100;
    public const int MaxMonths = 12;

    public required string Name { get; set; }

    public long CourseId { get; set; }

    public Course? Course { get; set; }

    /// <summary>Necha oyga (1..12).</summary>
    public int Months { get; set; } = 1;

    /// <summary>Rejaning to'liq narxi (so'm) — <see cref="Months"/> uchun.</summary>
    public decimal Price { get; set; }

    /// <summary>O'chirilgan reja yangi obunaga berilmaydi; eskilari saqlanadi.</summary>
    public bool IsActive { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
            throw new DomainException($"Reja nomi 1..{MaxNameLength} belgi bo'lishi kerak.");
        if (Months is < 1 or > MaxMonths)
            throw new DomainException($"Muddat 1 dan {MaxMonths} oygacha bo'lishi kerak.");
        if (Price < 0)
            throw new DomainException("Narx manfiy bo'lishi mumkin emas.");
    }
}
