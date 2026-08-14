namespace Zinnur.Application.LiveSessions.Dtos;

/// <summary>
/// BITTA DARSNING baho varag'i: dars sarlavhasi + o'quvchilar qatorlari.
///
/// ★ SHAKL <see cref="SessionAttendanceDto"/> BILAN AYNI — ATAYLAB.
/// Ustoz panelidagi "Baholar" va "Davomat" tablari bir xil matritsa
/// (qator — o'quvchi, ustun — dars) va frontend ikkalasini AYNI naqsh
/// bilan yig'adi: ustunlar oynasi -> har dars uchun bitta varaq ->
/// `sessionId:studentId` kaliti bo'yicha katak.
///
/// ★ NIMA UCHUN "GURUH BO'YICHA MATRITSA" EMAS: sabab davomatdagi bilan
/// bir xil — birlik DARS. Ustoz aynan bugungi darsni baholaydi, matritsa
/// esa frontendda KO'RINIB TURGAN ustunlardan yig'iladi.
/// </summary>
/// <param name="DefaultMaxScore">
/// Shkala ko'rsatilmaganda ishlatiladigan maxraj (odatda 5). Javobda
/// ATAYLAB bor: oynadagi tugmalar shu songa qarab chiziladi va Domain
/// doimiysi frontendda QAYTA YOZILMAYDI.
/// </param>
/// <param name="CanEdit">
/// Chaqiruvchi baho qo'ya oladimi. Hozircha ko'rish va qo'yish huquqi
/// ustma-ust tushadi, lekin maydon ATAYLAB javobda — frontend tugmani shu
/// bo'yicha chizadi.
/// </param>
/// <param name="Rows">
/// Guruhning FAOL o'quvchilari + shu darsda bahosi bo'lgan HAMMA o'quvchi
/// (arxivlangani ham). Tartib: ism bo'yicha.
/// </param>
public sealed record SessionLessonGradesDto(
    long SessionId,
    long GroupId,
    string GroupName,
    string? Title,
    string Type,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    decimal DefaultMaxScore,
    bool CanEdit,
    IReadOnlyList<LessonGradeRowDto> Rows);

/// <summary>
/// Bitta o'quvchining shu darsdagi bahosi.
/// </summary>
/// <param name="Score">
/// <c>null</c> — BAHO YO'Q: hech kim baholamagan. Bu <c>0</c> DAN
/// BOSHQA holat va jadvalda ataylab "·" ko'rinadi. Reytingda bahosi
/// yo'q dars UMUMAN hisobga olinmaydi (o'quvchi ustoz baholamagani uchun
/// jazolanmasin), <c>0</c> esa to'liq hisobga olinadi.
/// </param>
/// <param name="MaxScore">
/// Shu bahoning maxraji. <c>null</c> — standart shkala
/// (<see cref="SessionLessonGradesDto.DefaultMaxScore"/>).
/// </param>
/// <param name="Percent">
/// Foiz (0..100), bir xona aniqlikda. Server hisoblaydi — frontend
/// <c>score/maxScore</c> ni qayta bo'lsa, yaxlitlash qoidasi ikki joyda
/// ayri-ayri bo'lib qolardi.
/// </param>
/// <param name="GradedByName">Bahoni OXIRGI qo'ygan xodim.</param>
public sealed record LessonGradeRowDto(
    long StudentId,
    string StudentName,
    decimal? Score,
    decimal? MaxScore,
    decimal? Percent,
    string? Comment,
    long? GradedById,
    string? GradedByName,
    DateTimeOffset? GradedAt);

/// <summary>
/// Baho qo'yish/o'zgartirish so'rovi.
///
/// ★ PUT — TO'LIQ ALMASHTIRISH: <c>comment</c> yuborilmasa yoki
/// <c>null</c> bo'lsa avvalgi izoh O'CHADI (<c>UpdateAttendanceRequest</c>
/// dagi bilan AYNI shartnoma).
/// </summary>
/// <param name="Score">
/// Ball. MAJBURIY: <c>null</c> bo'lsa 400.
///
/// Nima uchun nullable: nullable BO'LMASA yuborilmagan maydon jimgina
/// <c>0</c> bo'lib ketardi — ya'ni maydonni unutgan klient butun sinfga
/// "0" qo'yib chiqardi.
/// </param>
/// <param name="MaxScore">
/// Maxraj. <c>null</c> — standart shkala. Musbat bo'lishi va balldan kichik
/// bo'lmasligi SHART, aks holda 400.
/// </param>
/// <param name="Comment">Izoh, 500 belgigacha. Uzunroq bo'lsa 400.</param>
public sealed record UpsertLessonGradeRequest(
    decimal? Score = null,
    decimal? MaxScore = null,
    string? Comment = null);
