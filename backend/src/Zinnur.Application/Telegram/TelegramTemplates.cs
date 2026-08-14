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

        _ => TelegramMarkup.None,
    };

    // ---------------------------------------------------------------- matnlar

    /// <summary>Bog'lanmagan foydalanuvchi uchun salom va yo'riq.</summary>
    public static string StartUnlinkedText() =>
        "Assalomu alaykum! 👋\n"
        + "Bu — <b>ZIN-NUR Online</b> o'quv platformasining rasmiy boti.\n\n"
        + "Tizimga kirish uchun pastdagi <b>«📱 Raqamni ulashish»</b> tugmasini bosing.\n\n"
        + "⚠️ Raqamni <b>qo'lda yozib bo'lmaydi</b> — u faqat shu tugma orqali "
        + "ulashiladi. Shu tufayli hech kim begona raqamni kiritib, "
        + "boshqa odamning profiliga kira olmaydi.";

    /// <summary>Allaqachon bog'langan o'quvchi uchun salom.</summary>
    public static string StartLinkedText(string? fullName) =>
        $"Assalomu alaykum, <b>{NotificationText.Parameter(fullName)}</b>! 👋\n"
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
}
