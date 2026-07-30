using Zinnur.Domain.Enums;

namespace Zinnur.Application.LiveSessions.Dtos;

/// <summary>
/// BITTA DARSNING davomat varag'i: dars sarlavhasi + o'quvchilar qatorlari.
///
/// ★ NIMA UCHUN "DARS BO'YICHA", "GURUH BO'YICHA MATRITSA" EMAS: eski
/// ilovadagi ustoz paneli butun guruh × butun kurs matritsasini BITTA
/// javobda berardi (30 o'quvchi × 70 dars = 2100 katak) va u har tab
/// ochilganda qayta yuklanardi. Bu yerda birlik — DARS: ustoz aynan
/// bugungi darsni tuzatadi, kalendar esa qaysi darsni ochishni allaqachon
/// biladi. Matritsa kerak bo'lsa frontend kunlar bo'yicha aylanadi va
/// KO'RINIB TURGAN ustunlarnigina so'raydi.
/// </summary>
/// <param name="CanEdit">
/// Chaqiruvchi bu varaqni TUZATA oladimi. Hozircha ko'rish va tuzatish
/// huquqi ustma-ust tushadi (ko'ra olgan tuzata ham oladi), lekin maydon
/// ATAYLAB javobda bor: frontend tugmani shu bo'yicha chizadi va
/// qoida kelajakda o'zgarsa (masalan yakunlangan darsni faqat o'quv
/// bo'limi tuzatsin) ilova o'zgarishsiz to'g'ri ishlaydi.
/// </param>
/// <param name="Rows">
/// Guruhning FAOL o'quvchilari + shu darsda davomat yozuvi bo'lgan
/// HAMMA o'quvchi (arxivlangani ham). Tartib: ism bo'yicha.
/// </param>
public sealed record SessionAttendanceDto(
    long SessionId,
    long GroupId,
    string GroupName,
    string? Title,
    string Type,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    bool CanEdit,
    IReadOnlyList<AttendanceRowDto> Rows);

/// <summary>
/// Bitta o'quvchining shu darsdagi davomati.
/// </summary>
/// <param name="Status">
/// <c>null</c> — YOZUV YO'Q: o'quvchi xonaga umuman kirmagan va hali
/// hech kim qo'lda belgilamagan. Hisobotlarda bu "kelmagan" deb
/// sanaladi, lekin jadvalda ATAYLAB "belgilanmagan" (·) ko'rinadi —
/// "o'lchov yo'q" bilan "kelmagan deb qaror qilindi" bir narsa emas.
/// </param>
/// <param name="IsManual">
/// <c>true</c> — bahoni ODAM qo'ygan, platformaning o'lchovi emas.
/// Bunday qator dars yakunlanganda QAYTA HISOBLANMAYDI.
/// </param>
/// <param name="Reason">Qo'lda tuzatish sababi. Faqat <paramref name="IsManual"/> da ma'noli.</param>
/// <param name="DurationSeconds">
/// XONADA o'tkazgan vaqt (yakunlangan seanslar yig'indisi). Qo'lda
/// tuzatishda O'ZGARMAYDI — u o'lchov, baho emas. Shuning uchun
/// "Present, 0 daqiqa" mumkin va bu ZIDDIYAT EMAS: ustoz internet
/// uzilgan o'quvchini qo'lda kelgan deb belgilagan.
/// </param>
/// <param name="EditedByName">Oxirgi tuzatishni kim qildi. Tuzatilmagan qatorda <c>null</c>.</param>
/// <param name="EditedAt">Oxirgi tuzatish vaqti.</param>
public sealed record AttendanceRowDto(
    long StudentId,
    string StudentName,
    AttendanceStatus? Status,
    bool IsManual,
    string? Reason,
    DateTimeOffset? FirstJoinAt,
    DateTimeOffset? LeftAt,
    int DurationSeconds,
    long? EditedById,
    string? EditedByName,
    DateTimeOffset? EditedAt);

/// <summary>
/// Qo'lda tuzatish so'rovi.
///
/// ★ PUT — TO'LIQ ALMASHTIRISH: <c>reason</c> yuborilmasa yoki
/// <c>null</c> bo'lsa, avvalgi sabab O'CHIRILADI. "Sababni saqlab qol"
/// degan ma'no YO'Q — aks holda noto'g'ri sabab qatorga yopishib qolardi
/// va uni olib tashlashning umuman yo'li bo'lmasdi.
/// </summary>
/// <param name="Status">
/// <c>Present</c>, <c>Late</c>, <c>Partial</c> yoki <c>Absent</c>.
/// MAJBURIY: <c>null</c> bo'lsa 400.
///
/// Nima uchun nullable: nullable BO'LMASA yuborilmagan maydon jimgina
/// <c>Absent</c> (enum'ning 0 qiymati) bo'lib ketardi — ya'ni maydonni
/// unutgan klient butun sinfni "kelmagan" qilib qo'yardi.
/// </param>
/// <param name="Reason">Sabab, 300 belgigacha. Uzunroq bo'lsa 400.</param>
public sealed record UpdateAttendanceRequest(
    AttendanceStatus? Status = null,
    string? Reason = null);
