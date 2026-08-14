namespace Zinnur.Application.Recordings.Dtos;

/// <summary>
/// Bitta yozuv urinishi — dars kartochkasida ko'rsatiladigan shakl.
/// </summary>
/// <param name="Status">
/// <c>RecordingStatus</c> nomi (<c>Requested</c>, <c>Active</c>,
/// <c>Completed</c>…). ATAYLAB SATR: enum raqami klientga hech narsa
/// anglatmaydi va tartib o'zgarsa jimgina noto'g'ri holat ko'rsatilardi.
/// </param>
/// <param name="IsPlayable">
/// Ko'rish havolasini so'rash mumkinmi. Klient <c>Status</c> ni o'zi
/// tahlil qilmasin — qoida bitta joyda (Domain) qolsin.
/// </param>
/// <param name="Error">
/// Nega chiqmagani (faqat xodimga ko'rsatiladi). O'quvchiga bu maydon
/// ko'rsatilmaydi — unga faqat "yozuv yo'q" degani muhim.
/// </param>
/// <param name="IsVisibleToStudents">
/// SHU yozuvning ko'rinish bayrog'i (R5).
///
/// ⚠️ "O'quvchi buni ko'radi" DEGANI EMAS: amaldagi ko'rinish uchta
/// kalitning ko'paytmasi (global sozlama × guruh × shu bayroq). Bu maydon
/// faqat XODIM interfeysidagi kalitning holatini ko'rsatadi —
/// o'quvchiga yuboriladigan javobda u har doim <c>true</c> bo'ladi,
/// chunki ko'rinmaydigan yozuv ro'yxatga UMUMAN tushmaydi.
/// </param>
/// <param name="HasReview">
/// Bu darsda o'quv bo'limining tahlili bormi (R29).
///
/// ★ NIMA UCHUN YOZUV DTO'SIDA, GARCHI TAHLIL DARSGA BOG'LANGAN BO'LSA
/// HAM: ro'yxat AYNAN yozuv kartochkalaridan iborat va nishon o'sha
/// kartochkada chiziladi. Maydon bo'lmasa klient har kartochka uchun
/// alohida so'rov yuborardi (N+1) — 30 ta yozuvli sahifada 30 ta so'rov.
/// Server tomonda esa bu BITTA korrelyatsion so'rov.
///
/// 🔴 O'QUVCHIGA HAR DOIM <c>false</c> / <c>null</c>. Tahlil undan
/// yopiq va u haqda "bor" degan ishora ham berilmaydi.
/// </param>
/// <param name="ReviewStatus">
/// Tahlil xulosasi (<c>NotReviewed</c> / <c>Approved</c> / <c>HasIssue</c>)
/// yoki <c>null</c> — tahlil yo'q. Eski ilovadagi uch holatli nishon
/// AYNAN shu ikki maydondan tiklanadi.
/// </param>
public sealed record RecordingDto(
    long Id,
    long SessionId,
    string Status,
    bool IsPlayable,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int? DurationSeconds,
    long? SizeBytes,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    bool IsVisibleToStudents,
    bool HasReview,
    string? ReviewStatus);

/// <summary>
/// Yozuvning o'quvchilarga ko'rinishini o'zgartirish (R5).
/// </summary>
/// <param name="Visible">
/// <c>true</c> — ochish, <c>false</c> — yashirish.
///
/// ⚠️ Ochish TAYYOR bo'lmagan yozuvda rad etiladi (409) — domain qoidasi
/// <c>SessionRecording.ShowToStudents</c> da. Yashirish esa HAR QANDAY
/// holatda ishlaydi.
/// </param>
public sealed record UpdateRecordingVisibilityRequest(bool Visible);

