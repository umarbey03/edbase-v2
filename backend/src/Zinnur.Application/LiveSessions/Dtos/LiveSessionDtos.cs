namespace Zinnur.Application.LiveSessions.Dtos;

public sealed record LiveSessionDto(
    long Id,
    long GroupId,
    string GroupName,
    string? Title,
    string Type,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    DateTimeOffset? ActualStart,
    DateTimeOffset? EndsAt,
    bool IsHost);

/// <summary>
/// KALENDAR uchun dars.
///
/// ★ NIMA UCHUN <see cref="LiveSessionDto"/> DAN ALOHIDA TUR: mavjud
/// <c>GET /live-sessions</c> shartnomasi frontend tomonidan ALLAQACHON
/// ishlatiladi va unga maydon qo'shish/olib tashlash mumkin emas.
/// Kalendarga esa boshqa narsa kerak: o'tgan darsdagi O'Z davomatim va
/// dars qaysi MAHALLIY kunga tushishi. Bir DTO'ni ikki maqsadga
/// cho'zish o'rniga — ikki oshkora shartnoma.
/// </summary>
/// <param name="LocalDate">
/// Dars boshlanadigan MAHALLIY (markaz vaqti) kalendar kuni.
///
/// Frontend darslarni kunlarga shu maydon bo'yicha guruhlaydi va
/// <c>ScheduledStart</c> dan O'ZI sana chiqarmaydi: brauzer o'z vaqt
/// zonasida hisoblaydi va chet eldagi o'quvchida 20:00 dagi dars
/// KECHAGI kunga tushib qolardi.
/// </param>
/// <param name="MyAttendance">
/// O'quvchining shu darsdagi davomati: <c>Present</c>, <c>Late</c>,
/// <c>Partial</c>, <c>Absent</c>. <c>null</c> — davomat yozuvi yo'q
/// (dars hali o'tmagan yoki o'quvchi umuman kirmagan). Xodim uchun doim
/// <c>null</c>.
/// </param>
public sealed record CalendarSessionDto(
    long Id,
    long GroupId,
    string GroupName,
    string? Title,
    string Type,
    string Status,
    DateOnly LocalDate,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    bool IsHost,
    string? MyAttendance);

/// <summary>
/// ========================================================================
/// DARSLAR JADVALI QATORI — "Darslarim" bo'limidagi agregat (R31)
/// ========================================================================
///
/// Talab (2026-08-13): *"darslarim bo'limida jadval ma'lumoti sifatida
/// nechta student borligi, nechta qatnashganligi, davomiyligi"*.
///
/// ★ NIMA UCHUN YANA YANGI TUR, <see cref="LiveSessionDto"/> KENGAYTIRILMADI:
/// yuqoridagi qoida (16–24-qatorlar) o'zgarmadi — mavjud
/// <c>GET /live-sessions</c> shartnomasini frontend ALLAQACHON ishlatadi va
/// unga maydon qo'shilmaydi. <see cref="CalendarSessionDto"/> ham AYNAN shu
/// sababdan tug'ilgan edi; bu — o'sha naqshning uchinchi qo'llanishi.
///
/// 🔴 SANOQLAR SERVERDA HISOBLANADI. Mijoz har dars uchun
/// <c>/live-sessions/{id}/attendance</c> ga borishi MUMKIN EMAS: bir guruhda
/// 69 tagacha dars bo'ladi va davomat matritsasi shu sababli 10 ta ustun
/// bilan cheklangan (<c>attendance-matrix.ts</c>). Jadval esa BUTUN ro'yxatni
/// ko'rsatadi, ya'ni yagona yo'l — agregat.
/// </summary>
/// <param name="StudentCount">
/// Guruhdagi HOZIRGI faol o'quvchilar soni.
///
/// 🔴 "DARS PAYTIDAGI" SON EMAS — VA BU TANLOV EMAS, YAGONA IMKONIYAT.
/// <c>GroupMember</c> da chiqish VAQTI saqlanmaydi (faqat <c>Status</c> va
/// <c>JoinedAt</c>), davomat qatori esa faqat XONAGA KIRGAN yoki qo'lda
/// belgilangan o'quvchida paydo bo'ladi — ya'ni "dars kunida guruhda kim
/// bor edi" degan savolga baza javob BERA OLMAYDI. Uni javob beradigan
/// qilish yangi ustun va migratsiya talab qilardi.
///
/// ⚠️ QOLDIQ CHEKINISH: guruhni tark etgan o'quvchi eski darsda qatnashgan
/// bo'lsa, <paramref name="AttendedCount"/> shu sondan KATTA chiqishi
/// mumkin. Bu ataylab yashirilmaydi — teskarisi (sanoqni sun'iy ravishda
/// tenglashtirish) haqiqiy davomatni yo'q qilardi.
///
/// KURATOR guruhida a'zolar bevosita yo'q — ular bog'langan ustoz
/// guruhlaridan sanaladi (<c>GroupService.Project</c> bilan AYNI ifoda).
/// </param>
/// <param name="AttendedCount">
/// QATNASHGANLAR soni: <c>Present</c> + <c>Late</c> + <c>Partial</c>.
///
/// ★ NIMA UCHUN <c>Late</c> VA <c>Partial</c> HAM QATNASHGAN HISOBLANADI:
/// savol "nechta o'quvchi darsga keldi?" degani, "nechtasi mukammal
/// qatnashdi?" degani emas. <c>Partial</c> ni <c>Attendance.Finalize</c>
/// xonada NOLDAN ko'p vaqt o'tkazgan har kimga qo'yadi — uni "kelmagan"
/// deb sanash davomat jadvalidagi katak bilan ZID bo'lardi (u yerda katak
/// aniq "yo'q" emas) va ustoz ikki ekranda ikki xil raqam ko'rardi.
///
/// ⚠️ YOZUVI YO'Q o'quvchi qatnashmagan hisoblanadi — hisobotlardagi
/// qoida bilan bir xil. Davomat jadvalida esa u "belgilanmagan" bo'lib
/// ko'rinadi: farq ATAYLAB, chunki u yerda ustoz uni BELGILAY oladi, bu
/// yerda esa faqat sanoq bor.
/// </param>
/// <param name="PlannedMinutes">
/// Rejadagi davomiylik (<c>ScheduledEnd − ScheduledStart</c>) — DOIM bor.
/// </param>
/// <param name="ActualMinutes">
/// HAQIQIY davomiylik (<c>ActualEnd − ActualStart</c>). <c>null</c> — dars
/// boshlanmagan yoki hali yakunlanmagan.
///
/// ★ "DAVOMIYLIK" NING UCH O'QILISHIDAN QAYSI BIRI TANLANDI VA NEGA:
///   • HAQIQIY (tanlandi) — ustoz jadvalga "dars qanday o'tdi" degan savol
///     bilan keladi; 80 daqiqalik dars 45 daqiqada tugagani AYNAN shu
///     yerda ko'rinishi kerak;
///   • REJADAGI — u guruh sozlamasi va guruhning HAMMA qatorida bir xil
///     son bo'lardi, ya'ni ustun sifatida hech nima aytmasdi. Shunga
///     qaramay u ham qaytariladi: <c>null</c> haqiqiy qiymat yonida
///     taqqoslash uchun asos kerak ("reja 80, haqiqiy 45");
///   • O'QUVCHILARNING XONADA O'TKAZGAN O'RTACHA VAQTI — bu BOSHQA savol
///     ("jalb qilinganlik") va u dars TAHLILIGA tegishli (R29/R30), dars
///     ro'yxatiga emas. Uni shu ustunga qo'yish "dars 12 daqiqa davom
///     etdi" degan yolg'on o'qilishga olib kelardi.
/// </param>
public sealed record SessionStatsDto(
    long Id,
    long GroupId,
    string GroupName,
    string? Title,
    string Type,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    DateTimeOffset? ActualStart,
    DateTimeOffset? ActualEnd,
    int PlannedMinutes,
    int? ActualMinutes,
    int StudentCount,
    int AttendedCount,
    bool IsHost);

