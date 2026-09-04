using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARSNING XOM BO'LAGI (TrackEgress / xona ovozi → obyekt ombori)
/// ════════════════════════════════════════════════════════════════════════
///
/// Bitta qator = OMBORDAGI BITTA XOM FAYL. Odatdagi dars: 1 ta
/// <see cref="RecordingTrackKind.RoomAudio"/> + 1–2 ta video bo'lak.
/// Ustoz ikki marta uzilib-ulansa va ekranni uch marta yoqib-o'chirsa —
/// 10 dan ortiq video qator, lekin ovoz qatori BARIBIR bitta.
///
/// 🔴 KO'P VIDEO BO'LAK — ODATIY HOL, CHEKKA HOL EMAS. Bu jumla shu
/// yerda turibdi, chunki "bitta darsga bitta fayl" degan taxmin bilan
/// yozilgan har qanday kod (masalan <c>Single()</c>) darsning yarmini
/// jimgina yo'qotadi.
///
/// ── NIMA UCHUN ALOHIDA JADVAL, <see cref="SessionRecording"/> USTUNLARI EMAS ──
///
/// Bo'laklar SONI oldindan noma'lum va u darsning borishiga bog'liq
/// (ekran necha marta yoqildi, ustoz necha marta uzildi). Ustunlarga
/// sig'dirish uchun "1-ekran kaliti", "2-ekran kaliti" kabi chegara
/// qo'yish kerak bo'lardi — chegara esa albatta bir kuni yetmay qoladi
/// va o'shanda yo'qolgan bo'lak haqida BAZADA HECH QANDAY iz qolmasdi.
///
/// ── NIMA UCHUN HOLAT UCHUN YANGI ENUM YO'Q ──────────────────────────────
///
/// <see cref="Status"/> — <see cref="RecordingStatus"/>, ya'ni
/// <see cref="SessionRecording"/> BILAN AYNI beshta ma'no. Sabab: bo'lak
/// ham xuddi shu LiveKit Egress mexanizmi bilan olinadi va AYNI
/// webhook'lar bilan boshqariladi (<c>egress_started</c>,
/// <c>egress_ended</c>). Ikkinchi enum faqat ikki joyda qo'lda
/// solishtiriladigan ikki nusxa yaratardi.
///
/// ⚠️ TUNGI YIG'ISH holati BU YERDA EMAS: u BUTUN yozuvning xossasi,
/// bo'lakniki emas, va <c>SessionRecording.CompositionStatus</c> da turadi.
///
/// ── XOM FAYLLAR VAQTINCHALIK ────────────────────────────────────────────
///
/// <see cref="ObjectKey"/> <c>raw/…</c> prefiksida — YAKUNIY fayldan
/// (<c>recordings/…</c>) ATAYLAB ajratilgan, ya'ni mavjud ro'yxatlar,
/// lifecycle qoidalari va admin ekranlari ularni umuman ko'rmaydi. Ular
/// hech qachon foydalanuvchiga berilmaydi va yig'ish muvaffaqiyatli
/// tugagach o'chiriladi (<c>SessionRecording.RawPurgedAt</c>).
/// </summary>
public class RecordingTrack : BaseEntity
{
    /// <summary>
    /// XONA OVOZI qatorining <see cref="TrackSid"/> o'rnidagi sentinel.
    ///
    /// ★ NIMA UCHUN SENTINEL, <c>NULL</c> EMAS: <c>(RecordingId, TrackSid)</c>
    /// unikal indeksi ikki vazifani bajaradi — takroriy
    /// <c>track_published</c> hodisasidan himoya VA "bitta darsga bitta
    /// mikser" kafolati. Postgres unikal indeksda <c>NULL</c> lar
    /// bir-biridan farqli hisoblanadi, ya'ni <c>NULL</c> qo'ysak
    /// ikkinchi kafolat YO'QOLARDI va qayta yetkazilgan
    /// <c>room_started</c> ikkinchi mikserni ishga tushirardi.
    ///
    /// LiveKit trek identifikatorlari DOIM <c>TR_</c> bilan boshlanadi,
    /// shuning uchun bu qiymat haqiqiy trek bilan to'qnasha olmaydi.
    ///
    /// ⚠️ Mikser dars o'rtasida o'lib, o'rniga yangisi qo'yilsa ikkinchi
    /// ovoz fayli paydo bo'ladi va uning sentineli tartib raqami bilan
    /// yoziladi (<c>ROOM2</c>, <c>ROOM3</c> …) — indeks buni to'smaydi va
    /// TO'SMASLIGI kerak, chunki bu bo'laklar bir-birining nusxasi emas,
    /// vaqt o'qining ketma-ket qismlari.
    /// </summary>
    public const string RoomAudioSid = "ROOM";

