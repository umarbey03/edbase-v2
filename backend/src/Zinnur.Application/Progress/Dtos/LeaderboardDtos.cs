namespace Zinnur.Application.Progress.Dtos;

/// <summary>
/// Reyting QAMROVI.
///
/// ★ BAZAGA YOZILMAYDI — bu faqat so'rov vaqtidagi tanlov, shuning uchun
/// tartib raqamlari ham ahamiyatsiz. Eski tizimda aynan shu tushuncha
/// `leaderboard_snapshots.scope` USTUNI edi va `group_id` NULL bo'lgan
/// `overall` qatorlari `ON CONFLICT` ni buzib dublikat hosil qilardi
/// (batafsil <c>LeaderboardService</c> izohida). v2 da qamrov hech qayerda
/// SAQLANMAYDI.
/// </summary>
public enum LeaderboardScope
{
    /// <summary>O'quvchining o'z guruhi.</summary>
    Group,

    /// <summary>
    /// Butun o'quv markaz.
    ///
    /// 🔴 "MARKAZ" — "TIZIMDAGI HAMMA" DEGANI EMAS. Bugun ikkalasi bir xil
    ///    to'plamga tushadi (bitta deployment = bitta markaz), lekin
    ///    qamrovni <c>ILearningCenterScope</c> hal qiladi va ko'p-markazli
    ///    o'zgarishdan keyin ham bu yorliqning MA'NOSI o'zgarmaydi.
    /// </summary>
    Center,
}

/// <summary>
/// Reyting jadvalining bitta qatori.
/// </summary>
/// <param name="Rank">
/// O'rin (1 dan). ★ TAKRORLANISHI MUMKIN: aynan bir xil balga ega ikki
/// o'quvchi bir xil o'rin oladi (1, 2, 2, 4). Frontend podiumni
/// <c>Rows</c> tartibiga qarab chizsin, <c>Rank</c> ni esa YORLIQ sifatida
/// ko'rsatsin.
/// </param>
/// <param name="Total">Yakuniy ball 0..100 (MAVJUD mezonlar o'rtachasi).</param>
/// <param name="AttendancePercent"><c>null</c> — shu oyda o'tilgan dars yo'q.</param>
/// <param name="AssignmentPercent"><c>null</c> — shu oyda baholangan vazifa yo'q.</param>
/// <param name="TestPercent"><c>null</c> — shu oyda topshirilgan test yo'q.</param>
/// <param name="IsMe">Bu qator so'rov yuborgan foydalanuvchiniki.</param>
/// <param name="LessonPercent">
/// DARS BAHOSI foizi (R24). <c>null</c> — shu oyda dars bahosi yo'q.
///
/// ★ <paramref name="AssignmentPercent"/> BILAN ARALASHTIRILMAYDI: u
/// topshirilgan ISHNING bahosi, bu esa DARSNING bahosi (topshirilgan ish
/// umuman bo'lmasligi mumkin).
///
/// 🔴 MAYDON ENG OXIRIDA, <paramref name="IsMe"/> DAN HAM KEYIN — bu
/// ataylab qilingan noqulaylik. Sabab: bu yozuv Redis'da JSON bo'lib
/// saqlanadi (<c>CachedLeaderboard</c>) va pozitsion o'rtaga qo'shilgan
/// maydon eski keshdagi qatorlarni jimgina SURIB yuborardi. Standart
/// qiymat bilan oxirida turgani esa eski JSON'ni ham to'g'ri o'qiydi
/// (yo'q maydon = <c>null</c> = "dars bahosi yo'q").
/// </param>
public sealed record LeaderboardRowDto(
    long StudentId,
    string StudentName,
    int Rank,
    decimal Total,
    decimal? AttendancePercent,
    decimal? AssignmentPercent,
    decimal? TestPercent,
    bool IsMe,
    decimal? LessonPercent = null);

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
/// ========================================================================
/// BUTUN O'QUV MARKAZ BO'YICHA JADVAL — TOP-N + O'Z QATORING
/// ========================================================================
///
/// ★ NIMA UCHUN GURUH DTO'SI QAYTA ISHLATILMADI: markaz jadvalida
/// <c>GroupId</c>/<c>GroupName</c> ma'nosiz bo'lardi (ularni <c>null</c>
/// qilib yuborish "guruh topilmadi" degan MUTLAQO BOSHQA holat bilan
/// aralashardi) va bu yerda guruhda umuman bo'lmagan ikki maydon bor:
/// <see cref="TopCount"/> va "o'z qatoring jadvaldan tashqarida" holati.
/// </summary>
/// <param name="Period">Qaysi oy (<c>YYYY-MM</c>).</param>
/// <param name="StudentCount">
/// Markazdagi reytingga kirgan FAOL o'quvchilar soni — TO'LIQ son,
/// <see cref="Rows"/> uzunligi emas.
/// </param>
/// <param name="TopCount">
/// Jadvalda ko'pi bilan shuncha qator yuboriladi (bugun 100).
/// Frontend "eng yaxshi N" yozuvini shundan oladi — sonni ikki joyda
/// qo'lda yozib qo'yish kerak emas.
/// </param>
/// <param name="Me">
/// So'rovchining o'z qatori — o'RNI TO'LIQ ro'yxatdan olingan, ya'ni
/// 100 dan pastda bo'lsa ham HAQIQIY o'rin ("847-o'rin").
///
/// ★ <see cref="Rows"/> ICHIDA BO'LMASLIGI MUMKIN: shuning uchun frontend
/// uni alohida ko'rsatadi. Xodim so'rasa <c>null</c> — u jadvalning
/// ichida emas.
/// </param>
/// <param name="Rows">Eng yaxshi <see cref="TopCount"/> qator, 1-o'rindan boshlab.</param>
public sealed record CenterLeaderboardDto(
    string Period,
    int StudentCount,
    int TopCount,
    LeaderboardRowDto? Me,
    IReadOnlyList<LeaderboardRowDto> Rows);

/// <summary>
/// "Mening o'rnim" — jadvalsiz, yengil ko'rinish (bosh sahifadagi kartochka).
/// </summary>
/// <param name="Scope">
/// Qaysi qamrov bo'yicha o'rin berilgan — <c>Group</c> yoki <c>Center</c>.
///
/// ★ NIMA UCHUN IKKINCHI O'RIN MAYDONI EMAS, DISKRIMINATOR: javobga
/// "markaz o'rni" ni QO'SHIB qo'yish har bosh sahifa ochilishida BUTUN
/// markaz jadvalini hisoblashga majbur qilardi — hatto markaz tabini
/// hech qachon ochmaydigan o'quvchi uchun ham. Diskriminator esa
/// narxni IXTIYORIY qiladi: qimmat hisob faqat <c>?scope=center</c>
/// so'ralganda bajariladi.
/// </param>
/// <param name="GroupId">
/// <c>null</c> — o'quvchi hech qaysi faol guruhda emas.
/// ★ <c>Center</c> qamrovida HAR DOIM <c>null</c>: markaz jadvalining
/// guruhi yo'q.
/// </param>
/// <param name="StudentCount">
/// Qamrovdagi o'quvchilar soni: guruh a'zolari yoki markaz o'quvchilari.
/// </param>
public sealed record MyRankDto(
    LeaderboardScope Scope,
    long? GroupId,
    string? GroupName,
    string Period,
    int StudentCount,
    LeaderboardRowDto? Me);
