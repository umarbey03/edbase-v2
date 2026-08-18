namespace Zinnur.Application.Absentees.Dtos;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARSGA KIRMAGANLAR — KUNLIK XARITA (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi: *"bir kun avval darsga kirmagan o'quvchilarni bittada
/// ko'ra olishimiz uchun"*.
///
/// ★ NEGA MAVJUD DAVOMAT EKRANI YETARLI EMAS: u BITTA DARS kesimida
/// ishlaydi. Kurator esa ertalab "kecha kim kelmadi?" deb so'raydi va
/// buni bilish uchun o'nlab guruhni birma-bir ochishi kerak edi. Bu
/// yerda esa BITTA kunning barcha guruhlari bitta ro'yxatda.
///
/// ★ GURUH BO'YICHA GURUHLANADI: qo'ng'iroqlar amalda guruh kuratori
/// bo'yicha taqsimlanadi, ya'ni ro'yxat aynan shu tartibda kerak.
/// </summary>
/// <param name="Date">Qaysi kun (mahalliy). Bo'sh — KECHA.</param>
/// <param name="IncludePartial">
/// Darsdan erta chiqib ketganlar ham kirsinmi. Standart <c>false</c> —
/// asosiy savol "umuman kelmaganlar".
/// </param>
/// <param name="MinStreak">
/// Faqat shu sondan ko'p KETMA-KET dars qoldirganlar. <c>0</c> — hammasi.
/// </param>
public sealed record AbsenteeQuery(
    DateOnly? Date = null,
    long? GroupId = null,
    long? TeacherId = null,
    bool IncludePartial = false,
    int MinStreak = 0,
    string? Search = null);

/// <param name="Status"><c>Absent</c> yoki <c>Partial</c>.</param>
/// <param name="Phone">Qo'ng'iroq qilish uchun — ro'yxatdan chiqmasdan.</param>
/// <param name="TelegramLinked">Telegram ulanganmi — xabar yuborish mumkinmi.</param>
/// <param name="ConsecutiveMisses">
/// Shu guruhda KETMA-KET nechta darsni qoldirgan (shu kun ham kiradi).
///
/// ★ ENG MUHIM USTUN: bitta qoldirilgan dars odatiy hol, ketma-ket
/// uchtasi esa "bu o'quvchi ketyapti" degan signal. Kurator kimga
/// birinchi qo'ng'iroq qilishini aynan shunga qarab hal qiladi.
/// </param>
/// <param name="MissedInLast30Days">Oxirgi 30 kunda jami nechta dars qoldirgan.</param>
public sealed record AbsenteeStudentDto(
    long StudentId,
    string StudentName,
    string? Phone,
    bool TelegramLinked,
    long SessionId,
    DateTimeOffset SessionStart,
    string Status,
    int ConsecutiveMisses,
    int MissedInLast30Days);

/// <param name="AbsentCount">Shu guruhdan nechta o'quvchi kelmagan.</param>
/// <param name="ActiveMembers">Guruhdagi faol o'quvchilar — nisbatni ko'rish uchun.</param>
public sealed record AbsenteeGroupDto(
    long GroupId,
    string GroupName,
    string? TeacherName,
    string? AssistantName,
    int AbsentCount,
    int ActiveMembers,
    IReadOnlyList<AbsenteeStudentDto> Students);

/// <param name="SessionCount">Shu kuni yakunlangan darslar soni.</param>
/// <param name="TotalAbsent">Jami kelmagan o'quvchilar (takrorlanmaydi).</param>
/// <param name="RiskCount">Ketma-ket 3 va undan ko'p dars qoldirganlar.</param>
public sealed record AbsenteeReportDto(
    DateOnly Date,
    int SessionCount,
    int TotalAbsent,
    int RiskCount,
    IReadOnlyList<AbsenteeGroupDto> Groups);