    /// <summary>
    /// Xato matnining bazadagi chegarasi —
    /// <see cref="SessionRecording.MaxErrorLength"/> bilan AYNI qiymat va
    /// AYNI sabab: to'liq javob LOGDA qoladi, bu yerda faqat xodimga
    /// ko'rinadigan qisqa sabab.
    /// </summary>
    public const int MaxErrorLength = 500;

    public long RecordingId { get; set; }

    public SessionRecording? Recording { get; set; }

    /// <summary>
    /// LiveKit trek identifikatori (<c>TR_…</c>), yoki xona ovozi uchun
    /// <see cref="RoomAudioSid"/>.
    /// </summary>
    public required string TrackSid { get; set; }

    /// <summary>
    /// Trekni e'lon qilgan ishtirokchi — LiveKit <c>identity</c>, ya'ni
    /// <c>User.Id</c> ning satr ko'rinishi
    /// (<c>LiveSessionService.CreateJoinTokenAsync</c>).
    ///
    /// 🔴 <see cref="RecordingTrackKind.RoomAudio"/> uchun <c>NULL</c>.
    /// Butun xonaning aralashmasi HECH KIMGA tegishli emas; u yerga
    /// bo'sh satr yozish — kelajakdagi biror <c>WHERE</c> ishonadigan
    /// YOLG'ON bo'lardi.
    /// </summary>
    public string? ParticipantIdentity { get; set; }

    public RecordingTrackKind Kind { get; set; }

    /// <summary>
    /// <c>track_published</c> aytgan kodek (<c>video/vp8</c>,
    /// <c>audio/opus</c> …). Fayl kengaytmasi shundan TAXMIN qilinadi.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Ombordagi XOM kalit (<c>raw/{sessionId}/{recordingId}/{trackSid}.{ext}</c>).
    ///
    /// ⚠️ Kengaytma qator yaratilganda TAXMIN qilinadi, lekin unga
    /// ISHONILMAYDI: <c>egress_ended</c> haqiqiy <c>file.filename</c> ni
    /// qaytaradi va u boshqa bo'lsa shu ustun USTIDAN yoziladi
    /// (<c>SessionRecording.MarkCompleted</c> dagi AYNI qoida).
    /// </summary>
    public required string ObjectKey { get; set; }

    /// <summary>
    /// SHU bo'lak uchun LiveKit bergan egress identifikatori. Webhook
    /// qatorni aynan shu ustun bo'yicha topadi, shuning uchun u UNIKAL.
    /// </summary>
    public string? EgressId { get; set; }

    public RecordingStatus Status { get; set; } = RecordingStatus.Requested;

    /// <summary>Bo'lak HAQIQATAN yozila boshlagan payt (Egress hodisasidan).</summary>
    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>Egress aytgan uzunlik (sekund).</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// <c>ffprobe</c> yig'ish paytida O'LCHAGAN haqiqiy uzunlik (ms).
    ///
    /// ★ NIMA UCHUN AYRIM USTUN, <see cref="DurationSeconds"/> USTIDAN
    /// YOZILMAYDI: ikkalasining FARQI — signal. Egress aytgan uzunlik
    /// bilan faylning o'lchangan uzunligi bir necha soniyaga ajralsa,
    /// bu ovoz-tasvir siljishining sababi va uni faqat IKKALA raqam
    /// saqlanganda ko'rish mumkin.
    /// </summary>
    public int? ProbedDurationMs { get; set; }

    /// <summary>Egress'ni boshlashga necha marta urinilgan.</summary>
    public int Attempts { get; set; }

    /// <summary>Oxirgi urinish payti — moslashtiruvchi vazifa ikki urinish orasida kutadi.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// To'xtatish so'rovi yuborilgan payt —
    /// <see cref="SessionRecording.StopRequestedAt"/> bilan AYNI maqsad:
    /// takroriy <c>StopEgress</c> LiveKit'da xato beradi va log'ni
    /// bekorga to'ldiradi.
    /// </summary>
    public DateTimeOffset? StopRequestedAt { get; set; }

    /// <summary>Nima uchun chiqmagani — XODIM uchun qisqa sabab.</summary>
    public string? Error { get; set; }

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>Yakuniy holatmi — bunga qayta tegilmaydi.</summary>
    public bool IsFinished =>
        Status is RecordingStatus.Completed or RecordingStatus.Failed;

