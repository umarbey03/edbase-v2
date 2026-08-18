using Zinnur.Domain.Enums;

namespace Zinnur.Application.Penalties.Dtos;

/// <summary>"Jarimalar" paneli so'rovi (2026-08-18).</summary>
/// <param name="Period">Oylik davri <c>YYYY-MM</c>. Bo'sh — barcha davrlar.</param>
/// <param name="Search">Xodim ismi yoki sabab matni bo'yicha.</param>
public sealed record PenaltyListQuery(
    string? Period = null,
    long? UserId = null,
    PenaltyKind? Kind = null,
    PenaltyStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <param name="Kind"><see cref="PenaltyKind"/> nomi (matn).</param>
/// <param name="Status"><see cref="PenaltyStatus"/> nomi (matn).</param>
/// <param name="LateMinutes">Faqat kechikish jarimasida — necha daqiqa.</param>
/// <param name="SessionScheduledStart">Isbot uchun: dars REJADAGI vaqti.</param>
/// <param name="SessionActualStart">Isbot uchun: dars HAQIQATDA boshlangan vaqti.</param>
public sealed record PenaltyRowDto(
    long Id,
    long UserId,
    string UserName,
    string UserRole,
    long? SessionId,
    string? GroupName,
    DateTimeOffset? SessionScheduledStart,
    DateTimeOffset? SessionActualStart,
    string Kind,
    string Status,
    int? LateMinutes,
    decimal Amount,
    string Reason,
    DateTimeOffset OccurredAt,
    DateOnly PeriodStart,
    string? CreatedByName,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt);

/// <summary>
/// Filtrga mos BUTUN to'plam bo'yicha yig'ma (sahifalashdan mustaqil —
/// loyihadagi AYNI qaror).
/// </summary>
/// <param name="PendingAmount">Hali tasdiqlanmagan summa — oylikka HALI tushmagan.</param>
/// <param name="ApprovedAmount">Tasdiqlangan summa — oylikdan ushlanadi.</param>
public sealed record PenaltySummaryDto(
    int Total,
    int PendingCount,
    int ApprovedCount,
    int CancelledCount,
    decimal PendingAmount,
    decimal ApprovedAmount);

/// <summary>Xodim kesimi — "kimda ko'p jarima".</summary>
public sealed record PenaltyByUserDto(
    long UserId,
    string UserName,
    string UserRole,
    int PendingCount,
    int ApprovedCount,
    decimal ApprovedAmount,
    int TotalLateMinutes);

/// <param name="Amount">Musbat summa (so'm) — ushlab qolinadi.</param>
/// <param name="OccurredAt">Hodisa sanasi. Bo'sh — bugun.</param>
public sealed record CreateManualPenaltyRequest(
    long UserId,
    decimal Amount,
    string Reason,
    DateTimeOffset? OccurredAt = null);

/// <param name="Reason">Bekor qilish sababi — jarima matniga qo'shiladi.</param>
public sealed record CancelPenaltyRequest(string? Reason = null);
