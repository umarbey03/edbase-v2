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

    /// <summary>Raqam xodim profiliga tegishli — Telegram orqali kirish yo'q.</summary>
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
        + "Endi ilovaga <b>parolsiz</b> kirasiz — quyidagi tugmani bosing.";

    /// <summary>Raqam bazada topilmadi.</summary>
    public static string ContactUnknownText() =>
        "❌ Bu raqam ro'yxatda yo'q.\n\n"
        + "Iltimos, <b>o'quv bo'limiga murojaat qiling</b> va ro'yxatdagi "
        + "telefon raqamingizni tekshiring. Raqam to'g'rilangach, qaytadan urinib ko'ring.";

    /// <summary>Raqam xodimga tegishli.</summary>
    public static string ContactStaffText() =>
        "Bu raqam <b>xodim</b> profiliga tegishli.\n\n"
        + "Xodimlar tizimga <b>email va parol</b> bilan kiradi. "
        + "Telegram orqali kirish faqat o'quvchilar uchun.";

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
