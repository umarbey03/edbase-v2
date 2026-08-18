using Zinnur.Application.Penalties.Dtos;

namespace Zinnur.Application.Penalties.Services;

/// <summary>
/// JARIMA TARIFLARI KATALOGI (2026-08-18).
///
/// ★ RUXSAT — O'QUV BO'LIMI VA ADMIN (loyiha egasi qarori, 2026-08-18):
/// katalog "Sozlamalar" sahifasining bo'limi bo'lib turadi va o'sha
/// sahifa allaqachon o'quv bo'limiga ochiq.
///
/// ★ NEGA JARIMANI TASDIQLASHDAN KO'RA KENGROQ: tarif — QOIDA, jarima
/// esa aniq odamdan ushlab qolinadigan PUL. Tarif o'zgarishi yozilgan
/// jarimalarga TEGMAYDI (summa yaratilganda muzlatilgan), shuning uchun
/// bu yerda "yozgan odam tasdiqlamasin" cheklovi kerak emas.
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
