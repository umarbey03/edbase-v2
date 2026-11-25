using Zinnur.Application.Gating.Dtos;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.SelfStudy;

/// <summary>Kuratorning o'quvchi bo'yicha "darslar taxtasi".</summary>
/// <param name="CanDecide">Ko'ruvchi «o'zlashtirdi / qayta ishlasin» bosa oladimi.</param>
/// <param name="CanOverride">Ko'ruvchi administrator — darsni istisno sifatida ocha oladi.</param>
public sealed record StudentLessonBoardDto(
    long StudentId,
    string StudentName,
    string? CourseName,
    bool SubscriptionActive,
    bool CanDecide,
    bool CanOverride,
    IReadOnlyList<LessonBoardRowDto> Lessons);

/// <param name="WorkDone">O'quvchi video, vazifa va testni bajargan (kurator qaroridan qat'i nazar).</param>
public sealed record LessonBoardRowDto(
    long LessonId,
    int Index,
    string ModuleName,
    string LessonName,
    bool Unlocked,
    LessonLockReason? LockReason,
    bool WorkDone,
    bool HasVideo,
    bool VideoWatched,
    bool HasAssignment,
    BoardAssignmentDto? Assignment,
    bool HasTest,
    bool TestTaken,
    decimal? TestPercent,
    MasteryStatus? Mastery,
    string? MasteryNote,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    bool UnlockedOverride,
    string? OverrideReason,
    bool Provisional = false);

/// <summary>Darsning kurs vazifasi va o'quvchining javobi (bo'lsa).</summary>
public sealed record BoardAssignmentDto(
    long AssignmentId,
    string Title,
    decimal MaxScore,
    long? SubmissionId,
    SubmissionStatus? SubmissionStatus,
    decimal? Score,
    DateTimeOffset? SubmittedAt,
    int? AttemptNumber);

/// <summary>Kurator qarori. «Qayta ishlasin» da izoh majburiy.</summary>
public sealed record DecideMasteryRequest(MasteryStatus Status, string? Note);

/// <summary>Administrator istisnosi: darsni ochish yoki ochishni bekor qilish. Sabab majburiy.</summary>
public sealed record AdminLessonUnlockRequest(bool Unlocked, string? Reason);

/// <summary>O'zlashtirish jurnalidagi bitta yozuv.</summary>
public sealed record MasteryEventDto(
    long Id,
    long LessonId,
    string LessonName,
    MasteryEventKind Kind,
    string? Note,
    string ActorName,
    DateTimeOffset At);

/// <summary>O'quvchining o'zi ko'radigan kurator qarori (izoh bilan).</summary>
public sealed record MyLessonMasteryDto(long LessonId, MasteryStatus Status, string? Note, DateTimeOffset DecidedAt);
