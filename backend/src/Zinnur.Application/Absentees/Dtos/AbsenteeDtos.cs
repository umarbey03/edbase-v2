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
/// <param name="From">
/// Davr boshi (mahalliy, KIRADI). Bo'sh — <paramref name="To"/> bilan
/// bir xil, ya'ni bitta kun.
/// </param>
/// <param name="To">Davr oxiri (mahalliy, KIRADI). Bo'sh — KECHA.</param>
/// <param name="IncludePartial">
/// Darsdan erta chiqib ketganlar ham kirsinmi. Standart <c>false</c> —
/// asosiy savol "umuman kelmaganlar".
/// </param>
/// <param name="MinStreak">
/// Faqat shu sondan ko'p KETMA-KET dars qoldirganlar. <c>0</c> — hammasi.
/// </param>
/// <param name="Page">
/// GURUHLAR sahifasi (o'quvchilar emas).
///
/// ★ NEGA GURUH BO'YICHA: ro'yxat guruhlarga bo'lingan va qo'ng'iroqlar
/// ham guruh bo'yicha taqsimlanadi. O'quvchilar bo'yicha sahifalansa,
/// bitta guruh ikki sahifaga bo'linib, kurator uni ikki marta ochishi
/// kerak bo'lardi.
/// </param>
public sealed record AbsenteeQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    long? GroupId = null,
    long? TeacherId = null,
    bool IncludePartial = false,
    int MinStreak = 0,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

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
/// <param name="MissedInRange">
/// Tanlangan DAVRDA shu guruhda nechta darsni qoldirgan.
///
/// ★ NEGA KERAK: davr bir kundan uzun bo'lsa, bitta o'quvchi bir necha
/// darsni qoldirgan bo'lishi mumkin. Har biri alohida qator bo'lsa,
/// ro'yxat takrorlarga to'lib, "nechta odamga qo'ng'iroq qilaman?"
/// degan savolga javob bermay qolardi. Shuning uchun BITTA qator va
/// yonida soni.
/// </param>
/// <param name="SessionStart">Davrdagi ENG SO'NGGI qoldirilgan dars vaqti.</param>
public sealed record AbsenteeStudentDto(
    long StudentId,
    string StudentName,
    string? Phone,
    bool TelegramLinked,
    long SessionId,
    DateTimeOffset SessionStart,
    string Status,
    int ConsecutiveMisses,
    int MissedInLast30Days,
    int MissedInRange);

/// <param name="AbsentCount">Shu guruhdan nechta o'quvchi kelmagan.</param>
/// <param name="ExpectedStudents">
/// Davrdagi darslarda QATNASHISHI KUTILGAN noyob o'quvchilar —
/// <paramref name="AbsentCount"/> ning maxraji.
///
/// ★ NEGA "HOZIRGI FAOL A'ZOLAR" EMAS (2026-08-18 da to'g'rilandi):
/// surat TARIXIY (o'sha kuni kim kelmagan), maxraj esa HOZIRGI holat
/// edi. Guruhdan keyin chiqib ketganlar bo'lsa "4/1" kabi ma'nosiz
/// nisbat chiqardi. Endi ikkalasi ham AYNI to'plamdan hisoblanadi.
/// </param>
public sealed record AbsenteeGroupDto(
    long GroupId,
    string GroupName,
    string? TeacherName,
    string? AssistantName,
    int AbsentCount,
    int ExpectedStudents,
    IReadOnlyList<AbsenteeStudentDto> Students);

/// <param name="SessionCount">Davrda yakunlangan darslar soni.</param>
/// <param name="TotalAbsent">Jami kelmagan o'quvchilar (takrorlanmaydi).</param>
/// <param name="RiskCount">Ketma-ket 3 va undan ko'p dars qoldirganlar.</param>
/// <param name="TotalGroups">Jami guruhlar — sahifalashdan MUSTAQIL.</param>
/// <param name="Groups">Faqat joriy sahifadagi guruhlar.</param>
public sealed record AbsenteeReportDto(
    DateOnly From,
    DateOnly To,
    int SessionCount,
    int TotalAbsent,
    int RiskCount,
    int TotalGroups,
    int Page,
    int PageSize,
    IReadOnlyList<AbsenteeGroupDto> Groups);