/// <summary>
/// "Dars yozuvlari bo'limi umuman ochiqmi" — O'QUVCHI interfeysi uchun.
///
/// ★ NIMA UCHUN ALOHIDA ENDPOINT KERAK BO'LDI: o'quvchining "O'quv"
/// ekranida yozuvlar bo'limiga KIRISH KARTOCHKASI turadi
/// (<c>StudentLearnPage.vue</c>). Bo'lim yopilganda kartochka qolsa,
/// o'quvchi uni bosib abadiy bo'sh sahifaga tushardi va buni nosozlik deb
/// o'ylardi. Ro'yxat endpointining O'ZI bu savolga javob bera olmaydi:
/// bo'sh ro'yxat "yopilgan" ni ham, "hali yozuv yo'q" ni ham bildiradi va
/// bu ikki holat foydalanuvchi uchun BUTUNLAY boshqacha.
/// </summary>
/// <param name="Visible">
/// Bo'lim ochiqmi. Xodim uchun HAR DOIM <c>true</c> — global va guruh
/// kalitlari faqat o'quvchiga tegishli.
/// </param>
public sealed record RecordingSectionDto(bool Visible);

/// <summary>
/// "Dars yozuvlari" ro'yxatining bitta qatori: yozuv + u tegishli bo'lgan
/// darsning konteksti.
///
/// ★ NIMA UCHUN DARS MA'LUMOTI SHU YERDA: ro'yxat sahifasi "qaysi guruh,
/// qaysi kun" savoliga javob bermasa foydasiz bo'lardi, klient esa har
/// yozuv uchun alohida so'rov yuborishga majbur bo'lardi (N+1).
/// </summary>
public sealed record RecordingListItemDto(
    RecordingDto Recording,
    long GroupId,
    string GroupName,
    string? Title,
    DateOnly LocalDate,
    DateTimeOffset ScheduledStart);

/// <summary>
/// Ko'rish havolasi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ HAVOLA BAZAGA YOZILMAYDI VA KESHLANMAYDI. U har so'rovda yangidan
/// imzolanadi — ya'ni ruxsat va to'lov darvozasi HAR SAFAR tekshiriladi.
///
/// ★ <see cref="ExpiresAt"/> KLIENTGA ATAYLAB BERILADI: pleyer havola
/// muddati tugashidan oldin yangisini so'rab, ko'rish o'rnini (currentTime)
/// saqlab qolishi kerak. Bu shartnomaning bir qismi — busiz video o'rtada
/// "sababsiz" to'xtab qolardi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record RecordingLinkDto(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// "Hozir yozib olinyaptimi" — JONLI XONADAGI INDIKATOR javobi.
///
/// 🔴 BU JAVOB O'QUVCHIGA HAM BERILADI, shuning uchun unda ICHKI
/// TAFSILOT YO'Q: na ombor kaliti, na egress Id'si, na xato matni, na
/// urinishlar soni. Ikki maydon — indikator chizish uchun yetarli va
/// undan ortig'i shunchaki oshkorlik bo'lardi.
///
/// ★ HAJMI MUHIM: bu javob xonadagi HAR ODAMDAN har 10 soniyada
/// so'raladi (80 daqiqalik darsda 25 kishilik guruh uchun ~12 000
/// so'rov). <see cref="RecordingDto"/> ni qaytarish o'sha trafikni
/// bekorga o'n barobar oshirardi.
/// </summary>
/// <param name="IsRecording">
/// Yakunlanmagan yozuv qatori bormi (<c>Requested</c>, <c>Starting</c>
/// yoki <c>Active</c>).
///
/// ⚠️ ATAYLAB "<c>Active</c> emas": sabab —
/// <c>IRecordingService.GetLiveStatusAsync</c> izohidagi ASIMMETRIYA
/// bo'limi. Qisqasi: rozilik indikatorida shubha "ha" foydasiga hal
/// qilinadi.
/// </param>
/// <param name="StartedAt">
/// Yozuv HAQIQATAN boshlangan payt (<c>egress_started</c> hodisasidan).
/// <c>null</c> — hali boshlanmagan, lekin navbatda
/// (<see cref="IsRecording"/> shunda ham <c>true</c>). Klient buni
/// faqat izoh matnida ("… dan beri") ishlatadi, indikatorni yoqish
/// qaroriga U EMAS, <see cref="IsRecording"/> javob beradi.
/// </param>
public sealed record RecordingLiveStatusDto(bool IsRecording, DateTimeOffset? StartedAt);

