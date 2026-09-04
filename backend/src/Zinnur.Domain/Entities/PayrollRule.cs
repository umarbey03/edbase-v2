using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
///  OYLIK QOIDASI — bitta "shunday hisoblansin" gapi (2026-09-04)
/// ============================================================================
///
/// ★ NIMA UCHUN <c>TeacherRate</c> O'RNIGA: eski jadval BITTA qatorda beshta
/// pul ustunini saqlardi (dars stavkasi, o'quvchi bonusi, oklad, KPI, ustama)
/// va ular HAMMASI birdan qo'llanardi. Har yangi hisoblash usuli — soatbay,
/// bosqichli, tushumdan foiz — YANA BITTA USTUN va yana bitta `if` degani
/// edi; "moslashuvchan" jadval aslida qotib qolgan edi.
///
/// Endi har bir gap ALOHIDA QATOR: turi (<see cref="Kind"/>), kimga/nimaga
/// tegishli (maqsad ustunlari), qancha (<see cref="Amount"/>), qachondan
/// (<see cref="ActiveFrom"/>) va yoqilganmi (<see cref="IsActive"/>).
/// Yangi usul qo'shish = <see cref="PayrollRuleKind"/> ga bitta qiymat.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ QO'SHILISH QOIDASI — BUTUN DVIGATELNING MARKAZI
/// ══════════════════════════════════════════════════════════════════════
/// BIR XIL <see cref="Kind"/> ichida — FAQAT ENG ANIQ mos qoida ishlaydi
/// (<c>PayrollRuleSelection.PickPerKind</c>, <see cref="Specificity"/>).
/// TURLI <see cref="Kind"/> lar esa QO'SHILADI.
///
/// Ya'ni "oklad 3 000 000 + soatbay 40 000 + har o'quvchiga 5 000" birga
/// ishlaydi, lekin bitta darsga ikkita soatbay stavka HECH QACHON tushmaydi.
/// Buning muqobili — hamma mos qoidani qo'shish — jimgina IKKI BARAVAR
/// to'lovga olib kelardi (umumiy "Ustoz" qoidasi + shu ustozga atalgan
/// shaxsiy qoida ikkalasi ham mos keladi).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NARX TARIXI — <see cref="Tariff"/> BILAN AYNI FALSAFA
/// ══════════════════════════════════════════════════════════════════════
/// Qator JOYIDA tahrirlanmaydi: stavka o'zgarsa — yangi <see cref="ActiveFrom"/>
/// bilan YANGI qator kiritiladi. Bunga qo'shimcha <see cref="ActiveTo"/> bor
/// (eski jadvalda yo'q edi): "bu qoida shu sanagacha ishladi" ni yozib
/// qo'yish uchun — aks holda qoidani to'xtatishning yagona yo'li uni
/// o'chirish bo'lardi va sabab yo'qolardi.
///
/// O'TGAN OY XAVFSIZ: dars qamrovidagi natija <see cref="SessionPayout"/>
/// ichida muzlatilgan, bu yerdagi o'zgarish unga TEGMAYDI.
/// </summary>
public class PayrollRule : BaseEntity
{
    public const int MaxNameLength = 120;
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>Adminga ko'rinadigan nom — "Ustoz: arab tili, soatbay".</summary>
    public required string Name { get; set; }

    public PayrollRuleKind Kind { get; set; }

    // ------------------------------------------------------------ MAQSAD
    //
    // Barchasi `null` = "cheklov yo'q". Rol esa MAJBURIY: oylik faqat
    // Teacher/Assistant uchun hisoblanadi (`PayrollService` ruxsat izohi).

    /// <summary>Aniq xodimga atalgan bo'lsa. <c>null</c> — shu rolning hammasi.</summary>
    public long? UserId { get; set; }

    public User? User { get; set; }

    /// <summary><see cref="UserRole.Teacher"/> yoki <see cref="UserRole.Assistant"/>.</summary>
    public UserRole Role { get; set; }

    /// <summary>Aniq kursga (fanga) atalgan bo'lsa. <c>null</c> — barcha kurslar.</summary>
    public long? CourseId { get; set; }

    public Course? Course { get; set; }

    /// <summary>Aniq guruhga atalgan bo'lsa. <c>null</c> — barcha guruhlar.</summary>
    public long? GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>Guruh kategoriyasiga atalgan bo'lsa (VIP, intensiv...). <c>null</c> — barchasi.</summary>
    public long? CategoryId { get; set; }

