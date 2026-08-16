using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payroll.Dtos;

// ---------------------------------------------------------------- stavka

public sealed record TeacherRateDto(
    long Id,
    long? UserId,
    string? UserName,
    UserRole Role,
    decimal PerSessionRate,
    decimal PerStudentBonusRate,
    /// <summary>Oylik kafolatlangan summa (asosan kurator uchun) — 0 = yo'q.</summary>
    decimal BaseSalary,
    /// <summary>Har bir faol o'quvchi uchun oylik KPI bonusi (asosan kurator uchun) — 0 = yo'q.</summary>
    decimal ActiveStudentBonusRate,
    /// <summary>Dam olish/bayram kuni asosiy stavkaga ko'paytiruvchi — <c>null</c> = ustama yo'q.</summary>
    decimal? WeekendHolidayMultiplier,
    DateOnly ActiveFrom,
    bool IsActive,
    int Specificity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateTeacherRateRequest(
    UserRole Role,
    decimal PerSessionRate,
    decimal PerStudentBonusRate,
    DateOnly ActiveFrom,
    long? UserId = null,
    bool IsActive = true,
    decimal BaseSalary = 0m,
    decimal ActiveStudentBonusRate = 0m,
    decimal? WeekendHolidayMultiplier = null);

/// <summary>★ <c>PUT</c> — TO'LIQ ALMASHTIRISH (izoh: <c>UpdateTariffRequest</c> bilan AYNI naqsh).</summary>
public sealed record UpdateTeacherRateRequest(
    UserRole Role,
    decimal PerSessionRate,
    decimal PerStudentBonusRate,
    DateOnly ActiveFrom,
    bool IsActive,
    long? UserId = null,
    decimal BaseSalary = 0m,
    decimal ActiveStudentBonusRate = 0m,
    decimal? WeekendHolidayMultiplier = null);

// ---------------------------------------------------------------- hisob-kitob

/// <summary>Bitta xodim uchun davr yig'indisi — ro'yxat qatori.</summary>
public sealed record PayrollSummaryRowDto(
    long UserId,
    string FullName,
    UserRole Role,
    int SessionCount,
    int TotalStudentsAttended,
    decimal BaseAmount,
    decimal BonusAmount,
    /// <summary>Davr uchun BIR MARTA qo'shiladigan oylik kafolatlangan summa (kurator baza oylik).</summary>
    decimal BaseSalaryAmount,
    /// <summary>Davr OXIRIDAGI faol o'quvchilar soni (KPI hisob asosi) — sessiya bilan bog'liq emas.</summary>
    int ActiveStudentCount,
    /// <summary>KPI bonusi — <see cref="ActiveStudentCount"/> × stavka.</summary>
    decimal KpiBonusAmount,
    /// <summary>Qo'lda qo'shilgan tuzatishlar yig'indisi (ishorasi bilan: bonus musbat, ushlab qolish manfiy).</summary>
    decimal AdjustmentAmount,
    decimal Total,
    int SessionsWithoutRate,
    /// <summary>Bepul deb belgilanib, ustoz HAM haq olmagan darslar soni.</summary>
    int SessionsExcluded,
    PayrollApprovalStatus ApprovalStatus,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PaidAt);

public sealed record PayrollSummaryDto(
    string Period,
    List<PayrollSummaryRowDto> Rows,
    decimal GrandTotal);

/// <summary>Bitta xodimning bitta davrdagi dars-dars tafsiloti.</summary>
public sealed record PayrollSessionRowDto(
    long SessionId,
    long GroupId,
    string GroupName,
    DateTimeOffset ScheduledStart,
    int AttendedStudents,
    decimal SessionRate,
    decimal BonusAmount,
    decimal Total,
    bool RateMissing,
    /// <summary>Bepul dars deb belgilanib, ustoz shu darsdan haq olmadi.</summary>
    bool Excluded,
    /// <summary>Shu darsda qo'llangan dam olish/bayram ko'paytiruvchisi — ustama yo'q bo'lsa <c>1</c>.</summary>
    decimal PremiumMultiplierApplied);

/// <summary>Qo'lda qo'shilgan bonus/ushlab qolish (ishorasi bilan) — audit iz bilan.</summary>
public sealed record PayrollAdjustmentDto(
    long Id,
    long UserId,
    DateOnly PeriodStart,
    decimal Amount,
    string Reason,
    long CreatedById,
    string? CreatedByName,
    DateTimeOffset CreatedAt);

public sealed record CreatePayrollAdjustmentRequest(
    long UserId,
    string Period,
    decimal Amount,
    string Reason);

/// <summary>Davr bo'yicha holat amali (tasdiqlash/to'lov) so'rovi.</summary>
public sealed record PayrollPeriodActionRequest(long UserId, string Period);

public sealed record PayrollDetailDto(
    long UserId,
    string FullName,
    UserRole Role,
    string Period,
    List<PayrollSessionRowDto> Sessions,
    decimal BaseSalaryAmount,
    int ActiveStudentCount,
    decimal KpiBonusAmount,
    List<PayrollAdjustmentDto> Adjustments,
    decimal GrandTotal,
    PayrollApprovalStatus ApprovalStatus,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PaidAt);
