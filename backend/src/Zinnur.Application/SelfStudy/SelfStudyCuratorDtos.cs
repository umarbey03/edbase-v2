namespace Zinnur.Application.SelfStudy;

/// <summary>Bitta biriktirish (joriy yoki tarixdagi).</summary>
public sealed record SelfStudyCuratorDto(
    long Id,
    long CuratorId,
    string CuratorName,
    long? BackupCuratorId,
    string? BackupCuratorName,
    DateTimeOffset AssignedAt,
    string? AssignedByName,
    DateTimeOffset? EndedAt);

/// <summary>O'quvchi kartasi uchun: joriy kurator va tarix (yangisi birinchi).</summary>
public sealed record StudentCuratorInfoDto(
    SelfStudyCuratorDto? Current,
    IReadOnlyList<SelfStudyCuratorDto> History);

/// <summary>Kurator biriktirish yoki almashtirish.</summary>
public sealed record AssignSelfStudyCuratorRequest(long CuratorId, long? BackupCuratorId);

/// <summary>Kurator tanlash ro'yxati: kim, qaysi rolda, nechta o'quvchisi bor.</summary>
public sealed record CuratorCandidateDto(
    long Id,
    string FullName,
    string Role,
    int ActiveStudents);

/// <summary>Kuratorning "Mustaqil o'quvchilarim" ro'yxatidagi qator.</summary>
/// <param name="IsBackup">Xodim bu o'quvchiga zaxira kurator sifatida mas'ul.</param>
/// <param name="PendingSubmissions">Tekshiruv kutayotgan javoblar soni.</param>
public sealed record MySelfStudyStudentDto(
    long StudentId,
    string FullName,
    bool IsBackup,
    DateTimeOffset AssignedAt,
    DateTimeOffset? SubscriptionEndsAt,
    bool SubscriptionActive,
    int PendingSubmissions,
    DateTimeOffset? LastActivityAt = null,
    int? IdleDays = null,
    string? LastNudgeResponse = null,
    bool NudgesOff = false);

/// <summary>Faol obunasi bor, lekin kurator biriktirilmagan o'quvchi.</summary>
public sealed record UnassignedSelfStudyStudentDto(
    long StudentId,
    string FullName,
    DateTimeOffset SubscriptionEndsAt);

/// <summary>O'quvchining o'zi ko'radigan kurator (ism, xolos).</summary>
public sealed record MyCuratorDto(long CuratorId, string FullName);