    public GroupCategory? Category { get; set; }

    /// <summary>Guruh turiga atalgan bo'lsa (yakka dars boshqa stavkada). <c>null</c> — barchasi.</summary>
    public GroupType? GroupType { get; set; }

    // ------------------------------------------------------------ QIYMAT

    /// <summary>
    /// Summa (so'm) — yoki <see cref="PayrollRuleKind.PercentOfRevenue"/> da
    /// FOIZ (0..100). Qaysi ekani <c>PayrollRuleKinds.IsPercent</c> dan bilinadi.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Akademik soat necha daqiqa — <see cref="PayrollRuleKind.PerAcademicHour"/>
    /// uchun bo'luvchi. 45 — CIS bozorida odatiy, lekin markazlar 40/50/60 ni
    /// ham ishlatadi, shuning uchun QOIDANING O'ZIDA (global sozlamada emas):
    /// bitta markazda bolalar guruhi 40 daqiqa, kattalarniki 60 bo'lishi mumkin.
    /// </summary>
    public int AcademicHourMinutes { get; set; } = 45;

    /// <summary>Qaysi o'quvchilar sanaladi (<c>UsesStudentCount</c> turlarida ma'noli).</summary>
    public PayrollBasis Basis { get; set; }

    // ------------------------------------------------------------ SHARTLAR

    /// <summary>Shu sondan KAM o'quvchi bo'lsa qoida QO'LLANMAYDI. <c>null</c> — chegara yo'q.</summary>
    public int? MinStudents { get; set; }

    /// <summary>Shu sondan KO'P o'quvchi bo'lsa qoida QO'LLANMAYDI. <c>null</c> — chegara yo'q.</summary>
    public int? MaxStudents { get; set; }

    /// <summary>Dars shu daqiqadan qisqa bo'lsa qoida QO'LLANMAYDI. <c>null</c> — chegara yo'q.</summary>
    public int? MinDurationMinutes { get; set; }

    // ------------------------------------------------------------ REJA (foiz uchun)

    /// <summary>
    /// Oylik reja (so'm). <c>null</c> — reja yo'q, doim <see cref="Amount"/>% ishlaydi.
    /// </summary>
    public decimal? PlanAmount { get; set; }

    /// <summary>Reja BAJARILGANDA qo'llanadigan foiz. <c>null</c> — reja yo'q.</summary>
    public decimal? PlanReachedPercent { get; set; }

    // ------------------------------------------------------------ USTAMA

    /// <summary>
    /// Dam olish/bayram kuni ko'paytiruvchisi. <c>null</c> — ustama yo'q.
    /// FAQAT dars qamrovidagi turlarga ta'sir qiladi.
    /// </summary>
    public decimal? WeekendHolidayMultiplier { get; set; }

    // ------------------------------------------------------------ AMAL MUDDATI

    /// <summary>Shu sanadan kuchga kiradi (mahalliy kalendar).</summary>
    public DateOnly ActiveFrom { get; set; }

    /// <summary>Shu sanagacha (SHU KUN HAM KIRADI) ishlaydi. <c>null</c> — muddatsiz.</summary>
    public DateOnly? ActiveTo { get; set; }

    /// <summary>Yoqilgan/o'chirilgan — o'chirmasdan vaqtincha to'xtatish uchun.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Bosqichlar — faqat <see cref="PayrollRuleKind.TieredByAttendance"/> da.</summary>
    public ICollection<PayrollRuleTier> Tiers { get; set; } = new List<PayrollRuleTier>();

    // ---------------------------------------------------------------- qoidalar

    /// <summary>
    /// ANIQLIK DARAJASI — bir xil <see cref="Kind"/> ichidagi nomzodlardan
    /// qaysi biri ustun ekanini belgilaydi (<see cref="Tariff.Specificity"/>
    /// bilan AYNI naqsh, faqat o'lchamlar ko'p bo'lgani uchun BITLI vazn).
    ///
    /// 🔴 VAZNLAR TARTIBI ATAYLAB SHUNDAY:
    ///     guruh (8) &gt; xodim (4) &gt; kurs (2) &gt; kategoriya (1) = tur (1)
    ///
    /// "Guruh xodimdan ustun" — chunki guruh eng tor gap: "AYNAN shu guruh
    /// uchun boshqacha" deyilganda uni xodimga atalgan umumiy qoida bosib
    /// ketmasligi kerak. Kategoriya va tur bir xil vaznda: ikkalasi ham
    /// guruhning XOSSASI, biri ikkinchisidan aniqroq emas; ikkovi birga
    /// ishlatilsa yig'indi (2) kursdan ustun bo'ladi va bu to'g'ri —
    /// "VIP yakka dars" "arab tili" dan tor.
    /// </summary>
    public int Specificity =>
        (GroupId is not null ? 8 : 0)
        + (UserId is not null ? 4 : 0)
        + (CourseId is not null ? 2 : 0)
        + (CategoryId is not null ? 1 : 0)
        + (GroupType is not null ? 1 : 0);