/// <summary>
/// Darslar jadvali filtri (R31). Barcha maydonlar ixtiyoriy.
///
/// ★ SANA ORALIG'I EMAS, SAHIFALASH: kalendar
/// (<see cref="CalendarSessionDto"/>) oraliq bilan ishlaydi, chunki u
/// AYNAN oyni chizadi. Jadval esa "oxirgi darslarim" ro'yxati — u yerda
/// foydalanuvchi sanani emas, sahifani suradi.
/// </summary>
/// <param name="Status">
/// Dars holati bo'yicha filtr. Frontend sukut bo'yicha <c>Ended</c>
/// yuboradi — sabab <c>TeacherSessionsTable.vue</c> izohida.
/// </param>
/// <param name="GroupId">Bitta guruh kesimi (ixtiyoriy).</param>
/// <param name="Page">Sahifa (1 dan).</param>
/// <param name="PageSize">Sahifa hajmi (1..100, default 20).</param>
public sealed record SessionStatsQuery(
    Zinnur.Domain.Enums.SessionStatus? Status = null,
    long? GroupId = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Frontend LiveKit'ga aynan shu bilan ulanadi.</summary>
public sealed record LiveKitJoinDto(
    string ServerUrl,
    string Token,
    string RoomName,
    bool IsHost,
    DateTimeOffset? EndsAt);

/// <summary>
/// Jonli dars chat xabari.
///
/// ★ <c>ClientId</c> — REAL VAQTDAGI broadcast uchun BARQAROR va NOYOB kalit.
///
/// NIMA UCHUN <c>Id</c> YETMAYDI: xabar avval tarqatiladi, keyin fon navbatida
/// bazaga yoziladi (<c>ChatMessageWriter</c>) — ya'ni tarqatilayotgan payt baza
/// raqami HALI YO'Q va u yerda 0 turadi. Klient esa takrorlarni <c>Id</c>
/// bo'yicha filtrlaydi, natijada BIRINCHI xabardan keyingi hammasi
/// "allaqachon ko'rilgan" deb jimgina tashlanardi (batafsil:
/// <c>LiveClassHub.NormalizeClientId</c>).
///
/// REST tarixida (<c>GetRecentMessagesAsync</c>) bu maydon <c>null</c> bo'ladi —
/// u yerda haqiqiy <c>Id</c> bor va kalit sifatida o'sha ishlatiladi.
/// Bazada SAQLANMAYDI, ya'ni migratsiya talab qilmaydi.
/// </summary>
public sealed record ChatMessageDto(
    long Id,
    long SenderId,
    string SenderName,
    string Body,
    DateTimeOffset SentAt,
    string? ClientId = null);
