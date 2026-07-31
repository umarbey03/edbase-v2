namespace Zinnur.Infrastructure.Options;

/// <summary>
/// Telegram bot va Mini App sozlamalari (<c>Telegram:*</c>).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ IXTIYORIY, LEKIN «TO'LIQ YOKI BO'SH» (<c>StorageOptions</c> falsafasi).
///
/// Bo'sh bo'lsa ilova ODATDAGIDEK ko'tariladi: webhook endpointi 404
/// qaytaradi, Mini App kirishi 503 beradi, xabarlar esa vaqtinchalik
/// log-yuboruvchiga tushadi. Dev mashinasida hech kimda bot tokeni yo'q va
/// bu butun platformani to'xtatib qo'ymasligi kerak.
///
/// Yarim to'ldirilgan bo'lsa integratsiya INERT bo'ladi:
/// <see cref="IsConfigured"/> IKKALASINI ham talab qiladi, ya'ni "token bor,
/// sir yo'q" holatida webhook OCHIQ qolmaydi — controller uni 404 qiladi.
///
/// ⚠️ «TO'LIQ yoki BO'SH» qoidasi endi ISHGA TUSHISHDA emas, YOZISH paytida
/// qo'riqlanadi (<c>SettingCoupling</c>): token va sir bazadan keladi va ishga
/// tushish paytida ular hali o'qilgan ham bo'lmaydi. Batafsil sabab —
/// <c>DependencyInjection.AddOptions</c>.
///
/// ★★ QIYMATLAR ISH JARAYONIDA O'ZGARADI: iste'molchilar
/// <c>IRuntimeOptions&lt;TelegramOptions&gt;</c> ni o'qiydi (baza ustun),
/// <c>IOptions&lt;TelegramOptions&gt;</c> esa faqat BOSHLANG'ICH qiymat manbai.
///
/// ★ SIRLAR HECH QACHON LOGGA TUSHMAYDI. <see cref="BotToken"/> so'rov
/// URL'ining ICHIDA bo'lgani uchun (`/bot<token>/sendMessage`) yuboruvchi
/// URL'ni HECH QAYERGA yozmaydi va xato matnlarini
/// <c>TelegramMessageSender.Redact</c> orqali o'tkazadi. Sentry tomonida
/// esa `Telegram:BotToken` / `Telegram:WebhookSecret` kalitlari
/// `SentryScrubber` ning "token"/"secret" bo'laklariga tushib, avtomatik
/// tozalanadi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>
    /// BotFather bergan token: <c>123456789:AA...</c>.
    /// Ham Bot API chaqiruvlari, ham <c>initData</c> imzosi kaliti manbai.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Webhook siri. Telegram uni har so'rovda
    /// <c>X-Telegram-Bot-Api-Secret-Token</c> sarlavhasida qaytaradi
    /// (<c>setWebhook</c> da o'rnatiladi).
    ///
    /// ★ MAJBURIY: usiz webhook manzilini bilgan istalgan odam qalbaki
    /// "kontakt ulashildi" yangilanishini yuborardi. Bo'sh bo'lsa endpoint
    /// UMUMAN ishlamaydi — ochiq qolgandan ko'ra o'chiq bo'lgani xavfsiz.
    ///
    /// Telegram ruxsat etgan belgilar: <c>A-Z a-z 0-9 _ -</c>, 1..256 belgi.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Mini App'ning to'liq manzili (<c>https://...</c>) — botdagi
    /// «🚀 Ilovani ochish» tugmasi shu yerga olib boradi.
    ///
    /// IXTIYORIY: bo'sh bo'lsa tugma umuman qo'shilmaydi (xabar matni esa
    /// baribir ketadi). Telegram <c>web_app</c> tugmasi uchun FAQAT
    /// <c>https</c> qabul qiladi — <c>http</c> bo'lsa Bot API xabarni
    /// 400 bilan rad etadi, ya'ni tugma bilan birga MATN ham yo'qolardi.
    /// </summary>
    public string MiniAppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Bot foydalanuvchi nomi (<c>@</c> siz) — frontend uchun "botni ochish"
    /// havolasini yasashda ishlatiladi. Xavfsizlikka ta'siri yo'q.
    /// </summary>
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>
    /// Bot API manzili. Sozlanadigan, chunki (a) katta yuklamada Telegram
    /// o'zining lokal <c>telegram-bot-api</c> serverini tavsiya qiladi,
    /// (b) testda soxta serverga yo'naltirish mumkin bo'ladi.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>
    /// Bitta <c>sendMessage</c> uchun timeout (sekund).
    /// MAJBURIY: Telegram javob bermay qolsa so'rov mangu osilib turardi va
    /// fon worker'i butunlay to'xtardi.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// <c>initData</c> imzosining eng katta yoshi (soat).
    ///
    /// ★ NIMA UCHUN CHEGARA BOR: imzo o'z-o'zidan muddatsiz. Bir marta
    /// o'g'irlangan <c>initData</c> (qurilma logidan, ekran yozuvidan)
    /// ABADIY yaroqli qolardi — ya'ni akkaunt umrbod egallab olinardi.
    /// </summary>
    public int InitDataMaxAgeHours { get; set; } = 24;

    /// <summary>Telegram funksiyalari yoqilganmi (ikkala majburiy qiymat bor).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BotToken)
        && !string.IsNullOrWhiteSpace(WebhookSecret);

    // ⚠️ `IsPartiallyConfigured` BU YERDAN OLIB TASHLANDI — qoida endi
    // `SettingCoupling` da (sabab `StorageOptions` dagi bilan bir xil:
    // ikkinchi nusxa birinchisidan chetga chiqardi).

    /// <summary>
    /// Token shakli oqilonami: <c>&lt;raqamlar&gt;:&lt;kalit&gt;</c>, bo'shliqsiz.
    /// Tekshiruv ATAYLAB YUMSHOQ — Telegram formatni kelajakda o'zgartirsa,
    /// ishlab turgan tizim shu sababdan ko'tarilmay qolmasin. Maqsad —
    /// "bo'sh joy qo'shib qo'yildi" turkumidagi xatoni ishga tushishda tutish.
    /// </summary>
    public bool HasValidBotToken =>
        string.IsNullOrWhiteSpace(BotToken)
        || (BotToken.Length >= MinBotTokenLength
            && BotToken.Contains(':', StringComparison.Ordinal)
            && !BotToken.Any(char.IsWhiteSpace));

    /// <summary>
    /// Sir Telegram ruxsat etgan belgilardan iboratmi. Aks holda
    /// <c>setWebhook</c> uni QABUL QILMAYDI va bot jimgina ishlamay qolardi.
    /// </summary>
    public bool HasValidWebhookSecret =>
        string.IsNullOrWhiteSpace(WebhookSecret)
        || (WebhookSecret.Length <= MaxWebhookSecretLength
            && WebhookSecret.All(ch =>
                char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-'));

    /// <summary>Mini App manzili bo'sh yoki absolyut <c>https</c> bo'lishi shart.</summary>
    public bool HasValidMiniAppUrl =>
        string.IsNullOrWhiteSpace(MiniAppUrl)
        || (Uri.TryCreate(MiniAppUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Bot API manzili absolyut <c>http(s)</c> bo'lishi shart.</summary>
    public bool HasValidApiBaseUrl =>
        Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Imzo yoshi chegarasi — <c>TimeSpan</c> ko'rinishida.</summary>
    public TimeSpan InitDataMaxAge =>
        TimeSpan.FromHours(Math.Clamp(InitDataMaxAgeHours, 1, 168));

    /// <summary>Eng qisqa oqilona token uzunligi (`12345:AA...`).</summary>
    private const int MinBotTokenLength = 20;

    /// <summary>Telegram hujjatidagi chegara.</summary>
    private const int MaxWebhookSecretLength = 256;
}
