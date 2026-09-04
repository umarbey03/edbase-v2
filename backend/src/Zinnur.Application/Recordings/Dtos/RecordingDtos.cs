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
/// BITTA TREKNI yozib olish so'rovi (<c>TrackEgress</c>) — port kirishi.
///
/// ★ NIMA UCHUN <see cref="EgressStartRequest"/> QAYTA ISHLATILMADI:
/// so'rov LiveKit'ning BOSHQA metodiga (<c>StartTrackEgress</c>) va
/// boshqa shakldagi tanaga boradi. Bitta turga "<c>TrackId</c> bo'lsa
/// trek, bo'lmasa xona" degan qoida qo'ysak, o'sha qoida chaqiruv
/// joyidan UMUMAN ko'rinmasdi va uni faqat mijoz ichidan o'qib bilardik.
/// </summary>
/// <param name="RoomName">LiveKit xona nomi (<c>LiveSession.RoomName</c>).</param>
/// <param name="TrackId">
/// LiveKit trek identifikatori (<c>TR_…</c>) — <c>track_published</c>
/// hodisasidan keladi.
/// </param>
/// <param name="ObjectKey">
/// XOM bo'lakning kaliti (<c>raw/…</c> prefiksi) — uni
/// <c>IRecordingStorage.BuildRawObjectKey</c> yasaydi va Egress AYNAN
/// shu yo'lga yozadi.
///
/// ⚠️ KENGAYTMA — BASHORAT: u <c>track_published</c> dagi
/// <c>mime_type</c> dan taxmin qilinadi. Haqiqiy nom <c>egress_ended</c>
/// javobida keladi va farq qilsa qatordagi kalit O'SHA javob bilan
/// yangilanadi (yakuniy fayl uchun <c>SessionRecording.MarkCompleted</c>
/// allaqachon shunday qiladi).
/// </param>
public sealed record TrackEgressStartRequest(string RoomName, string TrackId, string ObjectKey);

/// <summary>
/// BUTUN XONANING ovozini yozib olish so'rovi — FAQAT-OVOZLI
/// <c>RoomCompositeEgress</c>. Bitta darsga bitta uzluksiz Opus fayl:
/// ustoz, ekran ovozi va gapirgan har bir o'quvchi.
///
/// 🔴 <see cref="EgressStartRequest"/> BILAN MAYDONLARI AYNI, LEKIN
/// ATAYLAB BOSHQA TUR. Ikkalasi LiveKit'ning AYNI metodiga
/// (<c>StartRoomCompositeEgress</c>) boradi, tanasi esa boshqacha:
/// bu yerda <c>layout</c> ham, <c>custom_base_url</c> ham UMUMAN
/// yuborilmaydi, chunki aynan o'sha ikki maydon Egress'ni brauzer
/// (Chrome) yo'liga buradi. Bitta tur bo'lsa "bir xil so'rov — bir xil
/// tana" degan taxmin bir kun albatta paydo bo'lardi va u dars boshiga
/// JIMGINA 0.3–0.5 yadro qo'shardi.
/// </summary>
/// <param name="RoomName">LiveKit xona nomi.</param>
/// <param name="ObjectKey">
/// Xom ovoz faylining kaliti — <c>raw/{sessionId}/{recordingId}/ROOM.ogg</c>.
///
/// ★ Kengaytma bu yerda BASHORAT EMAS: fayl turini so'rovning O'ZIDA
/// <c>OGG</c> deb belgilaymiz, ya'ni <c>.ogg</c> — fakt.
/// </param>
public sealed record RoomAudioEgressStartRequest(string RoomName, string ObjectKey);

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
/// LiveKit HOZIR ko'rib turgan e'lon qilingan trek (<c>ListParticipants</c>
/// javobidan). Ishtirokchi × trek — YASSI ro'yxat.
///
/// ★ NIMA UCHUN YASSI: chaqiruvchi (tiklash job'i) ishtirokchilar
/// daraxtini emas, "shu xonada qanday treklar bor va ular kimniki"
/// degan yagona savolga javob so'raydi. Ichma-ich ro'yxat har chaqiruv
/// joyida bitta qo'shimcha <c>foreach</c> talab qilardi.
///
/// ⚠️ BU <see cref="LiveKitTrackEventDto"/> EMAS va u bilan
/// birlashtirilmaydi: bu yerda hodisa identifikatori ham, hodisa nomi
/// ham YO'Q, chunki hech qanday hodisa bo'lgani yo'q — bu shunchaki
/// LiveKit'ning joriy holati. Ikkalasini bitta turga siqish
/// "idempotentlik kaliti" ni o'ylab topishga majbur qilardi.
/// </summary>
/// <param name="ParticipantIdentity">
/// LiveKit <c>identity</c> — bizda <c>User.Id</c> ning invariant satri
/// (<c>LiveSessionService.CreateJoinTokenAsync</c>).
/// </param>
/// <param name="TrackSid">Trek identifikatori (<c>TR_…</c>).</param>
/// <param name="Source">
/// <c>CAMERA</c>, <c>SCREEN_SHARE</c>, <c>MICROPHONE</c>,
/// <c>SCREEN_SHARE_AUDIO</c> yoki <c>UNKNOWN</c>. Xaritalash
/// (<c>RecordingTrackKind</c>) chaqiruvchida — port LiveKit atamasini
/// o'zgartirmasdan uzatadi.
/// </param>
/// <param name="MimeType">
/// <c>video/vp8</c>, <c>audio/opus</c> va h.k. Kengaytmani bashorat
/// qilish uchun kerak; LiveKit uni bermasligi ham mumkin.
/// </param>
public sealed record LiveKitPublishedTrackDto(
    string ParticipantIdentity,
    string TrackSid,
    string? Source,
    string? MimeType);

