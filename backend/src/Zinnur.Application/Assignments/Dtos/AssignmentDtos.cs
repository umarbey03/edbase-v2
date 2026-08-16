using Zinnur.Domain.Enums;
using Zinnur.Domain.Staffing;

namespace Zinnur.Application.Assignments.Dtos;

/// <summary>
/// Uy vazifasi (xodim ko'rinishi).
/// </summary>
/// <param name="GroupId">Guruh vazifasi bo'lsa — guruh; kurs vazifasida <c>null</c>.</param>
/// <param name="ModuleLessonId">Kurs vazifasi bo'lsa — dars; guruh vazifasida <c>null</c>.</param>
/// <param name="AllowedFormats">
/// Bayroqlar birlashmasi. JSON'da SATR: <c>"Text, Image"</c>. So'rov ham,
/// javob ham AYNI shakl — API assimetrik bo'lmasin.
/// </param>
/// <param name="SubmissionCount">Topshirilgan javoblar soni.</param>
/// <param name="GradedCount">Ulardan baholanganlari.</param>
/// <param name="ImageKey">
/// ⚠️ ESKIRGAN (deprecated) — o'rniga <paramref name="Attachments"/>.
///
/// Maydon FAQAT mavjud klientlar buzilmasligi uchun qoldirildi. Migratsiya
/// eski qiymatlarni <c>AssignmentAttachment</c> ga KO'CHIRDI (backfill),
/// ya'ni AYNI rasm endi <paramref name="Attachments"/> da ham bor. YANGI
/// UI bu maydonni o'qimasin va yubormasin.
///
/// 🔴 Bu maydonning O'ZI ombor kaliti, ya'ni 16-tuzoqning buzilishi. Uni
/// javobdan olib tashlash kerak, lekin bu mavjud frontendni buzadi —
/// shuning uchun alohida ish sifatida hisobotda qayd etilgan.
/// </param>
/// <param name="Attachments">
/// Vazifa SHARTIGA biriktirilgan fayllar (rasm/audio/hujjat), TARTIB
/// bo'yicha. 🔴 `objectKey` YO'Q.
/// </param>
public sealed record AssignmentDto(
    long Id,
    long? GroupId,
    string? GroupName,
    long? ModuleLessonId,
    string? ModuleLessonName,
    string Title,
    string? Description,
    decimal MaxScore,
    DateTimeOffset? DueAt,
    AnswerFormats AllowedFormats,
    string? ImageKey,
    IReadOnlyList<AssignmentAttachmentDto> Attachments,
    long? CreatedById,
    int SubmissionCount,
    int GradedCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,

    /* ===== R33 ===== */

    /// <summary>
    /// Shu vazifani KIM tekshiradi. <c>null</c> — guruh sozlamasi ishlaydi
    /// (<c>Group.AssignmentGraderRole</c>). JSON'da SATR: <c>"Assistant"</c>.
    ///
    /// ★ FAQAT GURUH vazifasida to'ldirilishi mumkin — sabab
    /// <c>Assignment.GraderRole</c> izohida.
    /// </summary>
    GroupStaffRole? GraderRole = null);

/// <summary>
/// Vazifa SHARTIGA biriktirilgan bitta fayl.
///
/// 🔴 `objectKey` BU YERDA YO'Q VA QO'SHILMAYDI (`DAVOM_ETTIRISH.md`
/// 6-bo'lim, 16-tuzoq). Fayl DOIM
/// <c>GET /api/v1/assignments/attachments/{id}</c> orqali, har so'rovda
/// tekshiriladigan ruxsat bilan o'qiladi.
/// </summary>
/// <param name="Kind">`"Image"`, `"Audio"` yoki `"Document"` (JSON'da SATR).</param>
/// <param name="DurationSec">
/// Audio davomiyligi (sekund). ⚠️ Qiymat KLIENTDAN keladi — serverda media
/// dekoder yo'q, ya'ni u faqat KO'RSATISH uchun.
/// </param>
public sealed record AssignmentAttachmentDto(
    long Id,
    long AssignmentId,
    AttachmentKind Kind,
    int Position,
    string ContentType,
    long SizeBytes,
    int? DurationSec,
    DateTimeOffset CreatedAt);

