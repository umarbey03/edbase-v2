using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payroll;

// ============================================================================
//  DVIGATELNING KIRISH/CHIQISH TURLARI (2026-09-04)
// ============================================================================
//
//  ★ NIMA UCHUN ALOHIDA FAYL: `PayrollCalculator` — mantiq, bular — lug'at.
//    Ularni bir faylga qo'shish hisob qoidasini o'qishni qiyinlashtirardi.
//
//  ★ NIMA UCHUN ENTITY EMAS, KONTEKST: kalkulyator bazani BILMAYDI. Unga
//    `LiveSession` berilsa, u navigatsiya xossalari orqali `Group`, `Course`,
//    `Attendance` ni so'rashga vasvasa qilinardi va sof funksiya bo'lmay
//    qolardi (sabab `PayrollCalculator` sinf izohida).

/// <summary>Bitta dars uchun hisob konteksti — chaqiruvchi to'liq to'ldiradi.</summary>
/// <param name="Mode">Guruh kartochkasidagi qo'lda tayinlash rejimi.</param>
/// <param name="ForcedRuleId">
/// <see cref="GroupPayrollMode.FixedRule"/> da majburlanadigan qoida.
/// </param>
/// <param name="DurationMinutes">
/// Darsning REJALASHTIRILGAN davomiyligi (jadvaldagi, markazda 80 daqiqa) —
/// soatbay hisob shu songa tayanadi. Uzaytirish (<c>ExtendedMin</c>) va
/// haqiqiy boshlanish/tugash vaqti ATAYLAB hisobga olinmaydi (2026-09-09,
/// loyiha egasi: "oylik faqat 80 daqiqa uchun hisoblanishi kerak").
/// </param>
/// <param name="IsWeekendOrHoliday">
/// Dam olish yoki bayram kunimi — ustama shu bayroqqa qarab qo'llanadi.
/// Kalendar qarori chaqiruvchida (bazada <c>Holidays</c> jadvali bor),
/// bu yerda faqat NATIJA.
/// </param>
/// <param name="AttendedCount">Darsga kelganlar (<c>AttendanceStatus != Absent</c>).</param>
/// <param name="ExcusedCount">SABABLI kelmaganlar — kelganlarga QO'SHILMAGAN.</param>
/// <param name="EnrolledCount">Guruhning shu darsdagi faol a'zolari.</param>
public sealed record PayrollSessionContext(
    long UserId,
    UserRole Role,
    long GroupId,
    long? CourseId,
    long? CategoryId,
    GroupType GroupType,
    GroupPayrollMode Mode,
    long? ForcedRuleId,
    int DurationMinutes,
    DateOnly Date,
    bool IsWeekendOrHoliday,
    int AttendedCount,
    int ExcusedCount,
    int EnrolledCount)
{
    /// <summary>
    /// Qoidaning ASOSIGA mos o'quvchilar soni.
    ///
    /// ★ HAR QOIDA UCHUN ALOHIDA hisoblanadi (bitta umumiy son emas):
    /// bir darsda "kelganlar uchun 5 000" va "ro'yxatdagilar uchun 1 000"
    /// qoidalari birga ishlashi mumkin va ular BOSHQA-BOSHQA sonni ko'radi.
    /// </summary>
    public int CountFor(PayrollBasis basis) => basis switch
    {
        PayrollBasis.AttendedAndExcused => AttendedCount + ExcusedCount,
        PayrollBasis.Enrolled => EnrolledCount,
        _ => AttendedCount,
    };
}

/// <summary>Haqning bitta tashkil etuvchisi — <c>SessionPayoutLine</c> ga aylanadi.</summary>
public sealed record PayrollLine(
    long RuleId,
    string RuleName,
    PayrollRuleKind Kind,
    decimal Amount,
    string? Basis);

/// <summary>Bitta dars uchun hisob natijasi.</summary>
/// <param name="BaseAmount">
/// ASOSIY stavka yig'indisi (dars/soat/bosqich turlari) — <c>SessionPayout.SessionRate</c> ga yoziladi.
/// </param>
/// <param name="BonusAmount">
/// O'QUVCHI bonusi yig'indisi — <c>SessionPayout.BonusAmount</c> ga yoziladi.
/// </param>
/// <param name="MultiplierApplied">
/// Qo'llangan dam olish/bayram ustamasi — bir nechta asosiy qoida bo'lsa
/// ENG KATTASI (faqat KO'RSATISH uchun; summalar allaqachon hisoblangan).
/// </param>
/// <param name="RuleMissing">
/// Mos qoida topilmadi — bu XATO belgisi (admin sozlamagan), "bepul dars" emas.
/// </param>
/// <param name="IncludedInSalary">
/// Guruh "oklad ichida" deb belgilangan — haq ONGLI ravishda 0.
/// </param>
public sealed record PayrollSessionResult(
    IReadOnlyList<PayrollLine> Lines,
    decimal BaseAmount,
    decimal BonusAmount,
    decimal MultiplierApplied,
    bool RuleMissing,
    bool IncludedInSalary)
{
    public decimal Total => BaseAmount + BonusAmount;

    /// <summary>Guruh oklad ichida — hech narsa hisoblanmadi, lekin bu xato emas.</summary>
    public static PayrollSessionResult Salaried { get; } =
        new([], 0m, 0m, 1m, false, true);

    /// <summary>Mos qoida yo'q — admin sozlashi kerak.</summary>
    public static PayrollSessionResult Missing { get; } =
        new([], 0m, 0m, 1m, true, false);
}

/// <summary>
/// Xodimning bitta guruhdan shu davrda HISOBLANGAN o'quv haqi — "tushumdan
/// foiz" qoidasining asosi.
/// </summary>
/// <remarks>
/// ★ HISOBLANGAN, TO'LANGAN EMAS (<c>LessonCharge</c>): ustozning haqi
/// o'quvchining to'lov intizomiga bog'lanmasligi kerak — u darsni o'tdi.
/// HolliHop'dagi qoida ham aynan shu.
/// </remarks>
public sealed record PayrollGroupRevenue(
    long GroupId,
    long? CourseId,
    long? CategoryId,
    GroupType GroupType,
    decimal Amount);

/// <summary>Bitta xodimning bitta davri uchun hisob konteksti.</summary>
/// <param name="PeriodEnd">
/// Davrning OXIRGI kuni — qoidalar shu sanada kuchda bo'lishi bo'yicha
/// tanlanadi (oy o'rtasida kuchga kirgan yangi oklad shu oydan ishlasin).
/// </param>
/// <param name="ActiveStudentCount">Davr oxiridagi faol o'quvchilar soni (ko'rsatish uchun).</param>
/// <param name="WeightedStudentUnits">
/// Koeffitsientlar bilan o'lchangan ulushlar yig'indisi — hisob ASOSI.
/// Koeffitsient yozilmagan o'quvchi 1.0 ulush beradi.
/// </param>
/// <param name="TotalAccruedRevenue">Xodimning butun davrdagi hisoblangan o'quv haqi — REJA shu songa qaraladi.</param>
/// <param name="GroupRevenues">Guruhma-guruh tushum — foiz qoidasi shular bo'yicha moslanadi.</param>
public sealed record PayrollPeriodContext(
    long UserId,
    UserRole Role,
    DateOnly PeriodEnd,
    int ActiveStudentCount,
    decimal WeightedStudentUnits,
    decimal TotalAccruedRevenue,
    IReadOnlyList<PayrollGroupRevenue> GroupRevenues);
