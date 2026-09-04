using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payroll.Dtos;

// ============================================================================
//  OYLIK — API SHARTNOMASI (2026-09-04 da qoida dvigateliga ko'chirildi)
// ============================================================================
//
//  ★ ESKI SHAKLDAN FARQI: ilgari `TeacherRateDto` bitta qatorda beshta pul
//    maydonini olib yurardi (`PerSessionRate`, `BaseSalary`, ...) va hisobot
//    ham shu beshtaga qattiq bog'langan edi (`BaseSalaryAmount`,
//    `KpiBonusAmount` ustunlari). Har yangi hisoblash usuli SHARTNOMANI ham
//    o'zgartirishni talab qilardi.
//
//    Endi hisobot QATORLAR ro'yxatini qaytaradi (`PayrollAmountLineDto`):
//    frontend turni nomi bilan ko'rsatadi, yangi tur qo'shilganda esa
//    hech qanday DTO o'zgarmaydi.

// ---------------------------------------------------------------- qoida

/// <summary>«Suzuvchi» stavkaning bitta bosqichi.</summary>
public sealed record PayrollRuleTierDto(long Id, int StudentCount, decimal Amount);

/// <summary>Bosqich kiritish/yangilash uchun — <c>Id</c> yo'q (to'liq almashtiriladi).</summary>
public sealed record PayrollRuleTierInput(int StudentCount, decimal Amount);

