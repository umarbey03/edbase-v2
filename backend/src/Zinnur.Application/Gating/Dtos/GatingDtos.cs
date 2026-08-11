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

    /// <summary>
    /// ★ Dars GURUH boshlagan qismdan OLDINDA.
    ///
    /// Guruh kursning o'rtasidan boshlagan
    /// (<c>Group.VideoStartLessonId</c>) — bu dars uning o'quv rejasiga
    /// UMUMAN kirmaydi. O'quvchining aybi yo'q, shuning uchun "oldingi
    /// darsni tugating" deyish ma'nosiz bo'lardi: sabab alohida.
    ///
    /// ★ NIMA UCHUN "YASHIRISH" EMAS, "BELGILASH": o'quvchi kursda nima
    /// borligini ko'rishi kerak (kursning boshqa guruhlari uni o'tadi) va
    /// keyinchalik unga qo'lda ruxsat berilishi mumkin
    /// (<c>UnlockedOverride</c>). Butunlay yashirish esa kurs daraxtini
    /// guruhga qarab O'ZGARTIRIB yuborardi va "3-dars" degan tushuncha
    /// guruhdan guruhga siljib ketardi — gating tartibi bilan tashqi
    /// ko'rinish mos kelmasdi.
    ///
    /// ★★ FRONTEND UCHUN SHARTNOMA: shu sabab bilan kelgan dars
    ///   • "guruh bu qismdan boshlamaydi" deb belgilanadi (kul rangda),
    ///     "oldingi darsni tugat" deb EMAS;
    ///   • KURS PROGRESSI MAXRAJIGA KIRMAYDI. Aks holda hech qachon
    ///     o'tilmaydigan 20 ta dars maxrajda qolib, progress abadiy
    ///     40% da qotib turardi.
    /// </summary>
    BeforeGroupStart = 3,
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
/// <param name="VideoStartLessonId">
/// Guruh video darslarni QAYSI darsdan boshlaydi (<c>Group.VideoStartLessonId</c>).
/// <c>null</c> — kurs boshidan.
///
/// ★ KESH UCHUN MUHIM: bu qiymat keshdagi snapshot'ni tekshirishda
/// <c>TaughtLessonCount</c> bilan bir qatorda taqqoslanadi. Aks holda o'quv
/// bo'limi boshlanish nuqtasini o'zgartirsa, o'quvchi TTL tugaguncha
/// (60 s) eski qulflar bilan qolardi.
/// </param>
/// <param name="StartIndex">
/// Boshlanish darsining kurs ichidagi GLOBAL tartib raqami (0 dan) —
/// zanjir va ustoz sur'ati AYNAN shu nuqtadan hisoblanadi.
/// Cheklov yo'q bo'lsa 0, ya'ni bugungi xatti-harakat.
/// </param>
public sealed record CourseGateDto(
    long? CourseId,
    int TaughtLessonCount,
    long? VideoStartLessonId,
    int StartIndex,
    IReadOnlyList<LessonGateDto> Lessons);
