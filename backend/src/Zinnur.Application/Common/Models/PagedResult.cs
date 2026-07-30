namespace Zinnur.Application.Common.Models;

/// <summary>
/// Sahifalangan ro'yxat javobi — barcha ro'yxat endpointlari uchun YAGONA shakl.
///
/// NIMA UCHUN: eski tizim ro'yxatlarni sahifalamasdan qaytarardi
/// (<c>SELECT * FROM users</c>) — 100 ming yozuvda javob ham, xotira ham
/// portlaydi. Bu turdan foydalanish sahifalashni "esdan chiqarib bo'lmaydigan"
/// qiladi: qaytish turi o'zi sahifa raqamini talab qiladi.
/// </summary>
/// <param name="Items">Joriy sahifadagi elementlar.</param>
/// <param name="Page">Sahifa raqami (1 dan boshlanadi).</param>
/// <param name="PageSize">Sahifadagi element soni.</param>
/// <param name="Total">Filtrga mos KELGAN umumiy soni (barcha sahifalar).</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total)
{
    /// <summary>Umumiy sahifalar soni (frontend paginator uchun).</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}
