using Zinnur.Application.Payroll.Dtos;

namespace Zinnur.Application.Payroll.Services;

/// <summary>
/// Oylik HISOBOTI — o'qish tomoni + davr holati (tasdiqlash/to'lov) va
/// qo'lda tuzatishlar.
///
/// ★ QOIDALARNI SOZLASH BU YERDA EMAS: u <see cref="IPayrollRuleService"/>
/// da (sabab o'sha interfeys izohida).
/// </summary>
public interface IPayrollService
{
    Task<PayrollSummaryDto> GetSummaryAsync(string? period, long actorId, CancellationToken ct = default);

    Task<PayrollDetailDto> GetDetailAsync(
        long userId, string? period, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- tuzatish

    Task<PayrollAdjustmentDto> CreateAdjustmentAsync(
        CreatePayrollAdjustmentRequest request, long actorId, CancellationToken ct = default);

    Task DeleteAdjustmentAsync(long id, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- tasdiqlash/to'lov

    Task ApproveAsync(PayrollPeriodActionRequest request, long actorId, CancellationToken ct = default);

    Task MarkPaidAsync(PayrollPeriodActionRequest request, long actorId, CancellationToken ct = default);
}
