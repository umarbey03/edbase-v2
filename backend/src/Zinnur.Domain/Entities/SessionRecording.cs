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

    /// <summary>
    /// Tungi yig'ish necha marta HAQIQATAN yiqilgach voz kechiladi.
    /// Izoh: <see cref="CompositionAttempts"/>.
    /// </summary>
    public const int MaxCompositionAttempts = 3;

    /// <summary>
    /// Tungi oyna necha marta yetmagach voz kechiladi. Chegara ATAYLAB
    /// katta: bu yerga yetish "vazifa buzuq" emas, "jadval sig'maydi"
    /// degani. Izoh: <see cref="CompositionInterruptions"/>.
    /// </summary>
    public const int MaxCompositionInterruptions = 10;

    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }

    /// <summary>
    /// Yozuvni kim so'ragan. Ikki ma'no bor va ular TENG HUQUQLI:
    ///
    ///   • <c>userId</c> — dars HOSTI tugmani bosgan (qo'lda boshlash);
    ///   • <c>null</c> — TIZIM boshlagan. Bu ikki holni qamraydi:
    ///     guruhning <c>Group.RecordEnabled</c> kaliti bo'yicha avtomatik
    ///     navbatga tushgan yozuv (2026-08-13) va fon vazifasi tiklagan
    ///     holat.
    ///
    /// ★ NIMA UCHUN SAQLANADI: yozuv — ishtirokchilar roziligiga tegadigan
    /// amal. "Kim yozib olishga qaror qildi" degan savolga javob bo'lishi
    /// SHART. Eski tizimda yozuv jimgina, hech kimning qaroriga
    /// bog'lanmagan holda boshlanardi.
    ///
    /// ★ <c>null</c> BU SAVOLNI JAVOBSIZ QOLDIRMAYDI, JAVOBNI BOSHQA
    /// JOYGA KO'CHIRADI: qaror manbai — guruh sozlamasi va uni yoqqan
    /// o'quv bo'limi xodimi (u <c>AppSettings</c> auditida emas, guruh
    /// tahriri orqali ko'rinadi). Ya'ni "hech kim qaror qilmagan" holati
    /// hamon mavjud emas.
    ///
    /// ⚠️ MAYDON ALLAQACHON <c>nullable</c> EDI — avtomatik yozuv uchun
    /// MIGRATSIYA KERAK BO'LMADI.
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

    // ---------------------------------------------------------------- R5: ko'rinish

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// BU YOZUV O'QUVCHIGA KO'RINADIMI (talab R5)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Loyiha egasi: *"dars yozuvlari qismi student uchun dynamic bo'lishi
    /// kerak, o'quv bo'limi va teacher tarafidan manage qilinadi, ko'rinish
    /// yoki ko'rinmasligi"*.
    ///
    /// ── NIMA UCHUN STANDART QIYMAT <c>false</c> (2026-08-15 dan) ─────────
    ///
    /// ★ ILGARI STANDART <c>true</c> EDI, chunki o'quv bo'limida "hammasini
    /// ochish" vositasi yo'q edi — <c>false</c> tanlansa ular yuzlab yangi
    /// yozuvni qo'lda ochishga majbur bo'lardi. Loyiha egasi endi buning
    /// aksini so'radi (2026-08-15: *"dars yozuvlari default holatda ko'rish
    /// unable bo'lishi kerak"*) va AYNI shu talab bilan birga
    /// `ManageRecordingsPage`ga "Hammasini ochish"/"Hammasini yopish"
    /// tugmasi qo'shildi (`RecordingBoard.vue`) — ya'ni eski to'siq
    /// (qo'lda ochish og'irligi) endi yo'q, standartni almashtirish
    /// xavfsiz.
    ///
    /// 🔴 BU FAQAT KELAJAKDAGI YOZUVLARGA TEGADI, MAVJUDLARGA EMAS:
    /// standart qiymat faqat YANGI qator INSERT qilinganda (qiymat
    /// ko'rsatilmasa) ishlaydi — migratsiya bazadagi USTUN DEFAULT'ini
    /// almashtiradi, mavjud qatorlarning saqlangan qiymatini QAYTA
    /// YOZMAYDI. Ya'ni bugungача ochilgan yozuvlar o'quvchiga ko'rinishda
    /// qoladi; faqat ENDI yakunlanadigan yozuvlar yashirin holda boshlanadi
    /// va ularni o'quv bo'limi/ustoz ochiq ravishda ochishi kerak bo'ladi
    /// (bitta-bitta `ShowToStudents()` orqali yoki bulk tugma bilan).
    ///
    /// ★ TALABNING O'ZI HAM SHUNI AYTADI: unda "yozuvlar yopiq bo'lsin"
    /// emas, "BOSHQARILADIGAN bo'lsin" deyilgan — bu bayroq hamon ORQAGA
    /// QAYTARISH (yopish) VA OLDINGA OCHISH ikkalasining ham vositasi,
    /// faqat boshlang'ich holat teskari bo'ldi.
    ///
    /// ★ AGAR kelajakda "avval tekshirilsin, keyin ochilsin" siyosati BUTUN
    /// bo'lim darajasida kerak bo'lsa, uni MIGRATSIYASIZ yoqish mumkin:
    /// global <c>recordings.visible_to_students</c> sozlamasini o'chirib
    /// qo'yish butun bo'limni bir bosishda yopadi (bu bayroqdan MUSTAQIL
    /// qatlam, pastga qarang).
    ///
    /// ⚠️ BU BAYROQ YAKKA O'ZI YETARLI EMAS. Amaldagi ko'rinish UCHTA
    /// kalitning MANTIQIY KO'PAYTMASI (eng qattig'i yutadi):
    ///   global <c>recordings.visible_to_students</c>
    ///   × <c>Group.RecordingsVisibleToStudents</c>
    ///   × shu bayroq × <c>Status == Completed</c>.
    /// Sabab va ustunlik qoidasi <c>IRecordingService</c> izohida.
    /// </summary>
    public bool IsVisibleToStudents { get; set; }

    /// <summary>
    /// Ko'rinishni OXIRGI marta kim o'zgartirgani.
    ///
    /// ★ NIMA UCHUN SAQLANADI — BU AUDIT EMAS, QOIDA MANBAI. Ustoz o'quv
    /// bo'limi YOPGAN yozuvni qayta ocha olmasligi kerak (aks holda
    /// "muammo bor" deb olib qo'yilgan dars ustozning bir bosishi bilan
    /// qaytib chiqardi va R29 ning butun ma'nosi yo'qolardi). Buni
    /// aniqlashning yagona yo'li — oxirgi qaror KIMNIKI ekanini bilish.
    ///
    /// <c>null</c> — hech kim tegmagan (tug'ma holat).
    ///
    /// ⚠️ ALOHIDA AUDIT JADVALI ATAYLAB QILINMADI (<c>AttendanceAudit</c>
    /// dan farqli): u yerda "nimadan-nimaga" ma'noli qiymat, bu yerda esa
    /// "dan" har doim "ga" ning inkori — jadval faqat vaqt va odamni
    /// takrorlagan bo'lardi.
    /// </summary>
    public long? VisibilityChangedById { get; set; }

    /// <summary>Ko'rinish oxirgi marta qachon o'zgartirilgani.</summary>
    public DateTimeOffset? VisibilityChangedAt { get; set; }

    /* ═══════════════════════════════════════════════════════════════════
       YOZUV YO'LI VA TUNGI YIG'ISH (yozuv quvuri v2)

       Alohida, uzluksiz blok — bu faylga bir necha tarmoq tegmoqda va
       mavjud bo'limlar orasiga qistirilgan qator merge paytida
       to'qnashuv beradi (`ApplicationDbContext` dagi AYNI qoida).

       🔴 BARCHA USTUNLAR QO'SHIMCHA. Mavjud birorta ustunning turi,
       nullligi yoki ma'nosi O'ZGARMADI va ishlab chiqarishdagi qatorlar
       QAYTA YOZILMADI: `Pipeline` ning standarti `0` = bugungi yo'l,
       ya'ni har bir eski qator hech qanday ma'lumot migratsiyasiz
       TO'G'RI qoladi.
       ═══════════════════════════════════════════════════════════════════ */

    /// <summary>
    /// Bu qatorni QAYSI yo'l yaratdi — sabab va qoidalar
    /// <see cref="RecordingPipeline"/> izohida.
    ///
    /// ⚠️ Fon vazifalari ishni AYNAN shu ustun bo'yicha bo'lishadi. Eski
    /// watchdog yangi yo'ldagi qatorga tegsa, u hali MAVJUD BO'LMAGAN
    /// yakuniy faylni qidirib, darsdan 10 daqiqa keyin butun yozuvni
    /// <c>Failed</c> qilib qo'yardi.
    /// </summary>
    public RecordingPipeline Pipeline { get; set; } = RecordingPipeline.RoomComposite;

    /// <summary>
    /// Tungi yig'ish qayerda turibdi.
    ///
    /// 🔴 <c>NULL</c> — <see cref="RecordingPipeline.RoomComposite"/>
    /// qatorlari uchun YAGONA to'g'ri qiymat: u yerda yig'ish bosqichi
    /// umuman yo'q. To'liq sabab <see cref="RecordingCompositionStatus"/>
    /// izohida.
    /// </summary>
    public RecordingCompositionStatus? CompositionStatus { get; set; }

    /// <summary>
    /// HAQIQIY nosozliklar soni: ffmpeg noldan farqli kod bilan chiqdi,
    /// tekshiruv o'tmadi, yuklash yiqildi. Chegara
    /// <see cref="MaxCompositionAttempts"/>.
    /// </summary>
    public int CompositionAttempts { get; set; }

    /// <summary>
    /// TUNGI OYNA tugagani uchun uzilishlar soni. Chegara
    /// <see cref="MaxCompositionInterruptions"/>.
    ///
    /// ★ NIMA UCHUN IKKINCHI HISOBLAGICH, BITTASI EMAS: uzilish —
    /// nosozlik EMAS. U "navbat kechadan uzun bo'ldi" degani. Ikkalasini
    /// bitta hisoblagichga qo'shsak, mutlaqo sog'lom yozuv beshta band
    /// kechadan keyin <c>Failed</c> bo'lib qolardi. Ajratilgani esa
    /// shuni beradi: haqiqatan qulaydigan ish 3 urinishdan keyin o'ladi,
    /// navbatda yutqazayotgani esa faqat 10 kechadan keyin — o'shanda
    /// muammo vazifada emas, JADVALDA.
    /// </summary>
    public int CompositionInterruptions { get; set; }

    /// <summary>Joriy (yoki oxirgi) <c>Running</c> qachon boshlangani.</summary>
    public DateTimeOffset? CompositionStartedAt { get; set; }

    /// <summary>Yig'ish yakunlangan payt (tayyor yoki yiqilgan).</summary>
    public DateTimeOffset? CompositionFinishedAt { get; set; }

    /// <summary>
    /// Ishchining IJARA muddati: shu paytgacha qatorni faqat uni
    /// egallagan ishchi tegadi.
    ///
    /// ★ NIMA UCHUN IJARA USTUNI, <c>IJobLock</c> EMAS: Postgres advisory
    /// lock butun ish davomida ALOHIDA ULANISHNI ushlab turadi. ffmpeg 90
    /// daqiqa ishlaydi va shu vaqt ichida tarmoqning bir lahzalik uzilishi
    /// qulfni yo'qotardi — natijada IKKI kodlovchi bitta kalitga yozardi.
    /// Ijara esa oddiy ustun: u yo'qolmaydi, shunchaki eskiradi.
    ///
    /// ⚠️ MUDDATI O'TGAN ijara "ish ketyapti" degani EMAS, "ishchi
    /// qulagan" degani — batafsil <see cref="TryClaimComposition"/> da.
    /// </summary>
    public DateTimeOffset? CompositionLeaseUntil { get; set; }

    /// <summary>
    /// Oxirgi yig'ish nosozligi yoki uzilishining sababi — XODIM uchun,
    /// o'zbekcha.
    ///
    /// ⚠️ <see cref="Error"/> DAN AYRIM: u foydalanuvchiga ko'rinadigan
    /// YAKUNIY sabab ("yozuv nega yo'q"), bu esa hali davom etayotgan
    /// jarayonning oxirgi holati ("tungi oyna tugadi"). Ularni bitta
    /// ustunga qo'shsak, vaqtinchalik uzilish tayyor bo'ladigan yozuvni
    /// yiqilgandek ko'rsatardi.
    /// </summary>
    public string? CompositionError { get; set; }

    /// <summary>
    /// Xom bo'laklar ombordan o'chirilgan payt. <c>NULL</c> — hali
    /// o'chirilmagan (yoki o'chirish yiqilgan va keyingi kecha qayta
    /// uriniladi).
    /// </summary>
    public DateTimeOffset? RawPurgedAt { get; set; }

    /// <summary>
    /// Shu yozuvning XOM bo'laklari — izoh <see cref="RecordingTrack"/> da.
    /// <see cref="RecordingPipeline.RoomComposite"/> qatorlarida DOIM
    /// bo'sh.
    /// </summary>
    public ICollection<RecordingTrack> Tracks { get; set; } = [];

    /* ═══════════════════════════════════ /yozuv yo'li va tungi yig'ish ═══ */

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

    /// <summary>
    /// Yana bir marta yig'ishga urinish mumkinmi.
    ///
    /// ⚠️ HISOBLAGICH OSHIRILGANDAN KEYIN so'raladi: nosozlik yuz berganda
    /// avval <see cref="ReleaseCompositionForRetry"/> chaqiriladi, keyin
    /// shu xossa tekshiriladi va <c>false</c> bo'lsa
    /// <see cref="MarkCompositionFailed"/> bilan yopiladi. Aks tartibda
    /// chegara bittaga adashadi.
    /// </summary>
    public bool CanRetryComposition => CompositionAttempts < MaxCompositionAttempts;

    /// <summary>
    /// Keyingi kechada davom ettirish mumkinmi (uzilishlar chegarasi).
    /// Tartib qoidasi <see cref="CanRetryComposition"/> bilan AYNI:
    /// <see cref="InterruptComposition"/> DAN KEYIN so'raladi.
    /// </summary>
    public bool CanResumeComposition =>
        CompositionInterruptions < MaxCompositionInterruptions;

    /// <summary>
    /// Bu qatorga tungi yig'ish umuman tegadimi. Eski yo'l qatorlarida
    /// yig'ish bosqichi YO'Q, shuning uchun uning holat metodlari ularga
    /// TEGMAYDI (jimgina, chunki ular umumiy vazifalardan chaqiriladi).
    /// </summary>
    private bool IsComposable => Pipeline == RecordingPipeline.TrackComposition;

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

    /// <summary>
    /// Yozuvni o'quvchilarga OCHADI (talab R5).
    ///
    /// ★ HOLAT DARVOZASI — <c>Test.Publish()</c> DAGI AYNI NAQSH: u bo'sh
    /// testni e'lon qilishni rad etadi, bu esa TAYYOR BO'LMAGAN yozuvni
    /// ochishni. Sabab bir xil: aks holda ro'yxatda bosilganda doim xato
    /// beradigan qator paydo bo'lardi va o'quvchi uni tizim nosozligi deb
    /// o'ylardi.
    ///
    /// ⚠️ "Ochish" — RUXSAT, KAFOLAT EMAS: guruh yoki global kalit yopiq
    /// bo'lsa yozuv baribir ko'rinmaydi (izoh: <see cref="IsVisibleToStudents"/>).
    /// </summary>
    public void ShowToStudents(long actorId, DateTimeOffset now)
    {
        if (!IsPlayable)
        {
            throw new DomainException(
                "Tayyor bo'lmagan yozuvni o'quvchilarga ochib bo'lmaydi.");
        }

        IsVisibleToStudents = true;
        VisibilityChangedById = actorId;
        VisibilityChangedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Yozuvni o'quvchilardan YASHIRADI (talab R5).
    ///
    /// 🔴 HOLAT DARVOZASI ATAYLAB YO'Q — <see cref="ShowToStudents"/> dan
    /// FARQLI. Yashirish har qanday holatda ishlashi SHART: u
    /// <c>IRecordingService.StopAsync</c> kabi zaxira chiqish, ya'ni "buni
    /// ko'rsatma" deyishning yo'li hech qachon yopiq bo'lmasligi kerak. Hali
    /// yozilayotgan darsni oldindan yopib qo'yish ham to'g'ri amal —
    /// fayl tayyor bo'lgan lahzada u chiqib ketmaydi.
    /// </summary>
    public void HideFromStudents(long actorId, DateTimeOffset now)
    {
        IsVisibleToStudents = false;
        VisibilityChangedById = actorId;
        VisibilityChangedAt = now;
        UpdatedAt = now;
    }

    /* ═══════════════════════════════════════════════════════════════════
       TUNGI YIG'ISHNING HOLAT MASHINASI (yozuv quvuri v2)

       ★ NIMA UCHUN SHU YERDA, SERVISDA EMAS: yig'ish holatini UCHTA
       mustaqil manba o'zgartiradi — moslashtiruvchi vazifa, kompozitor
       ishchisi va bekor qilish signali (tungi oyna tugadi). Egress
       holatida bo'lgani kabi, qoida servisda bo'lsa uchta yo'lning
       birida albatta buziladi.

       ★ BARCHASI IDEMPOTENT va YAKUNLANGAN yozuvga TEGMAYDI. Bu shunchaki
       ehtiyot emas: kompozitor ishchisining ijara muddati o'tib ketsa,
       ikkinchi ishchi AYNI qatorni oladi va birinchisi keyinroq o'z
       natijasini yozib qo'yishi mumkin.

       ⚠️ ESKI YO'L (`RoomComposite`) QATORLARIGA BU METODLAR TEGMAYDI —
       ular jimgina qaytadi. Yagona istisno `BeginComposition`, u
       DomainException tashlaydi: noto'g'ri yo'lda yig'ishni BOSHLASH
       dasturchi xatosi, jim qoldiriladigan hol emas.
       ═══════════════════════════════════════════════════════════════════ */

    /// <summary>
    /// Yig'ish jarayonini OCHADI: qator endi xom bo'laklarni yig'moqda.
    /// Yangi yo'l qatori YARATILGANDA chaqiriladi.
    /// </summary>
    /// <exception cref="DomainException">
    /// Qator <see cref="RecordingPipeline.RoomComposite"/> bo'lsa. Eski
    /// yo'lda yig'ish bosqichi yo'q va u yerda bo'sh bo'lmagan
    /// <see cref="CompositionStatus"/> — XATO, "hali boshlanmagan" emas.
    /// </exception>
    public void BeginComposition(DateTimeOffset now)
    {
        if (!IsComposable)
        {
            throw new DomainException(
                "Eski yo'l bilan olinayotgan yozuvda yig'ish bosqichi yo'q.");
        }

        if (IsFinished) return;

        CompositionStatus ??= RecordingCompositionStatus.Collecting;
        UpdatedAt = now;
    }

    /// <summary>
    /// Xom bo'laklar to'liq yig'ildi (hammasi yakuniy holatda) — qator
    /// TUNGI NAVBATGA tushadi.
    ///
    /// ⚠️ FAQAT <see cref="RecordingCompositionStatus.Collecting"/> dan
    /// o'tadi. Allaqachon navbatdagi yoki ishlanayotgan qatorni orqaga
    /// surib yuborish — takroriy webhook yoki kechikkan vazifa qo'lidagi
    /// eng oson buzish usuli.
    /// </summary>
    public void MarkRawCollected(DateTimeOffset now)
    {
        if (!IsComposable || IsFinished) return;
        if (CompositionStatus != RecordingCompositionStatus.Collecting) return;

        CompositionStatus = RecordingCompositionStatus.Queued;
        UpdatedAt = now;
    }

    /// <summary>
    /// Qatorni EGALLASHGA urinadi: navbatdagi yoki IJARASI O'TGAN qator
    /// <see cref="RecordingCompositionStatus.Running"/> ga o'tadi.
    ///
    /// 🔴 IJARASI O'TGAN QATORNI EGALLASH — QULAGAN ISHCHIDAN QOLGAN
    /// ISHNI OLISH, ya'ni bu urinish HAQIQIY nosozlik hisoblanadi va
    /// <see cref="CompositionAttempts"/> oshadi. Aks holda har safar
    /// o'sha joyda qulaydigan ish abadiy aylanardi va uni hech kim
    /// sezmasdi.
    ///
    /// ⚠️ YARIM QOLGAN ffmpeg NATIJASI DAVOM ETTIRILMAYDI — ish
    /// BOSHIDAN boshlanadi. Yarim yozilgan mp4 da <c>moov</c> atomi yo'q;
    /// unga qo'shib yozilgan fayl 3 soniya o'ynab to'xtaydi, ya'ni
    /// faylsizlikdan ham yomon.
    ///
    /// ★ BU METOD MUTLAQ MUSTASNOLIKNI O'ZI KAFOLATLAMAYDI: ikki ishchi
    /// bir qatorni olmasligini BAZA ta'minlaydi (bitta <c>UPDATE …
    /// WHERE … FOR UPDATE SKIP LOCKED</c>). Bu yerda o'sha o'tishning
    /// QOIDASI yozilgan — nima uchun mumkin va nima o'zgaradi.
    ///
    /// ⚠️ IJARASI BO'SH (<c>NULL</c>) <c>Running</c> QATOR EGALLANMAYDI.
    /// Bunday qator normal yo'lda paydo bo'lmaydi (egallash ijarani
    /// DOIM qo'yadi), lekin agar paydo bo'lsa — "muddati o'tgan" deb
    /// hisoblash ikki kodlovchini bitta kalitga yozdirib yuborishi
    /// mumkin. Osilib qolgan qator ro'yxatda KO'RINADI va tuzatiladi;
    /// ustma-ust yozilgan mp4 esa jimgina buziladi.
    /// </summary>
    /// <returns>Qator egallanganmi.</returns>
    public bool TryClaimComposition(DateTimeOffset now, TimeSpan lease)
    {
        if (!IsComposable || IsFinished) return false;

        var takeover =
            CompositionStatus == RecordingCompositionStatus.Running &&
            CompositionLeaseUntil is { } until && until <= now;

        if (CompositionStatus != RecordingCompositionStatus.Queued && !takeover)
            return false;

        if (takeover)
        {
            CompositionAttempts++;
            CompositionError = "Oldingi yig'ish urinishi uzilib qoldi — boshidan boshlanmoqda.";
        }

        CompositionStatus = RecordingCompositionStatus.Running;
        CompositionStartedAt = now;
        CompositionLeaseUntil = now + lease;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Ijarani uzaytiradi — ffmpeg ishlayotganda muntazam chaqiriladi.
    /// EGALLAMAGAN qatorga (navbatdagi yoki yakunlangan) TEGMAYDI.
    /// </summary>
    public void RenewCompositionLease(DateTimeOffset now, TimeSpan lease)
    {
        if (!IsComposable || IsFinished) return;
        if (CompositionStatus != RecordingCompositionStatus.Running) return;

        CompositionLeaseUntil = now + lease;
        UpdatedAt = now;
    }

    /// <summary>
    /// Yig'ish HAQIQATAN yiqildi (ffmpeg xatosi, tekshiruv yoki yuklash
    /// nosozligi) — qator navbatga QAYTADI va urinish sanaladi.
    ///
    /// ⚠️ CHEGARANI BU METOD QO'LLAMAYDI: chaqiruvchi shundan keyin
    /// <see cref="CanRetryComposition"/> ni tekshiradi va <c>false</c>
    /// bo'lsa <see cref="MarkCompositionFailed"/> bilan yopadi. Ikki
    /// qadamga bo'lingani ATAYLAB — yakunlash sababi (o'zbekcha matn)
    /// har xil bo'ladi va uni bu yerda taxmin qilib bo'lmaydi.
    /// </summary>
    public void ReleaseCompositionForRetry(string reason, DateTimeOffset now)
    {
        if (!IsComposable || IsFinished) return;

        CompositionAttempts++;
        CompositionStatus = RecordingCompositionStatus.Queued;
        CompositionError = Trim(reason);
        CompositionLeaseUntil = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Tungi oyna tugadi (yoki konteyner to'xtatilmoqda) — ish
    /// KEYINGI KECHAGA qoldiriladi.
    ///
    /// 🔴 <see cref="CompositionAttempts"/> OSHMAYDI. Uzilish nosozlik
    /// emas; ikkalasini bitta hisoblagichga qo'shish sog'lom yozuvni
    /// beshta band kechadan keyin o'ldirardi
    /// (<see cref="CompositionInterruptions"/> izohi).
    /// </summary>
    public void InterruptComposition(DateTimeOffset now)
    {
        if (!IsComposable || IsFinished) return;

        CompositionInterruptions++;
        CompositionStatus = RecordingCompositionStatus.Queued;
        CompositionError = "Tungi oyna tugadi — keyingi kechada davom etadi.";
        CompositionLeaseUntil = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Yig'ishdan voz kechildi — YAKUNIY. Yozuvning o'zi ham
    /// <see cref="RecordingStatus.Failed"/> bo'ladi, chunki fayl
    /// bo'lmaydi.
    ///
    /// ★ SABAB IKKI JOYGA YOZILADI va bu takrorlash emas:
    /// <see cref="CompositionError"/> — jarayonning oxirgi holati,
    /// <see cref="Error"/> — xodim ro'yxatda ko'radigan javob.
    /// </summary>
    public void MarkCompositionFailed(string reason, DateTimeOffset now)
    {
        if (!IsComposable || Status == RecordingStatus.Completed) return;

        CompositionStatus = RecordingCompositionStatus.Failed;
        CompositionError = Trim(reason);
        CompositionFinishedAt ??= now;
        CompositionLeaseUntil = null;
        MarkFailed(reason, now);
    }

    /// <summary>
    /// Yakuniy mp4 omborga tushdi va tekshirildi — yozuv TAYYOR.
    ///
    /// ★ KALIT O'ZGARMAYDI: yig'ish qator ALLAQACHON saqlab turgan
    /// <see cref="ObjectKey"/> ga yozadi, yangi kalit o'ylab topmaydi.
    /// Shuning uchun bu yerda <c>objectKey: null</c> beriladi.
    ///
    /// ★ <see cref="MarkCompleted"/> QAYTA YOZILMAYDI, QAYTA
    /// ISHLATILADI: u kech kelgan hodisalardan himoya, <c>Error</c> ni
    /// tozalash va vaqtlarni to'ldirish qoidalarini allaqachon
    /// bajaradi.
    /// </summary>
    public void MarkCompositionCompleted(
        long? sizeBytes,
        int? durationSeconds,
        DateTimeOffset endedAt,
        DateTimeOffset now)
    {
        if (!IsComposable || Status == RecordingStatus.Completed) return;

        CompositionStatus = RecordingCompositionStatus.Completed;
        CompositionFinishedAt ??= now;
        CompositionError = null;
        CompositionLeaseUntil = null;
        MarkCompleted(null, sizeBytes, durationSeconds, endedAt, now);
    }

    /// <summary>
    /// Xom bo'laklar ombordan o'chirildi.
    ///
    /// ⚠️ YAKUNLANGAN YOZUVDA CHAQIRILADI va shuning uchun
    /// <see cref="IsFinished"/> darvozasi ATAYLAB YO'Q — tozalash aynan
    /// muvaffaqiyatli yig'ishdan KEYIN bo'ladi.
    ///
    /// ★ O'chirish yiqilsa bu metod chaqirilmaydi va
    /// <see cref="RawPurgedAt"/> bo'sh qoladi — keyingi kecha qayta
    /// uriniladi. Yetim xom fayl PUL turadi, orqaga qaytarilgan sog'lom
    /// yozuv esa BUTUN DARSNI.
    /// </summary>
    public void MarkRawPurged(DateTimeOffset now)
    {
        RawPurgedAt ??= now;
        UpdatedAt = now;
    }

    /* ═════════════════════════════ /tungi yig'ishning holat mashinasi ═══ */

    /// <summary>Yana urinish mumkinmi.</summary>
    public bool CanRetry(int maxAttempts) =>
        Status == RecordingStatus.Requested && Attempts < maxAttempts;

    private static string Trim(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "Noma'lum xato."
            : reason.Length <= MaxErrorLength ? reason : reason[..MaxErrorLength];
}
