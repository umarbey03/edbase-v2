using Zinnur.Application.Groups.Dtos;

namespace Zinnur.Application.Groups.Services;

/// <summary>
/// Guruh kategoriyalari lug'ati (R21b) — CRUD.
///
/// ★ NEGA <see cref="IGroupService"/> GA QO'SHILMADI: u allaqachon 1100
/// qatordan oshgan va uning mavzusi — guruh, a'zolik va jadval. Lug'atning
/// hayot sikli butunlay boshqa (o'nlab qator, jadvalga ta'siri yo'q, tartib
/// va faollik) va uni o'sha faylga qo'shish "guruh servisi hamma narsani
/// qiladi" yo'liga yana bir qadam bo'lardi.
///
/// Har metod <c>actorId</c> oladi: ruxsat qoidasi SERVIS ichida tekshiriladi,
/// controller atributi faqat darvoza (loyihadagi umumiy naqsh).
/// </summary>
public interface IGroupCategoryService
{
    /// <summary>
    /// Ro'yxat — TARTIB bo'yicha (<c>Position</c> -> <c>Id</c>).
    /// Sahifalanmaydi (sabab <see cref="GroupCategoryListQuery"/> izohida).
    /// </summary>
    Task<IReadOnlyList<GroupCategoryDto>> ListAsync(
        GroupCategoryListQuery query, long actorId, CancellationToken ct = default);

    Task<GroupCategoryDto> CreateAsync(
        CreateGroupCategoryRequest request, long actorId, CancellationToken ct = default);

    Task<GroupCategoryDto> UpdateAsync(
        long id, UpdateGroupCategoryRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// O'chiradi.
    ///
    /// 🔴 GURUHGA BIRIKTIRILGAN kategoriya o'chirilmaydi (409) — FK
    /// <c>SET NULL</c> bo'lgani uchun o'chirish jimgina muvaffaqiyatli
    /// tugab, o'nlab guruh yorlig'ini yo'qotardi va buni hech kim sezmasdi.
    /// Bunday holatda ARXIVLASH (<c>isActive = false</c>) taklif qilinadi.
    /// </summary>
    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);
}
