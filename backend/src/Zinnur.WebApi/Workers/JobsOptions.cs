using System.Globalization;
using Zinnur.Application.Jobs;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Fon vazifalarining MUHITGA oid sozlamalari (<c>Jobs:*</c>).
///
/// ★ NIMA UCHUN QIYMATLAR QO'LDA O'QILADI (<c>Bind</c> emas): bo'sh muhit
/// o'zgaruvchisi (<c>Jobs__TickSeconds=</c>) konfiguratsiya bog'lagichida
/// istisno tashlaydi va ilova UMUMAN ko'tarilmay qoladi. Fon vazifalari —
/// yordamchi funksiya; ularning sozlamasidagi xato butun platformani
/// to'xtatib qo'yishi mumkin emas. Buzuq qiymat shunchaki standartga
/// tushadi (<c>NotificationsOptions</c> bilan bir xil yondashuv).
///
/// ★ HAR CHEGARA CHEKLANGAN (<c>Math.Clamp</c>): masalan
/// <c>GraceMinutes=0</c> hali davom etayotgan darsni ruxsat etilgan tugash
/// payti bilan bir onda uzib qo'yardi va o'quvchilar ekranidan video
/// yo'qolardi. Xato konfiguratsiya halokatga olib bormasin — pastki
/// chegaralar shuning uchun bor.
/// </summary>
internal sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>
    /// Rejalashtiruvchi ishga tushsinmi. <c>false</c> — vazifalar DI'da
    /// qoladi (testlar ularni O'ZI chaqiradi), lekin fon sikli yurmaydi.
    /// Integratsiya testlarida aynan shu rejim: fon xizmati parallel ishlab
    /// tursa, test yaratgan darsni "o'g'irlab" yakunlab qo'yishi mumkin edi
    /// va testlar tasodifiy (flaky) bo'lardi.
    /// </summary>
    public bool Enabled { get; private init; } = true;

    /// <summary>
    /// Rejalashtiruvchi necha sekundda bir uyg'onadi. Bu vazifalarning
    /// yurish tezligi EMAS — har vazifaning O'Z oralig'i bor; tick esa
    /// shunchaki eng mayda o'lchov birligi.
    /// </summary>
    public int TickSeconds { get; private init; } = 30;

    // ---------------------------------------------------------------- darslar

    public bool SessionAutoCloseEnabled { get; private init; } = true;

    /// <summary>Darslarni tekshirish oralig'i (sekund).</summary>
    public int SessionIntervalSeconds { get; private init; } = 60;

    /// <summary>
    /// Dars RUXSAT ETILGAN tugash paytidan (<c>EndsAt</c>) keyin qancha
    /// kutiladi. Sabab va tanlov izohi — <see cref="SessionAutoCloseJob"/>.
    /// </summary>
    public int SessionGraceMinutes { get; private init; } = 60;

    /// <summary>Bir yurishda ko'pi bilan nechta dars.</summary>
    public int SessionBatchSize { get; private init; } = 100;

    // ---------------------------------------------------------------- moliya

    public bool MonthlyBillingEnabled { get; private init; } = true;

    /// <summary>Oylik yozuvlarni tekshirish oralig'i (daqiqa).</summary>
    public int BillingIntervalMinutes { get; private init; } = 30;

    // ---------------------------------------------------------------- hosila

    public TimeSpan Tick => TimeSpan.FromSeconds(TickSeconds);

    public SessionAutoCloseSettings SessionAutoClose => new(
        Grace: TimeSpan.FromMinutes(SessionGraceMinutes),
        BatchSize: SessionBatchSize,
        Interval: TimeSpan.FromSeconds(SessionIntervalSeconds));

    public MonthlyBillingSettings MonthlyBilling =>
        new(Interval: TimeSpan.FromMinutes(BillingIntervalMinutes));

    /// <summary>Konfiguratsiyadan o'qiydi; yo'q yoki buzuq qiymat standartga tushadi.</summary>
    public static JobsOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var defaults = new JobsOptions();

        return new JobsOptions
        {
            Enabled = Flag(configuration, $"{SectionName}:Enabled", defaults.Enabled),
            TickSeconds = Number(
                configuration, $"{SectionName}:TickSeconds", defaults.TickSeconds, 1, 3600),

            SessionAutoCloseEnabled = Flag(
                configuration, $"{SectionName}:SessionAutoClose:Enabled",
                defaults.SessionAutoCloseEnabled),

            SessionIntervalSeconds = Number(
                configuration, $"{SectionName}:SessionAutoClose:IntervalSeconds",
                defaults.SessionIntervalSeconds, 10, 3600),

            // ★ PASTKI CHEGARA 5 DAQIQA — ATAYLAB. Kichikroq qiymat hali
            // davom etayotgan darsni uzib qo'yish xavfini keskin oshiradi.
            SessionGraceMinutes = Number(
                configuration, $"{SectionName}:SessionAutoClose:GraceMinutes",
                defaults.SessionGraceMinutes, 5, 1440),

            SessionBatchSize = Number(
                configuration, $"{SectionName}:SessionAutoClose:BatchSize",
                defaults.SessionBatchSize, 1, 500),

            MonthlyBillingEnabled = Flag(
                configuration, $"{SectionName}:MonthlyBilling:Enabled",
                defaults.MonthlyBillingEnabled),

            BillingIntervalMinutes = Number(
                configuration, $"{SectionName}:MonthlyBilling:IntervalMinutes",
                defaults.BillingIntervalMinutes, 1, 1440),
        };
    }

    private static int Number(IConfiguration configuration, string key, int fallback, int min, int max) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static bool Flag(IConfiguration configuration, string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value) ? value : fallback;
}
