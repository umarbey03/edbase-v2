using Zinnur.Domain.Enums;

namespace Zinnur.Application.SelfStudy.Panel;

// ============================================================================
//  MUSTAQIL PANELI (2026-11-19) — o'quv bo'limi va administrator uchun.
//
//  "Mustaqil o'quvchi" = bekor qilinmagan obunasi bor VA faol guruh a'zosi
//  EMAS (guruhga o'tgan o'quvchi Milliy dunyosida — `SelfStudyCuratorService`
//  dagi AYNI qoida). Alohida bayroq yo'q, shuning uchun ta'rif bitta joyda:
//  `SelfStudyPanelService.SelfStudyStudents`.
// ============================================================================

/// <summary>Obuna holati (panel filtri va ustuni).</summary>
public enum SelfStudySubscriptionState
{
    /// <summary>Hozir amal qilayotgan davr bor.</summary>
    Active = 0,

    /// <summary>Hozirgi davr yo'q, lekin kelajakdagi davr bor (to'langan, hali boshlanmagan).</summary>
    Upcoming = 1,

    /// <summary>Barcha davrlar tugagan.</summary>
    Expired = 2,
}

/// <summary>Panel tepasidagi raqamlar.</summary>
/// <param name="Students">Mustaqil o'quvchilar soni (har qanday holatda).</param>
/// <param name="ActiveSubscriptions">Hozir obunasi amal qilayotganlar.</param>
/// <param name="Unassigned">Faol obunasi bor, lekin kuratori yo'qlar — DARHOL ish kerak.</param>
/// <param name="PendingSubmissions">Tekshiruv kutayotgan javoblar.</param>
/// <param name="WaitingChats">Oxirgi xabari o'quvchidan bo'lgan (javob kutayotgan) yozishmalar.</param>
public sealed record SelfStudyPanelSummaryDto(
    int Students,
    int ActiveSubscriptions,
    int Unassigned,
    int PendingSubmissions,
    int WaitingChats);

/// <summary>O'quvchilar tabi filtri.</summary>
/// <param name="CuratorId">Shu xodim asosiy YOKI zaxira kurator bo'lgan o'quvchilar.</param>
/// <param name="UnassignedOnly">Faqat kuratorsizlar.</param>
public sealed record SelfStudyStudentQuery(
    string? Search = null,
    SelfStudySubscriptionState? State = null,
    long? CuratorId = null,
    bool UnassignedOnly = false,
    int Page = 1,
    int PageSize = 20);

/// <summary>O'quvchilar tabidagi bitta qator.</summary>
/// <param name="MasteredLessons">Kurator "o'zlashtirdi" deb belgilagan darslar.</param>
/// <param name="TotalLessons">Obuna kursidagi darslar soni (progress maxraji).</param>
/// <param name="WaitingSince">O'quvchi kuratorga yozgan va javob olmagan eng eski xabar vaqti.</param>
public sealed record SelfStudyStudentRowDto(
    long StudentId,
    string FullName,
    string? Phone,
    bool IsActive,
    string? PlanName,
    SelfStudySubscriptionState State,
    DateTimeOffset? SubscriptionEndsAt,
    long? CuratorId,
    string? CuratorName,
    long? BackupCuratorId,
    string? BackupCuratorName,
    DateTimeOffset? CuratorAssignedAt,
    int MasteredLessons,
    int TotalLessons,
    int PendingSubmissions,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset? WaitingSince);

/// <summary>Bir nechta o'quvchini bitta kuratorga biriktirish.</summary>
public sealed record BulkAssignSelfStudyCuratorRequest(
    IReadOnlyList<long> StudentIds,
    long CuratorId,
    long? BackupCuratorId);

/// <summary>Biriktirilmagan o'quvchi va sababi.</summary>
public sealed record BulkAssignFailureDto(long StudentId, string? FullName, string Error);

/// <summary>Ommaviy biriktirish natijasi: har o'quvchi alohida — biri yiqilsa qolganlari to'xtamaydi.</summary>
public sealed record BulkAssignResultDto(int Assigned, IReadOnlyList<BulkAssignFailureDto> Failed);

/// <summary>Kuratorlar tabidagi bitta qator.</summary>
/// <param name="PrimaryStudents">Asosiy kurator bo'lgan Mustaqil o'quvchilar.</param>
/// <param name="BackupStudents">Zaxira kurator bo'lgan o'quvchilar.</param>
/// <param name="PendingSubmissions">Asosiy o'quvchilarining tekshiruv kutayotgan javoblari.</param>
/// <param name="WaitingChats">Unga yozilgan va javob kutayotgan yozishmalar.</param>
/// <param name="LastReplyAt">Kuratorning Mustaqil o'quvchiga yozgan oxirgi xabari.</param>
public sealed record SelfStudyCuratorRowDto(
    long Id,
    string FullName,
    string Role,
    int PrimaryStudents,
    int BackupStudents,
    int PendingSubmissions,
    int WaitingChats,
    DateTimeOffset? LastReplyAt);

/// <summary>Vazifalar tabi filtri.</summary>
public sealed record SelfStudySubmissionQuery(
    SubmissionStatus? Status = null,
    long? CuratorId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Vazifalar tabidagi bitta javob.</summary>
public sealed record SelfStudySubmissionRowDto(
    long SubmissionId,
    long AssignmentId,
    string AssignmentTitle,
    string? LessonName,
    long StudentId,
    string StudentName,
    string? CuratorName,
    SubmissionStatus Status,
    decimal? Score,
    decimal MaxScore,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? GradedAt,
    string? GradedByName,
    bool IsLate,
    int AttemptNumber);

/// <summary>Chatlar tabi filtri.</summary>
/// <param name="WaitingOnly">Faqat javob kutayotgan yozishmalar.</param>
public sealed record SelfStudyChatQuery(
    long? CuratorId = null,
    bool WaitingOnly = false,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Chatlar tabidagi bitta yozishma (o'quvchi ↔ xodim juftligi).</summary>
/// <param name="IsCurrentCurator">Xodim hozir ham shu o'quvchining asosiy kuratorimi.</param>
/// <param name="UnreadByStaff">Xodim hali o'qimagan o'quvchi xabarlari.</param>
/// <param name="WaitingSince">Javobsiz qolgan eng eski o'quvchi xabari (javob bor bo'lsa <c>null</c>).</param>
public sealed record SelfStudyChatRowDto(
    long StudentId,
    string StudentName,
    long StaffId,
    string StaffName,
    bool IsCurrentCurator,
    int MessageCount,
    DateTimeOffset LastMessageAt,
    string LastMessagePreview,
    bool LastFromStudent,
    int UnreadByStaff,
    DateTimeOffset? WaitingSince);

/// <summary>Yozishmani kim, qachon ochgani (shaffoflik uchun panelda ko'rsatiladi).</summary>
public sealed record ChatAccessLogEntryDto(
    string ViewerName,
    DirectMessageAccessKind Kind,
    DateTimeOffset At);