public sealed record PayrollRuleDto(
    long Id,
    string Name,
    PayrollRuleKind Kind,

    // ── maqsad ──
    long? UserId,
    string? UserName,
    UserRole Role,
    long? CourseId,
    string? CourseName,
    long? GroupId,
    string? GroupName,
    long? CategoryId,
    string? CategoryName,
    GroupType? GroupType,

    // ── qiymat ──
    decimal Amount,
    int AcademicHourMinutes,
    PayrollBasis Basis,

    // ── shartlar ──
    int? MinStudents,
    int? MaxStudents,
    int? MinDurationMinutes,

    // ── reja ──
    decimal? PlanAmount,
    decimal? PlanReachedPercent,

    decimal? WeekendHolidayMultiplier,

    // ── muddat ──
    DateOnly ActiveFrom,
    DateOnly? ActiveTo,
    bool IsActive,

    /// <summary>Aniqlik darajasi — bir xil turdagi qoidalar orasidagi ustunlik (izoh: <c>PayrollRule.Specificity</c>).</summary>
    int Specificity,

    /// <summary>Har dars uchunmi (aks holda oyga bir marta) — UI shunga qarab maydonlarni ko'rsatadi.</summary>
    bool IsSessionScoped,

    List<PayrollRuleTierDto> Tiers,

    /// <summary>
    /// Bu qoidani QO'LDA tanlagan guruhlar soni (<c>GroupPayrollMode.FixedRule</c>).
    /// O'chirishdan oldin ogohlantirish uchun.
    /// </summary>
    int PinnedGroupCount,

    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// ★ Yaratish va yangilash BIR XIL shakl — <c>PUT</c> TO'LIQ ALMASHTIRADI
/// (loyihadagi <c>UpdateTariffRequest</c> bilan AYNI naqsh). Bosqichlar ham
/// to'liq almashadi: qisman yangilash "qaysi bosqich qaysi Id?" degan
/// murakkablikni frontendga yuklardi.
/// </summary>
public sealed record PayrollRuleRequest(
    string Name,
    PayrollRuleKind Kind,
    UserRole Role,
    decimal Amount,
    DateOnly ActiveFrom,
    long? UserId = null,
    long? CourseId = null,
    long? GroupId = null,
    long? CategoryId = null,
    GroupType? GroupType = null,
    int AcademicHourMinutes = 45,
    PayrollBasis Basis = PayrollBasis.Attended,
    int? MinStudents = null,
    int? MaxStudents = null,
    int? MinDurationMinutes = null,
    decimal? PlanAmount = null,
    decimal? PlanReachedPercent = null,
    decimal? WeekendHolidayMultiplier = null,
    DateOnly? ActiveTo = null,
    bool IsActive = true,
    List<PayrollRuleTierInput>? Tiers = null);

// ---------------------------------------------------------------- guruh tayinlash

/// <summary>Guruh kartochkasidagi oylik rejimi — HolliHop'dagi «ставка в карточке УЕ».</summary>
public sealed record GroupPayrollAssignmentDto(
    long GroupId,
    string GroupName,
    GroupPayrollMode Mode,
    long? RuleId,
    string? RuleName);

public sealed record SetGroupPayrollAssignmentRequest(GroupPayrollMode Mode, long? RuleId);

// ---------------------------------------------------------------- koeffitsient

public sealed record PayrollStudentCoefficientDto(
    long Id,
    long UserId,
    long StudentId,
    string StudentName,
    DateOnly PeriodStart,
    decimal Percent,
    string? Note);

/// <summary>
/// Xodimning shu davrdagi BITTA faol o'quvchisi va uning koeffitsienti.
/// </summary>
/// <remarks>
/// ★ NIMA UCHUN FAQAT ISTISNOLAR EMAS, BUTUN RO'YXAT: admin koeffitsient
/// qo'yish uchun avval o'quvchini TOPISHI kerak. Faqat mavjud istisnolar
/// qaytarilsa, UI o'quvchi tanlash uchun butun markazning o'quvchilar
/// ro'yxatini ko'rsatishga majbur bo'lardi — va u yerdan bu xodimga
/// aloqasi yo'q o'quvchini tanlash mumkin edi (koeffitsient saqlanardi,
/// lekin hech qachon ishlamasdi).
///
/// <c>Percent</c> yozuv bo'lmasa 100 — "istisno yo'q"
/// (<c>PayrollStudentCoefficient</c> izohi).
/// </remarks>
public sealed record PayrollStudentUnitDto(
    long StudentId,
    string StudentName,
    decimal Percent,
    string? Note);

/// <summary>
/// Koeffitsientni O'RNATADI (yo'q bo'lsa yaratadi, bor bo'lsa yangilaydi).
/// <c>Percent = 100</c> yuborilsa yozuv O'CHIRILADI — sabab
/// <c>PayrollStudentCoefficient</c> izohida: jadval faqat ISTISNONI saqlaydi.
/// </summary>
public sealed record SetPayrollStudentCoefficientRequest(
    long UserId,
    long StudentId,
    string Period,
    decimal Percent,
    string? Note);

// ---------------------------------------------------------------- hisob-kitob

/// <summary>
/// Haqning bitta tashkil etuvchisi — "nima uchun shuncha" qatori.
/// Dars ichida ham, davr yig'indisida ham AYNI shakl ishlatiladi: UI bitta
/// komponent bilan ikkovini ham chizadi.
/// </summary>
public sealed record PayrollAmountLineDto(
    long? RuleId,
    string RuleName,
    PayrollRuleKind Kind,
    decimal Amount,
    string? Basis);

/// <summary>Bitta xodim uchun davr yig'indisi — ro'yxat qatori.</summary>
public sealed record PayrollSummaryRowDto(
    long UserId,
    string FullName,
    UserRole Role,
    int SessionCount,
    int TotalStudentsAttended,

    /// <summary>Darslardan asosiy stavka yig'indisi (dars/soat/bosqich turlari).</summary>
    decimal SessionBaseAmount,

    /// <summary>Darslardan o'quvchi bonusi yig'indisi.</summary>
    decimal SessionBonusAmount,

    /// <summary>Davr qoidalari (oklad, foiz, oylik o'quvchi bonusi) yig'indisi.</summary>
    decimal PeriodAmount,

    /// <summary>Davr qoidalarining tafsiloti — hisobotda kengaytirib ko'rsatiladi.</summary>
    List<PayrollAmountLineDto> PeriodLines,

    /// <summary>Qo'lda tuzatishlar yig'indisi (ishorasi bilan: bonus musbat, ushlanma manfiy).</summary>
    decimal AdjustmentAmount,

    decimal Total,

    /// <summary>Mos qoida topilmagan darslar soni — SOZLASH KERAK degan signal.</summary>
    int SessionsWithoutRule,

    /// <summary>Bepul deb belgilanib, xodim HAM haq olmagan darslar soni.</summary>
    int SessionsExcluded,

    /// <summary>Guruhi "oklad ichida" bo'lgani uchun alohida haq hisoblanmagan darslar.</summary>
    int SessionsIncludedInSalary,

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

    /// <summary>Mos qoida topilmadi — "bepul dars" EMAS, SOZLANMAGAN.</summary>
    bool RuleMissing,

    /// <summary>Bepul dars deb belgilanib, xodim shu darsdan haq olmadi.</summary>
    bool Excluded,

    /// <summary>Guruh "oklad ichida" — haq ONGLI ravishda 0.</summary>
    bool IncludedInSalary,

    /// <summary>Shu darsda qo'llangan dam olish/bayram ko'paytiruvchisi — ustama yo'q bo'lsa <c>1</c>.</summary>
    decimal PremiumMultiplierApplied,

    /// <summary>Qoidama-qoida tafsilot (izoh: <c>SessionPayoutLine</c>).</summary>
    List<PayrollAmountLineDto> Lines);

/// <summary>Qo'lda qo'shilgan bonus/ushlab qolish (ishorasi bilan) — audit iz bilan.</summary>
/// <param name="FromPenalty">
/// Bu tuzatma JARIMA tasdiqlanganda avtomatik tug'ilganmi (2026-08-18).
///
/// 🔴 NIMA UCHUN DTO'DA: bunday tuzatmani O'CHIRIB BO'LMAYDI —
/// <c>Penalties.PayrollAdjustmentId</c> unga <c>Restrict</c> bilan havola
/// qiladi (moliyaviy iz saqlanishi kerak). Bayroqsiz oylik paneli har
/// qatorda "o'chirish" tugmasini ko'rsatardi va bosilganda foydalanuvchi
/// tushunarsiz "Serverda kutilmagan xato" olardi.
///
/// ★ IKKI QATLAM: UI tugmani yashiradi, servis esa baribir tekshiradi
/// (<c>PayrollService.DeleteAdjustmentAsync</c>) — API to'g'ridan
/// chaqirilsa ham javob 409 va tushunarli matn bo'lsin.
///
/// ★ JARIMANI BEKOR QILISH — TO'G'RI YO'L: ushlanmani olib tashlash uchun
/// "Jarimalar" panelidan foydalaniladi; u yerda hodisa ham, sabab ham
/// ko'rinadi (<c>Penalty</c> izohi).
/// </param>
public sealed record PayrollAdjustmentDto(
    long Id,
    long UserId,
    DateOnly PeriodStart,
    decimal Amount,
    string Reason,
    long CreatedById,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    bool FromPenalty);

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

    /// <summary>Davr qoidalari — oklad, tushumdan foiz, oylik o'quvchi bonusi.</summary>
    List<PayrollAmountLineDto> PeriodLines,
    decimal PeriodAmount,

    /// <summary>Davr oxiridagi faol o'quvchilar soni.</summary>
    int ActiveStudentCount,

    /// <summary>Koeffitsientlar bilan o'lchangan ulushlar yig'indisi.</summary>
    decimal WeightedStudentUnits,

    /// <summary>Xodimning faol o'quvchilari va koeffitsientlari (izoh: <see cref="PayrollStudentUnitDto"/>).</summary>
    List<PayrollStudentUnitDto> Students,

    List<PayrollAdjustmentDto> Adjustments,
    decimal GrandTotal,
    PayrollApprovalStatus ApprovalStatus,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PaidAt);
