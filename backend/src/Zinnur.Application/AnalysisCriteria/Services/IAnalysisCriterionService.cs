using Zinnur.Application.AnalysisCriteria.Dtos;

namespace Zinnur.Application.AnalysisCriteria.Services;

/// <summary>
/// Dars tahlili mezonlari katalogi (o'quv bo'limi/Admin sozlaydi,
/// <see cref="Zinnur.Domain.Entities.SessionReview"/> shundan ball tanlaydi).
///
/// Ruxsat: hammasi <c>Academic,Admin</c> — controller darvozasi bilan AYNI
/// (bu yerda "faqat o'z darsi" kabi qo'shimcha qoida yo'q, shuning uchun
/// alohida ruxsat qatlami shart emas — <see cref="SessionReview"/>dan farqi).
/// </summary>
public interface IAnalysisCriterionService
{
    /// <summary>Barcha mezonlar, <c>SortOrder</c> so'ng <c>Id</c> bo'yicha tartiblangan.</summary>
    Task<IReadOnlyList<AnalysisCriterionDto>> ListAsync(CancellationToken ct = default);

    Task<AnalysisCriterionDto> CreateAsync(
        SaveAnalysisCriterionRequest request, CancellationToken ct = default);

    Task<AnalysisCriterionDto> UpdateAsync(
        long id, SaveAnalysisCriterionRequest request, CancellationToken ct = default);

    /// <summary>
    /// O'chiradi. Xavfsiz: allaqachon yozilgan tahlillar ballarni SNAPSHOT
    /// sifatida saqlaydi (<see cref="Zinnur.Domain.Entities.SessionReviewScore"/>),
    /// ya'ni bu o'chirish ularga TA'SIR QILMAYDI.
    /// </summary>
    Task DeleteAsync(long id, CancellationToken ct = default);
}
