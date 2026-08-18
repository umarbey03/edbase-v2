using Zinnur.Application.Penalties.Dtos;

namespace Zinnur.Application.Penalties.Services;

/// <summary>
/// JARIMA TARIFLARI KATALOGI (2026-08-18).
///
/// ★ RUXSAT NOMUTANOSIB — ATAYLAB: ro'yxatni o'quv bo'limi ham ko'radi
/// (jarima kiritishda tanlash uchun), lekin TARIFNI FAQAT ADMIN
/// o'zgartiradi. Sabab <c>IPenaltyService</c> dagi bilan AYNI: tarif
/// — pul qoidasi, va uni o'zgartirish barcha kelajakdagi jarimalarga
/// ta'sir qiladi.
/// </summary>
public interface IPenaltyCategoryService
{
    /// <param name="activeOnly">
    /// <c>true</c> — jarima kiritish oynasi uchun (arxivlanganlar kerak emas);
    /// <c>false</c> — boshqaruv jadvali uchun (hammasi ko'rinadi).
    /// </param>
    Task<IReadOnlyList<PenaltyCategoryDto>> ListAsync(
        bool activeOnly, long actorId, CancellationToken ct = default);

    Task<PenaltyCategoryDto> CreateAsync(
        SavePenaltyCategoryRequest request, long actorId, CancellationToken ct = default);

    Task<PenaltyCategoryDto> UpdateAsync(
        long id, SavePenaltyCategoryRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// O'chirish. Jarimada ishlatilgan bo'lsa — ARXIVLANADI (tarix
    /// buzilmasin), aks holda haqiqatan o'chiriladi.
    /// </summary>
    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);
}
