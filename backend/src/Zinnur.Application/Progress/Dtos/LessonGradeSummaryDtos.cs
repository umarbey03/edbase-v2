namespace Zinnur.Application.Progress.Dtos;

/// <summary>
/// ========================================================================
/// O'QUVCHINING O'Z DARS BAHOLARI (R24 ning o'quvchi tomoni)
/// ========================================================================
///
/// ★ NIMA UCHUN ALOHIDA DTO, <c>SessionLessonGradesDto</c> QAYTA
/// ISHLATILMADI: xodim varag'ining birligi — DARS (qatorlar = o'quvchilar),
/// o'quvchi ekranining birligi esa — O'QUVCHI (qatorlar = darslar). Aynan
/// shu sababli xodim DTO'sida <c>StudentId</c>/<c>StudentName</c> bor va u
/// o'quvchiga BEGONA ismlarni ko'rsatadigan shaklga ega. Uni qayta
/// ishlatish "bitta qator qoldirib, qolganini filtrlash" degan
/// endpoint yasardi — ya'ni maxfiylik FILTRGA bog'lanib qolardi.
/// Bu yerda esa boshqa o'quvchi tushunchasi UMUMAN yo'q.
/// </summary>
/// <param name="GroupIds">Qaysi guruh(lar) hisobga olindi.</param>
/// <param name="From">Oraliq boshi (mahalliy sana). <c>null</c> — butun tarix.</param>
/// <param name="To">Oraliq oxiri (mahalliy sana, KIRADI).</param>
/// <param name="DefaultMaxScore">
/// Shkala ko'rsatilmaganda ishlatiladigan maxraj (odatda 5). Javobda
/// ATAYLAB bor — Domain doimiysi frontendda QAYTA YOZILMAYDI
/// (<c>SessionLessonGradesDto</c> dagi bilan AYNI mulohaza).
/// </param>
/// <param name="GradedCount">Nechta darsga baho qo'yilgan.</param>
/// <param name="AveragePercent">
/// Baholangan darslarning o'rtacha foizi (0..100, bir xona aniqlikda).
/// <c>null</c> — hali birorta baho yo'q.
///
/// ★ MAXRAJ — FAQAT BAHOLANGAN DARSLAR: baholanmagan dars 0 deb
/// sanalsa, ustoz baho qo'yishga ulgurmagani uchun O'QUVCHI jazolanardi
/// (<c>LessonGradeRowDto.Score</c> izohidagi AYNI qoida).
/// </param>
/// <param name="Items">Yangi darsdan eskisiga qarab tartiblangan.</param>
public sealed record MyLessonGradesDto(
    IReadOnlyList<long> GroupIds,
    DateOnly? From,
    DateOnly? To,
    decimal DefaultMaxScore,
    int GradedCount,
    decimal? AveragePercent,
    IReadOnlyList<MyLessonGradeDto> Items);

/// <summary>
/// Bitta darsdagi baho — o'quvchining ko'zi bilan.
/// </summary>
/// <param name="Percent">
/// Foiz (0..100). SERVER hisoblaydi: frontend <c>score/maxScore</c> ni
/// qayta bo'lsa yaxlitlash qoidasi ikki joyda ayri-ayri bo'lib qolardi.
/// </param>
/// <param name="Comment">
/// Ustozning izohi. ★ O'QUVCHIGA KO'RSATILADI — izoh aynan unga
/// yozilgan ("uy vazifasi to'liq emas"), va uni yashirish bahoni
/// TUSHUNTIRIBOLMAYDIGAN songa aylantirardi.
/// </param>
/// <param name="GradedByName">
/// Kim baholadi. Xodim ismi o'quvchiga allaqachon ochiq (dars jadvali,
/// guruh kartochkasi), ya'ni yangi ma'lumot oshkor qilinmayapti — lekin
/// "bahoni kim qo'ydi" savoliga javob bo'ladi.
/// </param>
public sealed record MyLessonGradeDto(
    long SessionId,
    long GroupId,
    string? Title,
    string Type,
    DateTimeOffset ScheduledStart,
    decimal Score,
    decimal MaxScore,
    decimal Percent,
    string? Comment,
    string? GradedByName,
    DateTimeOffset GradedAt);