    /// <summary>Shu sanada umuman kuchdami (maqsadga qaramay).</summary>
    public bool IsEffectiveOn(DateOnly on) =>
        IsActive && ActiveFrom <= on && (ActiveTo is null || ActiveTo >= on);

    /// <summary>
    /// Xodim uchun DAVR qamrovidagi qoida nomzodmi — o'quv o'lchamlari
    /// tekshirilmaydi (oklad/KPI butun oyga tegishli).
    /// </summary>
    public bool AppliesToStaff(long userId, UserRole role, DateOnly on)
    {
        if (!IsEffectiveOn(on)) return false;
        if (Role != role) return false;

        return UserId is null || UserId == userId;
    }

    /// <summary>
    /// GURUH uchun nomzodmi — FAQAT MAQSAD tekshiriladi (xodim + rol + o'quv
    /// o'lchamlari), shartlar emas.
    ///
    /// ★ SHARTLARDAN ALOHIDA: "tushumdan foiz" qoidasi guruhga bog'lanishi
    /// mumkin, lekin u DARS emas, butun OY bo'yicha hisoblanadi — u yerda
    /// "darsning davomiyligi" yoki "darsdagi o'quvchi soni" degan tushuncha
    /// yo'q. Ikkovi bitta metodda qolsa, foiz qoidasiga soxta qiymatlar
    /// (<c>int.MaxValue</c> kabi) uzatishga to'g'ri kelardi.
    /// </summary>
    public bool AppliesToGroup(
        long userId,
        UserRole role,
        long groupId,
        long? courseId,
        long? categoryId,
        GroupType groupType,
        DateOnly on)
    {
        if (!AppliesToStaff(userId, role, on)) return false;

        if (GroupId is not null && GroupId != groupId) return false;
        if (CourseId is not null && (courseId is null || CourseId != courseId)) return false;
        if (CategoryId is not null && (categoryId is null || CategoryId != categoryId)) return false;
        if (GroupType is not null && GroupType != groupType) return false;

        return true;
    }

    /// <summary>
    /// Aniq DARS uchun nomzodmi: maqsad (<see cref="AppliesToGroup"/>) + shartlar.
    /// </summary>
    /// <param name="studentCount">
    /// Qoidaning <see cref="Basis"/> i bo'yicha sanalgan o'quvchilar soni —
    /// <see cref="MinStudents"/>/<see cref="MaxStudents"/> shu songa qarab
    /// tekshiriladi (chaqiruvchi hisoblab beradi, chunki asos qoidaga bog'liq).
    /// </param>
    public bool AppliesToSession(
        long userId,
        UserRole role,
        long groupId,
        long? courseId,
        long? categoryId,
        GroupType groupType,
        int durationMinutes,
        int studentCount,
        DateOnly on) =>
        AppliesToGroup(userId, role, groupId, courseId, categoryId, groupType, on)
        && MeetsConditions(durationMinutes, studentCount);

