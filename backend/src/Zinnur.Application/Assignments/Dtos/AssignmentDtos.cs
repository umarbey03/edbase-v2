using Zinnur.Domain.Enums;

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
    DateTimeOffset? UpdatedAt);

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
    IReadOnlyList<SubmissionFileDto> Files);

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
    IReadOnlyList<SubmissionFileDto> Files);

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
    string? ImageKey = null);

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
    string? ImageKey = null);

public sealed record GradeSubmissionRequest(decimal Score, string? Feedback = null);

/// <param name="Note">O'quvchiga ko'rinadigan sabab ("xattingiz o'qilmadi").</param>
public sealed record ReopenSubmissionRequest(string? Note = null);

/// <summary>Ro'yxat filtri (xodim ko'rinishi).</summary>
public sealed record AssignmentListQuery(
    long? GroupId = null,
    long? ModuleLessonId = null,
    int Page = 1,
    int PageSize = 25);
