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

    /* ═══════════════════════════════════════════════════════════════════
       O'QUVCHI KESIMI (2026-08-18) — o'quv bo'limi so'rovi bo'yicha.

       ★ YUQORIDAGI `GetSummaryAsync` HODISALARNI sanaydi (guruh nuqtai
       nazari: "shu guruhdan nechta ketish bo'ldi"). Quyidagilar esa
       O'QUVCHILARNI sanaydi (markaz nuqtai nazari: "nechta odamni
       yo'qotdik va nechtasini qaytardik"). Ikkalasi ham kerak —
       bittasi ikkinchisining o'rnini bosa olmaydi.
       ═══════════════════════════════════════════════════════════════════ */

    /// <summary>
    /// Yo'qotilgan O'QUVCHILAR va ularning hozirgi holati: qaytgan /
    /// muzlatishda / butunlay ketgan + qayta jalb qilish ulushi.
    /// </summary>
    Task<AttritionStudentSummaryDto> GetStudentSummaryAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>
    /// To'kilib, keyin QAYTA faol bo'lganlar ro'yxati — qaysi guruhdan
    /// ketib, qaysi guruhda va necha kundan keyin qaytgani.
    /// </summary>
    Task<IReadOnlyList<AttritionReturnedDto>> GetReturnedAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>To'kilish sabablari FOIZ bilan.</summary>
    Task<AttritionReasonsDto> GetReasonsAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>Ustoz kesimi — "kimning guruhida ko'p to'kiladi".</summary>
    Task<IReadOnlyList<AttritionByTeacherDto>> GetByTeacherAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>Guruh kesimi.</summary>
    Task<IReadOnlyList<AttritionByGroupDto>> GetByGroupAsync(
        AttritionListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Bitta guruhning TAFSILOTI: guruh haqida to'liq ma'lumot (ustoz,
    /// boshlangan sana, kursda qayerga kelgani) + shu guruhdagi to'kilish
    /// yig'masi. O'quvchilar ro'yxati ALOHIDA olinadi (sabab
    /// <see cref="GroupAttritionDetailDto"/> izohida).
    /// </summary>
    Task<GroupAttritionDetailDto> GetGroupDetailAsync(
        long groupId, AttritionListQuery query, long actorId, CancellationToken ct = default);
}
