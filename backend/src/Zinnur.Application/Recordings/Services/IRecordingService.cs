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
    /*
       ════════════════════════════════════════════════════════════════════
       🔴 `StartAsync` VA `StopAsync` OLIB TASHLANDI (2026-09-01)
       ════════════════════════════════════════════════════════════════════

       Loyiha egasining qarori: "qo'lda yozuvni boshlash ham to'xtatish ham
       mumkin bo'lmasin — guruhga yozish-yozmaslik faqat tizimda GURUH
       DARAJASIDA boshqarilsa yetadi".

       Yozuv endi FAQAT avtomatik: `LiveSessionService.StartAsync` ->
       `IAutoRecordingScheduler` navbatga qator qo'yadi, `RecordingWatchdogJob`
       uni bo'shatadi. Ikkalasi ham `Group.RecordEnabled` ni o'qiydi.

       ⚠️ BU YERDA ILGARI QARAMA-QARSHI DALIL YOZILGAN EDI va uni
          yashirmasdan qoldiramiz: `StopAsync` "rozilikning zaxira chiqishi"
          deb ta'riflangandi — ya'ni yozuv avtomatik boshlangach, uni
          to'xtatishning yagona yo'li o'sha metod edi. Endi bunday yo'l
          YO'Q: dars davomida yozuvni to'xtatib bo'lmaydi.

          ★ O'RNIGA NIMA BOR: guruhning `RecordEnabled` kaliti. "Bu dars
            yozilmasin" degan qaror DARS BOSHLANISHIDAN OLDIN qabul
            qilinadi — o'quv bo'limi guruh kartochkasidan o'chiradi.
            Loyiha egasi aynan shuni tanladi: bitta boshqaruv nuqtasi,
            oldindan qabul qilinadigan qaror.
    */

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

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// YOZUVNI O'QUVCHILARGA OCHADI / YASHIRADI (talab R5)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Loyiha egasi: *"dars yozuvlari qismi student uchun dynamic bo'lishi
    /// kerak, o'quv bo'limi va teacher tarafidan manage qilinadi"*.
    ///
    /// ── UCHTA KALIT, "ENG QATTIG'I YUTADI" ──────────────────────────
    ///
    /// Amaldagi ko'rinish — MANTIQIY KO'PAYTMA, ya'ni istalgan bittasi
    /// yopiq bo'lsa o'quvchi yozuvni ko'rmaydi:
    ///
    ///   1) GLOBAL — <c>recordings.visible_to_students</c> sozlamasi.
    ///      "Butun bo'lim yopilsin" (talabdagi *"entire part of records"*).
    ///      Migratsiyasiz va admin panelida darhol ko'rinadi.
    ///   2) GURUH — <c>Group.RecordingsVisibleToStudents</c>. O'quv
    ///      bo'limi amalda AYNAN guruh bilan ishlaydi.
    ///   3) YOZUV — <c>SessionRecording.IsVisibleToStudents</c>. Bitta
    ///      dars uchun (bu metod).
    ///
    /// ★ NIMA UCHUN UCHALASI HAM KERAK: ular UCH XIL savolga javob
    /// beradi va biri ikkinchisining o'rnini bosmaydi. Faqat yozuv
    /// darajasi bo'lsa, bitta guruhni yopish o'nlab bosish bo'lardi;
    /// faqat global bo'lsa, bitta darsni yopish uchun butun markazni
    /// yopish kerak bo'lardi.
    ///
    /// ── KIM USTUN (talab bu haqda JIM) ──────────────────────────────
    ///
    /// 🔴 YASHIRISH — ikkala tomon ham, shartsiz.
    /// 🔴 OCHISH — o'quv bo'limi yopganini FAQAT o'quv bo'limi ochadi.
    ///
    /// Sabab batafsil <c>RecordingService.EnsureCanRevealAsync</c> da.
    /// Qisqasi: o'quv bo'limi yozuvni odatda AYNAN sifat muammosi
    /// (R29) sababli yopadi, ustoz esa uni bir bosishda qaytarib ocha
    /// olsa, sifat nazoratining yagona amaliy vositasi kuchsiz
    /// maslahatga aylanardi.
    ///
    /// ⚠️ OCHISH faqat TAYYOR yozuvda mumkin (409) — domain qoidasi
    /// <c>SessionRecording.ShowToStudents</c> da, <c>Test.Publish()</c>
    /// bilan AYNI naqsh. YASHIRISH esa har qanday holatda ishlaydi.
    /// </summary>
    Task<RecordingDto> SetVisibilityAsync(
        long recordingId, bool visible, long actorId, CancellationToken ct = default);

    /// <summary>
    /// "Dars yozuvlari bo'limi shu foydalanuvchi uchun ochiqmi" (R5).
    ///
    /// ★ NIMA UCHUN ALOHIDA METOD: o'quvchining "O'quv" ekranida bo'limga
    /// KIRISH KARTOCHKASI turadi. Bo'lim yopilganda kartochka qolsa,
    /// o'quvchi uni bosib abadiy bo'sh sahifaga tushardi. Ro'yxatning O'ZI
    /// bu savolga javob bera olmaydi: bo'sh ro'yxat "yopilgan" ni ham,
    /// "hali yozuv yo'q" ni ham bildiradi.
    ///
    /// ⚠️ Xodim uchun HAR DOIM <c>true</c>: kalitlar o'quvchiga qaratilgan,
    /// arxiv esa xodimga har doim kerak.
    /// </summary>
    Task<RecordingSectionDto> GetSectionAsync(long actorId, CancellationToken ct = default);
}
