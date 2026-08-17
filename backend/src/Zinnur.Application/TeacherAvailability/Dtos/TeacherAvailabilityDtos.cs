namespace Zinnur.Application.TeacherAvailability.Dtos;

/// <summary>
/// O'quv bo'limi paneli uchun — bugungi kunning bitta ustoz bo'yicha holati.
/// </summary>
/// <param name="CheckinId">Checkin yozuvi ID'si.</param>
/// <param name="TeacherName">Ustoz F.I.Sh.</param>
/// <param name="Status">
/// <see cref="Zinnur.Domain.Enums.TeacherCheckinStatus"/> nomi (matn —
/// frontend ko'rsatish uchun, qiymatni o'zi qayta hisoblamaydi).
/// </param>
/// <param name="DeclineReason">"Yo'q" bo'lsa — sabab.</param>
/// <param name="UnavailableDays">"Yo'q" bo'lsa — necha kunga.</param>
/// <param name="AffectedSessions">Ta'sirlangan darslar va ularning qamrov holati.</param>
public sealed record TeacherAvailabilityTodayDto(
    long CheckinId,
    string TeacherName,
    string Status,
    string? DeclineReason,
    int? UnavailableDays,
    IReadOnlyList<CoverageStatusDto> AffectedSessions);

/// <summary>Bitta ta'sirlangan darsning o'rinbosar qamrovi.</summary>
/// <param name="SessionId">Dars ID'si.</param>
/// <param name="GroupName">Guruh nomi.</param>
/// <param name="ScheduledStart">Dars vaqti.</param>
/// <param name="Status">
/// <see cref="Zinnur.Domain.Enums.CoverageRequestStatus"/> nomi — so'rov
/// umuman OCHILMAGAN bo'lsa (masalan hali qayta ishlanmoqda) <c>null</c>.
/// </param>
/// <param name="SubstituteTeacherName">Topilgan bo'lsa — o'rinbosar F.I.Sh.</param>
public sealed record CoverageStatusDto(
    long SessionId,
    string GroupName,
    DateTimeOffset ScheduledStart,
    string? Status,
    string? SubstituteTeacherName);