    /// <summary>
    /// Bu bo'lak butun XONANING ovozimi.
    ///
    /// ★ TEKSHIRUV <see cref="Kind"/> BO'YICHA, <see cref="TrackSid"/>
    /// BO'YICHA EMAS: mikser almashtirilganda sentinel <c>ROOM2</c>,
    /// <c>ROOM3</c> bo'lib ketadi, tur esa o'zgarmaydi.
    /// </summary>
    public bool IsRoomAudio => Kind == RecordingTrackKind.RoomAudio;

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Yangi urinish boshlanishini belgilaydi (Egress'ga murojaatdan OLDIN).
    /// </summary>
    public void BeginAttempt(DateTimeOffset now)
    {
        if (IsFinished)
            throw new DomainException("Yakunlangan bo'lakni qayta boshlab bo'lmaydi.");

        Attempts++;
        LastAttemptAt = now;
        UpdatedAt = now;
    }

    /// <summary>Egress so'rovni qabul qildi.</summary>
    public void MarkStarting(string egressId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(egressId);

        if (IsFinished) return;         // kech kelgan javob yakunni buzmasin

        EgressId = egressId;
        Status = RecordingStatus.Starting;
        Error = null;                   // oldingi urinishning sababi eskirdi
        UpdatedAt = now;
    }

    /// <summary>
    /// Bo'lak haqiqatan yozila boshladi (<c>egress_started</c>).
    /// IDEMPOTENT: LiveKit hodisani qayta yuborishi mumkin.
    ///
    /// 🔴 <see cref="StartedAt"/> BIRINCHI qiymatida qoladi va bu shunchaki
    /// tozalik emas — u YIG'ISHNING VAQT O'QI. Uni kechki hodisa bilan
    /// surib yuborish butun bo'lakni tasvirda noto'g'ri joyga qo'yardi.
    /// </summary>
    public void MarkActive(DateTimeOffset startedAt, DateTimeOffset now)
    {
        if (IsFinished) return;

        Status = RecordingStatus.Active;
        StartedAt ??= startedAt;
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Xom fayl omborda. IDEMPOTENT va QAYTMAS.
    ///
    /// 🔴 <see cref="SessionRecording.MarkCompleted"/> DAN ATAYLAB
    /// QAT'IYROQ: u faqat <c>Completed</c> ni to'sadi, bu esa HAR QANDAY
    /// yakunni — <c>Failed</c> ni ham. Sabab: bo'lak <c>Failed</c> bo'lishi
    /// uchun uni OMBORDA QIDIRIB TOPMAGAN bo'lishimiz kerak
    /// (moslashtiruvchi vazifa <c>HEAD</c> qiladi). Kech kelgan
    /// <c>egress_ended</c> uni tiriltirsa, tungi yig'ish MAVJUD BO'LMAGAN
    /// faylni yuklab olishga urinardi va butun yozuv yiqilardi — bitta
    /// bo'lakni yo'qotish o'rniga.
    /// </summary>
    /// <param name="objectKey">
    /// Egress qaytargan HAQIQIY kalit. Bo'sh bo'lsa taxmin qilingan kalit
    /// qoladi.
    /// </param>
    public void MarkCompleted(
        string? objectKey,
        long? sizeBytes,
        int? durationSeconds,
        DateTimeOffset endedAt,
        DateTimeOffset now)
    {
        if (IsFinished) return;

        if (!string.IsNullOrWhiteSpace(objectKey))
            ObjectKey = objectKey;

        Status = RecordingStatus.Completed;
        SizeBytes = sizeBytes ?? SizeBytes;
        DurationSeconds = durationSeconds ?? DurationSeconds;
        EndedAt ??= endedAt;
        StartedAt ??= endedAt;
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Bo'lak chiqmadi. TAYYOR bo'lakka TEGMAYDI — kech kelgan yoki
    /// takroriy "xato" hodisasi omborda turgan faylni ro'yxatdan
    /// o'chirib yubormasin.
    ///
    /// ⚠️ Yiqilgan bo'lak butun yozuvni yiqitmaydi: yig'ish qolgan
    /// bo'laklardan davom etadi va yo'qolgan joy qora ekran (video) yoki
    /// jimlik (ovoz) bo'lib chiqadi. Yozuv faqat BITTA HAM bo'lak
    /// tayyor bo'lmaganda yiqiladi.
    /// </summary>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        if (Status == RecordingStatus.Completed) return;

        Status = RecordingStatus.Failed;
        Error = Trim(reason);
        EndedAt ??= now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Urinish yiqildi, lekin YAKUNIY xato emas — moslashtiruvchi vazifa
    /// qayta uradi. Holat <see cref="RecordingStatus.Requested"/> bo'lib
    /// qoladi (<see cref="SessionRecording.RecordAttemptError"/> dagi AYNI
    /// naqsh).
    /// </summary>
    public void RecordAttemptError(string reason, DateTimeOffset now)
    {
        if (IsFinished) return;

        Error = Trim(reason);
        UpdatedAt = now;
    }

    /// <summary>To'xtatish so'rovi yuborilganini belgilaydi (takrorni to'sish uchun).</summary>
    public void MarkStopRequested(DateTimeOffset now)
    {
        StopRequestedAt ??= now;
        UpdatedAt = now;
    }

    /// <summary>Yana urinish mumkinmi.</summary>
    public bool CanRetry(int maxAttempts) =>
        Status == RecordingStatus.Requested && Attempts < maxAttempts;

    private static string Trim(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "Noma'lum xato."
            : reason.Length <= MaxErrorLength ? reason : reason[..MaxErrorLength];
}
