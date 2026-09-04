namespace Zinnur.Domain.Enums;

// ============================================================================
//  OYLIK HISOBLASH QOIDALARI (2026-09-04)
// ============================================================================
//
//  ★ NIMA UCHUN ALOHIDA FAYL: `Enums.cs` allaqachon 450 qatordan oshgan va
//    unda butun tizimning enum'lari aralash yotibdi. Oylik dvigateli o'z
//    lug'atiga ega — uni bir joyda ko'rish qoidani o'qishni osonlashtiradi.
//
//  🔴 TARTIB MUHIM: qiymatlar bazaga `int` sifatida yoziladi. Yangi qiymat
//    FAQAT oxiriga qo'shiladi, mavjud raqamlar hech qachon o'zgartirilmaydi.
// ============================================================================

/// <summary>
/// Oylik qoidasining TURI — «pul qanday hisoblanadi» degan savolga javob.
/// </summary>
/// <remarks>
/// ★ IKKI QAMROV (`PayrollRuleKinds.IsSessionScoped`):
///
///   • DARS QAMROVI — har YAKUNLANGAN dars uchun bir marta hisoblanadi va
///     <c>SessionPayout</c> ichida MUZLATILADI (stavka keyin o'zgarsa ham
///     o'tgan oy hisoboti o'zgarmaydi — `SessionPayout` sinf izohi).
///     Bular: <see cref="PerSession"/>, <see cref="PerAcademicHour"/>,
///     <see cref="PerAttendedStudent"/>, <see cref="TieredByAttendance"/>.
///
///   • DAVR QAMROVI — oyga BIR MARTA, davr oxiridagi holat bo'yicha JONLI
///     hisoblanadi (darslar soniga bog'liq emas). Bular:
///     <see cref="FixedMonthly"/>, <see cref="PercentOfRevenue"/>,
///     <see cref="MonthlyPerActiveStudent"/>.
///
/// ★ QO'SHILISH QOIDASI (butun dvigatelning markazi):
///   BIR XIL turdagi qoidalardan FAQAT ENG ANIQI qo'llanadi
///   (`PayrollRuleSelection`), TURLI turdagi qoidalar esa QO'SHILADI.
///   Ya'ni "oklad + soatbay + o'quvchi bonusi" birga ishlaydi, lekin bitta
///   darsga ikkita soatbay stavka HECH QACHON tushmaydi.
/// </remarks>
public enum PayrollRuleKind
{
    /// <summary>Fiksirlangan oylik (oklad) — dars soniga BOG'LIQ EMAS, davrga bir marta.</summary>
    FixedMonthly = 0,

    /// <summary>Har yakunlangan dars uchun belgilangan summa.</summary>
    PerSession = 1,

    /// <summary>
    /// Akademik soat uchun: dars davomiyligi <c>AcademicHourMinutes</c> ga
    /// bo'linadi va <c>Amount</c> ga ko'paytiriladi.
    /// </summary>
    PerAcademicHour = 2,

    /// <summary>Darsdagi HAR BIR (asosga mos) o'quvchi uchun qo'shimcha summa.</summary>
    PerAttendedStudent = 3,

    /// <summary>
    /// «Suzuvchi» stavka — o'quvchi soniga qarab BOSQICHLI summa
    /// (<c>PayrollRuleTier</c>): 0 ta uchun X, 1 ta uchun Y, 2 ta uchun Z...
    /// Eng yuqori bosqichdan ko'p o'quvchi kelsa — eng yuqori summa beriladi.
    /// </summary>
    TieredByAttendance = 4,

    /// <summary>
    /// Hisoblangan o'quv haqidan foiz. TO'LANGAN puldan EMAS, HISOBLANGANIDAN
    /// (<c>LessonCharge</c>) — HolliHop'dagi bilan ayni: aks holda ustozning
    /// haqi o'quvchining to'lov intizomiga bog'lanib qolardi.
    /// Reja (<c>PlanAmount</c>) berilsa: rejagacha <c>Amount</c>%,
    /// rejaga yetgach <c>PlanReachedPercent</c>%.
    /// </summary>
    PercentOfRevenue = 5,

    /// <summary>
    /// Oylik: har bir FAOL o'quvchi uchun summa, har o'quvchiga alohida
    /// KOEFFITSIENT bilan (<c>PayrollStudentCoefficient</c>) — oy o'rtasida
    /// qo'shilgan o'quvchi uchun 50% kabi.
    /// </summary>
    MonthlyPerActiveStudent = 6,
}