/// <summary>
/// O'quvchi ko'rinishi: vazifa + O'ZINING javobi holati.
/// </summary>
/// <param name="LessonUnlocked">
/// Kurs vazifasi bo'lsa — darsi ochiqmi (gating). Guruh vazifasida DOIM
/// <c>true</c>: guruh vazifasi kurs sur'atiga bog'lanmagan.
/// </param>
/// <param name="CanSubmit">
/// Hozir topshirish mumkinmi. Server qarori — klient buni QAYTA hisoblamasin.
/// </param>
public sealed record StudentAssignmentDto(
    long Id,
    long? GroupId,
    string? GroupName,
    long? ModuleLessonId,
    string? ModuleLessonName,
    string Title,
    string? Description,
    decimal MaxScore,
    DateTimeOffset? DueAt,
    AnswerFormats AllowedFormats,
    string? ImageKey,

    /// <summary>
    /// Shart biriktirmalari. QULFLANGAN darsning vazifasida BO'SH
    /// (`objectKey` esa hech qachon yo'q).
    /// </summary>
    IReadOnlyList<AssignmentAttachmentDto> Attachments,
    bool IsOverdue,
    bool LessonUnlocked,
    bool CanSubmit,
    StudentSubmissionDto? MySubmission);

/// <summary>O'quvchining o'z javobi (qisqa shakl).</summary>
/// <param name="Files">O'QUVCHI biriktirgan fayllar.</param>
/// <param name="FeedbackFiles">
/// USTOZ tekshirishda biriktirgan fayllar (R37) — tuzatilgan varaq surati,
/// namuna talaffuzi, PDF sharh.
///
/// 🔴 <paramref name="Files"/> BILAN ARALASHTIRILMASIN: bu ikkalasi
/// BOSHQA-BOSHQA jadvaldan keladi va ularning yuklab olish manzillari ham
/// boshqa (<c>/submissions/files/{id}</c> va
/// <c>/submissions/feedback-files/{id}</c>). Sabab
/// <c>SubmissionFeedbackFile</c> izohida.
/// </param>
public sealed record StudentSubmissionDto(
    long Id,
    SubmissionStatus Status,
    string? Text,
    decimal? Score,
    decimal? ScorePercent,
    string? Feedback,
    DateTimeOffset SubmittedAt,
    int AttemptNumber,
    bool AllowResubmit,
    string? ResubmitNote,
    bool IsLate,
    IReadOnlyList<SubmissionFileDto> Files,
    IReadOnlyList<SubmissionFeedbackFileDto> FeedbackFiles);

/// <summary>Baholash ro'yxati uchun to'liq javob (xodim ko'rinishi).</summary>
public sealed record SubmissionDto(
    long Id,
    long AssignmentId,
    long StudentId,
    string StudentName,
    string? Text,
    SubmissionStatus Status,
    decimal? Score,
    decimal? ScorePercent,
    string? Feedback,
    long? GradedById,
    DateTimeOffset? GradedAt,
    DateTimeOffset SubmittedAt,
    int AttemptNumber,
    bool AllowResubmit,
    string? ResubmitNote,
    bool IsLate,
    IReadOnlyList<SubmissionFileDto> Files,
    IReadOnlyList<SubmissionFeedbackFileDto> FeedbackFiles);

