using Zinnur.Application.Recordings.Dtos;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARS YOZUVI USE-CASE'LARI
/// ════════════════════════════════════════════════════════════════════════
///
/// ┌───────────────────────────────────────────────────────────────────┐
/// │ ★★ QAROR (2026-08-13, loyiha egasi): YOZUV AVTOMATIK BOSHLANADI. │
/// │    Guruh kaliti — <c>Group.RecordEnabled</c>. Qo'lda boshlash/    │
/// │    to'xtatish tugmasi QOLADI, lekin endi u OVERRIDE.              │
/// └───────────────────────────────────────────────────────────────────┘
///
/// ═══════════════════════════════════════════════════════════════════════
/// ★★ BU QAROR OLDINGI QARORNI BEKOR QILADI
/// ═══════════════════════════════════════════════════════════════════════
///
/// 2026-07 da bu yerda TESKARI qaror yozilgan edi: *"yozuv QO'LDA
/// boshlanadi, avtomatik emas"*. U to'rt dalilga tayanardi. Qaror bekor
/// qilindi, lekin dalillar YO'Q QILINMADI — quyida har biri va unga
/// nima bo'lgani. Bekor qilingan qarorning sababini o'chirish eng yomon
/// variant bo'lardi: bir yildan keyin kimdir AYNI muhokamani noldan
/// boshlardi.
///
///  1) 🔴 ROZILIK VA JAVOBGARLIK — DALIL KUCHIDA QOLDI, JAVOB O'ZGARDI.
///     Yozuv ishtirokchilarga (izohda yozilganidek, *"ko'pincha
///     bolalarga"*) tegishli amal. Eski dalil shunday edi: tugma bosilsa
///     holat darsning o'zida ko'rinadi, ya'ni indikator TUZILISH bilan
///     hal bo'ladi.
///
///     ⚠️ Bu dalilning zaif joyi bor edi va u endi ochiq aytiladi: host
///     tugmasi HOST uchun indikator edi, O'QUVCHI uchun emas. O'quvchi
///     ekranida "yozib olinmoqda" degan hech narsa YO'Q edi — ya'ni
///     rozilik dalili amalda faqat yarim bajarilgan.
///
///     🔴 SHUNING UCHUN AVTOMATIK REJIMNING SHARTI: har bir ishtirokchi
///     (o'quvchi ham) jonli xonada YOZUV KETAYOTGANINI ko'radi. Buni
///     <see cref="GetLiveStatusAsync"/> ta'minlaydi va u HAQIQIY yozuv
///     holatiga ulanadi — "guruhda yozuv yoqilgan" degan sozlamaga emas.
///     Avtomatik rejim bu indikatorsiz JORIY ETILMAYDI: u qarorning
///     shartli qismi, keyinga qoldiriladigan yaxshilanish emas.
///
///     ★ JAVOBGARLIK YO'QOLMADI, KO'CHDI: <c>RequestedBy = null</c>
///     "tizim boshladi" degani, ya'ni qaror manbai — guruhning
///     <c>RecordEnabled</c> kaliti va uni qo'ygan o'quv bo'limi xodimi.
///     "Kim yozib olishga qaror qildi" savoli javobsiz qolmaydi, javob
///     shunchaki ikki xil bo'ladi.
///
///  2) 🔴 YOZUV NOSOZLIGI DARSNI TO'XTATMASLIGI SHART — DALIL TO'LIQ
///     KUCHIDA VA U ARXITEKTURANI BELGILADI. Egress alohida xizmat, u
///     sekin javob berishi yoki yo'q bo'lishi mumkin; dars boshlash esa
///     platformaning eng vaqt-tanqis amali.
///
///     Shuning uchun avtomatik rejim Egress'ni dars boshlash yo'liga
///     ULAMAYDI. Dars boshlanganda faqat NAVBAT qatori yoziladi
///     (<see cref="IAutoRecordingScheduler"/>), Egress bilan gaplashishni esa
///     watchdog qiladi. Ya'ni eski dalil rad etilmadi — u yangi
///     yechimning ASOSIY cheklovi bo'ldi.
///
///     ★ "UCH JOYDA BIR ISH" MUAMMOSI QAYTMADI. Eski tizim yozuvni dars
///     boshlashda, `room_started` webhook'ida VA watchdog'da — uch
///     bir-biridan bexabar joyda boshlardi. Bu yerda QAROR bitta joyda
///     (<c>LiveSessionService.StartAsync</c>), IJRO bitta joyda
///     (watchdog → <c>RecordingStarter</c>). Watchdog hamon o'zi yangi
///     yozuv IXTIRO QILMAYDI.
///
///  3) "HAR DARS YOZILISHI SHART EMAS" — DALIL O'RINLI, LEKIN U
///     AVTOMATIKLIKKA EMAS, QAMROVGA QARSHI EDI. Javob:
///     <c>Group.RecordEnabled</c> guruh darajasida ajratadi, qo'lda
///     to'xtatish tugmasi esa BITTA dars darajasida (qayta o'tilgan dars,
///     konsultatsiya, texnik sinov — host to'xtatadi).
///
///  4) EGRESS — QIMMAT RESURS. ★ BU DALIL QARORDAN TUSHDI: loyiha egasi
///     *"biz uni o'zimizni serverda emas cloudflareda saqlaymiz, shuning
///     uchun bu muammo emas"* dedi. Ya'ni saqlash hajmi endi cheklov
///     emas. ⚠️ TRANSKODLASH VA TARMOQ narxi baribir qoladi (u
///     Cloudflare'niki emas, LiveKit'niki) — shuning uchun
///     <c>RecordingWatchdogSettings.MaxDuration</c> chegarasi
///     O'ZGARTIRILMADI: unutilgan xona kunlab yozib turmasin.
///
/// ⚠️ ESKI QARORDAGI "USTOZ UNUTADI" XAVFI ENDI YO'Q — aynan shu xavf
/// bekor qilinishning asosiy sababi edi. Uning o'rniga TESKARI xavf
/// paydo bo'ldi: "yozilmasligi kerak bo'lgan dars yozilib qoldi". U
/// to'xtatish tugmasi bilan qoplanadi va bu SEZILADIGAN xato: indikator
/// hammaga ko'rinib turadi, ya'ni xatoni xona ichidagi har kim ko'radi.
/// Unutilgan yozuv esa hech kimga ko'rinmasdi.
///
/// ── RUXSAT ──────────────────────────────────────────────────────────────
///
/// Ruxsat qoidasi BU YERDA TAKRORLANMAYDI: servis
/// <c>ILiveSessionService</c> ni chaqiradi va u darsga kirish huquqini
/// allaqachon tekshiradi (a'zolik, faol guruh, faol profil, host'lik).
/// Ikkinchi nusxa yozilsa, vaqt o'tib ular ajralib ketardi va bir yo'lda
/// zaifroq tekshiruv qolardi.
/// </summary>
public interface IRecordingService
{
    /// <summary>
    /// Yozuvni QO'LDA boshlaydi (faqat dars HOSTI, faqat JONLI dars).
    ///
    /// ★ AVTOMATIK REJIMDAN KEYIN BU — OVERRIDE, ASOSIY YO'L EMAS. U
    /// UCH holatda kerak bo'ladi va shuning uchun saqlab qolindi:
    ///   1) guruhda <c>RecordEnabled</c> O'CHIQ, lekin AYNAN shu darsni
    ///      yozib olish kerak (ochiq dars, o'rnini bosuvchi ustoz);
    ///   2) dars boshlangan paytda Egress/ombor SOZLANMAGAN edi, ya'ni
    ///      avtomatik navbat qator qo'shmadi (sabab:
    ///      <see cref="IAutoRecordingScheduler"/>). Administrator paneldan
    ///      sozlamani tuzatgach, host darsni QAYTA BOSHLAMASDAN yozuvni
    ///      yoqa oladi;
    ///   3) host <see cref="StopAsync"/> bilan to'xtatgan yozuvni qaytadan
    ///      boshlamoqchi (tanaffusdan keyin).
    ///
    /// IDEMPOTENT: yozuv allaqachon ketayotgan bo'lsa AYNI qator qaytadi,
    /// ikkinchi egress boshlanmaydi. ⚠️ Ya'ni avtomatik navbat qo'ygan
    /// qator ustiga bosilgan tugma IKKINCHI yozuv YARATMAYDI.
    ///
    /// ⚠️ Egress javob bermasa metod ISTISNO TASHLAMAYDI: qator
    /// <c>Requested</c> holatida saqlanadi, xato matni <c>Error</c> da
    /// qaytadi va watchdog qayta uradi. Ustoz darsni odatdagidek davom
    /// ettiradi.
    /// </summary>
    Task<RecordingDto> StartAsync(long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Yozuvni to'xtatadi (faqat host). Fayl DARHOL tayyor bo'lmaydi —
    /// yakuniy holat webhook bilan keladi.
    ///
    /// 🔴 AVTOMATIK REJIMDA BU METOD ROZILIKNING ZAXIRA CHIQISHI. Yozuv
    /// endi o'z-o'zidan boshlanadi, ya'ni "yozilmasin" deyishning YAGONA
    /// yo'li — shu. Metodni (va uni chaqiradigan tugmani) olib tashlash
    /// darsni to'xtatmasdan yozuvni to'xtatish imkonini butunlay yo'q
    /// qilardi.
    /// </summary>
    Task<RecordingDto> StopAsync(long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// "HOZIR YOZIB OLINYAPTIMI" — JONLI XONADAGI INDIKATOR UCHUN
    /// ════════════════════════════════════════════════════════════════
    ///
    /// 🔴 BU AVTOMATIK YOZUV QARORINING SHARTLI QISMI (izoh yuqorida).
    /// Darsni O'TKAZUVCHI emas, darsda QATNASHUVCHI ham chaqira oladi —
    /// o'quvchi ham. Ruxsat <c>ILiveSessionService.GetAsync</c> orqali,
    /// ya'ni "shu darsni ko'ra oladigan har kim" (guruh a'zosi, host,
    /// o'quv bo'limi).
    ///
    /// ── NIMA UCHUN ALOHIDA METOD, <see cref="ListForSessionAsync"/> NI
    ///    OChIB QO'YISH EMAS ──────────────────────────────────────────
    ///
    ///  1) 🔴 RO'YXAT O'QUVCHIGA FAQAT <c>Completed</c> QATORLARNI
    ///     BERADI (u yerdagi izohga qarang) — ya'ni KETAYOTGAN yozuv
    ///     o'quvchiga UMUMAN ko'rinmaydi. Indikatorni o'sha ro'yxatga
    ///     ulash "yozuv ketmayapti" degan JIM YOLG'ON bo'lardi. Filtrni
    ///     bo'shatish esa teskari zarar: o'quvchi "urinish yiqildi"
    ///     qatorlarini ko'rib, ro'yxatni buzuq deb o'ylardi.
    ///  2) BU JAVOB HAR 10 SONIYADA, XONADAGI HAR ODAMDAN so'raladi.
    ///     Unda ombor kaliti, egress Id'si, xato matni va urinishlar
    ///     soni kabi ICHKI tafsilotlar bo'lishi mumkin emas — javobda
    ///     ATIGI IKKI maydon bor.
    ///
    /// ── ★ ASIMMETRIYA: SHUBHA "HA" FOYDASIGA HAL QILINADI ───────────
    ///
    /// Indikator <c>Requested</c> va <c>Starting</c> holatlarida HAM
    /// yonadi, faqat <c>Active</c> da emas. Ikki xato mumkin va ular
    /// TENG EMAS:
    ///   • "yozilmayapti" deyish, aslida yozilayotganda — ROZILIKNING
    ///     BUZILISHI (<c>egress_started</c> hodisasi kechikkan bir necha
    ///     soniyada aynan shu bo'lardi);
    ///   • "yozilmoqda" deyish, aslida hali boshlanmaganda — ortiqcha
    ///     ogohlantirish, zarari yo'q.
    /// Shuning uchun indikator YAKUNLANMAGAN har qanday qatorda yonadi.
    /// ⚠️ Narxi ochiq: Egress yiqilgan bo'lsa qator <c>Failed</c>
    /// bo'lgunicha (~10 daqiqa) indikator "yolg'on ha" ko'rsatadi. Bu
    /// ONGLI tanlov — teskarisi jimgina yozib olish bo'lardi.
    /// </summary>
    Task<RecordingLiveStatusDto> GetLiveStatusAsync(
        long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>Darsning barcha yozuv urinishlari (yangisi birinchi).</summary>
    Task<IReadOnlyList<RecordingDto>> ListForSessionAsync(
        long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// "Dars yozuvlari" bo'limi: sana oralig'idagi yozuvlar.
    ///
    /// ★ RO'YXAT QAMROVI KALENDAR ORQALI OLINADI
    /// (<c>ILiveSessionService.GetCalendarAsync</c>): u foydalanuvchi
    /// ko'ra oladigan darslarni ROL bo'yicha allaqachon filtrlaydi va bu
    /// testlar bilan qoplangan. Shu tufayli bu yerda ikkinchi (va albatta
    /// bir kun ajralib ketadigan) ruxsat so'rovi yozilmaydi.
    /// </summary>
    Task<IReadOnlyList<RecordingListItemDto>> ListAsync(
        long actorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>
    /// Ko'rish uchun MUDDATLI imzolangan havola.
    ///
    /// 🔴 TO'LOV DARVOZASI AYNAN SHU YERDA. Havola berilgandan keyin
    /// serverning "yo'q" deyishi mumkin emas — brauzer to'g'ridan-to'g'ri
    /// omborga boradi. Ya'ni tekshiruv HAVOLA BERILISHIDAN oldin bo'lishi
    /// shart (bu <c>ILiveSessionService.CreateJoinTokenAsync</c> dagi AYNI
    /// mulohaza: LiveKit tokeni ham shunday).
    /// </summary>
    Task<RecordingLinkDto> CreateViewLinkAsync(
        long recordingId, long actorId, CancellationToken ct = default);
}
