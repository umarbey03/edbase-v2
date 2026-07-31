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