/// <summary>
/// USTOZ tekshirishda biriktirgan fayl (R37).
///
/// 🔴 <see cref="SubmissionFileDto"/> DAN FARQI — <c>ObjectKey</c> YO'Q.
/// U yerda kalit tarixiy sabablarga ko'ra qolgan (eski klientlar), yangi
/// maydonda esa uni takrorlashning ma'nosi yo'q: baytlar
/// <c>GET /api/v1/submissions/feedback-files/{id}</c> orqali, ruxsat
/// tekshiruvidan o'tib olinadi.
/// </summary>
/// <param name="FileName">
/// Ustoz bergan nom (tozalangan). Hujjat uchun MUHIM — o'quvchi
/// "tuzatilgan-varaq.pdf" ni ko'rishi kerak.
/// </param>
/// <param name="CreatedById">Kim biriktirgani (xodim Id'si).</param>
public sealed record SubmissionFeedbackFileDto(
    long Id,
    long SubmissionId,
    AttachmentKind Kind,
    string ContentType,
    string? FileName,
    long SizeBytes,
    long? CreatedById,
    DateTimeOffset CreatedAt);

/// <summary>
/// Ilova qilingan fayl.
///
/// FAQAT OBYEKT KALITI qaytadi, to'liq URL EMAS: presigned URL muddatli
/// (bir soat) va uni javobga solib qo'yish "linkim ishlamayapti" muammosini
/// keltirardi. Ko'rish linki autentifikatsiyalangan media endpointi orqali
/// alohida so'raladi (FAZA 5.4).
/// </summary>
public sealed record SubmissionFileDto(
    long Id,
    string ObjectKey,
    AttachmentKind Kind,
    long SizeBytes,
    string? ContentType);

/// <param name="GroupId">Guruh vazifasi uchun. <c>ModuleLessonId</c> bilan BIRGA bo'lmaydi.</param>
/// <param name="ModuleLessonId">Kurs vazifasi uchun (faqat o'quv bo'limi/admin).</param>
/// <param name="AllowedFormats">Masalan <c>"Text, Audio"</c> — arab tili talaffuzi uchun.</param>
public sealed record CreateAssignmentRequest(
    string Title,
    long? GroupId = null,
    long? ModuleLessonId = null,
    string? Description = null,
    decimal MaxScore = 5m,
    DateTimeOffset? DueAt = null,
    AnswerFormats AllowedFormats = AnswerFormats.Text | AnswerFormats.Image,
    string? ImageKey = null,

    /// <summary>
    /// R33 — shu vazifaning tekshiruvchisi. <c>null</c> (standart) — guruh
    /// sozlamasi ishlaydi. KURS vazifasida to'ldirilsa 409 (sabab
    /// <c>Assignment.GraderRole</c> izohida).
    /// </summary>
    GroupStaffRole? GraderRole = null);

/// <summary>
/// Tahrirlash. Vazifa NISHONI (guruh yoki dars) o'zgartirilmaydi — bu boshqa
/// vazifa degani: mavjud javoblar begona vazifaga tegib qolardi.
/// </summary>
public sealed record UpdateAssignmentRequest(
    string Title,
    string? Description = null,
    decimal MaxScore = 5m,
    DateTimeOffset? DueAt = null,
    AnswerFormats AllowedFormats = AnswerFormats.Text | AnswerFormats.Image,
    string? ImageKey = null,

    /// <summary>R33 — tekshiruvchi (<c>null</c> = guruh sozlamasi).</summary>
    GroupStaffRole? GraderRole = null);

public sealed record GradeSubmissionRequest(decimal Score, string? Feedback = null);

/// <param name="Note">O'quvchiga ko'rinadigan sabab ("xattingiz o'qilmadi").</param>
public sealed record ReopenSubmissionRequest(string? Note = null);

/// <summary>Ro'yxat filtri (xodim ko'rinishi).</summary>
public sealed record AssignmentListQuery(
    long? GroupId = null,
    long? ModuleLessonId = null,
    int Page = 1,
    int PageSize = 25);