/// <summary>
/// Xonadagi treklar ro'yxati — YOKI so'rovning muvaffaqiyatsizligi.
///
/// 🔴 NIMA UCHUN BO'SH RO'YXAT YETMAYDI: chaqiruvchi uchun "xonada trek
/// yo'q" va "LiveKit'ga umuman yetib bo'lmadi" IKKI BOSHQA javob.
/// Ikkalasini ham bo'sh ro'yxat bilan qaytarsak, tarmoq uzilgan
/// daqiqada job "hech narsa yozilmayapti" deb xulosa chiqarib, mavjud
/// egress ustiga IKKINCHISINI ishga tushirardi.
///
/// ★ SHAKL <see cref="EgressStartResult"/> DAN NUSXA: istisno emas,
/// natija — sabab o'sha yerda va u bu yerda ham AYNI (tiklash job'i
/// LiveKit yiqilgani uchun to'xtamasligi kerak).
/// </summary>
public sealed record LiveKitTrackListResult(
    bool Succeeded,
    IReadOnlyList<LiveKitPublishedTrackDto> Tracks,
    string? Error)
{
    public static LiveKitTrackListResult Ok(IReadOnlyList<LiveKitPublishedTrackDto> tracks) =>
        new(true, tracks, null);

    public static LiveKitTrackListResult Fail(string error) => new(false, [], error);
}

/// <summary>LiveKit'da HOZIR mavjud egress (<c>ListEgress</c> javobidan).</summary>
/// <param name="EgressId">Egress identifikatori (<c>EG_…</c>).</param>
/// <param name="Status">
/// <c>EGRESS_ACTIVE</c>, <c>EGRESS_ENDING</c> va h.k. Ro'yxat FAQAT faol
/// egress'lar uchun so'raladi, lekin holat baribir uzatiladi: "tugayotgan"
/// va "ishlayotgan" farqi log'da qimmatli.
/// </param>
public sealed record LiveKitEgressInfoDto(string EgressId, string? Status);

