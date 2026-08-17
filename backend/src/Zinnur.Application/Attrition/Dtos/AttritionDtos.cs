using Zinnur.Domain.Enums;

namespace Zinnur.Application.Attrition.Dtos;

/// <summary>
/// "To'kilishlar" paneli so'rovi (2026-08-17).
/// </summary>
/// <param name="Search">O'quvchi ismi, guruh nomi yoki sabab matni bo'yicha.</param>
/// <param name="Kind">Hodisa turi bo'yicha (chiqarish/muzlatish/ko'chirish...).</param>
/// <param name="GroupId">Aniq guruh bo'yicha.</param>
/// <param name="TeacherId">Aniq ustoz bo'yicha — hodisa PAYTIDAGI ustoz surati bo'yicha.</param>
/// <param name="From">Davr boshi (mahalliy sana, KIRADI).</param>
/// <param name="To">Davr oxiri (mahalliy sana, KIRADI).</param>
/// <param name="Trial">
/// <c>true</c> — faqat SINOV (probniy) davridagi to'kilishlar;
/// <c>false</c> — faqat AKTIV o'quvchilar to'kilishi; <c>null</c> — ikkisi ham.
/// Chegara <c>GroupMembershipEvent.TrialLessonCount</c> (8 dars).
/// </param>
public sealed record AttritionListQuery(
    string? Search = null,
    MembershipEventKind? Kind = null,
    long? GroupId = null,
    long? TeacherId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool? Trial = null,
    AttritionSort Sort = AttritionSort.Date,
    bool Desc = true,
    int Page = 1,
    int PageSize = 20);

/// <summary>
/// Saralash ustuni — OQ RO'YXAT (sabab
/// <see cref="TeacherAvailability.Dtos.TeacherAvailabilitySort"/> izohida
/// bilan AYNI: erkin satr bilan klient istalgan ustunni yubora olardi).
/// </summary>
public enum AttritionSort
{
    /// <summary>Hodisa sanasi (standart).</summary>
    Date = 0,

    /// <summary>O'quvchi F.I.Sh.</summary>
    Student = 1,

    /// <summary>Guruh nomi.</summary>
    Group = 2,

    /// <summary>O'tilgan darslar soni — "eng erta ketganlar" ni topish uchun.</summary>
    Lessons = 3,
}

/// <summary>Ro'yxatdagi bitta hodisa.</summary>
/// <param name="Kind"><see cref="MembershipEventKind"/> nomi (matn).</param>
/// <param name="TeacherName">Hodisa PAYTIDAGI ustoz (surat) — keyin almashtirilgani ta'sir qilmaydi.</param>
/// <param name="LessonsCompleted">O'quvchi ketishdan oldin nechta yakunlangan darsni o'tagan.</param>
/// <param name="IsTrial">Sinov (probniy) davrida sodir bo'ldimi.</param>
public sealed record AttritionRowDto(
    long EventId,
    DateTimeOffset OccurredAt,
    long StudentId,
    string StudentName,
    long GroupId,
    string GroupName,
    long? TeacherId,
    string? TeacherName,
    string Kind,
    string? Reason,
    long? MovedToGroupId,
    string? MovedToGroupName,
    string ActorName,
    int LessonsCompleted,
    bool IsTrial);

/// <summary>
/// Filtrga mos BUTUN to'plam bo'yicha yig'ma.
///
/// ★ RO'YXATDAN ALOHIDA: ro'yxat sahifalangan, yig'ma butun to'plamni
/// sanaydi (loyihadagi AYNI qaror — `AssignmentDtos` va
/// `TeacherAvailabilitySummaryDto` izohlari).
/// </summary>
/// <param name="Stopped">Butunlay chiqarilganlar — HAQIQIY to'kilish.</param>
/// <param name="Paused">Muzlatilganlar.</param>
/// <param name="Moved">Boshqa guruhga ko'chirilganlar.</param>
/// <param name="TrialLosses">Sinov davrida (8 darsdan oldin) ketganlar.</param>
/// <param name="ActiveLosses">8 dars va undan ko'p o'tab, keyin ketganlar.</param>
/// <param name="AverageLessonsBeforeLeaving">
/// Chiqarilganlar o'rtacha nechta darsdan keyin ketgan — markazning
/// "qachon yo'qotamiz" savoliga eng qisqa javob.
/// </param>
public sealed record AttritionSummaryDto(
    int Total,
    int Stopped,
    int Paused,
    int Moved,
    int TrialLosses,
    int ActiveLosses,
    double AverageLessonsBeforeLeaving);

/// <summary>
/// GURUH TAFSILOTI modali uchun (2026-08-17) — guruh haqida to'liq
/// ma'lumot + o'sha guruhdagi to'kilish yig'masi.
///
/// ★ O'QUVCHILAR RO'YXATI BU YERDA EMAS: u mavjud
/// <c>GET /attrition?groupId=X</c> orqali olinadi (sahifalash va saralash
/// tekinga keladi). Bu yerda faqat SARLAVHA ma'lumoti.
/// </summary>
/// <param name="CurrentPosition">
/// "Hozir qaysi modul, qaysi darsga kelgani" — masalan
/// <c>"Harflar moduli · 12-dars"</c>. Hali dars o'tilmagan bo'lsa <c>null</c>.
/// </param>
/// <param name="NextPosition">Navbatdagi dars. Kurs tugagan bo'lsa <c>null</c>.</param>
/// <param name="TaughtLessonCount">Guruhda yakunlangan ustoz darslari soni.</param>
/// <param name="CoveredLessons">Kurs boshlanish nuqtasidan jami qoplangan darslar.</param>
/// <param name="TotalLessons">Kursdagi jami darslar.</param>
public sealed record GroupAttritionDetailDto(
    long GroupId,
    string GroupName,
    string? CourseName,
    string? TeacherName,
    string? AssistantName,
    DateOnly StartDate,
    DateOnly EndDate,
    int ActiveMembers,
    string? CurrentPosition,
    string? NextPosition,
    int TaughtLessonCount,
    int CoveredLessons,
    int TotalLessons,
    int Stopped,
    int Paused,
    int Moved,
    int TrialLosses);

/// <summary>Ustoz kesimidagi to'kilish — "kimning guruhida ko'p to'kiladi".</summary>
public sealed record AttritionByTeacherDto(
    long? TeacherId,
    string TeacherName,
    int Stopped,
    int Paused,
    int Moved,
    int TrialLosses);

/// <summary>
/// Guruh kesimidagi to'kilish.
///
/// ★ <c>Paused</c>/<c>Moved</c> HAM BOR (2026-08-17): bu DTO ikki joyda
/// ishlatiladi — "Guruhlar kesimi" jadvalida VA "Ustozlar kesimi" dagi
/// ochiladigan guruh tafsilotida. Ikkinchisida ustoz qatorining o'zi
/// uchta ustunni ko'rsatadi, shuning uchun ochilgan guruhlar ham AYNI
/// uchta ustunni ko'rsatishi kerak — aks holda raqamlar "yig'ilmayotgandek"
/// tuyulardi.
/// </summary>
/// <param name="ActiveMembers">Guruhdagi hozirgi FAOL o'quvchilar soni — to'kilishni nisbatda ko'rish uchun.</param>
public sealed record AttritionByGroupDto(
    long GroupId,
    string GroupName,
    string? TeacherName,
    int Stopped,
    int Paused,
    int Moved,
    int TrialLosses,
    int ActiveMembers);
