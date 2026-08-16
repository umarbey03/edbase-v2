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
    bool IsActive = true);

/// <summary>★ <c>PUT</c> — TO'LIQ ALMASHTIRISH (izoh: <c>UpdateTariffRequest</c> bilan AYNI naqsh).</summary>
public sealed record UpdateTeacherRateRequest(
    UserRole Role,
    decimal PerSessionRate,
    decimal PerStudentBonusRate,
    DateOnly ActiveFrom,
    bool IsActive,
    long? UserId = null);

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
    decimal Total,
    int SessionsWithoutRate,
    /// <summary>Bepul deb belgilanib, ustoz HAM haq olmagan darslar soni.</summary>
    int SessionsExcluded);

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
    bool Excluded);

public sealed record PayrollDetailDto(
    long UserId,
    string FullName,
    UserRole Role,
    string Period,
    List<PayrollSessionRowDto> Sessions,
    decimal GrandTotal);
