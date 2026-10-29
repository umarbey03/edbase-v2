using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Darslikning bitta birligi (dars yoki imtihon), 2026-10-29.
///
/// ★ MAVJUD DARSGA BOG'LANADI (<see cref="ModuleLessonId"/>), alohida yo'l
///   emas: gating, sur'at, obuna muddati — hammasi darsning o'zidan keladi.
///   Bog'lanmagan birlik (<c>null</c>) o'quvchiga ko'rinmaydi.
///
/// ★ KONTENT VERSIYALANADI: har import o'zgargan birlik uchun yangi
///   <see cref="InteractiveUnitVersion"/> yozadi, <see cref="CurrentVersionId"/>
///   unga ko'chadi. O'quvchi natijalari BIRLIKKA bog'langan — kontent
///   tuzatilsa natija yo'qolmaydi.
/// </summary>
public class InteractiveUnit : BaseEntity
{
    public const int MaxCodeLength = 32;
    public const int MaxTitleLength = 300;

    /// <summary>Imtihon birligining tartib raqami (ro'yxat oxirida tursin).</summary>
    public const int ExamNumber = 99;

    public long BookId { get; set; }

    public InteractiveBook? Book { get; set; }

    /// <summary>Papka nomi: "birlik_01" … "imtihon".</summary>
    public required string Code { get; set; }

    public int Number { get; set; }

    public required string TitleUz { get; set; }

    public string? TitleAr { get; set; }

    /// <summary>Qaysi kurs darsiga bog'langan. <c>null</c> — bog'lanmagan.</summary>
    public long? ModuleLessonId { get; set; }

    public ModuleLesson? ModuleLesson { get; set; }

    public long? CurrentVersionId { get; set; }

    public InteractiveUnitVersion? CurrentVersion { get; set; }

    public int Position { get; set; }
}
