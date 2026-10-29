using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// INTERAKTIV DARSLIK — bosqich (ATF-1, ATF-2), 2026-10-29.
///
/// ★ KONTENT PLATFORMADA YOZILMAYDI: u `Zinnur.ContentTool import` buyrug'i
///   bilan tashqi papkadan (`dars.json` + audio) yuklanadi. Bu jadvalda faqat
///   kitobning nomi va holati turadi; mazmun — <see cref="InteractiveUnitVersion"/>.
///
/// ★ <see cref="IsPublished"/> = <c>false</c> bo'lsa kitob o'quvchiga UMUMAN
///   ko'rinmaydi (pilot shu bilan boshqariladi), xodim esa ko'ra oladi.
/// </summary>
public class InteractiveBook : BaseEntity
{
    public const int MaxCodeLength = 32;
    public const int MaxTitleLength = 200;

    /// <summary>Barqaror kod — import shu bo'yicha topadi ("ATF-1").</summary>
    public required string Code { get; set; }

    public required string Title { get; set; }

    public string? Subtitle { get; set; }

    /// <summary>
    /// Ma'lumot uchun: kitob qaysi kursga mo'ljallangan. Kirish huquqi
    /// buni EMAS, birlik bog'langan darsning kursini tekshiradi.
    /// </summary>
    public long? CourseId { get; set; }

    public Course? Course { get; set; }

    public int Position { get; set; }

    public bool IsPublished { get; set; }

    public ICollection<InteractiveUnit> Units { get; set; } = new List<InteractiveUnit>();
}
