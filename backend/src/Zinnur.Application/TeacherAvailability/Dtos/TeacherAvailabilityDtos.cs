using Zinnur.Domain.Enums;

namespace Zinnur.Application.TeacherAvailability.Dtos;

/// <summary>
/// "Ustozlar holati" ro'yxati uchun so'rov (2026-08-17 kengaytmasi).
///
/// ★ SANA FILTRI UTC O'GIRISHNI TALAB QILMAYDI: <c>TeacherDailyCheckin.
/// CheckinDate</c> allaqachon MAHALLIY kalendar sanasi (<c>DateOnly</c>)
/// sifatida saqlanadi. UTC chegaralari faqat <c>LiveSession.ScheduledStart</c>
/// uchun kerak — u bu yerda filtrlanmaydi.
/// </summary>
/// <param name="Search">Ustoz ismi yoki sabab matni bo'yicha (ixtiyoriy).</param>
/// <param name="Status">Aniq bir holat bo'yicha.</param>
/// <param name="From">Davr boshi (mahalliy sana, KIRADI).</param>
/// <param name="To">Davr oxiri (mahalliy sana, KIRADI).</param>
/// <param name="OnlyUncovered">
/// Faqat DIQQAT TALAB QILADIGANLAR: "yo'q" degan, lekin hali o'rinbosar
/// topilmagan yozuvlar. O'quv bo'limining kundalik "kim bilan ishlash
/// kerak" savoliga bitta bosishda javob beradi.
/// </param>
public sealed record TeacherAvailabilityListQuery(
    string? Search = null,
    TeacherCheckinStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool OnlyUncovered = false,
    TeacherAvailabilitySort Sort = TeacherAvailabilitySort.Date,
    bool Desc = true,
    int Page = 1,
    int PageSize = 20);

/// <summary>
/// Saralash ustuni — OQ RO'YXAT.
///
/// ★ NIMA UCHUN ENUM, ERKIN SATR EMAS: erkin `sortBy=...` satri bilan
/// klient bazadagi istalgan ustunni (yoki umuman mavjud bo'lmagan nomni)
/// yuborishi mumkin bo'lardi. Enum bilan noto'g'ri qiymat MODEL BOG'LASH
/// bosqichida rad etiladi va servisga umuman yetib bormaydi.
///
/// 🔴 TARTIB MUHIM EMAS (bazaga yozilmaydi), lekin nomlar API shartnomasi:
/// `JsonStringEnumConverter` ularni SATR sifatida qabul qiladi (`?sort=Teacher`).
/// </summary>
public enum TeacherAvailabilitySort
{
    /// <summary>Kunlik sana bo'yicha (standart).</summary>
    Date = 0,

    /// <summary>Ustoz F.I.Sh. bo'yicha.</summary>
    Teacher = 1,

    /// <summary>Holat bo'yicha (Kutilmoqda → Tasdiqladi → ... → Yo'q dedi).</summary>
    Status = 2,
}

/// <summary>Ro'yxatdagi bitta qator — bitta ustozning bitta kunlik javobi.</summary>
/// <param name="Status"><see cref="TeacherCheckinStatus"/> nomi (matn).</param>
public sealed record TeacherAvailabilityRowDto(
    long CheckinId,
    long TeacherId,
    string TeacherName,
    DateOnly CheckinDate,
    string Status,
    string? DeclineReason,
    int? UnavailableDays,
    DateTimeOffset SentAt,
    DateTimeOffset? RespondedAt,
    IReadOnlyList<CoverageStatusDto> AffectedSessions);

/// <summary>Bitta ta'sirlangan darsning o'rinbosar qamrovi.</summary>
/// <param name="Status">
/// <see cref="CoverageRequestStatus"/> nomi — so'rov umuman OCHILMAGAN
/// bo'lsa <c>null</c>.
/// </param>
public sealed record CoverageStatusDto(
    long SessionId,
    string GroupName,
    DateTimeOffset ScheduledStart,
    string? Status,
    string? SubstituteTeacherName);

/// <summary>
/// Filtrga mos BUTUN to'plam bo'yicha yig'ma ko'rsatkichlar.
///
/// ★ RO'YXATDAN ALOHIDA ENDPOINT — ATAYLAB: ro'yxat SAHIFALANGAN, yig'ma
/// esa butun to'plamni sanashi kerak. Bitta javobga qo'shilsa, ikkinchi
/// sahifada raqamlar "o'zgargandek" ko'rinardi (AYNI qaror
/// `AssignmentDtos` da ham izohlangan).
/// </summary>
/// <param name="InProgress">Suhbat yarim qolgan (dars tanlash/sabab/kun kutilmoqda).</param>
/// <param name="AffectedSessions">"Yo'q" javoblari ta'sir qilgan darslar soni.</param>
/// <param name="CoverageResolved">Shulardan o'rinbosar TOPILGANI.</param>
/// <param name="CoverageOpen">Shulardan hali OCHIQ (o'rinbosar kutilmoqda).</param>
public sealed record TeacherAvailabilitySummaryDto(
    int Total,
    int Confirmed,
    int Declined,
    int Pending,
    int InProgress,
    int AffectedSessions,
    int CoverageResolved,
    int CoverageOpen);

