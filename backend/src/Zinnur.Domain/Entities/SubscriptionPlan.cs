using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// MUSTAQIL O'QISH REJASI (2026-10-23, loyiha egasi talabi): guruhsiz
/// o'quvchi ma'lum summaga N oy davomida bitta kursning video darslarini
/// ko'radi, vazifa va testlarni bajaradi va boshqa mustaqil o'quvchilar
/// orasidagi reytingini ko'radi. Jonli dars, chat va guruh ma'lumotlari
/// UMUMAN kirmaydi — ular guruh a'zoligiga bog'liq va mustaqil o'quvchi
/// hech qachon guruh a'zosi bo'lmaydi.
///
/// ★ 2026-10-29 — ONLAYN SOTUV: reja endi saytdagi TARIF ham. <see cref="Kind"/>
/// = <see cref="PlanKind.Group"/> bo'lsa bu guruhdagi jonli kursning oylik
/// narxi: tasdiqlangan to'lov o'quvchi balansiga tushadi, obuna OCHILMAYDI.
/// </summary>
public class SubscriptionPlan : BaseEntity
{
    public const int MaxNameLength = 100;
    public const int MaxMonths = 12;
    public const int MaxDescriptionLength = 1000;

    public required string Name { get; set; }

    public long CourseId { get; set; }

    public Course? Course { get; set; }

    /// <summary>Necha oyga (1..12).</summary>
    public int Months { get; set; } = 1;

    /// <summary>Rejaning to'liq narxi (so'm) — <see cref="Months"/> uchun.</summary>
    public decimal Price { get; set; }

    /// <summary>O'chirilgan reja yangi obunaga berilmaydi; eskilari saqlanadi.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Tarif turi — mustaqil o'qish yoki guruhdagi jonli kurs.</summary>
    public PlanKind Kind { get; set; } = PlanKind.SelfStudy;

    /// <summary>
    /// Saytdagi taklif matni: har qatori alohida afzallik ("Haftada 3 marta
    /// jonli dars"). <c>null</c> — tarif kartasida faqat nom va narx.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Saytda ko'rinadimi (faol bo'lsa ham ichki reja bo'lishi mumkin).</summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Tarifga interaktiv darslik kiradimi («Mustaqil+», 2026-10-29). Saytdagi
    /// solishtirmada shu belgi bo'yicha «Interaktiv darslik» qatori ✓ bo'ladi;
    /// darslik platformaga qo'shilganda unga kirish ham shu belgiga bog'lanadi.
    /// </summary>
    public bool IncludesInteractiveBook { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
            throw new DomainException($"Reja nomi 1..{MaxNameLength} belgi bo'lishi kerak.");
        if (Months is < 1 or > MaxMonths)
            throw new DomainException($"Muddat 1 dan {MaxMonths} oygacha bo'lishi kerak.");
        if (Price < 0)
            throw new DomainException("Narx manfiy bo'lishi mumkin emas.");
        if (Description is { Length: > MaxDescriptionLength })
            throw new DomainException($"Taklif matni {MaxDescriptionLength} belgidan oshmasin.");
        if (!Enum.IsDefined(Kind))
            throw new DomainException("Tarif turi noma'lum.");
        if (IsPublic && Price <= 0)
            throw new DomainException("Saytdagi tarifning narxi noldan katta bo'lishi kerak.");
    }
}
