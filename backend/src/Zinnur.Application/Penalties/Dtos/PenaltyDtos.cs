using Zinnur.Domain.Enums;

namespace Zinnur.Application.Penalties.Dtos;

/// <summary>"Jarimalar" paneli so'rovi (2026-08-18).</summary>
/// <param name="Period">Oylik davri <c>YYYY-MM</c>. Bo'sh — barcha davrlar.</param>
/// <param name="OccurredOn">
/// ANIQ SANA (mahalliy). <paramref name="Period"/> dan MUSTAQIL: "shu oyning
/// hammasi" va "aynan 12-avgust" — ikki xil savol, ikkalasi birga ham
/// ishlatilishi mumkin.
/// </param>
/// <param name="CategoryId">Jarima turi (tarif katalogidan).</param>
/// <param name="Search">Xodim ismi yoki sabab matni bo'yicha.</param>
public sealed record PenaltyListQuery(
    string? Period = null,
    DateOnly? OccurredOn = null,
    long? UserId = null,
    long? CategoryId = null,
    PenaltyKind? Kind = null,
    PenaltyStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <param name="Kind"><see cref="PenaltyKind"/> nomi (matn).</param>
/// <param name="Status"><see cref="PenaltyStatus"/> nomi (matn).</param>
/// <param name="CategoryLabel">Tarif nomi. Kategoriyasiz jarimada <c>null</c>.</param>
/// <param name="Quantity">Songa qarab hisoblangan bo'lsa — necha birlik.</param>
/// <param name="UnitLabel">Birlik nomi ("daqiqa") — <paramref name="Quantity"/> bilan birga ko'rsatiladi.</param>
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
    long? CategoryId,
    string? CategoryLabel,
    decimal? Quantity,
    string? UnitLabel,
    int? LateMinutes,
    decimal Amount,
    string Reason,
    DateTimeOffset OccurredAt,
    DateOnly PeriodStart,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
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

// ══════════════════════════════════════════════════════════════════ hisobot

/// <summary>
/// OYLIK HISOBOT — xodim kesimida, ichida tur kesimi.
///
/// ★ NEGA ALOHIDA ENDPOINT (jadvalni frontendda guruhlash o'rniga):
/// hisobot SAHIFALANMAGAN butun oyni qamraydi. Jadval esa 20 tadan
/// keladi — undan guruhlansa, hisobot faqat birinchi sahifani
/// ko'rsatib, jami summani NOTO'G'RI chiqarardi.
/// </summary>
/// <param name="Period">Davr <c>YYYY-MM</c>.</param>
/// <param name="Total">Barcha xodimlar bo'yicha umumiy summa.</param>
public sealed record PenaltyReportDto(
    string Period,
    decimal Total,
    IReadOnlyList<PenaltyReportUserDto> Users);

/// <param name="Lines">Tur kesimi — summasi bo'yicha kamayish tartibida.</param>
public sealed record PenaltyReportUserDto(
    long UserId,
    string UserName,
    string UserRole,
    decimal Total,
    IReadOnlyList<PenaltyReportLineDto> Lines);

/// <param name="Label">Tarif nomi yoki kategoriyasiz jarima uchun tur nomi.</param>
/// <param name="Count">Necha marta — "1 marta" bo'lsa frontend yashiradi.</param>
public sealed record PenaltyReportLineDto(
    string Label,
    int Count,
    decimal Amount);

// ══════════════════════════════════════════════════════════════════ yozish

/// <param name="CategoryId">
/// Tarif katalogidan. Berilsa summa TARIFDAN hisoblanadi va
/// <paramref name="Amount"/> e'tiborga olinmaydi.
/// </param>
/// <param name="Quantity">Songa qarab hisoblanadigan tarifda majburiy.</param>
/// <param name="Amount">Kategoriyasiz jarimada — musbat summa (so'm).</param>
/// <param name="OccurredAt">Hodisa sanasi. Bo'sh — bugun.</param>
public sealed record CreateManualPenaltyRequest(
    long UserId,
    string Reason,
    long? CategoryId = null,
    decimal? Quantity = null,
    decimal? Amount = null,
    DateTimeOffset? OccurredAt = null);

/// <param name="Reason">Bekor qilish sababi — jarima matniga qo'shiladi.</param>
public sealed record CancelPenaltyRequest(string? Reason = null);

// ══════════════════════════════════════════════════════════════════ kategoriyalar

/// <param name="IsSystem">Tizim tarifi — o'chirilmaydi, faqat summasi tahrirlanadi.</param>
/// <param name="UsageCount">Nechta jarimada ishlatilgan — o'chirishdan oldin ogohlantirish uchun.</param>
public sealed record PenaltyCategoryDto(
    long Id,
    string Label,
    decimal Amount,
    bool PerUnit,
    string? UnitLabel,
    bool IsActive,
    bool IsSystem,
    string? SystemKey,
    int UsageCount);

public sealed record SavePenaltyCategoryRequest(
    string Label,
    decimal Amount,
    bool PerUnit = false,
    string? UnitLabel = null,
    bool IsActive = true);