/// <summary>Egress'ni boshlash so'rovi (port kirishi).</summary>
/// <param name="RoomName">LiveKit xona nomi (<c>LiveSession.RoomName</c>).</param>
/// <param name="ObjectKey">Ombordagi to'liq kalit — Egress AYNAN shu yo'lga yozadi.</param>
public sealed record EgressStartRequest(string RoomName, string ObjectKey);

/// <summary>
/// Egress javobi.
///
/// ★ ISTISNO O'RNIGA NATIJA: yozuvning boshlanmasligi DARSNI to'xtatishi
/// mumkin emas. Chaqiruvchi xatoni ko'radi, qatorga yozadi va watchdog'ga
/// qoldiradi — hech narsa "portlamaydi".
/// </summary>
public sealed record EgressStartResult(bool Succeeded, string? EgressId, string? Error)
{
    public static EgressStartResult Ok(string egressId) => new(true, egressId, null);

    public static EgressStartResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// LiveKit webhook hodisasining BIZGA KERAKLI qismi.
///
/// ⚠️ LiveKit JSON'i o'z qoidasi bo'yicha keladi va versiyalar orasida
/// <c>snake_case</c> / <c>camelCase</c> o'rtasida farq qiladi. Shuning
/// uchun bu yozuv to'g'ridan-to'g'ri deserializatsiya QILINMAYDI —
/// <c>LiveKitWebhookParser</c> har maydonni IKKALA nom bilan qidiradi
/// (batafsil sabab o'sha sinfda).
/// </summary>
/// <param name="EventId">
/// LiveKit bergan hodisa Id'si (<c>EV_…</c>) — idempotentlik kaliti.
/// Bo'lmasa tananing xeshi ishlatiladi.
/// </param>
/// <param name="EventName">
/// <c>egress_started</c>, <c>egress_updated</c>, <c>egress_ended</c>,
/// <c>room_finished</c> va h.k.
/// </param>
/// <param name="EgressStatus">
/// <c>EGRESS_STARTING</c>, <c>EGRESS_ACTIVE</c>, <c>EGRESS_COMPLETE</c>,
/// <c>EGRESS_FAILED</c>, <c>EGRESS_ABORTED</c>, <c>EGRESS_LIMIT_REACHED</c>.
/// </param>
/// <param name="FileSizeBytes">Yozilgan faylning hajmi (baytda).</param>
/// <param name="DurationSeconds">Videoning haqiqiy uzunligi.</param>
public sealed record LiveKitWebhookEventDto(
    string EventId,
    string EventName,
    string? EgressId,
    string? RoomName,
    string? EgressStatus,
    string? ObjectKey,
    long? FileSizeBytes,
    int? DurationSeconds,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Error);

/// <summary>
/// Webhook qayta ishlash natijasi — LOG va qo'lda tekshirish (`curl`) uchun.
/// Bu qiymat LiveKit'ga qaytariladigan javobning ichida ko'rinadi.
/// </summary>
public enum RecordingWebhookOutcome
{
    /// <summary>JSON o'qib bo'lmadi.</summary>
    Malformed = 0,

    /// <summary>Bizga aloqasi yo'q hodisa (masalan <c>participant_joined</c>).</summary>
    Ignored = 1,

    /// <summary>Ayni hodisa allaqachon ishlangan.</summary>
    Duplicate = 2,

    /// <summary>Hodisa keldi, lekin bunday <c>EgressId</c> bizda yo'q.</summary>
    Unknown = 3,

    /// <summary>Yozuv faol deb belgilandi.</summary>
    Started = 4,

    /// <summary>Yozuv tugallandi, fayl kaliti saqlandi.</summary>
    Completed = 5,

    /// <summary>Yozuv xato bilan tugadi.</summary>
    Failed = 6,
}
