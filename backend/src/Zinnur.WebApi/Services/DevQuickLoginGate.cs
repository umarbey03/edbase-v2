namespace Zinnur.WebApi.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 TEST UCHUN BIR BOSISHDA KIRISH — DARVOZA (GATE)
/// ════════════════════════════════════════════════════════════════════════
///
/// Bu sinf HECH QANDAY token bermaydi va bazaga ham tegmaydi. U faqat
/// BITTA savolga javob beradi: "quick-login endpointi UMUMAN mavjudmi?".
/// Xususiyat aslida AUTENTIFIKATSIYANI CHETLAB O'TISH bo'lgani uchun
/// darvoza feature'ning O'ZIDAN ALOHIDA turadi — shunda uni o'qish,
/// tekshirish va kerak bo'lsa butunlay o'chirish bitta joyda bo'ladi.
///
/// ──────────────────────────────────────────────────────────────────────
/// NIMA UCHUN BU XUSUSIYAT BOR (loyiha egasining talabi, 2026-08-14)
///
/// Kirishning yagona yo'li — telefon + Telegram'ga keladigan kod. Ya'ni
/// har bir ekranni "kurator ko'zi bilan" ko'rish uchun HAQIQIY telefon
/// va ISHLAYOTGAN bot kerak. Dev mashinasida bot tokeni soxta, kod esa
/// `MessageOutbox` jadvalida qoladi — tekshiruvchi har rol uchun SQL
/// so'rov yozishga majbur. Egasining so'zi: *"real telefon va bot bilan
/// sinash qiyin"*.
///
/// ──────────────────────────────────────────────────────────────────────
/// 🔴 UCHTA MUSTAQIL SHART — VA UCHALASI HAM BAJARILISHI KERAK
///
/// Shartlar ATAYLAB bir-biriga bog'liq emas: bittasi xato sozlansa
/// qolgan ikkitasi hamon ushlab turadi.
///
///   1) OSHKOR KALIT — <see cref="EnabledKey"/> (<c>Dev__QuickLogin</c>).
///      Standart qiymat — <c>false</c>. "Hech nima qilmaslik" —
///      harakatsizlikdagi holat; xususiyat faqat kimdir uni ATAYLAB
///      so'raganda paydo bo'ladi. (Bu — `Seed__Demo` bilan AYNI naqsh.)
///
///   2) MUHIT <c>Production</c> EMAS. Bu shart 1-shartni RAD ETA OLADI:
///      kalit yoqiq bo'lsa ham prod'da endpoint yo'q.
///
///      ★ EGASIGA OCHIQ AYTILADI: uning yangi serveri
///        <c>ASPNETCORE_ENVIRONMENT=Production</c> bilan ishlashi mumkin
///        va U HOLDA BU TUGMALAR CHIQMAYDI. Bu — kamchilik emas, aynan
///        maqsad. Ko'rish kerak bo'lsa muhitni <c>Staging</c> ga
///        o'tkazish YETARLI (`IsProduction()` faqat `Production` nomiga
///        qaraydi) — himoyani yumshatish shart emas.
///
///   3) FAQAT NAMUNAVIY HISOBLAR (bu darvozada emas, so'rovni
///      bajaradigan `DevQuickLoginService` da — u har QATORNI alohida
///      tekshiradi). Sabab va nima uchun aynan Telegram ID diapazoni —
///      `DemoDataSeeder.DemoTelegramIdMin` izohida.
///
/// ──────────────────────────────────────────────────────────────────────
/// ★ QIYMAT BIR MARTA — ISHGA TUSHISHDA — O'QILADI
///
/// `IConfiguration` har so'rovda qayta o'qilsa, kalitni ish paytida
/// yoqish mumkin bo'lardi. Sozlamalar panelidan (`SettingsController`)
/// yozilishi mumkin bo'lgan runtime qiymatlardan farqli o'laroq, BU
/// kalit faqat qayta ishga tushirish bilan o'zgaradi — ya'ni u har doim
/// startdagi BALAND OVOZLI ogohlantirish bilan juft yuradi.
/// </summary>
public sealed class DevQuickLoginGate
{
    /// <summary>
    /// Konfiguratsiya kaliti. Muhit o'zgaruvchisi ko'rinishi —
    /// <c>Dev__QuickLogin</c> (<c>__</c> — .NET dagi bo'lim ajratgichi).
    /// </summary>
    public const string EnabledKey = "Dev:QuickLogin";

    public DevQuickLoginGate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        FlagRequested = ReadFlag(configuration);
        EnvironmentName = environment.EnvironmentName;
        IsProduction = environment.IsProduction();
    }

    /// <summary>Kalit so'ralganmi (muhit shartidan QAT'I NAZAR).</summary>
    /// <remarks>
    /// Alohida saqlanadi, chunki "so'raldi, lekin prod'da rad etildi"
    /// holati LOGDA ko'rinishi kerak: aks holda operator kalitni qo'yib,
    /// tugmalar chiqmaganda sababini topa olmasdi.
    /// </remarks>
    public bool FlagRequested { get; }

    /// <summary>Joriy muhit nomi (javobda ham ko'rsatiladi).</summary>
    public string EnvironmentName { get; }

    /// <summary>Muhit <c>Production</c> mi.</summary>
    public bool IsProduction { get; }

    /// <summary>
    /// Endpoint mavjudmi. IKKALA shart ham bajarilishi kerak.
    /// </summary>
    public bool IsEnabled => FlagRequested && !IsProduction;

    /// <summary>
    /// Ishga tushishda holatni logga yozadi.
    ///
    /// 🔴 YOQILGAN HOLAT — <c>Warning</c>, ATAYLAB. Bu satr prod'ga
    /// noto'g'ri sozlama bilan chiqqan konteynerni ko'rsatadigan YAGONA
    /// signal. <c>Information</c> bo'lsa u boshqa yuzlab satr orasida
    /// yo'qolardi va hech kim sezmasdi.
    /// </summary>
    public void LogStartupState(ILogger logger)
    {
        if (IsEnabled)
        {
            ApiLog.DevQuickLoginEnabled(logger, EnvironmentName, EnabledKey);
            return;
        }

        // Kalit so'ralgan, lekin muhit rad etdi — sabab logda qolsin.
        if (FlagRequested)
            ApiLog.DevQuickLoginRefused(logger, EnvironmentName, EnabledKey);
    }

    /// <summary>
    /// Kalitni o'qiydi. <c>"1"</c> ham qabul qilinadi — Docker Compose va
    /// CI fayllarida odatiy yozuv (`DemoDataSeeder.IsEnabled` bilan AYNI
    /// qoida: ikki kalit bir xil o'qilsin, aks holda operator birini
    /// yoqib, ikkinchisi ishlamaganda sababini topmasdi).
    /// </summary>
    private static bool ReadFlag(IConfiguration configuration)
    {
        var raw = configuration[EnabledKey];

        if (string.IsNullOrWhiteSpace(raw)) return false;

        return bool.TryParse(raw, out var value)
            ? value
            : string.Equals(raw.Trim(), "1", StringComparison.Ordinal);
    }
}
