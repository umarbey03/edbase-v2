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

    // ---------------------------------------------------------------- ustoz kunlik tasdiqlash

    public bool TeacherMorningCheckinEnabled { get; private init; } = true;

    /// <summary>Ertalabki oynani tekshirish oralig'i (daqiqa).</summary>
    public int TeacherMorningCheckinIntervalMinutes { get; private init; } = 15;

    // ---------------------------------------------------------------- chat tarixi
    //
    // 🔴 BU YERDA "Enabled" BAYROG'I YO'Q — VA BU ATAYLAB. Tozalash
    // yoqilgan-yoqilmagani ADMINISTRATOR qarori va u paneldagi
    // `chat.retention_enabled` kalitida (vazifa uni HAR YURISHDA o'qiydi).
    // Bu yerga muhit bayrog'i qo'yilsa, panel hech kim qaramaydigan
    // kalitni tahrirlab, "yoqdim, lekin ishlamadi" holatini yaratardi.
    // Quyidagilar esa faqat EKSPLUATATSIYA parametrlari.

    /// <summary>Tozalashni tekshirish oralig'i (daqiqa).</summary>
    public int ChatRetentionIntervalMinutes { get; private init; } = 60;

    /// <summary>Bitta <c>DELETE</c> dagi qatorlar soni.</summary>
    public int ChatRetentionBatchSize { get; private init; } = 5000;

    /// <summary>
    /// Bitta yurishdagi paketlar chegarasi (standart: 20 × 5000 = 100 000
    /// qator). Sabab <see cref="Zinnur.Application.Jobs.ChatRetentionJob"/>
    /// izohida: birinchi yoqilganda orqada yillik tarix turgan bo'lishi
    /// mumkin va u bitta yurishda emas, bir necha yurishda tozalanadi.
    /// </summary>
    public int ChatRetentionMaxBatchesPerRun { get; private init; } = 20;

    // ---------------------------------------------------------------- hosila

    public TimeSpan Tick => TimeSpan.FromSeconds(TickSeconds);

    public SessionAutoCloseSettings SessionAutoClose => new(
        Grace: TimeSpan.FromMinutes(SessionGraceMinutes),
        BatchSize: SessionBatchSize,
        Interval: TimeSpan.FromSeconds(SessionIntervalSeconds));

    public MonthlyBillingSettings MonthlyBilling =>
        new(Interval: TimeSpan.FromMinutes(BillingIntervalMinutes));

    public TeacherMorningCheckinSettings TeacherMorningCheckin =>
        new(Interval: TimeSpan.FromMinutes(TeacherMorningCheckinIntervalMinutes));

    /// <summary>Jarima skaneri oralig'i (daqiqa).</summary>
    public int PenaltyScanIntervalMinutes { get; private init; } = 30;

    public PenaltyScanSettings PenaltyScan =>
        new(Interval: TimeSpan.FromMinutes(PenaltyScanIntervalMinutes));

    public ChatRetentionSettings ChatRetention => new(
        Interval: TimeSpan.FromMinutes(ChatRetentionIntervalMinutes),
        BatchSize: ChatRetentionBatchSize,
        MaxBatchesPerRun: ChatRetentionMaxBatchesPerRun);

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

            TeacherMorningCheckinEnabled = Flag(
                configuration, $"{SectionName}:TeacherMorningCheckin:Enabled",
                defaults.TeacherMorningCheckinEnabled),

            TeacherMorningCheckinIntervalMinutes = Number(
                configuration, $"{SectionName}:TeacherMorningCheckin:IntervalMinutes",
                defaults.TeacherMorningCheckinIntervalMinutes, 1, 60),

            // ★ PASTKI CHEGARA 5 DAQIQA: tozalash — kunlik hodisa. Har
            // daqiqada yurish hech nima yutmasdi, lekin har yurish
            // kesimni qidirish uchun bazaga borardi.
            PenaltyScanIntervalMinutes = Number(
                configuration, $"{SectionName}:PenaltyScan:IntervalMinutes",
                defaults.PenaltyScanIntervalMinutes, 5, 1440),

            ChatRetentionIntervalMinutes = Number(
                configuration, $"{SectionName}:ChatRetention:IntervalMinutes",
                defaults.ChatRetentionIntervalMinutes, 5, 1440),

            // Yuqori chegara 20 000: undan katta paket uzoq tranzaksiya va
            // katta WAL demakdir — ya'ni fon tozalashi ilovaning O'ZINI
            // sekinlashtirardi.
            ChatRetentionBatchSize = Number(
                configuration, $"{SectionName}:ChatRetention:BatchSize",
                defaults.ChatRetentionBatchSize, 100, 20_000),

            ChatRetentionMaxBatchesPerRun = Number(
                configuration, $"{SectionName}:ChatRetention:MaxBatchesPerRun",
                defaults.ChatRetentionMaxBatchesPerRun, 1, 1000),
        };
    }

    private static int Number(IConfiguration configuration, string key, int fallback, int min, int max) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static bool Flag(IConfiguration configuration, string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value) ? value : fallback;
}
