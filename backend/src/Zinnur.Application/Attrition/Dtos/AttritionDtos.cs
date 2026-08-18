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
    /// <summary>Tanlangan sabab tasnifi (2026-08-18). Tasnifsiz yozuvda <c>null</c>.</summary>
    string? ReasonLabel,
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

/* ════════════════════════════════════════════════════════════════════════
   O'QUVCHI KESIMI — "QAYTA JALB QILISH" (2026-08-18)

   O'quv bo'limi talabi (Dilrabo): *"umumiy 10 ta o'quvchi to'kildi, undan
   6 tasi qanchadir muddatda davom ettiradi, 4 tasi aniq o'qimidi"* va
   *"to'kilishdagi qancha o'quvchini qayta jalb qilolishimizni ko'rsatib
   turardi"*.

   ★ NEGA MAVJUD `AttritionSummaryDto` YETARLI EMAS: u HODISALARNI sanaydi.
   Bitta o'quvchi ikki marta muzlatilsa — ikkita hodisa, lekin BITTA
   o'quvchi. Dilrabo esa "10 ta o'QUVCHI" deb o'ylayapti. Shuning uchun bu
   yig'ma HAR O'QUVCHINI BIR MARTA sanaydi.

   ★ NEGA "KO'CHIRILGAN" BU YERGA KIRMAYDI: guruh uchun ko'chirish —
   yo'qotish, MARKAZ uchun esa emas (o'quvchi qolyapti). Kirsa, u darhol
   "qaytgan" bo'lib sanalib, qayta jalb qilish foizini soxta ko'tarardi.
   ════════════════════════════════════════════════════════════════════════ */

/// <summary>
/// To'kilgan o'quvchilarning HOZIRGI holati bo'yicha bo'linishi.
///
/// Uchala bo'lak <see cref="StudentsLost"/> ni to'liq qoplaydi:
/// <c>Returned + Paused + Gone = StudentsLost</c>.
/// </summary>
/// <param name="StudentsLost">Davrda chiqarilgan yoki muzlatilgan NOYOB o'quvchilar.</param>
/// <param name="Returned">Shundan hozir qaytadan FAOL bo'lganlari — qayta jalb qilinganlar.</param>
/// <param name="Paused">Hozir muzlatishda turganlari — vaqtinchalik tanaffus, qaytishi mumkin.</param>
/// <param name="Gone">Qaytmaganlari — hech qayerda faol emas.</param>
/// <param name="ReturnRate">Qayta jalb qilish ulushi (%, bir kasr xonasi).</param>
public sealed record AttritionStudentSummaryDto(
    int StudentsLost,
    int Returned,
    int Paused,
    int Gone,
    double ReturnRate);

/// <summary>
/// "To'kilib, keyin yangi guruhda qayta faol bo'lganlar" ro'yxatidagi
/// bitta qator (Dilrabo: *"alohida holda ko'rolimizmi?"*).
/// </summary>
/// <param name="LeftGroupName">Qaysi guruhdan ketgan.</param>
/// <param name="LeftKind"><see cref="MembershipEventKind"/> nomi — chiqarilgan yoki muzlatilgan.</param>
/// <param name="ReturnedGroupName">Qaysi guruhda qayta faol bo'lgan.</param>
/// <param name="SameGroup">O'sha guruhning O'ZIGA qaytganmi (yangi guruh emas).</param>
/// <param name="DaysAway">Necha kun tashqarida bo'lgan — "qanchadan keyin qaytaradi" savoliga javob.</param>
public sealed record AttritionReturnedDto(
    long StudentId,
    string StudentName,
    long LeftGroupId,
    string LeftGroupName,
    DateTimeOffset LeftAt,
    string LeftKind,
    string? LeftReason,
    int LessonsCompleted,
    long ReturnedGroupId,
    string ReturnedGroupName,
    DateTimeOffset ReturnedAt,
    bool SameGroup,
    int DaysAway);

/// <summary>
/// Sabab kesimi FOIZ bilan (Dilrabo: *"to'kilish sabablarini foizda"*).
/// </summary>
/// <param name="Label">Tasnif nomi yoki tasnifsiz yozuvlar uchun "Belgilanmagan".</param>
/// <param name="Share">Ulush (%, bir kasr xonasi) — barcha qatorlar yig'indisi ≈ 100.</param>
/// <param name="Classified">Katalogdan tanlangan tasnifmi (aks holda — "Belgilanmagan" qatori).</param>
public sealed record AttritionReasonShareDto(
    long? ReasonId,
    string Label,
    int Count,
    double Share,
    bool Classified);

/// <summary>Sabablar hisoboti — qatorlar + aniqlik ko'rsatkichi.</summary>
/// <param name="Total">Hisobga olingan to'kilish hodisalari.</param>
/// <param name="ClassifiedShare">
/// Sababi TANLANGAN yozuvlar ulushi (%). Past bo'lsa foizlarga ishonch
/// kam — shuning uchun raqam yashirilmaydi, ekranda ko'rsatiladi.
/// </param>
public sealed record AttritionReasonsDto(
    int Total,
    double ClassifiedShare,
    IReadOnlyList<AttritionReasonShareDto> Rows);

/// <summary>Sabablar katalogi yozuvi.</summary>
/// <param name="UsageCount">Nechta hodisada ishlatilgan — o'chirishdan oldin ogohlantirish uchun.</param>
public sealed record AttritionReasonDto(
    long Id,
    string Label,
    bool IsActive,
    int UsageCount);

public sealed record SaveAttritionReasonRequest(string Label, bool IsActive = true);

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