/// <summary>
/// Bitta yozuvning TO'LIQ tafsiloti (modal uchun) — jumladan KIMGA taklif
/// yuborilgani va kim qanday javob bergani.
///
/// ★ NIMA UCHUN RO'YXATDA EMAS: taklif tarixi bitta darsga 5-10 qator
/// bo'lishi mumkin. Ro'yxatning har qatorida yuklansa, 20 qatorli sahifa
/// yuzlab ortiqcha yozuvni tortardi — u faqat SO'RALGANDA olinadi.
/// </summary>
public sealed record TeacherAvailabilityDetailDto(
    long CheckinId,
    long TeacherId,
    string TeacherName,
    DateOnly CheckinDate,
    string Status,
    string? DeclineReason,
    int? UnavailableDays,
    DateTimeOffset SentAt,
    DateTimeOffset? RespondedAt,
    IReadOnlyList<CoverageDetailDto> Coverages);

/// <summary>Bitta dars uchun o'rinbosar qidiruvining to'liq tarixi.</summary>
public sealed record CoverageDetailDto(
    long SessionId,
    string GroupName,
    DateTimeOffset ScheduledStart,
    string? Status,
    string? SubstituteTeacherName,
    string? Reason,
    IReadOnlyList<SubstituteOfferRowDto> Offers);

/// <summary>Bitta nomzodga yuborilgan taklif va uning javobi.</summary>
/// <param name="Status"><see cref="SubstituteOfferStatus"/> nomi (matn).</param>
public sealed record SubstituteOfferRowDto(
    long OfferId,
    long CandidateTeacherId,
    string CandidateTeacherName,
    string Status,
    DateTimeOffset SentAt,
    DateTimeOffset? RespondedAt);

/* ════════════════════════════════════════════════════════════════════════
   BO'SH USTOZLAR (2026-08-18)

   Loyiha egasi: *"14:00 da bugunni belgilasam qaysi ustozlar bo'shligini
   ko'rsatsin, ind qo'yib berayotganda birinchi shunga qarardim"*.

   ★ NEGA MAVJUD JADVALDAN TOPIB BO'LMAYDI: "Jonli darslar" paneli KIM
   DARS O'TAYAPTI ni ko'rsatadi. Operatorga esa TESKARISI kerak — kim
   dars o'tMAYAPTI. Bo'sh ustozni band darslar ro'yxatidan ayirib topish
   ko'z bilan bajariladigan ish edi va xatoga juda moyil.
   ════════════════════════════════════════════════════════════════════════ */

/// <param name="Date">Qaysi kun (mahalliy sana).</param>
/// <param name="Time">Qaysi vaqtdan (mahalliy). Bo'sh — 09:00.</param>
/// <param name="DurationMinutes">Necha daqiqalik oyna tekshiriladi.</param>
/// <param name="IncludeAssistants">Kuratorlar ham ro'yxatga kirsinmi.</param>
/// <param name="OnlyFree"><c>true</c> — faqat bo'shlar; <c>false</c> — bandlar ham (sababi bilan).</param>
public sealed record FreeTeacherQuery(
    DateOnly? Date = null,
    TimeOnly? Time = null,
    int DurationMinutes = 60,
    bool IncludeAssistants = false,
    bool OnlyFree = true,
    string? Search = null);

/// <summary>
/// Bitta ustozning tanlangan oynadagi holati.
/// </summary>
/// <param name="IsFree">Shu oynada darsi ham, "o'tolmayman" javobi ham yo'q.</param>
/// <param name="BusyGroupName">Band bo'lsa — qaysi guruh bilan.</param>
/// <param name="BusyFrom">Band darsning boshlanishi.</param>
/// <param name="BusyTo">Band darsning tugashi.</param>
/// <param name="UnavailableReason">
/// Ustoz o'sha kunga "o'tolmayman" deb javob bergan bo'lsa — sababi.
/// Bunday ustoz darsi bo'lmasa ham BO'SH DEB SANALMAYDI.
/// </param>
/// <param name="LessonsThatDay">O'sha kundagi jami darslari — yuklamani ko'rish uchun.</param>
/// <param name="DayFirstLesson">O'sha kundagi birinchi darsi (mahalliy vaqt).</param>
/// <param name="DayLastLessonEnd">O'sha kundagi oxirgi darsining tugashi (mahalliy vaqt).</param>
public sealed record FreeTeacherDto(
    long TeacherId,
    string TeacherName,
    string Role,
    bool IsFree,
    string? BusyGroupName,
    DateTimeOffset? BusyFrom,
    DateTimeOffset? BusyTo,
    string? UnavailableReason,
    int LessonsThatDay,
    TimeOnly? DayFirstLesson,
    TimeOnly? DayLastLessonEnd);

/// <param name="WindowStart">Tekshirilgan oynaning boshi (UTC) — UI aynan shuni ko'rsatadi.</param>
public sealed record FreeTeacherResultDto(
    DateOnly Date,
    TimeOnly Time,
    int DurationMinutes,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int FreeCount,
    int BusyCount,
    IReadOnlyList<FreeTeacherDto> Teachers);