/// <summary>
/// Xonadagi FAOL egress'lar ro'yxati — yoki so'rovning
/// muvaffaqiyatsizligi.
///
/// 🔴 <see cref="LiveKitTrackListResult"/> DAGI BILAN AYNI SABAB, LEKIN
/// OQIBATI OG'IRROQ: bu ro'yxat "mikser hali tirikmi" degan savolga
/// javob beradi. Xato holatni bo'sh ro'yxat deb ko'rsatish darsning
/// o'rtasida IKKINCHI mikserni ishga tushirardi — ya'ni bitta darsda ikki
/// ovoz fayli va tungi yig'ishda ikki karra ovoz.
/// </summary>
public sealed record LiveKitEgressListResult(
    bool Succeeded,
    IReadOnlyList<LiveKitEgressInfoDto> Items,
    string? Error)
{
    public static LiveKitEgressListResult Ok(IReadOnlyList<LiveKitEgressInfoDto> items) =>
        new(true, items, null);

    public static LiveKitEgressListResult Fail(string error) => new(false, [], error);
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
/// LiveKit webhook hodisasining TREK/XONA qismi — yangi yozuv quvuri
/// (<c>RecordingPipeline.TrackComposition</c>) uchun.
///
/// ★ NIMA UCHUN <see cref="LiveKitWebhookEventDto"/> KENGAYTIRILMADI:
/// o'sha DTO <c>egress_info</c> ATROFIDA qurilgan va uni ishlatadigan
/// <c>RecordingWebhookHandler</c> <c>EgressId</c> bo'lmasa hodisani
/// <c>Ignored</c> deb qaytaradi — bu uning SHARTNOMASI va u
/// o'zgarmasligi kerak. <c>room_started</c>, <c>track_published</c>,
/// <c>track_unpublished</c>, <c>participant_left</c> hodisalarida esa
/// <c>egress_info</c> UMUMAN yo'q: ular xona, ishtirokchi va trek
/// haqida. Ikkala ma'noni bitta turga siqish har bir maydonni
/// "qaysi hodisada to'ldiriladi?" degan izohsiz o'qib bo'lmaydigan
/// holga keltirardi.
/// </summary>
/// <param name="EventId">
/// LiveKit bergan hodisa Id'si (<c>EV_…</c>) — idempotentlik kaliti.
/// Bo'lmasa tananing xeshi ishlatiladi (<c>LiveKitWebhookParser</c>).
/// </param>
/// <param name="EventName">
/// <c>room_started</c>, <c>room_finished</c>, <c>track_published</c>,
/// <c>track_unpublished</c>, <c>participant_left</c>.
/// </param>
/// <param name="RoomName">
/// Xona nomi — <c>LiveSession.RoomName</c> bilan solishtiriladi. Bu
/// hodisalarda yozuv qatorini topishning YAGONA yo'li: egress Id yo'q.
/// </param>
/// <param name="ParticipantIdentity">
/// LiveKit <c>identity</c> = <c>User.Id</c> ning invariant satri.
/// Ustozniki emasmi — trek yozuvga tushishining birinchi sharti.
/// </param>
/// <param name="TrackSid">
/// Trek identifikatori (<c>TR_…</c>). Xona ovozi qatoriga HECH QACHON
/// tegishli emas — u LiveKit treki emas
/// (<c>RecordingTrack.RoomAudioSid</c>).
/// </param>
/// <param name="TrackSource">
/// <c>CAMERA</c>, <c>SCREEN_SHARE</c>, <c>MICROPHONE</c>,
/// <c>SCREEN_SHARE_AUDIO</c>, <c>UNKNOWN</c> — LiveKit atamasi
/// O'ZGARTIRILMASDAN uzatiladi. <c>RecordingTrackKind</c> ga xaritalash
/// qabul qiluvchida, bitta joyda.
/// </param>
/// <param name="MimeType">
/// <c>video/vp8</c>, <c>audio/opus</c>… — xom fayl kengaytmasini
/// bashorat qilish uchun. LiveKit uni bermasligi ham mumkin.
/// </param>
public sealed record LiveKitTrackEventDto(
    string EventId,
    string EventName,
    string? RoomName,
    string? ParticipantIdentity,
    string? TrackSid,
    string? TrackSource,
    string? MimeType);

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

    /// <summary>
    /// Hodisa BIZNIKI va qayta ishlandi, lekin yakuniy holatga olib
    /// kelmadi: to'xtatish so'raldi yoki oraliq egress holati
    /// (<c>EGRESS_STARTING</c> / <c>EGRESS_ENDING</c>) keldi.
    ///
    /// 🔴 NIMA UCHUN BU QIYMAT KERAK BO'LDI — VA NIMA UCHUN
    /// <see cref="Ignored"/> BU YERDA ISHLAMAYDI. Controller ikki
    /// ishlovchini KETMA-KET chaqiradi va <see cref="Ignored"/> uning
    /// uchun "bu hodisa menga tegishli emas, keyingisiga uzat" degan
    /// SIGNAL (<c>LiveKitWebhookController</c>). Trek ishlovchisi
    /// egress'ni O'ZINIKI deb tanigan, lekin unga tegmagan holatda
    /// <see cref="Ignored"/> qaytarsa, hodisa eski ishlovchiga tushardi
    /// va u har trek egress'i uchun "noma'lum egress" ogohlantirishi
    /// yozardi — bir kechada yuzlab yolg'on ogohlantirish, ya'ni
    /// HAQIQIY ogohlantirish ko'rinmay qolardi.
    ///
    /// Faqat <c>TrackComposition</c> quvurida uchraydi; eski ishlovchi
    /// (<c>RecordingWebhookHandler</c>) bu qiymatni HECH QACHON
    /// qaytarmaydi.
    /// </summary>
    Handled = 7,
}
