using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// BITTA MEZON BO'YICHA QO'YILGAN BALL (bitta <see cref="SessionReview"/> ichida)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ <see cref="CriterionName"/> va <see cref="MaxScore"/> SNAPSHOT —
/// <see cref="AnalysisCriterion"/>dan YOZISH vaqtida nusxalanadi.
/// <see cref="CriterionId"/> esa faqat "keyinroq Sozlamalarga qaytish"
/// uchun yumshoq havola (<c>SetNull</c> — mezon o'chsa qator qoladi).
/// Bu ikkalasi BIRGA <see cref="AnalysisCriterion"/> izohidagi qarorni
/// ta'minlaydi: mezon nomi/balli keyin o'zgarsa ham, ALLAQACHON yozilgan
/// tahlil o'sha kundagi qiymatlarni ko'rsatishda davom etadi.
/// </summary>
public class SessionReviewScore : BaseEntity
{
    public long SessionReviewId { get; set; }

    public SessionReview? SessionReview { get; set; }

    /// <summary>Mezon katalogiga yumshoq havola. <c>null</c> — mezon keyin o'chirilgan.</summary>
    public long? CriterionId { get; set; }

    public AnalysisCriterion? Criterion { get; set; }

    /// <summary>Yozish vaqtidagi mezon nomi (snapshot).</summary>
    public required string CriterionName { get; set; }

    /// <summary>Yozish vaqtidagi maksimal ball (snapshot).</summary>
    public decimal MaxScore { get; set; }

    /// <summary>Qo'yilgan ball.</summary>
    public decimal Score { get; set; }

    /// <exception cref="DomainException">Ball manfiy yoki maksimal balldan katta.</exception>
    public static SessionReviewScore Create(
        long? criterionId, string criterionName, decimal maxScore, decimal score)
    {
        if (score < 0 || score > maxScore)
        {
            throw new DomainException(
                $"'{criterionName}' bo'yicha ball 0 dan {maxScore} gacha bo'lishi kerak.");
        }

        return new SessionReviewScore
        {
            CriterionId = criterionId,
            CriterionName = criterionName,
            MaxScore = maxScore,
            Score = score,
        };
    }
}
