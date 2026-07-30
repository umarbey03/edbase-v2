namespace Zinnur.Application.Gating.Dtos;

/// <summary>Dars nima uchun yopiq. <c>null</c> — ochiq.</summary>
public enum LessonLockReason
{
    /// <summary>Oldingi dars tugatilmagan (video/vazifa/test).</summary>
    PreviousIncomplete = 0,

    /// <summary>Ustoz hali bu darsga yetmagan (sur'at nazorati).</summary>
    TeacherPace = 1,

    /// <summary>Dars o'quvchining kursiga tegishli emas (yoki guruhga kurs biriktirilmagan).</summary>
    NotInCourse = 2,
}

/// <summary>
/// Bitta darsning ochiqlik holati.
///
/// <c>Completed</c> — dars TUGATILGANMI (video ko'rilgan + vazifa topshirilgan +
/// test yechilgan, mavjud bo'lganlari uchun). <c>Unlocked</c> — dars OCHIQMI.
/// Ikkisi boshqa-boshqa savol: dars ochiq, lekin tugatilmagan bo'lishi mumkin.
/// </summary>
public sealed record LessonGateDto(
    long LessonId,
    int Index,
    bool Unlocked,
    LessonLockReason? LockReason,
    bool Completed,
    bool HasVideo,
    bool VideoWatched,
    bool HasAssignment,
    bool AssignmentSubmitted,
    bool HasTest,
    bool TestTaken,
    bool UnlockedOverride);

/// <summary>
/// O'quvchining butun kursi bo'yicha ochiqlik xaritasi.
///
/// KESHLANADI (Redis, ~60 s). Eski tizim bu daraxtni HAR SO'ROVDA qayta
/// qurardi — hatto bitta darsning ochiqligini tekshirish uchun ham
/// (bitta test topshirishga ~30 ta so'rov ketardi).
/// </summary>
/// <param name="CourseId">O'quvchi guruhiga biriktirilgan kurs. <c>null</c> — kurs yo'q.</param>
/// <param name="TaughtLessonCount">
/// Ustoz sur'ati: guruhda YAKUNLANGAN ustoz darslari soni.
/// Shu songa teng indeksdagi dars ham ochiq bo'ladi (ustoz o'tgan darsdan
/// KEYINGI dars ochiladi) — batafsil <see cref="Zinnur.Application.Gating.LessonGate"/>.
/// </param>
public sealed record CourseGateDto(
    long? CourseId,
    int TaughtLessonCount,
    IReadOnlyList<LessonGateDto> Lessons);
