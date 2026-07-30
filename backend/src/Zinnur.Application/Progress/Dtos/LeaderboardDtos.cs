namespace Zinnur.Application.Progress.Dtos;

/// <summary>
/// Reyting jadvalining bitta qatori.
/// </summary>
/// <param name="Rank">
/// O'rin (1 dan). ★ TAKRORLANISHI MUMKIN: aynan bir xil balga ega ikki
/// o'quvchi bir xil o'rin oladi (1, 2, 2, 4). Frontend podiumni
/// <c>Rows</c> tartibiga qarab chizsin, <c>Rank</c> ni esa YORLIQ sifatida
/// ko'rsatsin.
/// </param>
/// <param name="Total">Yakuniy ball 0..100 (uch mezon o'rtachasi).</param>
/// <param name="AttendancePercent"><c>null</c> — shu oyda o'tilgan dars yo'q.</param>
/// <param name="AssignmentPercent"><c>null</c> — shu oyda baholangan vazifa yo'q.</param>
/// <param name="TestPercent"><c>null</c> — shu oyda topshirilgan test yo'q.</param>
/// <param name="IsMe">Bu qator so'rov yuborgan foydalanuvchiniki.</param>
public sealed record LeaderboardRowDto(
    long StudentId,
    string StudentName,
    int Rank,
    decimal Total,
    decimal? AttendancePercent,
    decimal? AssignmentPercent,
    decimal? TestPercent,
    bool IsMe);

/// <summary>
/// Guruhning bir oylik reyting jadvali.
/// </summary>
/// <param name="Period">Qaysi oy (<c>YYYY-MM</c>).</param>
/// <param name="StudentCount">Guruhdagi FAOL o'quvchilar soni (jadval uzunligi).</param>
/// <param name="Me">
/// So'rov yuborgan o'quvchining qatori. Xodim so'rasa <c>null</c> —
/// u jadvalning ichida emas.
/// </param>
public sealed record GroupLeaderboardDto(
    long GroupId,
    string GroupName,
    string Period,
    int StudentCount,
    LeaderboardRowDto? Me,
    IReadOnlyList<LeaderboardRowDto> Rows);

/// <summary>
/// "Mening o'rnim" — jadvalsiz, yengil ko'rinish (bosh sahifadagi kartochka).
/// </summary>
/// <param name="GroupId"><c>null</c> — o'quvchi hech qaysi faol guruhda emas.</param>
public sealed record MyRankDto(
    long? GroupId,
    string? GroupName,
    string Period,
    int StudentCount,
    LeaderboardRowDto? Me);
