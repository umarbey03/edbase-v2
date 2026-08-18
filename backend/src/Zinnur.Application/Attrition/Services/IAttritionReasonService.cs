using Zinnur.Application.Attrition.Dtos;

namespace Zinnur.Application.Attrition.Services;

/// <summary>
/// TO'KILISH SABABLARI KATALOGI (2026-08-18).
///
/// ★ RUXSAT — O'QUV BO'LIMI VA ADMIN: ro'yxat "Sozlamalar" sahifasidan
/// boshqariladi va aynan o'quv bo'limi kundalik ishda qaysi sabablar
/// uchraydi/uchramaydi degan bilimga ega.
/// </summary>
public interface IAttritionReasonService
{
    /// <param name="activeOnly">
    /// <c>true</c> — chiqarish/muzlatish oynasi uchun (arxivlanganlar kerak emas);
    /// <c>false</c> — sozlamalar jadvali uchun.
    /// </param>
    Task<IReadOnlyList<AttritionReasonDto>> ListAsync(
        bool activeOnly, long actorId, CancellationToken ct = default);

    Task<AttritionReasonDto> CreateAsync(
        SaveAttritionReasonRequest request, long actorId, CancellationToken ct = default);

    Task<AttritionReasonDto> UpdateAsync(
        long id, SaveAttritionReasonRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// O'chirish. Hodisada ishlatilgan bo'lsa — ARXIVLANADI (hisobot
    /// tarixi buzilmasin), aks holda haqiqatan o'chiriladi.
    /// </summary>
    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);
}
