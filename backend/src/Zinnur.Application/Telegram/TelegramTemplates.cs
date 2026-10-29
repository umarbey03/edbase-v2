using System.Globalization;
using Zinnur.Application.Notifications;

namespace Zinnur.Application.Telegram;

/// <summary>
/// Bot javoblarining MATNI va TURI.
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ ESCAPE QAYERDA: har foydalanuvchi qiymati
/// <see cref="NotificationText.Parameter"/> orqali o'tadi — shu yerda,
/// matn YASALAYOTGAN paytda. Yuboruvchi (sender) tayyor matnni QAYTA
/// ISHLAMAYDI (<c>IMessageSender</c> shartnomasi). Aks holda shablonning
/// o'z <c>&lt;b&gt;</c> teglari o'quvchi ekranida so'zma-so'z ko'rinardi.
///
/// ★ TUGMALAR (reply_markup) NIMA UCHUN BU YERDA EMAS: navbat yozuvi
/// (<c>MessageOutbox</c>) faqat MATNNI saqlaydi va <c>IMessageSender</c>
/// shartnomasida tugma uchun maydon YO'Q (shartnomani o'zgartirish esa
/// taqiqlangan). Yechim: yuboruvchi tugmani <c>TemplateKey</c> BO'YICHA
/// tanlaydi — <see cref="MarkupFor"/>. Bu to'g'ri taqsimot ham:
/// "raqamni ulash tugmasi" — TELEGRAM'ning ko'rinish tafsiloti, use-case
/// esa faqat "qanday xabar" ekanini biladi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public static class TelegramTemplates
{
    // ---------------------------------------------------------------- kalitlar
    //
    // Kalit navbatda GURUHLASH uchun saqlanadi ("bugun nechta raqam
    // ulanmadi") va yuboruvchi uchun TUGMA ko'rsatkichi.
    // Uzunlik chegarasi 64 belgi (`MessageOutboxConfiguration`).

    /// <summary>Bog'lanmagan foydalanuvchiga <c>/start</c> javobi — raqam so'raladi.</summary>
    public const string StartUnlinked = "bot_start_unlinked";

    /// <summary>Allaqachon bog'langan o'quvchiga <c>/start</c> javobi.</summary>
    public const string StartLinked = "bot_start_linked";

    /// <summary>Raqam tasdiqlandi va profil bog'landi.</summary>
    public const string ContactLinked = "bot_contact_linked";

    /// <summary>Raqam ro'yxatda yo'q.</summary>
    public const string ContactUnknown = "bot_contact_unknown";

    /// <summary>
    /// Brauzerdan kirish uchun bir martalik kod.
    ///
    /// ★ BOT JAVOBI EMAS — bu yagona shablon, uni webhook emas,
    /// <c>IPhoneLoginService</c> yozadi. Kalit baribir SHU YERDA, chunki
    /// kalitlar ro'yxati bitta bo'lishi kerak: <see cref="MarkupFor"/>
    /// tugmani AYNAN kalit bo'yicha tanlaydi va noma'lum kalit jimgina
    /// "tugmasiz" holatga tushardi.
    /// </summary>
    public const string LoginCode = "auth_login_code";

    /// <summary>
    /// Bot orqali kirish havolasi eskirgan (2026-08-28).
    ///
    /// ★ NIMA UCHUN ALOHIDA KALIT, <see cref="Help"/> YETARLI EMAS:
    /// foydalanuvchi AYNAN kirishga urinyapti va unga aytiladigan yagona
    /// foydali gap — "saytga qaytib tugmani qayta bosing". Umumiy yordam
    /// matni uni bot ichida yechim izlashga majbur qilardi (yechim esa
    /// bot ichida yo'q).
    /// </summary>
    public const string LoginLinkExpired = "auth_login_link_expired";

    /// <summary>
    /// ⚠️ ESKI KALIT — 2026-08-17 dan BOSHLAB ISHLATILMAYDI.
    ///
    /// Telefon almashtirishning o'zi (o'z profilini o'zi tahrirlash bilan
    /// birga) loyiha egasi tomonidan bekor qilindi — sabab
    /// `TelegramUpdateHandler.HandleContactAsync` izohida.
    ///
    /// ★ NIMA UCHUN O'CHIRILMADI — <see cref="ContactStaff"/> dagi bilan
    /// AYNI sabab: kalit `MessageOutbox` jadvalidagi eski qatorlarda
    /// saqlanib turibdi.
    /// </summary>
    public const string PhoneChangeCode = "profile_phone_change_code";

    /// <summary>
    /// ⚠️ ESKI KALIT — 2026-08-13 dan BOSHLAB ISHLATILMAYDI.
    ///
    /// Ilgari xodim raqami botga yuborilganda "Telegram orqali kirish
    /// faqat o'quvchilar uchun" javobi ketardi. Endi xodim ham
    /// bog'lanadi (sabab: email va parol bilan kirish olib tashlandi),
    /// ya'ni bu shox umuman ishlamaydi.
    ///
    /// ★ NIMA UCHUN O'CHIRILMADI: kalit BAZADA — `MessageOutbox`
    /// jadvalidagi eski qatorlarda saqlanib turibdi va "qaysi tur xabar
    /// nechta ketgan" hisobotlari shu satrga tayanadi. Uni o'chirish
    /// tarixiy ma'lumotni nomsiz qoldirardi.
    /// </summary>
    public const string ContactStaff = "bot_contact_staff";

    /// <summary>BOSHQA odamning kontakti yuborilgan (audit: X-1).</summary>
    public const string ContactMismatch = "bot_contact_mismatch";

    /// <summary>Profil allaqachon boshqa Telegram akkauntga bog'langan.</summary>
    public const string ContactProfileTaken = "bot_contact_profile_taken";

    /// <summary>Bu Telegram akkaunt allaqachon boshqa profilga bog'langan.</summary>
    public const string ContactTelegramTaken = "bot_contact_telegram_taken";

    /// <summary>Profil faol emas.</summary>
    public const string ContactInactive = "bot_contact_inactive";

    /// <summary>Tushunilmagan matnga javob.</summary>
    public const string Help = "bot_help";

    /* ===== R35/R36 · BIZNES HODISASI =====

       ★ NEGA ALOHIDA BLOK: bu faylga bir necha tarmoq AYNI vaqtda qo'shadi. */

    /// <summary>
    /// Uy vazifasi tekshirildi (baho qo'yildi).
    ///
    /// 🔴 BU FAYLDAGI BIRINCHI SHABLON — VA U BOT JAVOBI EMAS.
    ///
    /// Bugungacha (<see cref="LoginCode"/> dan tashqari) hamma kalit
    /// Telegram'ning O'Z yangilanishiga javob edi: o'quvchi yozadi — bot
    /// javob beradi. Bu esa PLATFORMADAGI hodisa: ustoz brauzerda tugma
    /// bosadi, o'quvchi Telegram'da xabar oladi. Ya'ni R35 dan boshlab bu
    /// sinf "bot javoblari" emas, "chiquvchi xabarlar" ro'yxati.
    ///
    /// ★ TUGMASIZ (<see cref="MarkupFor"/> da ro'yxatdan tashqari, ya'ni
    /// <c>None</c>): «Ilovani ochish» tugmasi bu yerda foydali BO'LARDI,
    /// lekin u Mini App'ni BOSH SAHIFADA ochadi — vazifa sahifasida emas.
    /// "Ochdim, lekin kerakli joyni o'zim qidirdim" tajribasi tugmaning
    /// va'dasini buzardi. Chuqur havola (deep link) qo'shilganda bu qaror
    /// qayta ko'rib chiqiladi.
    /// </summary>
    public const string SubmissionGraded = "submission_graded";

    /* ===== /R35/R36 ===== */

    /* ===== 2026-08-17 · USTOZ KUNLIK TASDIQLASH + O'RINBOSAR ===== */

    /// <summary>Ertalabki savol: bugungi darslarga o'ta olasizmi (✅/❌ tugma).</summary>
    public const string AvailabilityAsk = "availability_ask";

    /// <summary>"Yo'q" bosilgach — qaysi dars(lar) (toggle tugmalar).</summary>
    public const string AvailabilitySessions = "availability_sessions";

    /// <summary>Sabab so'raladi (tugmasiz, erkin matn kutiladi).</summary>
    public const string AvailabilityReason = "availability_reason";

    /// <summary>Necha kunga (tez tugmalar).</summary>
    public const string AvailabilityDays = "availability_days";

    /// <summary>"Ha" tasdiqlandi — yakuniy xabar.</summary>
    public const string AvailabilityConfirmed = "availability_confirmed";

    /// <summary>Kun soni noto'g'ri kiritildi.</summary>
    public const string AvailabilityDaysInvalid = "availability_days_invalid";

    /// <summary>O'rinbosarga yuboriladigan taklif (✅/❌ tugma).</summary>
    public const string SubstituteOfferAsk = "substitute_offer_ask";

    /// <summary>Boshqa nomzod rozi bo'lgani uchun taklif bekor qilindi.</summary>
    public const string SubstituteOfferWithdrawn = "substitute_offer_withdrawn";

    /// <summary>O'quv bo'limiga: ustoz darsga o'ta olmaydi.</summary>
    public const string TeacherDeclinedSession = "teacher_declined_session";

    /// <summary>O'quv bo'limiga: o'rinbosar topildi.</summary>
    public const string SubstituteFound = "substitute_found";

    /// <summary>
    /// O'quvchiga: guruhingizda dars BOSHLANDI (2026-08-18).
    ///
    /// ★ TUGMASI BOR (<see cref="TelegramMarkup.OpenApp"/>): bu xabarning
    /// butun maqsadi — o'quvchini DARHOL darsga kiritish. `SubmissionGraded`
    /// da tugma ATAYLAB yo'q edi (u vazifa sahifasiga emas, bosh sahifaga
    /// olib borardi), bu yerda esa bosh sahifaning O'ZI to'g'ri joy: jonli
    /// dars kartochkasi eng tepada turadi.
    /// </summary>
    public const string SessionStarted = "session_started";

    /// <summary>
    /// Darsdan keyingi ball xabari (2026-10-23, ball tizimi): nechta ball
    /// oldi yoki ayirildi, ketma-ketlik va oydagi jami. Sozlamada o'chiq
    /// bo'lsa umuman yozilmaydi (<c>gamification.lesson_message_enabled</c>).
    /// Tugmasi — «Ilovani ochish»: o'quvchi reytingdagi o'rnini ko'radi.
    /// </summary>
    public const string PointsLesson = "points_lesson";

    /// <summary>
    /// Darsga kelmagan o'quvchiga yuborilgan xabar (2026-08-18).
    ///
    /// ★ MATN SHU YERDA EMAS: uni o'quv bo'limi xodimi o'zi yozadi
    /// (shablondan olib, o'rin egallovchilar bilan). Bu kalit faqat
    /// navbatda guruhlash va tugma tanlash uchun.
    /// </summary>
    public const string AbsenceNotice = "absence_notice";

    /// <summary>
    /// O'quvchi darsga kelmaganlik sababini yozib yuborgach beriladigan
    /// tasdiq (2026-08-18).
    ///
    /// ★ NEGA JAVOB QAYTARILADI: javobsiz qolgan xabar "yetib bordimi?"
    /// degan shubha qoldiradi va o'quvchi sababini QAYTA yozib yuboradi.
    /// Qisqa tasdiq buni to'xtatadi.
    /// </summary>
    public const string AbsenceReplySaved = "absence_reply_saved";

    /// <summary>"Boshqa sabab" bosilgach: sababni yozishni so'rovchi yo'riq (2026-08-18).</summary>
    public const string AbsenceReplyPrompt = "absence_reply_prompt";

    /* ===== 2026-10-22 · O'RINBOSAR — O'QUV BO'LIMI TASDIG'I ===== */

    /// <summary>O'quv bo'limiga: nomzod rozi bo'ldi, tasdiq kerak.</summary>
    public const string SubstituteApprovalRequest = "substitute_approval_request";

    /// <summary>O'rinbosarga: o'quv bo'limi qarori.</summary>
    public const string SubstituteApprovalDecision = "substitute_approval_decision";

    /// <summary>Nomzod rozi bo'lganda unga javob — dars hali unga o'tmagan.</summary>
    /// <param name="groupName">Guruh nomi.</param>
    public static string SubstituteAwaitingApprovalText(string groupName) =>
        $"✅ Rahmat! <b>{NotificationText.Parameter(groupName, 100)}</b> darsi o'quv bo'limi "
        + "tasdiqlagandan keyin sizga o'tadi — tasdiq haqida xabar beramiz.";

    /// <summary>O'quv bo'limiga: o'rinbosarni tasdiqlash so'rovi (tugmalar bilan).</summary>
    public static string SubstituteApprovalRequestText(
        string substituteName, string originalTeacherName, string groupName, string when) =>
        $"🔁 <b>{NotificationText.Parameter(substituteName)}</b> "
        + $"<b>{NotificationText.Parameter(originalTeacherName)}</b> o'rniga "
        + $"<b>{NotificationText.Parameter(groupName, 100)}</b> darsini ({NotificationText.Parameter(when, 40)}) "
        + "o'tib berishga rozi. Tasdiqlaysizmi?";

    /// <summary>O'rinbosarga: tasdiqlandi.</summary>
    public static string SubstituteApprovedText(string groupName) =>
        $"✅ O'quv bo'limi tasdiqladi: <b>{NotificationText.Parameter(groupName, 100)}</b> darsini siz o'tasiz.";

    /// <summary>O'rinbosarga: rad etildi.</summary>
    public static string SubstituteRejectedText(string groupName) =>
        $"ℹ️ O'quv bo'limi <b>{NotificationText.Parameter(groupName, 100)}</b> darsi uchun "
        + "boshqa qaror qabul qildi — dars sizga o'tmaydi. Rahmat!";

    /* ===== /2026-10-22 ===== */
    /// <summary>
    /// Yangi onlayn buyurtma — XODIMLAR GURUHIGA, «Tasdiqlash / Rad etish»
    /// tugmalari bilan (2026-10-29). <c>callback_data</c>: <c>so:ok:{id}</c>,
    /// <c>so:no:{id}</c>.
    /// </summary>
    public const string ShopOrderStaff = "shop_order_staff";

    /// <summary>Buyurtma hal qilindi — xodimlar guruhiga qisqa izoh (kim, nima qildi).</summary>
    public const string ShopOrderDecided = "shop_order_decided";

    /// <summary>O'quvchiga: to'lov tasdiqlandi. Tugma — «Ilovani ochish».</summary>
    public const string ShopOrderConfirmed = "shop_order_confirmed";

    /// <summary>O'quvchiga: to'lov topilmadi.</summary>
    public const string ShopOrderRejected = "shop_order_rejected";

    /* ===== /2026-08-17 ===== */

    // ---------------------------------------------------------------- tugmalar

    /// <summary>
    /// <paramref name="templateKey"/> uchun qanday tugma ko'rsatilsin.
    /// Noma'lum kalit (masalan <c>lesson_reminder</c>) — tugmasiz.
    /// </summary>
    public static TelegramMarkup MarkupFor(string? templateKey) => templateKey switch
    {
        StartUnlinked or ContactUnknown or ContactMismatch => TelegramMarkup.RequestContact,

        // Bog'lanish tugagach raqam so'rov klaviaturasi OLIB TASHLANADI —
        // aks holda u ekran ostida abadiy osilib turardi va o'quvchi uni
        // qayta bosib, keraksiz yangilanishlar yuborardi.
        StartLinked or ContactLinked => TelegramMarkup.OpenApp,

        ContactStaff or ContactProfileTaken or ContactTelegramTaken or ContactInactive =>
            TelegramMarkup.RemoveKeyboard,

        // 🔴 KIRISH KODI — TUGMASIZ, VA BU ATAYLAB.
        //
        // «Ilovani ochish» tugmasi bu yerda ZARARLI bo'lardi: kod
        // BRAUZERDA kutilyapti, tugma esa foydalanuvchini Mini App'ga olib
        // ketib, u boshlagan oqimni tashlab ketishga majbur qilardi.
        LoginCode => TelegramMarkup.None,

        // 🔴 ESKIRGAN HAVOLA — TUGMASIZ, VA BU HAM ATAYLAB. Yechim
        // BRAUZERDA (saytga qaytib tugmani qayta bosish), ya'ni
        // «Ilovani ochish» tugmasi odamni yana boshqa tomonga olib
        // ketardi. «Raqamni ulashish» ham noto'g'ri bo'lardi: raqam
        // bilan bog'liq muammo yo'q, chipta shunchaki eskirgan.
        LoginLinkExpired => TelegramMarkup.None,

        AvailabilityAsk or AvailabilitySessions or AvailabilityDays or SubstituteOfferAsk =>
            TelegramMarkup.InlineButtons,

        // Dars boshlandi — «🚀 Ilovani ochish» (sabab kalit izohida).
        SessionStarted => TelegramMarkup.OpenApp,

        PointsLesson => TelegramMarkup.OpenApp,

        ShopOrderStaff => TelegramMarkup.InlineButtons,
        ShopOrderConfirmed => TelegramMarkup.OpenApp,
        ShopOrderDecided or ShopOrderRejected => TelegramMarkup.None,

        // Kelmaganlik xabari — «Ilovani ochish» tugmasi bilan: o'quvchi
        // dars yozuvini ko'rishi yoki jadvalni tekshirishi mumkin, ya'ni
        // tugma haqiqiy keyingi qadamga olib boradi.
        // Kelmaganlik xabari — TUGMASIZ, VA BU ATAYLAB (2026-08-18):
        // xabar o'quvchidan SABABNI YOZIShNI so'raydi. «Ilovani ochish»
        // tugmasi uni Mini App'ga olib ketib, aynan kutilayotgan
        // javobdan chalg'itardi (kirish kodi bilan AYNI mulohaza).
        // Kelmaganlik xabari — TAYYOR SABAB TUGMALARI bilan (2026-08-18):
        // "sababini yozing" degan matnning o'zi yetarli emas, tugma
        // javob berish darajasini keskin oshiradi.
        AbsenceNotice => TelegramMarkup.InlineButtons,

        AbsenceReplySaved or AbsenceReplyPrompt => TelegramMarkup.None,

        _ => TelegramMarkup.None,
    };

    /// <summary>
    /// Inline tugmalarni <c>OutboxMessage.CallbackData</c>ga kodlaydi.
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// ★ NIMA UCHUN JSON EMAS: navbat yozuvidagi maydon (2000 belgi) uchun
    /// oddiy matn formatidagi ajratgichlar (boshqaruv belgilari — foydalanuvchi
    /// matnida HECH QACHON uchramaydigan ASCII kodlar) yetarli va tezroq
    /// parslanadi. Format <see cref="Infrastructure.Services.TelegramMessageSender"/>
    /// dagi <c>WriteInlineButtons</c> bilan AYNAN mos bo'lishi shart:
    ///   • qatorlar — <c>\n</c> bilan;
    ///   • bitta qatordagi tugmalar — <c>\t</c> bilan;
    ///   • tugma matni va <c>callback_data</c>si — <c></c> (Unit
    ///     Separator) bilan ajratiladi.
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="rows">Har qator — bir nechta (matn, callback_data) juftligi.</param>
    public static string EncodeButtons(IReadOnlyList<IReadOnlyList<(string Label, string CallbackData)>> rows) =>
        string.Join('\n', rows.Select(row =>
            string.Join('\t', row.Select(button => $"{button.Label}{LabelDataSeparator}{button.CallbackData}"))));

    /// <summary>Tugma matni va <c>callback_data</c>si orasidagi ajratgich (ASCII Unit Separator, 0x1F).</summary>
    private const char LabelDataSeparator = '';

    // ---------------------------------------------------------------- matnlar

    /// <summary>Bog'lanmagan foydalanuvchi uchun salom va yo'riq.</summary>
    public static string StartUnlinkedText() =>
        "Salom! 👋\n"
        + "Bu — <b>ZIN-NUR Online</b> o'quv platformasining rasmiy boti.\n\n"
        + "Tizimga kirish uchun pastdagi <b>«📱 Raqamni ulashish»</b> tugmasini bosing.\n\n"
        + "⚠️ Raqamni <b>qo'lda yozib bo'lmaydi</b> — u faqat shu tugma orqali "
        + "ulashiladi. Shu tufayli hech kim begona raqamni kiritib, "
        + "boshqa odamning profiliga kira olmaydi.";

    /// <summary>Allaqachon bog'langan o'quvchi uchun salom.</summary>
    public static string StartLinkedText(string? fullName) =>
        $"Salom, <b>{NotificationText.Parameter(fullName)}</b>! 👋\n"
        + "Profilingiz Telegram akkauntingizga ulangan.\n\n"
        + "Darslar, davomat va vazifalarni ochish uchun quyidagi tugmani bosing.";

    /// <summary>Bog'lanish muvaffaqiyatli.</summary>
    public static string ContactLinkedText(string? fullName) =>
        $"✅ Rahmat, <b>{NotificationText.Parameter(fullName)}</b>!\n"
        + "Raqamingiz tasdiqlandi va profilingizga ulandi.\n\n"
        + "Endi tizimga <b>parolsiz</b> kirasiz:\n"
        + "• telefonda — quyidagi tugma orqali;\n"
        + "• kompyuterda — saytda telefon raqamingizni kiriting, "
        + "kod shu chatga keladi.";

    /// <summary>
    /// Brauzerdan kirish uchun bir martalik kod.
    ///
    /// ★ KOD <c>&lt;code&gt;</c> TEGIDA: Telegram uni bosganda BUFERGA
    ///   nusxalaydi. Bu shunchaki qulaylik emas — kodni qo'lda ko'chirish
    ///   eng ko'p xato qilinadigan qadam, xato kod esa urinishlar
    ///   chegarasini yeydi.
    ///
    /// 🔴 XABAR MATNI OGOHLANTIRISH BILAN TUGAYDI. Bu bir martalik
    ///    kodlarning eng keng tarqalgan hujumiga qarshi yagona chora:
    ///    hujumchi qurbonga qo'ng'iroq qilib, "bank/o'quv markazi
    ///    xodimiman, kodni ayting" deydi. Texnik himoya bu yerda ojiz —
    ///    faqat xabarning o'zi ogohlantira oladi.
    /// </summary>
    /// <param name="code">6 xonali kod.</param>
    /// <param name="ttl">Kod qancha yashashi (matnda daqiqada ko'rsatiladi).</param>
    public static string LoginCodeText(string code, TimeSpan ttl) =>
        "🔐 <b>Kirish kodi</b>\n\n"
        + $"<code>{NotificationText.Parameter(code, MaxCodeLength)}</code>\n\n"
        + $"Kod {Math.Max(1, (int)Math.Round(ttl.TotalMinutes))} daqiqa yaroqli va "
        + "faqat <b>bir marta</b> ishlatiladi.\n\n"
        + "⚠️ Bu kodni <b>hech kimga aytmang</b>. ZIN-NUR xodimlari uni "
        + "hech qachon so'ramaydi. Agar kirishga urinmagan bo'lsangiz — "
        + "xabarni e'tiborsiz qoldiring va o'quv bo'limiga bildiring.";

    /// <summary>
    /// Bot orqali kirish havolasi eskirgan yoki allaqachon ishlatilgan.
    ///
    /// ★ MATN «QAYTA URINING» DEMAYDI, «SAYTGA QAYTING» DEYDI: chipta
    /// BRAUZERDA yasaladi va botdagi hech qanday harakat uni tiriltira
    /// olmaydi. "Qayta urining" degan umumiy maslahat foydalanuvchini
    /// botda <c>/start</c> ni qayta-qayta bosishga majbur qilardi —
    /// natijasi esa har safar AYNI xabar bo'lardi.
    /// </summary>
    public static string LoginLinkExpiredText() =>
        "⌛️ <b>Havola eskirgan</b>\n\n"
        + "Xavfsizlik uchun kirish havolasi 15 daqiqa amal qiladi.\n\n"
        + "Saytga qayting va <b>«Telegram orqali kirish»</b> tugmasini "
        + "qaytadan bosing — bu bir necha soniya vaqt oladi.";

    /// <summary>
    /// ⚠️ ESKI MATN — 2026-08-17 dan boshlab HECH QAYERDAN chaqirilmaydi
    /// (sabab <see cref="PhoneChangeCode"/> izohida). <see cref="ContactStaffText"/>
    /// dagi bilan AYNI sababga ko'ra qoldirildi.
    ///
    /// TELEFON ALMASHTIRISH kodi.
    ///
    /// 🔴 MATN ATAYLAB "KIRISH" DEMAYDI: foydalanuvchi bu kodni
    /// raqamini ALMASHTIRISH uchun so'ragan. "Kirish kodi" deb yozilsa,
    /// u kirish oynasiga qaytib, kodni u yerga kiritishga urinardi —
    /// va kod u yerda ishlamasdi (u BOSHQA raqam bo'yicha berilgan).
    ///
    /// ★ RAQAMNING O'ZI HAM YOZILADI: foydalanuvchi qaysi raqamni
    /// biriktirayotganini ko'rib tursin. Agar u bu amalni qilmagan
    /// bo'lsa — begona odam uning raqamini o'ziga biriktirmoqchi
    /// bo'layotganini AYNAN shu xabardan biladi.
    /// </summary>
    /// <param name="code">6 xonali kod.</param>
    /// <param name="phone">Biriktirilayotgan raqam (E.164).</param>
    /// <param name="ttl">Kod qancha yashashi.</param>
    public static string PhoneChangeCodeText(string code, string phone, TimeSpan ttl) =>
        "📱 <b>Raqamni biriktirish kodi</b>\n\n"
        + $"<code>{NotificationText.Parameter(code, MaxCodeLength)}</code>\n\n"
        + $"Bu kod <b>{NotificationText.Parameter(phone, MaxPhoneLength)}</b> raqamini "
        + "ZIN-NUR profilingizga biriktirish uchun.\n"
        + $"Kod {Math.Max(1, (int)Math.Round(ttl.TotalMinutes))} daqiqa yaroqli va "
        + "faqat <b>bir marta</b> ishlatiladi.\n\n"
        + "⚠️ Agar siz raqam almashtirmayotgan bo'lsangiz — bu kodni "
        + "<b>hech kimga aytmang</b> va darhol o'quv bo'limiga xabar bering.";

    /// <summary>Raqam uzunligi chegarasi (E.164 — ko'pi bilan 16 belgi).</summary>
    private const int MaxPhoneLength = 20;

    /// <summary>
    /// Kod uzunligi chegarasi (<see cref="LoginCodeText"/> parametri uchun).
    /// Kod SERVER yasagan raqam, ya'ni foydalanuvchi ma'lumoti emas —
    /// lekin u baribir <c>Parameter</c> orqali o'tkaziladi, chunki
    /// "escape'ni faqat ba'zi joyda qo'llash" qoidasi birinchi
    /// unutilganda buziladi.
    /// </summary>
    private const int MaxCodeLength = 16;

    /// <summary>Raqam bazada topilmadi.</summary>
    public static string ContactUnknownText() =>
        "❌ Bu raqam ro'yxatda yo'q.\n\n"
        + "Iltimos, <b>o'quv bo'limiga murojaat qiling</b> va ro'yxatdagi "
        + "telefon raqamingizni tekshiring. Raqam to'g'rilangach, qaytadan urinib ko'ring.";

    /// <summary>
    /// ⚠️ ESKI MATN — 2026-08-13 dan boshlab HECH QAYERDAN chaqirilmaydi
    /// (sabab <see cref="ContactStaff"/> kaliti izohida). Matn eski
    /// navbat qatorlarini o'qish uchun emas — ular tayyor holda saqlangan —
    /// balki kalitning ma'nosi kodda ko'rinib tursin uchun qoldirildi.
    ///
    /// 🔴 YANGI CHAQIRUV QO'SHMANG: bu matn MAVJUD BO'LMAGAN kirish
    /// yo'liga ("email va parol") yo'naltiradi.
    /// </summary>
    public static string ContactStaffText() =>
        "Bu raqam <b>xodim</b> profiliga tegishli.\n\n"
        + "Xodimlar ham Telegram orqali kiradi — «📱 Raqamni ulashish» "
        + "tugmasini bosing.";

    /// <summary>Boshqa odamning kontakti yuborilgan.</summary>
    public static string ContactMismatchText() =>
        "❌ Siz <b>boshqa odamning</b> kontaktini yubordingiz.\n\n"
        + "Iltimos, pastdagi <b>«📱 Raqamni ulashish»</b> tugmasi orqali "
        + "<b>o'z</b> raqamingizni yuboring.";

    /// <summary>Profil boshqa Telegram akkauntga bog'langan.</summary>
    public static string ContactProfileTakenText() =>
        "❌ Bu raqamdagi profil <b>boshqa Telegram akkauntga</b> bog'langan.\n\n"
        + "Agar bu haqiqatan sizning profilingiz bo'lsa — o'quv bo'limiga "
        + "murojaat qiling, ular eski bog'lanishni bekor qiladi.";

    /// <summary>Telegram akkaunt boshqa profilga bog'langan.</summary>
    public static string ContactTelegramTakenText() =>
        "❌ Sizning Telegram akkauntingiz <b>boshqa profilga</b> bog'langan.\n\n"
        + "Bitta Telegram akkaunt faqat bitta profilga ulanadi. "
        + "O'quv bo'limiga murojaat qiling.";

    /// <summary>Profil faol emas.</summary>
    public static string ContactInactiveText() =>
        "Profilingiz hozircha <b>faol emas</b>.\n\n"
        + "O'quv bo'limi bilan bog'laning.";

    /// <summary>Tushunilmagan matnga javob.</summary>
    public static string HelpText() =>
        "Men ro'yxatdan o'tishga yordam beraman.\n\n"
        + "Boshlash uchun <b>/start</b> buyrug'ini yuboring.";

    /* ===== R35/R36 · BIZNES HODISASI ===== */

    /// <summary>
    /// «Vazifangiz tekshirildi» xabari.
    ///
    /// ★ HAR FOYDALANUVCHI QIYMATI <see cref="NotificationText.Parameter"/>
    /// ORQALI: vazifa sarlavhasi va ustozning izohi — ikkalasi ham ODAM
    /// yozgan matn. Ustoz izohida bitta <c>&lt;</c> bo'lsa (masalan
    /// "javob &lt; 5 bo'lishi kerak edi") Telegram butun so'rovni
    /// <c>400 can't parse entities</c> bilan rad etardi va xabar UMUMAN
    /// yetib bormasdi.
    ///
    /// ★ BALL <c>NotificationTemplates.FormatScore</c> ORQALI: u
    /// <c>InvariantCulture</c> ni majburlaydi. <c>Score</c> va
    /// <c>MaxScore</c> — <c>decimal</c>, ya'ni server madaniyati
    /// <c>uz-UZ</c>/<c>ru-RU</c> bo'lsa o'nlik ajratgich VERGUL bo'lardi.
    /// Ball SERVER hisoblagan son, foydalanuvchi matni emas — shuning
    /// uchun u <c>Parameter</c> dan O'TKAZILMAYDI (ekranlanadigan belgi
    /// bo'lishi mumkin emas).
    ///
    /// ★ IZOH BO'LMASA — SATR HAM YO'Q. Bo'sh "💬" satri o'quvchida
    /// "ustoz nimadir yozganu, ko'rinmayapti" degan taassurot qoldirardi.
    /// </summary>
    /// <param name="assignmentTitle">Vazifa sarlavhasi (bo'sh bo'lishi mumkin).</param>
    /// <param name="score">Qo'yilgan ball.</param>
    /// <param name="maxScore">Maksimal ball.</param>
    /// <param name="feedback">Ustozning izohi (bo'lmasligi mumkin).</param>
    public static string SubmissionGradedText(
        string? assignmentTitle, decimal score, decimal maxScore, string? feedback)
    {
        var title = NotificationText.Parameter(assignmentTitle);
        var head = title.Length == 0 ? "Vazifa" : title;

        var text = "✅ <b>Vazifangiz tekshirildi</b>\n\n"
            + $"📝 {head}\n"
            + $"⭐️ Baho: <b>{NotificationTemplates.FormatScore(score)}</b> / "
            + $"{NotificationTemplates.FormatScore(maxScore)}";

        // Izoh UZUNROQ chegara oladi (500): u xabarning eng qimmat qismi va
        // 200 belgi ustozning odatiy izohini o'rtasidan kesardi. Umumiy 4096
        // chegarasidan hamon ancha uzoq.
        var note = NotificationText.Parameter(feedback, FeedbackMaxLength);

        if (note.Length > 0) text += $"\n\n💬 {note}";

        return text;
    }

    /// <summary>Izoh uchun chegara (<see cref="SubmissionGradedText"/> parametri).</summary>
    private const int FeedbackMaxLength = 500;

    /* ===== /R35/R36 ===== */

    /* ===== 2026-08-17 · USTOZ KUNLIK TASDIQLASH + O'RINBOSAR ===== */

    /// <summary>Ertalabki savol matni — bugungi darslar ro'yxati bilan.</summary>
    /// <param name="sessions">Vaqt va guruh nomi juftliklari, vaqt bo'yicha tartiblangan.</param>
    public static string AvailabilityAskText(IReadOnlyList<(string Time, string GroupName)> sessions)
    {
        var lines = string.Join(
            '\n',
            sessions.Select(s =>
                $"🕐 {NotificationText.Parameter(s.Time, 16)} — {NotificationText.Parameter(s.GroupName, 100)}"));

        return "☀️ <b>Xayrli tong!</b>\n\n"
            + "Bugungi darslaringiz:\n" + lines + "\n\n"
            + "Barchasiga o'ta olasizmi?";
    }

    /// <summary>Dars tanlash bosqichi — har dars oldida ☑/☐ belgisi bilan.</summary>
    /// <param name="sessions">(vaqt, guruh, tanlanganmi) uchligi.</param>
    public static string AvailabilitySessionsText(
        IReadOnlyList<(string Time, string GroupName, bool Selected)> sessions)
    {
        var lines = string.Join(
            '\n',
            sessions.Select(s =>
                $"{(s.Selected ? "☑" : "☐")} {NotificationText.Parameter(s.Time, 16)} — "
                + $"{NotificationText.Parameter(s.GroupName, 100)}"));

        return "Qaysi dars(lar)ga o'ta olmaysiz?\n\n" + lines
            + "\n\nKerakli darslarni belgilang, so'ng <b>«▶ Davom etish»</b> ni bosing.";
    }

    /// <summary>Sabab so'raladi (erkin matn kutiladi).</summary>
    public static string AvailabilityReasonPromptText() =>
        "Sababini yozib yuboring (masalan: <i>kasal bo'lib qoldim</i>).";

    /// <summary>Necha kunga so'raladi (tez tugmalar + erkin son).</summary>
    public static string AvailabilityDaysPromptText() =>
        "Necha kunga o'ta olmaysiz?\n\n"
        + "Tugmalardan birini tanlang yoki kun sonini yozib yuboring.";

    /// <summary>Kun soni noto'g'ri kiritildi (matn orqali).</summary>
    public static string AvailabilityDaysInvalidText() =>
        "Iltimos, 1 dan 30 gacha bo'lgan butun son yuboring (masalan: <i>3</i>).";

    /// <summary>"Ha" tasdiqlandi.</summary>
    public static string AvailabilityConfirmedText() =>
        "✅ Rahmat! Bugungi barcha darslaringiz rejadagidek qoladi.";

    /// <summary>O'rinbosarga yuboriladigan taklif.</summary>
    /// <param name="originalTeacherName">Darsni o'ta olmayotgan ustoz.</param>
    /// <param name="groupName">Guruh nomi.</param>
    /// <param name="time">Dars vaqti (matn).</param>
    /// <param name="reason">Sabab.</param>
    public static string SubstituteOfferText(
        string originalTeacherName, string groupName, string time, string reason)
    {
        var name = NotificationText.Parameter(originalTeacherName);
        var group = NotificationText.Parameter(groupName, 100);
        var when = NotificationText.Parameter(time, 16);
        var why = NotificationText.Parameter(reason, 300);

        var reasonPart = why.Length > 0 ? $"({why}) " : "";

        return $"🔔 <b>{name}</b> {reasonPart}"
            + "quyidagi darsni o'tolmaydi:\n\n"
            + $"🕐 {when} — <b>{group}</b>\n\n"
            + "Siz o'tib bera olasizmi?";
    }

    /// <summary>Boshqa nomzod rozi bo'lgani uchun taklif bekor qilindi.</summary>
    public static string SubstituteOfferWithdrawnText() =>
        "Rahmat! Bu darsga boshqa ustoz allaqachon rozi bo'ldi, sizga endi kerak emas.";

    /// <summary>O'rinbosar rozi bo'lganda unga tasdiq.</summary>
    /// <param name="groupName">Guruh nomi.</param>
    public static string SubstituteAcceptedText(string groupName) =>
        $"✅ Rahmat! Siz <b>{NotificationText.Parameter(groupName, 100)}</b> darsini o'tib berasiz.";

    /// <summary>O'quv bo'limiga: ustoz darsga o'ta olmaydi.</summary>
    /// <param name="teacherName">Ustoz F.I.Sh.</param>
    /// <param name="affectedCount">Ta'sirlangan darslar soni.</param>
    /// <param name="reason">Sabab.</param>
    /// <param name="days">Necha kunga.</param>
    public static string TeacherDeclinedSessionText(
        string teacherName, int affectedCount, string reason, int days)
    {
        var name = NotificationText.Parameter(teacherName);
        var why = NotificationText.Parameter(reason, 300);
        var dayText = days <= 1 ? "bugun" : $"{days} kunga";

        return $"⚠️ <b>{name}</b> {dayText} <b>{affectedCount}</b> ta darsga o'ta olmaydi.\n\n"
            + $"Sabab: {why}";
    }

    /// <summary>O'quv bo'limiga: o'rinbosar topildi.</summary>
    /// <param name="substituteName">O'rinbosar F.I.Sh.</param>
    /// <param name="originalTeacherName">Asl ustoz F.I.Sh.</param>
    /// <param name="groupName">Guruh nomi.</param>
    public static string SubstituteFoundText(
        string substituteName, string originalTeacherName, string groupName) =>
        $"✅ <b>{NotificationText.Parameter(substituteName)}</b> "
        + $"<b>{NotificationText.Parameter(originalTeacherName)}</b> o'rniga "
        + $"<b>{NotificationText.Parameter(groupName, 100)}</b> darsini o'tib beradi.";

    /* ===== /2026-08-17 ===== */

    /* ===== 2026-08-18 · JONLI DARS BOSHLANDI ===== */

    /// <summary>
    /// «Darsingiz boshlandi» xabari.
    ///
    /// ★ SARLAVHA IXTIYORIY: ko'p darslarda u bo'sh va o'shanda guruh
    /// nomining O'ZI yetarli. Bo'sh sarlavha uchun alohida qator
    /// chizilsa, xabar "yarim to'ldirilgan" ko'rinardi.
    /// </summary>
    /// <param name="groupName">Guruh nomi.</param>
    /// <param name="title">Dars sarlavhasi (bo'lmasligi mumkin).</param>
    /// <summary>
    /// Sabab qabul qilingani haqida qisqa tasdiq (2026-08-18).
    ///
    /// ★ QISQA VA YAKUNIY: "kuratoringiz ko'radi" deyilishi o'quvchiga
    /// ish tugaganini bildiradi. Aks holda u sababini qayta-qayta
    /// yozib yuborardi.
    /// </summary>
    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// TAYYOR SABABLAR — BIR BOSISHLIK JAVOB (2026-08-18)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Loyiha egasi: *"javob yoki sababini yuborish degan tugma bo'lsin,
    /// bo'lmasa o'quvchi sababini qanday yozib yuborishni tushunmasligi
    /// mumkin"*.
    ///
    /// ★ NEGA MATNNING O'ZI YETARLI EMAS: "sababini shu yerga yozing"
    /// degan yo'riqni o'qish va bajarish — ikki qadam. Telegramda
    /// odam tugma ko'rsa bosadi, matn ko'rsa ko'pincha o'tkazib
    /// yuboradi. Tugma javob berish darajasini keskin oshiradi, bu esa
    /// kuratorning qo'ng'iroq ro'yxatini to'g'ridan-to'g'ri qisqartiradi.
    ///
    /// ★ TAYYOR SABABLAR + ERKIN MATN: eng ko'p uchraydigan ikkitasi bir
    /// bosishda tugaydi, qolgani uchun "boshqa sabab" tugmasi yo'riqni
    /// takrorlaydi.
    ///
    /// Kalitlar QISQA: `callback_data` chegarasi — 64 bayt.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Label, string Text)> AbsenceReasons =
    [
        ("ill", "🤒 Kasal bo'ldim", "Kasal bo'ldim"),
        ("busy", "📌 Bandlik bo'ldi", "Oilaviy/ish bandligi bo'ldi"),
    ];

    /// <summary>"Boshqa sabab" tugmasining kodi — erkin matn so'raladi.</summary>
    public const string AbsenceReasonOther = "other";

    /// <summary>Kelmaganlik xabari uchun inline tugmalar.</summary>
    /// <param name="noticeId">Qaysi xabarga javob berilyapti.</param>
    public static string AbsenceReasonButtons(long noticeId)
    {
        var quick = AbsenceReasons
            .Select(r => (r.Label, $"ab:r:{noticeId.ToString(CultureInfo.InvariantCulture)}:{r.Code}"))
            .ToList();

        return EncodeButtons(
        [
            quick,
            [("✍️ Boshqa sabab — yozaman",
              $"ab:r:{noticeId.ToString(CultureInfo.InvariantCulture)}:{AbsenceReasonOther}")],
        ]);
    }

    /// <summary>"Boshqa sabab" bosilganda ko'rsatiladigan yo'riq.</summary>
    public static string AbsenceReplyPromptText() =>
        "✍️ <b>Sababingizni shu yerga yozing.</b>\n\n"
        + "Xabar yozib, yuborish tugmasini bosing — kuratoringiz uni ko'radi.";

    public static string AbsenceReplySavedText() =>
        "✅ <b>Rahmat, sababingiz qabul qilindi.</b>\n\n"
        + "Uni kuratoringiz ko'radi. Keyingi darsni qoldirmang!";

    public static string SessionStartedText(string groupName, string? title)
    {
        var group = NotificationText.Parameter(groupName, 100);
        var lesson = NotificationText.Parameter(title, 150);

        var text = "🔴 <b>Dars boshlandi</b>\n\n"
            + $"👥 {group}";

        if (lesson.Length > 0) text += $"\n📚 {lesson}";

        return text + "\n\nDarsga qo'shilish uchun quyidagi tugmani bosing.";
    }

    /* ===== /2026-08-18 ===== */

    /* ===== 2026-10-23 · BALL TIZIMI ===== */

    private static readonly string[] MonthNames =
    [
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
        "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr",
    ];

    /// <summary>
    /// Darsdan keyingi ball xabari.
    ///
    /// ★ OHANG — AYBLOVSIZ: kelmagan o'quvchiga ham "keyingi darsda kutamiz"
    ///   deyiladi; minus ball yashirilmaydi (tizim shaffof bo'lishi kerak),
    ///   lekin xabar uni sharmanda qilmaydi.
    /// </summary>
    /// <param name="studentName">O'quvchi ismi.</param>
    /// <param name="groupName">Guruh nomi.</param>
    /// <param name="attended">Darsga keldimi.</param>
    /// <param name="lessonPoints">Shu dars uchun jami (davomat + bonus + baho).</param>
    /// <param name="streak">Ketma-ket qatnashilgan darslar (kelgan bo'lsa).</param>
    /// <param name="streakBonus">Shu darsda berilgan bonus (bo'lmasa <c>null</c>).</param>
    /// <param name="month">Oy raqami (1..12).</param>
    /// <param name="monthTotal">Oydagi jami ball.</param>
    public static string PointsLessonText(
        string studentName, string groupName, bool attended, int lessonPoints,
        int streak, int? streakBonus, int month, int monthTotal)
    {
        var name = NotificationText.Parameter(studentName, 60);
        var group = NotificationText.Parameter(groupName, 100);
        var monthName = MonthNames[Math.Clamp(month, 1, 12) - 1];
        var signed = lessonPoints > 0 ? $"+{lessonPoints}" : lessonPoints.ToString(CultureInfo.InvariantCulture);

        string text;
        if (attended)
        {
            text = $"✅ {name}, «{group}» darsida qatnashdingiz: <b>{signed}</b> ball";

            if (streakBonus is { } bonus)
                text += $"\n🔥 Ketma-ket {streak} dars — <b>+{bonus}</b> bonus!";
            else if (streak >= 2)
                text += $"\n🔥 Ketma-ket {streak} dars";
        }
        else
        {
            text = $"{name}, «{group}» darsiga kelmadingiz: <b>{signed}</b> ball.\n"
                + "Keyingi darsda kutamiz — har bir kelgan dars uchun +10 ball.";
        }

        return text + $"\n\n🏆 {monthName}: jami <b>{monthTotal}</b> ball";
    }

    /* ===== /2026-10-23 ===== */

    /* ===== 2026-10-29: onlayn sotuv ===== */

    /// <summary>Xodimlar guruhiga: yangi buyurtma.</summary>
    public static string ShopOrderStaffText(
        string code, string studentName, string? phone, string? telegramUsername,
        string planName, string courseName, decimal amount)
    {
        var contact = NotificationText.Parameter(phone ?? "—", 32);
        if (!string.IsNullOrWhiteSpace(telegramUsername))
            contact += $", @{NotificationText.Parameter(telegramUsername, 32)}";

        return $"🛒 <b>Yangi buyurtma {NotificationText.Parameter(code, 12)}</b>\n"
            + $"👤 {NotificationText.Parameter(studentName, 80)} ({contact})\n"
            + $"📦 {NotificationText.Parameter(planName, 100)} — {NotificationText.Parameter(courseName, 100)}\n"
            + $"💰 <b>{FormatMoney(amount)} so'm</b>\n\n"
            + "Payme/Click xabarlarida shu summani toping va tasdiqlang.";
    }

    /// <summary>Xodimlar guruhiga: o'quvchi «To'ladim» ni bosdi.</summary>
    public static string ShopOrderPaidClaimText(string code, string studentName, string planName, decimal amount) =>
        $"💳 <b>{NotificationText.Parameter(code, 12)}</b> — o'quvchi to'lov qildim dedi\n"
        + $"👤 {NotificationText.Parameter(studentName, 80)}\n"
        + $"📦 {NotificationText.Parameter(planName, 100)} · <b>{FormatMoney(amount)} so'm</b>\n\n"
        + "Payme/Click xabarlarida tekshirib, tasdiqlang.";

    /// <summary>Xodim tugmalari.</summary>
    public static string ShopOrderButtons(long orderId) =>
        EncodeButtons([[
            ("✅ Tasdiqlash", string.Create(CultureInfo.InvariantCulture, $"so:ok:{orderId}")),
            ("❌ Rad etish", string.Create(CultureInfo.InvariantCulture, $"so:no:{orderId}")),
        ]]);

    /// <summary>Xodimlar guruhiga: buyurtma hal qilindi.</summary>
    public static string ShopOrderDecidedText(string code, bool confirmed, string actorName) =>
        (confirmed ? "✅ " : "❌ ")
        + $"<b>{NotificationText.Parameter(code, 12)}</b> "
        + (confirmed ? "tasdiqlandi" : "rad etildi")
        + $" — {NotificationText.Parameter(actorName, 80)}";

    /// <summary>O'quvchiga: to'lov tasdiqlandi.</summary>
    public static string ShopOrderConfirmedText(string code, string planName, bool selfStudy) =>
        $"✅ To'lovingiz tasdiqlandi (<b>{NotificationText.Parameter(code, 12)}</b>).\n"
        + $"📦 {NotificationText.Parameter(planName, 100)}\n\n"
        + (selfStudy
            ? "Darslar ochildi — ilovani oching va o'qishni boshlang!"
            : "To'lov hisobingizga tushdi. O'quv bo'limi sizni guruhga joylab, dars vaqtini xabar qiladi.");

    /// <summary>O'quvchiga: to'lov topilmadi.</summary>
    public static string ShopOrderRejectedText(string code, string? reason) =>
        $"❌ <b>{NotificationText.Parameter(code, 12)}</b> buyurtmasi bo'yicha to'lov tasdiqlanmadi."
        + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $"\nSabab: {NotificationText.Parameter(reason, 300)}")
        + "\n\nTo'lagan bo'lsangiz, chekni o'quv bo'limiga yuboring.";

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

    /* ===== /2026-10-29 ===== */
}

/// <summary>
/// Xabar bilan birga ko'rsatiladigan tugma turi.
/// Telegram'ning <c>reply_markup</c> obyektiga yuboruvchi ichida aylanadi.
/// </summary>
public enum TelegramMarkup
{
    /// <summary>Tugma yo'q.</summary>
    None = 0,

    /// <summary>
    /// «📱 Raqamni ulashish» — <c>request_contact</c> tugmali klaviatura.
    /// Telefon FAQAT shu yo'l bilan olinadi: Telegram raqamning AYNAN shu
    /// akkauntga tegishli ekanini kafolatlaydi.
    /// </summary>
    RequestContact = 1,

    /// <summary>«🚀 Ilovani ochish» — Mini App'ni ochuvchi inline tugma.</summary>
    OpenApp = 2,

    /// <summary>Ekran ostidagi klaviaturani olib tashlaydi.</summary>
    RemoveKeyboard = 3,

    /// <summary>
    /// Dinamik inline tugmalar (2026-08-17, ustoz kunlik tasdiqlash) —
    /// mazmuni <c>OutboxMessage.CallbackData</c> dan o'qiladi
    /// (<see cref="TelegramTemplates.EncodeButtons"/>).
    /// </summary>
    InlineButtons = 4,
}
