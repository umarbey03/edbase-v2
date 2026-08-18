using Zinnur.Application.Absentees.Dtos;

namespace Zinnur.Application.Absentees.Services;

/// <summary>
/// DARSGA KIRMAGANLAR XARITASI (2026-08-18) — kunlik, guruh kesimida.
///
/// Sabab va falsafa <see cref="AbsenteeQuery"/> izohida.
///
/// ★ FAQAT O'QIYDI: davomatni <c>AttendanceService</c> yozadi.
/// </summary>
public interface IAbsenteeService
{
    Task<AbsenteeReportDto> GetAsync(
        AbsenteeQuery query, long actorId, CancellationToken ct = default);
}
