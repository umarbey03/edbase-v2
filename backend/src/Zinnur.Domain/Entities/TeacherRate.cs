using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Ustoz/kurator oylik stavkasi. <see cref="Tariff"/> bilan AYNAN bir xil
/// falsafa: narx TARIXI saqlanadi, eski qator o'zgartirilmaydi — yangi
/// stavka yangi qator sifatida <see cref="ActiveFrom"/> bilan kiritiladi.
///
/// NIMA UCHUN: stavkani joyida tahrirlash o'tgan oyning haqini jimgina
/// qayta yozardi — iyulda 40 000/dars bo'lgan stavka, avgustda ko'tarilgach,
/// iyul hisobotida ham 50 000 bo'lib chiqardi.
/// </summary>
public class TeacherRate : BaseEntity
{
    /// <summary>Aniq xodimga atalgan bo'lsa. <c>null</c> — shu rolning standart stavkasi.</summary>
    public long? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// <see cref="UserRole.Teacher"/> yoki <see cref="UserRole.Assistant"/> —
    /// stavka faqat shu ikkisidan biriga tegishli bo'ladi.
    /// </summary>
    public UserRole Role { get; set; }

    /// <summary>Bir YAKUNLANGAN dars uchun asosiy stavka (so'm).</summary>
    public decimal PerSessionRate { get; set; }

    /// <summary>Darsda QATNASHGAN har bir o'quvchi uchun bonus (so'm).</summary>
    public decimal PerStudentBonusRate { get; set; }

    /// <summary>
    /// ★ 2026-08-16 — OYLIK KAFOLATLANGAN SUMMA (asosan kurator uchun,
    /// GetCourse/CIS bozorida odatiy model: "baza oylik + KPI", dars soniga
    /// BOG'LIQ EMAS). 0 — bu stavkada baza oylik yo'q (faqat dars asosidagi
    /// hisob). `PayrollService` buni davr uchun BIR MARTA qo'shadi (sessiya
    /// sonidan qat'i nazar).
    /// </summary>
    public decimal BaseSalary { get; set; }

    /// <summary>
    /// ★ 2026-08-16 — har bir FAOL o'quvchi uchun oylik KPI bonusi (asosan
    /// kurator uchun). Faol o'quvchilar soni <c>Group.AssistantId == userId</c>
    /// bo'lgan guruhlardan hisoblanadi (`PayrollService.GetSummaryAsync`),
    /// davr OXIRIDAGI holat bo'yicha — dars/sessiya bilan bog'liq emas.
    /// </summary>
    public decimal ActiveStudentBonusRate { get; set; }

    /// <summary>
    /// ★ 2026-08-16 — dars shanba/yakshanba yoki <c>Holidays</c> jadvalidagi
    /// sanaga to'g'ri kelsa, <see cref="PerSessionRate"/>ga qo'llanadigan
    /// ko'paytiruvchi (masalan <c>1.5</c> = +50%). <c>null</c> — ustama yo'q
    /// (oddiy stavka, bugungi xatti-harakat). Bonusga (
    /// <see cref="PerStudentBonusRate"/>) QO'LLANMAYDI — faqat asosiy
    /// stavkaga (`LessonAccrualService`da qo'llanadi).
    /// </summary>
    public decimal? WeekendHolidayMultiplier { get; set; }

    /// <summary>Shu sanadan kuchga kiradi (mahalliy kalendar).</summary>
    public DateOnly ActiveFrom { get; set; }

    public bool IsActive { get; set; } = true;

    // ---------------------------------------------------------------- qoidalar

    /// <summary>
    /// Tanlash ANIQLIKDAN UMUMIYGA: aniq xodim &gt; rol standarti —
    /// <see cref="Tariff.Specificity"/> bilan AYNI naqsh.
    /// </summary>
    public int Specificity => UserId is not null ? 1 : 0;

    /// <summary>Shu xodim/rol uchun nomzod bo'la oladimi (sana ham hisobga olinadi).</summary>
    public bool AppliesTo(long userId, UserRole role, DateOnly on)
    {
        if (!IsActive || ActiveFrom > on) return false;
        if (Role != role) return false;
        if (UserId is not null) return UserId == userId;
        return true;
    }

    public void Validate()
    {
        if (Role is not (UserRole.Teacher or UserRole.Assistant))
            throw new DomainException("Stavka faqat ustoz yoki kurator uchun bo'lishi mumkin.");

        if (PerSessionRate < 0)
            throw new DomainException("Dars stavkasi manfiy bo'lmaydi.");

        if (PerStudentBonusRate < 0)
            throw new DomainException("O'quvchi bonusi manfiy bo'lmaydi.");

        if (BaseSalary < 0)
            throw new DomainException("Baza oylik manfiy bo'lmaydi.");

        if (ActiveStudentBonusRate < 0)
            throw new DomainException("KPI bonusi manfiy bo'lmaydi.");

        if (WeekendHolidayMultiplier is { } multiplier && multiplier < 1)
            throw new DomainException("Dam olish/bayram ko'paytiruvchisi 1 dan kichik bo'lmaydi.");
    }
}
