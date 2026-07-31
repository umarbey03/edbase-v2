using System.Globalization;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Notifikatsiya navbatining MUHITGA oid sozlamalari (<c>Notifications:*</c>).
///
/// ★ NIMA UCHUN QIYMATLAR QO'LDA O'QILADI (<c>Bind</c> emas): bo'sh muhit
/// o'zgaruvchisi (<c>Notifications__BatchSize=</c>) konfiguratsiya
/// bog'lagichida istisno tashlaydi va ilova UMUMAN ko'tarilmay qoladi.
/// Xabar yuborish — yordamchi funksiya; uning sozlamasidagi xato butun
/// platformani to'xtatib qo'yishi mumkin emas. Buzuq qiymat shunchaki
/// standartga tushadi (<c>SentrySetup.ParseSampleRate</c> bilan bir xil
/// yondashuv).
/// </summary>
internal sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// Fon worker'i ishga tushsinmi. <c>false</c> — navbatga yozish
    /// ishlayveradi, faqat yuborilmaydi. Testlar aylanishni o'zi
    /// chaqirgani uchun aynan shu rejimda ishlaydi.
    /// </summary>
    public bool Enabled { get; private init; } = true;

    /// <summary>Bir aylanishda olinadigan xabarlar soni.</summary>
    public int BatchSize { get; private init; } = 50;

    /// <summary>Navbat bo'sh bo'lganda keyingi tekshiruvgacha kutish (sekund).</summary>
    public int PollSeconds { get; private init; } = 5;

    /// <summary>
    /// Band qilish muddati (sekund): shu vaqt ichida olingan xabarni
    /// boshqa instance ko'rmaydi. Bitta xabarni yuborish vaqtidan
    /// sezilarli uzun bo'lishi kerak — aks holda sekin javob bergan
    /// Telegram tufayli xabar ikkinchi worker'ga ham tushib, IKKI MARTA
    /// yuborilardi.
    /// </summary>
    public int LeaseSeconds { get; private init; } = 120;

    /// <summary>Kanalning umumiy tezligi (xabar/sekund) — Redis'da hisoblanadi.</summary>
    public int RatePerSecond { get; private init; } = 25;

    /// <summary>Bir zumdagi eng katta portlash (token chelagining hajmi).</summary>
    public int RateBurst { get; private init; } = 30;

    public TimeSpan Poll => TimeSpan.FromSeconds(PollSeconds);

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);

    /// <summary>Konfiguratsiyadan o'qiydi; yo'q yoki buzuq qiymat standartga tushadi.</summary>
    public static NotificationsOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var defaults = new NotificationsOptions();

        return new NotificationsOptions
        {
            Enabled = Flag(configuration, $"{SectionName}:Enabled", defaults.Enabled),

            // Yuqori chegaralar ATAYLAB: 1000 lik paket bitta tranzaksiyada
            // uzoq qulf ushlab turardi, 1 soatlik "poll" esa eslatmani
            // butunlay ma'nosiz qilardi.
            BatchSize = Number(configuration, $"{SectionName}:BatchSize", defaults.BatchSize, 1, 500),
            PollSeconds = Number(configuration, $"{SectionName}:PollSeconds", defaults.PollSeconds, 1, 300),
            LeaseSeconds = Number(configuration, $"{SectionName}:LeaseSeconds", defaults.LeaseSeconds, 5, 3600),
            RatePerSecond = Number(
                configuration, $"{SectionName}:RateLimit:PerSecond", defaults.RatePerSecond, 1, 1000),
            RateBurst = Number(
                configuration, $"{SectionName}:RateLimit:Burst", defaults.RateBurst, 1, 10_000),
        };
    }

    private static int Number(IConfiguration configuration, string key, int fallback, int min, int max) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static bool Flag(IConfiguration configuration, string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value) ? value : fallback;
}
