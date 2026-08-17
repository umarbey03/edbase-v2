using Zinnur.Application.Attrition.Dtos;
using Zinnur.Application.Common.Models;

namespace Zinnur.Application.Attrition.Services;

/// <summary>
/// "TO'KILISHLAR" HISOBOTI (2026-08-17) — o'quvchi qachon, qaysi guruhdan,
/// qaysi ustozdan va nima sababdan ketgani (yoki muzlatilgani/ko'chirilgani).
///
/// ★ MANBA — <c>GroupMembershipEvent</c> jurnali (o'chmaydigan). <c>GroupMember</c>
/// qatorining o'zi ishlatilMAYDI: u faqat OXIRGI holatni saqlaydi va o'quvchi
/// guruhga qaytsa tozalanadi (sabab entity izohida batafsil).
///
/// ★ FAQAT O'QIYDI: hodisalarni <c>GroupService</c> yozadi (a'zolik o'zgarishi
/// bilan BITTA tranzaksiyada). Bu servis hisobot uchun.
/// </summary>
public interface IAttritionService
{
    /// <summary>Hodisalar ro'yxati — filtr, qidiruv, saralash, sahifalash.</summary>
    Task<PagedResult<AttritionRowDto>> ListAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>AYNI filtrga mos butun to'plam bo'yicha yig'ma.</summary>
    Task<AttritionSummaryDto> GetSummaryAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>Ustoz kesimi — "kimning guruhida ko'p to'kiladi".</summary>
    Task<IReadOnlyList<AttritionByTeacherDto>> GetByTeacherAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>Guruh kesimi.</summary>
    Task<IReadOnlyList<AttritionByGroupDto>> GetByGroupAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);
}
