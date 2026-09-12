using Zinnur.Application.Payroll.Dtos;

namespace Zinnur.Application.Payroll.Services;

/// <summary>
/// Oylik QOIDALARINI boshqarish — sozlash tomoni.
///
/// ★ <see cref="IPayrollService"/> DAN ALOHIDA: u HISOBOTNI beradi (o'qish),
/// bu esa SOZLAMANI o'zgartiradi (yozish). Ikkovi bitta interfeysda bo'lganda
/// sinf 900 qatordan oshib ketardi va "hisobot" bilan "sozlama" qismlarini
/// alohida sinash qiyinlashardi (<c>IPenaltyService</c>/
/// <c>IPenaltyCategoryService</c> ajratilishi bilan AYNI mulohaza).
/// </summary>
public interface IPayrollRuleService
{
    Task<IReadOnlyList<PayrollRuleDto>> ListAsync(long actorId, CancellationToken ct = default);

    Task<PayrollRuleDto> CreateAsync(
        PayrollRuleRequest request, long actorId, CancellationToken ct = default);

    /// <summary>★ TO'LIQ ALMASHTIRISH (bosqichlar bilan birga) — izoh: <see cref="PayrollRuleRequest"/>.</summary>
    Task<PayrollRuleDto> UpdateAsync(
        long id, PayrollRuleRequest request, long actorId, CancellationToken ct = default);

    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- guruh tayinlash

    /// <summary>Qoida QO'LDA tayinlangan yoki "oklad ichida" deb belgilangan guruhlar.</summary>
    Task<IReadOnlyList<GroupPayrollAssignmentDto>> ListGroupAssignmentsAsync(
        long actorId, CancellationToken ct = default);

    Task<GroupPayrollAssignmentDto> SetGroupAssignmentAsync(
        long groupId, SetGroupPayrollAssignmentRequest request, long actorId,
        CancellationToken ct = default);

    // ---------------------------------------------------------------- koeffitsient

    /// <summary>
    /// Oylik o'quvchi bonusi koeffitsientini o'rnatadi.
    /// <c>Percent = 100</c> — yozuv o'chiriladi (izoh:
    /// <see cref="SetPayrollStudentCoefficientRequest"/>).
    /// </summary>
    Task<PayrollStudentCoefficientDto?> SetStudentCoefficientAsync(
        SetPayrollStudentCoefficientRequest request, long actorId, CancellationToken ct = default);
}