    /// <summary>
    /// Faqat SHARTLAR (maqsad emas): davomiylik va o'quvchi soni chegaralari.
    ///
    /// ★ MAQSADDAN ALOHIDA: guruh kartochkasida qoida QO'LDA majburlanganda
    /// (<see cref="GroupPayrollMode.FixedRule"/>) maqsad tekshirilmaydi —
    /// admin ataylab shuni tanlagan. Lekin shartlar BARIBIR tekshiriladi:
    /// "10 daqiqadan qisqa darsga to'lanmaydi" degan qoida qo'lda tanlanganda
    /// ham kuchda qolishi kerak, aks holda majburlash shartlarni jimgina
    /// o'chirib yuborardi.
    /// </summary>
    public bool MeetsConditions(int durationMinutes, int studentCount)
    {
        if (MinDurationMinutes is { } minDuration && durationMinutes < minDuration) return false;
        if (MinStudents is { } min && studentCount < min) return false;
        if (MaxStudents is { } max && studentCount > max) return false;

        return true;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("Qoida nomi kiritilishi shart.");

        if (Name.Length > MaxNameLength)
            throw new DomainException($"Qoida nomi {MaxNameLength} belgidan oshmasligi kerak.");

        if (!Enum.IsDefined(Kind))
            throw new DomainException("Qoida turi noto'g'ri.");

        if (Role is not (UserRole.Teacher or UserRole.Assistant))
            throw new DomainException("Oylik qoidasi faqat ustoz yoki kurator uchun bo'ladi.");

        if (Amount < 0 || Amount > MaxAmount)
            throw new DomainException("Qoida summasi 0..1 000 000 000 oralig'ida bo'lishi kerak.");

        if (PayrollRuleKinds.IsPercent(Kind) && Amount > 100)
            throw new DomainException("Foiz 0..100 oralig'ida bo'lishi kerak.");

        if (ActiveTo is { } to && to < ActiveFrom)
            throw new DomainException("Tugash sanasi boshlanish sanasidan oldin bo'lmaydi.");

        // ★ Guruh/kurs maqsadi FAQAT dars bilan bog'liq turlarda ma'noli —
        //   sabab `PayrollRuleKinds.SupportsGroupTargeting` izohida. Bu
        //   tekshiruvsiz admin "oklad, faqat 5-guruh uchun" degan qoida
        //   yaratardi va u hech qachon ishlamasdi (davr hisobi guruhni
        //   ko'rmaydi) — jimgina noto'g'ri, eng yomon xato turi.
        if (!PayrollRuleKinds.SupportsGroupTargeting(Kind)
            && (GroupId is not null || CourseId is not null
                || CategoryId is not null || GroupType is not null))
        {
            throw new DomainException(
                "Oklad va oylik o'quvchi bonusi butun oyga tegishli — ularni "
                + "guruh, kurs, kategoriya yoki guruh turiga bog'lab bo'lmaydi.");
        }

        if (Kind == PayrollRuleKind.PerAcademicHour && AcademicHourMinutes is < 10 or > 240)
            throw new DomainException("Akademik soat 10..240 daqiqa oralig'ida bo'lishi kerak.");

        if (MinStudents is < 0)
            throw new DomainException("Eng kam o'quvchi soni manfiy bo'lmaydi.");

        if (MaxStudents is < 0)
            throw new DomainException("Eng ko'p o'quvchi soni manfiy bo'lmaydi.");

        if (MinStudents is { } lo && MaxStudents is { } hi && lo > hi)
            throw new DomainException("Eng kam o'quvchi soni eng ko'pidan katta bo'lmaydi.");

        if (MinDurationMinutes is < 0 or > 600)
            throw new DomainException("Eng kam davomiylik 0..600 daqiqa oralig'ida bo'lishi kerak.");

        if (WeekendHolidayMultiplier is < 1 or > 10)
            throw new DomainException("Ko'paytiruvchi 1..10 oralig'ida bo'lishi kerak.");

        // ★ Reja IKKI ustundan iborat va ular BIRGA to'ldiriladi: faqat
        //   summasi kiritilgan reja "yetgach nechchi foiz?" savolini javobsiz
        //   qoldirardi, faqat foizi kiritilgani esa "qachondan?" ni.
        if ((PlanAmount is null) != (PlanReachedPercent is null))
            throw new DomainException("Reja summasi va reja foizi birga kiritiladi.");

        if (PlanAmount is not null && Kind != PayrollRuleKind.PercentOfRevenue)
            throw new DomainException("Reja faqat \"tushumdan foiz\" qoidasida ishlatiladi.");

        if (PlanAmount is { } plan && (plan < 0 || plan > MaxAmount))
            throw new DomainException("Reja summasi 0..1 000 000 000 oralig'ida bo'lishi kerak.");

        if (PlanReachedPercent is < 0 or > 100)
            throw new DomainException("Reja foizi 0..100 oralig'ida bo'lishi kerak.");

        if (ActiveFrom.Year is < 2000 or > 2200)
            throw new DomainException("Kuchga kirish sanasi kiritilishi shart (masalan 2026-09-01).");
    }
}