/* ============================================================================
   O'QUV BO'LIMI UMUMIY KO'RINISHI (2026-08-15 talabi)

   Loyiha egasi: *"bir ko'rganda qaysi guruhdagi vazifalar tekshirilmagani
   nechtasi tekshirilgani ... javoblari qachon yuborilgani, qachon
   tekshirilgani, kim tekshirishi kerakligi, o'quvchi yuborgan javobi,
   olgan bahosi — hammasi ko'rinib turishi kerak"*, ustoz va guruh turi va
   guruh bo'yicha filtr bilan.

   ★ IKKI ALOHIDA SO'ROV, BITTA EMAS: guruh xulosasi ("nechta tekshirilmagan")
   BUTUN filtrlangan to'plam bo'yicha aniq son bo'lishi kerak, javoblar
   ro'yxati esa SAHIFALANADI (yuzlab qator bo'lishi mumkin). Ikkalasini bitta
   javobga qo'shsak, sahifalangan ro'yxatdan hisoblangan xulosa NOTO'G'RI
   son berardi (faqat joriy sahifa ko'rinardi).

   ★ FAQAT `ManageRoles` (Academic/Admin): bu — o'quv bo'limining nazorat
   ekrani, ustoz/kurator o'z "Tekshirish" sahifasida (`ListSubmissionsAsync`)
   ishlaydi. Shuning uchun bu yerda staff-scoping (`StaffGroupIds`) YO'Q —
   Academic/Admin har doim BARCHA guruhni ko'radi.
   ============================================================================ */

/// <summary>
/// Guruh/kurs-vazifa xulosasi va javoblar ro'yxati uchun UMUMIY filtr.
/// </summary>
/// <param name="TeacherId">Guruhning ASOSIY ustozi (<c>Group.TeacherId</c>). Kurs vazifalarida hech qachon mos kelmaydi.</param>
/// <param name="GroupType">Guruh turi. Kurs vazifalarida hech qachon mos kelmaydi.</param>
/// <param name="Search">Vazifa sarlavhasi, guruh nomi yoki ustoz ismi bo'yicha (bo'sh/berilmagan — filtrlanmaydi).</param>
public sealed record AssignmentOverviewFilter(
    long? TeacherId = null,
    GroupType? GroupType = null,
    long? GroupId = null,
    string? Search = null);

/// <summary>
/// Bitta guruh (yoki "Kurs vazifalari" — <see cref="GroupId"/> <c>null</c>)
/// bo'yicha uy vazifalari xulosasi.
/// </summary>
public sealed record AssignmentGroupOverviewDto(
    long? GroupId,
    string GroupName,
    GroupType? GroupType,
    long? TeacherId,
    string? TeacherName,
    int AssignmentCount,
    int SubmissionCount,
    int GradedCount,
    int UngradedCount,
    DateTimeOffset? LastSubmittedAt);

/// <summary>Javoblar ro'yxati filtri — <see cref="AssignmentOverviewFilter"/> + sahifalash/holat.</summary>
public sealed record SubmissionOverviewQuery(
    long? TeacherId = null,
    GroupType? GroupType = null,
    long? GroupId = null,
    long? AssignmentId = null,
    SubmissionStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>
/// Bitta javob — o'quv bo'limi umumiy ko'rinishi uchun TO'LIQ qator (guruh,
/// ustoz va tekshiruvchi konteksti bilan birga).
/// </summary>
/// <param name="GraderLabel">
/// "Kim tekshirishi kerak" — <c>Assignment.GraderRole ?? Group.AssignmentGraderRole</c>
/// dan hosil qilingan KO'RSATISH matni (ism yoki "Ustoz / Kurator").
/// Kurs vazifasida <c>null</c>: u hamma guruhga taalluqli, ya'ni BITTA aniq
/// tekshiruvchi yo'q (har ustoz o'z o'quvchisini baholaydi).
/// </param>
public sealed record SubmissionOverviewDto(
    long SubmissionId,
    long AssignmentId,
    string AssignmentTitle,
    long? GroupId,
    string? GroupName,
    GroupType? GroupType,
    long? TeacherId,
    string? TeacherName,
    long StudentId,
    string StudentName,
    SubmissionStatus Status,
    decimal? Score,
    decimal MaxScore,
    decimal? ScorePercent,
    DateTimeOffset SubmittedAt,
    bool IsLate,
    int AttemptNumber,
    DateTimeOffset? GradedAt,
    long? GradedById,
    string? GradedByName,
    string? GraderLabel);
