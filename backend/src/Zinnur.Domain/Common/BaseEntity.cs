namespace Zinnur.Domain.Common;

/// <summary>
/// Barcha entity'lar uchun umumiy asos (DRY).
/// Vaqt DOIM <see cref="DateTimeOffset"/> va UTC — mahalliy vaqt hech qachon
/// bazaga yozilmaydi. Eski tizimda naive datetime ishlatilgani uchun oy
/// chegarasida hisobotlar noto'g'ri chiqardi.
/// </summary>
public abstract class BaseEntity
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
