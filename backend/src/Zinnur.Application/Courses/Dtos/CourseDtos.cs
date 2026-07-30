using Zinnur.Application.Gating.Dtos;

namespace Zinnur.Application.Courses.Dtos;

// ============================================================================
// KURS KONTENTI DTO'LARI (kurs -> modul -> dars)
//
// ENUM'LAR haqida: `LessonLockReason` bu yerda ENUM sifatida saqlanadi,
// satrga qo'lda o'girilmaydi — `Program.cs` dagi `JsonStringEnumConverter`
// uni JSON'da baribir SATR qilib chiqaradi ("TeacherPace"), lekin DTO tur
// xavfsizligini yo'qotmaydi. `Groups` moduli bilan AYNAN bir xil naqsh.
// ============================================================================

/// <summary>
/// Kurs — RO'YXAT qatori (daraxtsiz, yengil).
///
/// NIMA UCHUN DARAXT EMAS: ro'yxatda 50 ta kurs bo'lsa va har biri modul va
/// darslari bilan kelsa, javob megabaytlarga o'sardi. Daraxt faqat
/// <see cref="CourseTreeDto"/> — ya'ni BITTA kurs so'ralganda quriladi.
/// </summary>
/// <param name="ModuleCount">Modul soni (bazada sanaladi — N+1 yo'q).</param>
/// <param name="LessonCount">Kursdagi JAMI dars soni (barcha modullar bo'yicha).</param>
/// <param name="GroupCount">
/// Shu kursga biriktirilgan guruhlar soni. Kursni o'chirishga urinishdan
/// OLDIN ko'rinib tursin — nechta guruh ta'sirlanishini bilish uchun.
/// </param>
public sealed record CourseDto(
    long Id,
    string Name,
    string? Description,
    bool IsActive,
    int Position,
    int ModuleCount,
    int LessonCount,
    int GroupCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Kurs DARAXTI: modullar va ular ichidagi darslar.
///
/// ★ TARTIB KAFOLATI: modullar va darslar bu yerda AYNAN
/// <c>GatingService.OrderedLessons</c> dagi tartibda keladi
/// (modul.Position -> modul.Id -> dars.Position -> dars.Id). Ikki joyda
/// boshqa-boshqa tartib bo'lsa o'quvchi ko'rgan ro'yxat bilan gating
/// hisoblagan ketma-ketlik mos kelmasdi — "3-dars ochiq" deb yozilib,
/// aslida boshqa dars ochilardi.
/// </summary>
public sealed record CourseTreeDto(
    long Id,
    string Name,
    string? Description,
    bool IsActive,
    int Position,
    IReadOnlyList<CourseModuleDto> Modules,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <param name="Position">Kurs ichidagi tartib (0 dan, zich).</param>
public sealed record CourseModuleDto(
    long Id,
    long CourseId,
    string Name,
    int Position,
    IReadOnlyList<CourseLessonDto> Lessons);

/// <summary>
/// Modul ichidagi dars.
/// </summary>
/// <param name="Description">
/// ★ QULFLANGAN darsda <c>null</c>. Sarlavha (<paramref name="Name"/>)
/// KO'RINADI — o'quvchi kursda nima borligini va qayerga intilayotganini
/// bilishi kerak — lekin MAZMUN berilmaydi.
/// </param>
/// <param name="Unlocked">
/// O'quvchi uchun gating natijasi. Xodim (o'quv bo'limi, admin, ustoz,
/// kurator) uchun DOIM <c>true</c>: gating faqat o'quvchiga tegishli.
/// </param>
/// <param name="LockReason">Nima uchun yopiq. Ochiq bo'lsa <c>null</c>.</param>
/// <param name="Completed">
/// ★ Dars TUGATILGANMI: video ko'rilgan **VA** kurs vazifasi topshirilgan
/// **VA** e'lon qilingan test yechilgan — faqat MAVJUD bo'lganlari uchun
/// (qoida: <c>LessonGate.IsComplete</c>).
///
/// NIMA UCHUN <paramref name="Unlocked"/> DAN ALOHIDA: bu ikki BOSHQA-BOSHQA
/// savol. Dars OCHIQ, lekin hali TUGATILMAGAN bo'lishi mumkin — o'quvchi
/// hozir o'tirgan dars aynan shunday. "Ochilgan"ni "tugatilgan" deb
/// ko'rsatish o'quvchini chalg'itardi: u yo'lakchani yashil ko'rib
/// vazifasini topshirmasdi va keyingi dars nega ochilmayotganini
/// tushunmasdi.
///
/// ★ QULFLANGAN DARSDA DOIM <c>false</c>. Talabi yo'q dars (vazifasi ham,
/// e'lon qilingan testi ham yo'q) qoidaga ko'ra "tugatilgan" sanaladi —
/// gating keyingi darsni AYNAN shu asosda ochadi. Lekin ekranda
/// "qulflangan, lekin tugatilgan" ma'nosiz bo'lardi, shuning uchun tashqi
/// shartnomada bu maydon ochiqlikka bo'ysunadi:
/// <c>completed = unlocked &amp;&amp; talab qolmagan</c>.
///
/// ★ TALABI YO'Q OCHIQ DARS DARHOL <c>true</c> bo'ladi. Bu xato emas:
/// "tugatilgan" — "o'quvchi mehnat qildi" degani EMAS, "shu darsda
/// TALAB qilinadigan hech nima qolmadi" degani.
///
/// QO'SHIMCHA SO'ROV YO'Q: qiymat gating daraxtidan olinadi
/// (<c>LessonGateDto.Completed</c>), u esa butun kurs uchun BIR MARTA
/// hisoblanadi va keshlanadi.
///
/// XODIM uchun DOIM <c>false</c>: "tugatish" — o'quvchi progressi, xodimda
/// esa progress yozuvi umuman bo'lmaydi. (<paramref name="Unlocked"/> aksincha,
/// xodimda doim <c>true</c> — u kontentni to'liq ko'radi.)
/// </param>
/// <param name="HasTest">
/// Darsga E'LON QILINGAN test biriktirilganmi. Qoralama test SANALMAYDI —
/// `GatingService` ham aynan shunday hisoblaydi (`t.IsPublished`), ikki
/// joyda boshqa-boshqa ta'rif bo'lsa "test bor, lekin ochilmaydi" degan
/// ziddiyat chiqardi.
/// </param>
public sealed record CourseLessonDto(
    long Id,
    long ModuleId,
    string Name,
    string? Description,
    int Position,
    int? DurationMin,
    bool Unlocked,
    LessonLockReason? LockReason,
    bool Completed,
    bool HasAssignment,
    bool HasTest);

/// <summary>Ro'yxat filtri. Barcha maydonlar ixtiyoriy.</summary>
/// <param name="Search">Kurs nomi bo'yicha qism-satr (kamida 2 belgi).</param>
/// <param name="IsActive">Arxivlanganlarni ajratish uchun.</param>
public sealed record CourseListQuery(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>
/// Yangi kurs. <c>Position</c> SO'RALMAYDI — u oxiriga qo'shiladi va
/// keyin faqat "reorder" amali bilan o'zgaradi (tartib bir joyda boshqarilsin).
/// </summary>
public sealed record CreateCourseRequest(
    string Name,
    string? Description = null,
    bool IsActive = true);

/// <summary>
/// Kursni tahrirlash — TO'LIQ shakl (PUT semantikasi): yuborilmagan maydon
/// standart qiymatga tushadi, shuning uchun klient joriy qiymatlarni
/// qaytarib yuboradi. `Groups` moduli bilan bir xil kelishuv.
///
/// <c>Position</c> bu yerda ham YO'Q — u "reorder" amalining ishi.
/// </summary>
public sealed record UpdateCourseRequest(
    string Name,
    string? Description = null,
    bool IsActive = true);

public sealed record CreateModuleRequest(string Name);

public sealed record UpdateModuleRequest(string Name);

public sealed record CreateLessonRequest(
    string Name,
    string? Description = null,
    int? DurationMin = null);

public sealed record UpdateLessonRequest(
    string Name,
    string? Description = null,
    int? DurationMin = null);

/// <summary>
/// Tartibni o'zgartirish so'rovi.
///
/// ★ TO'LIQ ro'yxat kutiladi — "shu elementni 3-o'ringa ko'chir" EMAS.
///
/// NIMA UCHUN: qisman so'rov ("A ni 2-o'ringa") serverda qolganlarni
/// SURISHNI talab qiladi va ikki klient bir vaqtda surganda natija
/// aytib bo'lmaydigan bo'lib qoladi. To'liq ro'yxat esa bir ma'noli:
/// klient nima ko'rgan bo'lsa, o'shani qaytaradi va server AYNAN shu
/// ketma-ketlikni 0,1,2... qilib yozadi. Ro'yxat to'liq bo'lmasa yoki
/// begona Id bo'lsa — 400 (jimgina yarim tartib yozilmaydi).
/// </summary>
public sealed record ReorderRequest(IReadOnlyList<long> OrderedIds);

/// <summary>Reorder natijasi — har elementning YANGI tartib raqami.</summary>
public sealed record PositionDto(long Id, int Position);
