namespace Zinnur.Application.Scheduling.Dtos;

/// <summary>Bayram kalendari yozuvi.</summary>
public sealed record HolidayDto(
    long Id,
    DateOnly Date,
    string Label,
    long CreatedById,
    string? CreatedByName,
    DateTimeOffset CreatedAt);

public sealed record CreateHolidayRequest(DateOnly Date, string Label);

/// <summary>
/// Bayram e'lon qilingandan keyingi ta'sir — xodim "nechta guruhga tegdi"
/// degan savolga DARHOL javob olishi uchun (`HolidayService.CreateAsync`
/// sinxron ishlaydi, izohi shu servis faylida).
/// </summary>
public sealed record HolidayImpactDto(
    HolidayDto Holiday,
    int AffectedGroupCount,
    int CancelledSessionCount);
