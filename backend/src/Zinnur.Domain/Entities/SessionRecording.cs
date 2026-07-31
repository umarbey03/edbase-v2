using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARS YOZUVI (LiveKit Egress → obyekt ombori)
/// ════════════════════════════════════════════════════════════════════════
///
/// Bitta yozuv urinishi = bitta qator. Holat mashinasi va uning qoidalari
/// SHU YERDA — servis qatlamida takrorlanmaydi (loyihaning umumiy uslubi:
/// <see cref="LiveSession"/>, <see cref="Attendance"/>).
///
/// ── NIMA UCHUN ALOHIDA JADVAL, `LiveSession` USTUNI EMAS ────────────────
///
/// Eski tizimda yozuv `lessons` jadvalining ustunlari edi
/// (`recording_url`, `egress_id`, `recording_error`). Uchta oqibati bor edi:
///
///   1) BITTA DARSGA BITTA YOZUV. Birinchi urinish yiqilib, ikkinchisi
///      ishlaganda eski qiymat USTIDAN yozilardi — ya'ni "nima bo'lgani"
///      tarixi yo'q edi. Egress uzilib qayta boshlansa esa birinchi fayl
///      omborda yetim qolardi va uni hech kim topa olmasdi.
///   2) HOLAT YO'Q EDI: `recording_url IS NULL` ham "hali yozilmoqda", ham
///      "umuman boshlanmadi", ham "xato" degani edi.
///   3) DARS QATORI HAR WEBHOOK'DA YANGILANARDI — ya'ni jonli dars uchun
///      eng issiq jadval tashqi xizmat hodisalari tufayli qulflanardi.
///
/// Bu yerda esa har urinish o'z qatorida, dars qatoriga UMUMAN tegilmaydi.
///
/// ⚠️ <c>LiveSession.RecordingUrl</c> ustuni ATAYLAB TEGILMADI: u eski
/// modeldan qolgan va bu bosqichda ISHLATILMAYDI (nomida "Url" bo'lsa-da
/// unda hech qachon URL turmasligi kerak). Uni o'chirish alohida
/// migratsiya va koordinator qarori.
///
/// ── OBYEKT KALITI, TO'LIQ URL EMAS ──────────────────────────────────────
///
/// <see cref="ObjectKey"/> — ombordagi kalit (<c>recordings/2026-07/…mp4</c>).
/// Presigned havola HAR so'rovda yangidan imzolanadi va BAZAGA YOZILMAYDI:
/// u muddatli, bazaga tushsa bir soatdan keyin "linkim ishlamayapti"
/// muammosi boshlanardi (aynan shu sabab <c>SubmissionFile</c> da ham
/// yozilgan).
/// </summary>
public class SessionRecording : BaseEntity
{
    /// <summary>
    /// Egress'ga yuborilgan xato matnining bazadagi chegarasi.
    /// Uzun javob (S3 XML yoki twirp stack) TO'LIQ saqlanmaydi — u LOGDA
    /// bo'ladi; bu yerda faqat xodimga ko'rinadigan qisqa sabab.
    /// </summary>
    public const int MaxErrorLength = 500;

    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }

    /// <summary>
    /// Yozuvni kim so'ragan (dars hosti). <c>null</c> — fon vazifasi
    /// tiklagan holat.
    ///
    /// ★ NIMA UCHUN SAQLANADI: yozuv — ishtirokchilar roziligiga tegadigan
    /// amal. "Kim yozib olishga qaror qildi" degan savolga javob bo'lishi
    /// SHART. Eski tizimda yozuv jimgina, hech kimning qaroriga
    /// bog'lanmagan holda boshlanardi.
    /// </summary>
    public long? RequestedBy { get; set; }

    public RecordingStatus Status { get; set; } = RecordingStatus.Requested;

    /// <summary>
    /// LiveKit bergan egress identifikatori (<c>EG_…</c>). Webhook AYNAN
    /// shu qiymat bo'yicha qatorni topadi, shuning uchun u UNIKAL.
    /// </summary>
    public string? EgressId { get; set; }

    /// <summary>
    /// Ombordagi kalit. Qator yaratilganda BIZ tanlaymiz (Egress'ga aynan
    /// shu yo'l beriladi), yozuv tugaganda esa Egress qaytargan haqiqiy
    /// nom bilan tasdiqlanadi.
    /// </summary>
    public required string ObjectKey { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>
    /// VIDEONING haqiqiy uzunligi (sekund) — dars sessiyasining uzunligi
    /// EMAS.
    ///
    /// ★ FARQI MUHIM: eski tizim davomiylikni <c>actual_end - actual_start</c>
    /// dan hisoblardi. Yozuv esa darsdan kechroq boshlanishi yoki erta
    /// uzilishi mumkin — natijada ro'yxatda "80 daqiqa" yozilib, ochilganda
    /// 12 daqiqalik video chiqardi.
    /// </summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Yozuv HAQIQATAN boshlangan payt (Egress hodisasidan).</summary>
    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Egress'ni boshlashga necha marta urinilgan (watchdog cheklovi).</summary>
    public int Attempts { get; set; }

    /// <summary>Oxirgi urinish payti — watchdog ikki urinish orasida kutadi.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// To'xtatish so'rovi yuborilgan payt. Watchdog <c>StopEgress</c> ni
    /// har yurishda qayta yubormasligi uchun kerak: takroriy to'xtatish
    /// LiveKit'da xato beradi va log'ni bekorga to'ldirardi.
    /// </summary>
    public DateTimeOffset? StopRequestedAt { get; set; }

    /// <summary>Nima uchun chiqmagani — XODIM uchun qisqa sabab.</summary>
    public string? Error { get; set; }

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>Ko'rish mumkinmi (fayl omborda va kaliti ma'lum).</summary>
    public bool IsPlayable =>
        Status == RecordingStatus.Completed && !string.IsNullOrWhiteSpace(ObjectKey);

    /// <summary>Yakuniy holatmi — bunga qayta tegilmaydi.</summary>
    public bool IsFinished =>
        Status is RecordingStatus.Completed or RecordingStatus.Failed;

    /// <summary>Hali kutilyaptimi (watchdog nazoratidagi holat).</summary>
    public bool IsPending =>
        Status is RecordingStatus.Requested or RecordingStatus.Starting;

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Yangi urinish boshlanishini belgilaydi (Egress'ga murojaatdan OLDIN).
    /// </summary>
    public void BeginAttempt(DateTimeOffset now)
    {
        if (IsFinished)
            throw new DomainException("Yakunlangan yozuvni qayta boshlab bo'lmaydi.");

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
    /// Yozuv haqiqatan boshlandi (<c>egress_started</c>).
    /// IDEMPOTENT: LiveKit hodisani qayta yuborishi mumkin.
    /// </summary>
    public void MarkActive(DateTimeOffset startedAt, DateTimeOffset now)
    {
        if (IsFinished) return;

        Status = RecordingStatus.Active;
        StartedAt ??= startedAt;        // BIRINCHI boshlanish payti qoladi
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Fayl tayyor. IDEMPOTENT va QAYTMAS: tugallangan yozuv boshqa hech
    /// qanday hodisa bilan orqaga qaytmaydi — fayl allaqachon omborda va
    /// uni o'quvchilar ochayotgan bo'lishi mumkin.
    /// </summary>
    /// <param name="objectKey">
    /// Egress qaytargan haqiqiy kalit. Bo'sh bo'lsa biz tanlagan kalit
    /// qoladi (biz `filepath` ni shablonsiz beramiz, ya'ni ular mos keladi).
    /// </param>
    public void MarkCompleted(
        string? objectKey,
        long? sizeBytes,
        int? durationSeconds,
        DateTimeOffset endedAt,
        DateTimeOffset now)
    {
        if (Status == RecordingStatus.Completed) return;

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
    /// Yozuv chiqmadi. TUGALLANGAN yozuvga TEGMAYDI (kech kelgan yoki
    /// takroriy "xato" hodisasi tayyor faylni yo'q qilib qo'ymasin).
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
    /// Urinish yiqildi, lekin YAKUNIY xato emas — watchdog qayta uradi.
    /// Holat <see cref="RecordingStatus.Requested"/> bo'lib qoladi.
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
