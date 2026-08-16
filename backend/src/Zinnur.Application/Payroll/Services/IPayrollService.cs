using Zinnur.Application.Payroll.Dtos;

namespace Zinnur.Application.Payroll.Services;

public interface IPayrollService
{
    Task<PayrollSummaryDto> GetSummaryAsync(string? period, long actorId, CancellationToken ct = default);

    Task<PayrollDetailDto> GetDetailAsync(
        long userId, string? period, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<TeacherRateDto>> ListRatesAsync(long actorId, CancellationToken ct = default);

    Task<TeacherRateDto> CreateRateAsync(
        CreateTeacherRateRequest request, long actorId, CancellationToken ct = default);

    Task<TeacherRateDto> UpdateRateAsync(
        long id, UpdateTeacherRateRequest request, long actorId, CancellationToken ct = default);

    Task DeleteRateAsync(long id, long actorId, CancellationToken ct = default);
}