/// <summary>
/// Qoida QAYSI o'quvchilarni sanaydi — «kim uchun pul to'lanadi».
/// </summary>
/// <remarks>
/// ★ NIMA UCHUN KERAK: AlfaCRM'da bu «Тип расчёта» deb ataladi va amalda
/// eng ko'p so'raladigan moslashuv — ba'zi markazlar kelmagan, lekin PUL
/// TO'LAGAN o'quvchini ham sanaydi (joy band bo'lgan, ustoz darsga tayyorlangan).
/// Bu bayroqsiz har markaz uchun alohida kod yozish kerak bo'lardi.
/// </remarks>
public enum PayrollBasis
{
    /// <summary>Faqat darsga KELGANLAR (<c>AttendanceStatus != Absent</c>) — standart.</summary>
    Attended = 0,

    /// <summary>Kelganlar + SABABLI kelmaganlar (<c>Attendance.IsExcused</c>).</summary>
    AttendedAndExcused = 1,

    /// <summary>Guruhning shu darsdagi BARCHA faol a'zolari — kelgan-kelmaganidan qat'i nazar.</summary>
    Enrolled = 2,
}

/// <summary>
/// Guruh darajasidagi QO'LDA tayinlash — HolliHop'dagi «ставка в карточке УЕ».
/// </summary>
/// <remarks>
/// ★ NIMA UCHUN: avtomatik moslash (kurs/kategoriya/rol) hayotdagi
/// istisnolarni qoplamaydi — "bu bitta guruh ustozning okladiga kiradi",
/// "bu guruhga kelishuv bo'yicha boshqa stavka" kabi holatlar HAR markazda
/// bor. Bu ustunsiz admin butun qoida to'plamini bitta guruh uchun
/// buzishga majbur bo'lardi.
/// </remarks>
public enum GroupPayrollMode
{
    /// <summary>Qoidalar odatdagidek avtomatik moslanadi.</summary>
    Auto = 0,

    /// <summary>
    /// «Oklad ichida» — bu guruhning darslari uchun DARS QAMROVIDAGI hech
    /// qanday haq hisoblanmaydi (xodim buni fiksirlangan oylik ichida oladi).
    /// Davr qamrovidagi qoidalarga TA'SIR QILMAYDI.
    /// </summary>
    IncludedInSalary = 1,

    /// <summary>Aniq bitta qoida majburan qo'llanadi (<c>Group.PayrollRuleId</c>).</summary>
    FixedRule = 2,
}

/// <summary>
/// <see cref="PayrollRuleKind"/> ustidagi SOF yordamchi — qamrovni bir joyda
/// saqlash uchun (aks holda "bu tur sessiyalimi?" tekshiruvi servis, validator
/// va frontendda uch marta takrorlanardi va vaqt o'tib bir-biridan uzilardi).
/// </summary>
public static class PayrollRuleKinds
{
    /// <summary>Har dars uchun hisoblanadimi (aks holda — oyga bir marta).</summary>
    public static bool IsSessionScoped(PayrollRuleKind kind) => kind
        is PayrollRuleKind.PerSession
        or PayrollRuleKind.PerAcademicHour
        or PayrollRuleKind.PerAttendedStudent
        or PayrollRuleKind.TieredByAttendance;

    /// <summary><c>Amount</c> pul emas, FOIZ sifatida o'qiladimi.</summary>
    public static bool IsPercent(PayrollRuleKind kind) =>
        kind is PayrollRuleKind.PercentOfRevenue;

    /// <summary>
    /// Guruh/kurs/kategoriya kabi O'QUV o'lchamlari bo'yicha maqsadlash
    /// MA'NOLIMI. <see cref="PayrollRuleKind.FixedMonthly"/> va
    /// <see cref="PayrollRuleKind.MonthlyPerActiveStudent"/> — xodimning
    /// BUTUN oyiga tegishli, ularni bitta guruhga bog'lash ma'nosiz
    /// (va "qaysi guruh oyni to'laydi?" degan javobsiz savol tug'dirardi).
    /// </summary>
    public static bool SupportsGroupTargeting(PayrollRuleKind kind) => kind
        is not (PayrollRuleKind.FixedMonthly or PayrollRuleKind.MonthlyPerActiveStudent);

    /// <summary>
    /// Darsdagi o'quvchi soni natijaga TA'SIR QILADIMI — shu turlar uchungina
    /// <c>Basis</c>, <c>MinStudents</c>/<c>MaxStudents</c> ma'noli.
    /// </summary>
    public static bool UsesStudentCount(PayrollRuleKind kind) => kind
        is PayrollRuleKind.PerAttendedStudent
        or PayrollRuleKind.TieredByAttendance;
}
