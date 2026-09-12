using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Bitta darsdagi haqning BITTA TASHKIL ETUVCHISI — "nima uchun shuncha
/// chiqdi" savoliga javob (2026-09-04).
///
/// ★ NIMA UCHUN KERAK: qoida dvigateli bir darsga bir NECHTA qoidani
/// qo'llaydi (masalan soatbay + har o'quvchi uchun bonus). <c>SessionPayout</c>
/// dagi ikki yig'ma ustun (<c>SessionRate</c>, <c>BonusAmount</c>) faqat
/// NATIJANI ko'rsatadi. Ustoz "nega bu oy kam?" deb so'raganda admin
/// yig'indini qo'lda qaytadan yechishga majbur bo'lardi — va qoida o'shanda
/// allaqachon o'zgargan bo'lishi mumkin.
///
/// ★ MUZLATILGAN: <c>SessionPayout</c> bilan BIRGA yoziladi va keyin hech
/// qachon qayta hisoblanmaydi (o'sha sinf izohidagi qoida). <see cref="RuleName"/>
/// NUSXA sifatida saqlanadi — qoida keyin qayta nomlansa yoki o'chirilsa ham
/// tarix o'qilishli qoladi; shu sabab <see cref="RuleId"/> <c>null</c>
/// bo'lishi mumkin.
/// </summary>
public class SessionPayoutLine : BaseEntity
{
    public const int MaxRuleNameLength = 120;

    public long PayoutId { get; set; }

    public SessionPayout? Payout { get; set; }

    /// <summary>Qoidaga havola — o'chirilgan bo'lsa <c>null</c>.</summary>
    public long? RuleId { get; set; }

    public PayrollRule? Rule { get; set; }

    /// <summary>Qoida nomining SHU PAYTDAGI nusxasi.</summary>
    public required string RuleName { get; set; }

    public PayrollRuleKind Kind { get; set; }

    /// <summary>Shu tashkil etuvchi bergan summa (ustama ALLAQACHON qo'llangan).</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Hisob asosi — soatbayda "1.78 akademik soat", o'quvchi bonusida
    /// "7 o'quvchi", bosqichlida "5-bosqich". Faqat KO'RSATISH uchun,
    /// hisobga kirmaydi.
    /// </summary>
    public string? Basis { get; set; }

    public const int MaxBasisLength = 120;
}
