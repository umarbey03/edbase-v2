using Zinnur.Domain.Enums;
using Zinnur.Domain.Staffing;

namespace Zinnur.Application.Groups.Dtos;

// ============================================================================
// GURUH DTO'LARI
//
// ENUM'LAR haqida: bu yerda enum turlari SAQLANADI (`GroupType`, `DayOfWeek`,
// `MemberStatus`), satrga qo'lda o'girilmaydi. `Program.cs` da
// `JsonStringEnumConverter` ro'yxatdan o'tgani uchun JSON'da ular baribir
// SATR ko'rinishida chiqadi va satr ko'rinishida qabul qilinadi:
//     "type": "Curator",  "weekdays": ["Monday", "Wednesday"]
// Ya'ni sim ustidagi format `Users` moduliga AYNAN mos, lekin DTO tur
// xavfsizligini yo'qotmaydi (noto'g'ri qiymat 400 bo'lib qaytadi).
// ============================================================================

/// <summary>
/// Guruh — ro'yxat va kartochka uchun YAGONA shakl.
///
/// NIMA UCHUN BITTA DTO: ro'yxat va kartochka bir xil maydonlarni ko'rsatadi
/// (nomlar, a'zolar soni). Ikki DTO bo'lsa yangi maydon bittasiga qo'shilib,
/// ikkinchisida unutilardi.
/// </summary>
/// <param name="EndDate">Kurs tugash sanasi — <c>StartDate + CourseMonths</c>.</param>
/// <param name="MemberCount">
/// Faol a'zolar soni. KURATOR guruhida a'zolar bevosita yo'q — ular
/// <c>CuratorGroupId</c> orqali bog'langan ustoz guruhlaridan sanaladi.
/// </param>
/// <param name="ArchivedCount">
/// Faol BO'LMAGAN a'zolar soni (ko'chirilgan + muzlatilgan + chiqarilgan,
/// ya'ni <c>MemberStatus</c> — <c>Moved</c> + <c>Paused</c> + <c>Stopped</c>).
/// Hisoblash doirasi <paramref name="MemberCount"/> bilan bir xil (kurator
/// guruhida bog'langan ustoz guruhidan sanaladi).
/// </param>
/// <param name="SessionCount">Bekor qilinmagan darslar soni (jadval hajmi).</param>
/// <param name="VideoStartLessonId">
/// Video darslar QAYSI kurs darsidan boshlanadi. <c>null</c> — guruh kursni
/// BOSHIDAN boshlaydi (eng ko'p uchraydigan holat va bugungi xatti-harakat).
///
/// Sabab: bitta kursga ko'p guruh biriktiriladi va keyin ochilgan guruh
/// kursning O'RTASIDAN boshlaydi — batafsil
/// <see cref="Zinnur.Domain.Entities.Group.VideoStartLessonId"/>.
/// </param>
/// <param name="VideoStartLessonName">
/// Boshlanish darsining nomi. <paramref name="VideoStartLessonId"/>
/// <c>null</c> bo'lsa bu ham <c>null</c>.
/// </param>
/// <param name="VideoStartModuleName">
/// Boshlanish darsi QAYSI modulda. UI ikkisini birga ko'rsatadi:
/// "3-modul · 2-dars". Nomlar bazada ichki <c>SELECT</c> bilan olinadi —
/// kartochka uchun qo'shimcha so'rov ketmaydi (N+1 yo'q).
/// </param>
public sealed record GroupDto(
    long Id,
    string Name,
    GroupType Type,
    long? CourseId,
    string? CourseName,

    /// <summary>
    /// O'quv YO'NALISHI (R21b). <c>null</c> — yorliq qo'yilmagan (talab
    /// kelganidagi 33 guruhning holati).
    ///
    /// ⚠️ <paramref name="CourseId"/> BILAN ARALASHTIRILMASIN — chegara
    /// <see cref="Zinnur.Domain.Entities.GroupCategory"/> izohida.
    /// </summary>
    long? CategoryId,

    /// <summary>
    /// Kategoriya nomi. <paramref name="CategoryId"/> <c>null</c> bo'lsa bu
    /// ham <c>null</c>. Nom bazada JOIN bilan olinadi — ro'yxatning har
    /// qatori uchun qo'shimcha so'rov ketmaydi (N+1 yo'q).
    /// </summary>
    string? CategoryName,
    long? VideoStartLessonId,
    string? VideoStartLessonName,
    string? VideoStartModuleName,
    long? TeacherId,
    string? TeacherName,
    long? AssistantId,
    string? AssistantName,
    long? CuratorGroupId,
    string? CuratorGroupName,
    DateOnly StartDate,
    DateOnly EndDate,
    int CourseMonths,
    IReadOnlyList<DayOfWeek> Weekdays,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsActive,
    bool RecordEnabled,

    /// <summary>
    /// Shu guruhning yozuvlari o'quvchilarga ko'rinadimi (R5).
    /// ⚠️ <c>RecordEnabled</c> BILAN ARALASHTIRILMASIN — farqi
    /// <see cref="Zinnur.Domain.Entities.Group.RecordingsVisibleToStudents"/>
    /// izohida.
    /// </summary>
    bool RecordingsVisibleToStudents,

    /// <summary>
    /// Bu guruhning darslari QAYSI mexanizm bilan yoziladi (JSON'da SATR:
    /// <c>"RoomComposite"</c> / <c>"TrackComposition"</c>). Standart
    /// <c>RoomComposite</c> — bugungi xatti-harakat.
    ///
    /// ⚠️ <paramref name="RecordEnabled"/> BILAN ARALASHTIRILMASIN: u
    /// "yozilsinmi", bu esa "QANDAY yozilsin". Yozuvi o'chiq guruh bu
    /// yerda nima turishidan qat'i nazar yozilmaydi.
    ///
    /// 🔴 GLOBAL KALIT USTUNROQ: <c>recordings.track_pipeline_enabled</c>
    /// o'chiq bo'lsa bu tanlov E'TIBORGA OLINMAYDI va guruh eski yo'lga
    /// qaytadi. Interfeys buni ko'rsatishi kerak, aks holda o'quv bo'limi
    /// "tanladim, lekin ishlamadi" degan holatga tushadi.
    /// </summary>
    RecordingPipeline RecordingPipeline,

    /* ===== R33 + R40 · KIM MAS'UL ===== */

    /// <summary>
    /// R33 — bu guruhning topshirilgan ishlarini kim tekshiradi
    /// (JSON'da SATR: <c>"Both"</c> / <c>"Teacher"</c> / <c>"Assistant"</c>).
    /// Standart <c>Both</c> — bugungi xatti-harakat.
    /// </summary>
    GroupStaffRole AssignmentGraderRole,

    /// <summary>
    /// R40 — bu guruh o'quvchilarining savollariga kim javob beradi.
    /// Standart <c>Assistant</c> — bugungi xatti-harakat.
    /// </summary>
    GroupStaffRole QuestionResponderRole,
    int MemberCount,
    int ArchivedCount,
    int SessionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Ro'yxat filtri. Barcha maydonlar ixtiyoriy.</summary>
/// <param name="Search">
/// ERKIN MATN qidiruvi (kamida 2 belgi), qism-satr, katta-kichik harf
/// farqlamaydi. Qamrov (R22): <b>guruh nomi</b>, <b>ustoz F.I.Sh.</b>,
/// <b>kurator F.I.Sh.</b>, <b>biriktirilgan kurator guruhi nomi</b> va
/// <b>kurs nomi</b>. Bittasi mos kelsa yetarli (OR).
///
/// ★ SONLI/VAQT/ENUM maydonlari (davomiylik, boshlanish soati, hafta
/// kunlari) bu yerga KIRMAYDI — ular tuzilgan filtr bo'lib qoladi
/// (<paramref name="Type"/>, <paramref name="IsActive"/>). Sabab
/// <c>GroupService.ApplySearch</c> izohida.
/// </param>
/// <param name="Type">Guruh turi bo'yicha filtr.</param>
/// <param name="IsActive">Arxivlanganlarni ajratish uchun.</param>
/// <param name="CategoryId">
/// O'quv yo'nalishi bo'yicha filtr (R21b). <c>null</c> — filtrlanmaydi.
///
/// ★ "KATEGORIYASIZ GURUHLAR" uchun alohida qiymat ATAYLAB YO'Q: HTTP
/// so'rovda "bo'sh" va "berilmagan" ni ajratish uchun sun'iy sentinel
/// (masalan <c>categoryId=0</c>) kerak bo'lardi va u <c>long?</c> ning
/// ma'nosini buzardi. Bunday guruhlarni topish ehtiyoji hozircha yo'q —
/// paydo bo'lsa alohida <c>bool? HasCategory</c> qo'shiladi.
/// </param>
/// <param name="Page">Sahifa (1 dan).</param>
/// <param name="PageSize">Sahifa hajmi (1..100, default 25).</param>
public sealed record GroupListQuery(
    string? Search = null,
    GroupType? Type = null,
    bool? IsActive = null,
    long? CategoryId = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>
/// Yangi guruh. Jadval qoidasi (sana, kunlar, soat, davomiylik, oy) MAJBURIY —
/// guruh yaratilishi bilan butun kurs jadvali generatsiya qilinadi.
/// </summary>
public sealed record CreateGroupRequest(
    string Name,
    DateOnly StartDate,
    IReadOnlyList<DayOfWeek> Weekdays,
    TimeOnly StartTime,
    GroupType Type = GroupType.Group,
    int DurationMinutes = 80,
    int CourseMonths = 8,
    long? CourseId = null,
    long? TeacherId = null,
    long? AssistantId = null,
    long? CuratorGroupId = null,
    bool RecordEnabled = false,
    bool IsActive = true,
    long? VideoStartLessonId = null,

    /// <summary>O'quv yo'nalishi (R21b). <c>null</c> — yorliqsiz guruh.</summary>
    long? CategoryId = null,

    /// <summary>
    /// R5. 🔴 STANDART <c>true</c> VA BU MAJBURIY: <c>false</c> bo'lsa,
    /// maydonni yubormagan har bir klient guruh yozuvlarini JIMGINA yopib
    /// qo'yardi (bu — PUT semantikasi, pastdagi izohga qarang).
    /// </summary>
    bool RecordingsVisibleToStudents = true,

    /// <summary>
    /// Yozuv mexanizmi. 🔴 STANDART <c>RoomComposite</c> VA BU MAJBURIY:
    /// bu bugungi xatti-harakat, ya'ni maydonni yubormagan klient guruhni
    /// jimgina tajriba quvuriga o'tkazib yubora olmaydi.
    /// </summary>
    RecordingPipeline RecordingPipeline = RecordingPipeline.RoomComposite,

    /// <summary>
    /// R33 — tekshiruvchi. 🔴 STANDART <c>Both</c> VA BU MAJBURIY: bu
    /// bugungi xatti-harakat, ya'ni maydonni yubormagan klient hech
    /// narsani o'zgartirmaydi.
    /// </summary>
    GroupStaffRole AssignmentGraderRole = GroupStaffRole.Both,

    /// <summary>
    /// R40 — savollarga javob beruvchi. 🔴 STANDART <c>Assistant</c> VA
    /// BU MAJBURIY: standart <c>Both</c> bo'lsa, maydonni yubormagan har
    /// bir klient guruh savollarini JIMGINA ustozga ham ochib yuborardi.
    /// </summary>
    GroupStaffRole QuestionResponderRole = GroupStaffRole.Assistant);

/// <summary>
/// Guruhni tahrirlash. TO'LIQ shakl (PUT semantikasi): yuborilmagan maydon
/// standart qiymatga tushadi, shuning uchun klient joriy qiymatlarni
/// qaytarib yuboradi.
///
/// ⚠️ Jadvalga ta'siri: <see cref="Zinnur.Domain.Entities.Group.ScheduleRuleDiffersFrom"/>
/// qaysi maydon o'zgarganini aniqlaydi va faqat SHU asosda jadval qayta
/// tuziladi (batafsil: <c>GroupService.UpdateAsync</c> izohi).
///
/// 🔴 <c>VideoStartLessonId</c> ham AYNI qoidaga bo'ysunadi: yuborilmasa
/// <c>null</c> ga tushadi, ya'ni guruh kursni boshidan boshlaydigan holatga
/// qaytadi. Tahrirlash formasi joriy qiymatni YUKLAB, qaytarib yuborishi
/// shart. Kursni almashtirganda esa uni yuborMASLIK (yoki <c>null</c>
/// yuborish) kerak — begona kursning darsi 400 bilan rad etiladi.
/// </summary>
public sealed record UpdateGroupRequest(
    string Name,
    DateOnly StartDate,
    IReadOnlyList<DayOfWeek> Weekdays,
    TimeOnly StartTime,
    GroupType Type = GroupType.Group,
    int DurationMinutes = 80,
    int CourseMonths = 8,
    long? CourseId = null,
    long? TeacherId = null,
    long? AssistantId = null,
    long? CuratorGroupId = null,
    bool RecordEnabled = false,
    bool IsActive = true,
    long? VideoStartLessonId = null,

    /// <summary>
    /// O'quv yo'nalishi (R21b).
    ///
    /// 🔴 BU PUT: yuborilmasa <c>null</c> ga tushadi, ya'ni guruh yorlig'ini
    /// YO'QOTADI. Tahrirlash formasi joriy qiymatni yuklab, qaytarib
    /// yuborishi SHART — aynan shu tuzoq loyihada bir marta ishlagan
    /// (kurs uzilib, butun guruhda gating `NotInCourse` bo'lgan) va u
    /// frontendda `buildPayload` bilan yopilgan.
    /// </summary>
    long? CategoryId = null,

    /// <summary>
    /// R5. 🔴 STANDART <c>true</c> — sabab yuqoridagi
    /// <see cref="CreateGroupRequest"/> dagidek, LEKIN BU YERDA U YANADA
    /// MUHIM: bu PUT, ya'ni maydonni yubormagan eski klient guruh
    /// yozuvlarini har tahrirda yopib qo'yardi va buni hech kim
    /// so'ramagan bo'lardi.
    /// </summary>
    bool RecordingsVisibleToStudents = true,

    /// <summary>
    /// Yozuv mexanizmi. Standart <c>RoomComposite</c> = bugungi
    /// xatti-harakat.
    ///
    /// 🔴 PUT semantikasi bu yerda ham amal qiladi: maydonni yubormagan
    /// klient guruhni ESKI yo'lga qaytarib qo'yadi. Standart ataylab
    /// shunday tanlangan — teskarisi (yangi quvur) bo'lganda har tahrir
    /// guruhni hech kim so'ramagan holda tajriba yo'liga o'tkazardi.
    /// Tahrirlash formasi joriy qiymatni yuklab, qaytarib yuboradi
    /// (`buildPayload` naqshi).
    /// </summary>
    RecordingPipeline RecordingPipeline = RecordingPipeline.RoomComposite,

    /// <summary>R33 — tekshiruvchi. Standart <c>Both</c> = bugungi xatti-harakat.</summary>
    GroupStaffRole AssignmentGraderRole = GroupStaffRole.Both,

    /// <summary>
    /// R40 — savollarga javob beruvchi. Standart <c>Assistant</c> = bugungi
    /// xatti-harakat.
    ///
    /// 🔴 PUT semantikasi bu yerda ayniqsa xavfli: standart <c>Both</c>
    /// bo'lganda maydonni yubormagan eski klient HAR TAHRIRDA guruh
    /// savollarini ustozga ham ochib yuborardi va buni hech kim
    /// so'ramagan bo'lardi.
    /// </summary>
    GroupStaffRole QuestionResponderRole = GroupStaffRole.Assistant);

/// <summary>Yaratilgan guruh + generatsiya qilingan darslar soni.</summary>
public sealed record CreateGroupResponse(
    GroupDto Group,
    int SessionsCreated);

/// <summary>
/// Tahrirlash natijasi. <paramref name="Schedule"/> — jadvalga AYNAN nima
/// qilingani; klient buni foydalanuvchiga ko'rsatishi kerak, chunki jadval
/// qayta tuzilishi dars havolalarini o'zgartiradi.
/// </summary>
public sealed record UpdateGroupResponse(
    GroupDto Group,
    Scheduling.Dtos.ScheduleChangeSummary Schedule);

/// <summary>
/// Guruh a'zosi (o'quvchi).
/// </summary>
/// <param name="Id">A'zolik yozuvining Id'si (o'quvchining Id'si emas).</param>
/// <param name="Email">
/// 🔴 <c>null</c> — so'rovchi USTOZ (talab R27). Bazada ustun majburiy,
/// ya'ni bo'shlik faqat SERVER kesganidan darak beradi. Kesish
/// <c>GroupService.ProjectMembers</c> da, KURATOR bundan mustasno.
/// </param>
/// <param name="Phone">
/// <c>null</c> — raqam kiritilmagan YOKI so'rovchi ustoz (yuqoriga qarang).
/// Interfeys ikkalasini ajrata olmaydi, shuning uchun "Telefon kiritilmagan"
/// matni ustozga KO'RSATILMAYDI — u yolg'on bo'lardi.
/// </param>
/// <param name="PausedUntil">Pauza qachongacha (ixtiyoriy).</param>
/// <param name="SourceGroupId">
/// A'zolik AYNAN qaysi guruhda yozilgan. Kurator guruhi ro'yxatida bu
/// bog'langan USTOZ guruhi bo'ladi — kurator o'quvchi qaysi guruhdan
/// kelganini ko'rishi kerak.
/// </param>
/// <param name="LeftAt">
/// <c>Status</c> <c>Stopped</c>/<c>Moved</c>ga o'tgan vaqt. <c>null</c> —
/// hozir faol yoki pauzada (arxiv jadvali shu maydon bo'yicha filtrlaydi).
/// </param>
/// <param name="LeftByName">Chiqarish/ko'chirishni bajargan xodim ismi.</param>
/// <param name="MovedToGroupId">
/// <c>Status == Moved</c>da — qaysi guruhga ko'chirilgan. Boshqa holatda <c>null</c>.
/// </param>
/// <param name="MovedToGroupName">Yuqoridagi bilan JUFT — ko'rsatish uchun tayyor nom.</param>
/// <param name="Reason">Ko'chirish sababi (ko'chirishda MAJBURIY yozilgan).</param>
public sealed record GroupMemberDto(
    long Id,
    long StudentId,
    string FullName,
    string? Email,
    string? Phone,
    MemberStatus Status,
    DateTimeOffset JoinedAt,
    DateOnly? PausedUntil,
    long SourceGroupId,
    string SourceGroupName,
    DateTimeOffset? LeftAt,
    string? LeftByName,
    long? MovedToGroupId,
    string? MovedToGroupName,
    string? Reason);

/// <param name="StudentId">Faqat <c>Student</c> rolidagi foydalanuvchi.</param>
public sealed record AddMemberRequest(long StudentId);

/// <param name="PausedUntil">
/// Pauza tugash sanasi. <c>null</c> — muddatsiz pauza (qo'lda tiklanadi).
/// </param>
/// <param name="Reason">
/// Muzlatish sababi — MAJBURIY (loyiha egasi, 2026-08-17). "To'kilishlar"
/// paneli muzlatishni ham ko'rsatadi; sababsiz qator u yerda ma'nosiz.
/// </param>
/// <param name="ReasonId">
/// Sabab TASNIFI katalogdan (<c>AttritionReason</c>, 2026-08-18) — hisobotdagi
/// foizlar shu bo'yicha hisoblanadi. <paramref name="Reason"/> matni esa AYNI
/// holatning tafsiloti bo'lib qoladi; biri ikkinchisini almashtirmaydi.
/// </param>
public sealed record PauseMemberRequest(
    DateOnly? PausedUntil = null,
    string? Reason = null,
    long? ReasonId = null);

/// <summary>
/// Guruhdan chiqarish (2026-08-17 dan tanaga ega).
///
/// ★ NIMA UCHUN SO'ROV TANASI PAYDO BO'LDI: ilgari chiqarish sababsiz
/// bajarilardi va "nega bu o'quvchi ketdi?" savoliga javob HECH QAYERDA
/// yo'q edi. Endi sabab majburiy — u a'zolik qatoriga ham, o'chmaydigan
/// hodisa jurnaliga ham yoziladi.
/// </summary>
/// <param name="ReasonId">Sabab tasnifi katalogdan — <c>PauseMemberRequest</c> dagi AYNI ma'no.</param>
public sealed record RemoveMemberRequest(string? Reason = null, long? ReasonId = null);

/// <param name="TargetGroupId">Qaysi guruhga ko'chiriladi.</param>
/// <param name="Reason">
/// Ko'chirish sababi — MAJBURIY (loyiha egasi, 2026-08-15: *"guruhdan
/// guruhga olib o'tishda sabab kiritilishi shart"*). Bo'sh bo'lsa 409
/// (`GroupService.MoveMemberAsync`).
/// </param>
/// <param name="ReasonId">Sabab tasnifi katalogdan — <c>PauseMemberRequest</c> dagi AYNI ma'no.</param>
public sealed record MoveMemberRequest(long TargetGroupId, string Reason, long? ReasonId = null);

/// <summary>Ko'chirish natijasi — ikki tomon ham qaytadi (UI ikkalasini yangilaydi).</summary>
/// <param name="Left">Eski guruhdagi yozuv (holati <c>Moved</c>).</param>
/// <param name="Arrived">Yangi guruhdagi yozuv (holati <c>Active</c>).</param>
public sealed record MoveMemberResponse(
    GroupMemberDto Left,
    GroupMemberDto Arrived);

/// <summary>
/// Ustoz guruhi bog'lanishi MUMKIN bo'lgan kurator guruhi.
/// </summary>
/// <param name="LinkedGroupCount">Shu kuratorga allaqachon bog'langan guruhlar soni.</param>
public sealed record CuratorCandidateDto(
    long Id,
    string Name,
    long? AssistantId,
    string? AssistantName,
    long? CourseId,
    string? CourseName,
    IReadOnlyList<DayOfWeek> Weekdays,
    TimeOnly StartTime,
    int LinkedGroupCount);
