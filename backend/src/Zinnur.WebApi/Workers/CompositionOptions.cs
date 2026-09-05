using System.Globalization;
using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure.Services;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Tungi yig'ishning MUHITGA oid sozlamalari (<c>Composition:*</c>).
///
/// ★ NIMA UCHUN QIYMATLAR QO'LDA O'QILADI (<c>Bind</c> emas) —
/// <c>JobsOptions</c> dagi AYNI sabab: bo'sh muhit o'zgaruvchisi
/// (<c>Composition__PollSeconds=</c>) konfiguratsiya bog'lagichida istisno
/// tashlaydi va ilova UMUMAN ko'tarilmay qoladi. Buzuq qiymat shunchaki
/// standartga tushishi kerak.
///
/// ★ HAR CHEGARA CHEKLANGAN (<c>Math.Clamp</c>): masalan
/// <c>LeaseMinutes=0</c> ijarani darhol eskirtirib, IKKI kodlovchini
/// bitta kalitga yozdirib yuborardi.
/// </summary>
internal sealed class CompositionOptions
{
    public const string SectionName = "Composition";

    /// <summary>
    /// Kompozitor worker'i ishga tushsinmi.
    ///
    /// 🔴 STANDART QIYMAT <c>false</c> — VA BU ATAYLAB (§4.2). Yig'ish
    /// AYNAN BITTA konteynerda ishlashi kerak; ikkalasida ham yoqilgan
    /// bo'lsa ikki kodlovchi bitta kalitga yozardi. Standart <c>true</c>
    /// bo'lsa, bayroqni unutish AYNAN shu holatni yaratardi — ya'ni
    /// xavfli tomon standart bo'lib qolardi.
    ///
    /// Ishlab chiqarishda faqat <c>compositor</c> konteyneri buni
    /// <c>true</c> qiladi; <c>api</c> esa OSHKORA <c>false</c> beradi.
    ///
    /// ⚠️ Bayroq FAQAT fon siklini yoqadi. Xizmatlarning o'zi DI'da
    /// baribir qoladi — <c>JobsSetup</c> dagi AYNI naqsh: testlar
    /// aylanishni O'ZI chaqiradi va fon xizmatining uyqusini kutmaydi.
    /// </summary>
    public bool Enabled { get; private init; }

    /// <summary>
    /// Ishchi papkalarning ildizi.
    ///
    /// ⚠️ Bitta ish ~6 GB egallaydi (xom kirishlar + natija + faststart
    /// uchun vaqtinchalik nusxa), shuning uchun bu ALOHIDA volume
    /// bo'lishi kerak — konteynerning yozuv qatlami emas.
    /// </summary>
    public string ScratchPath { get; private init; } = "/var/lib/zinnur/compose";

    /// <summary>Navbat bo'sh bo'lganda keyingi tekshiruvgacha kutish (sekund).</summary>
    public int PollSeconds { get; private init; } = 60;

    /// <summary>
    /// Ijara muddati (daqiqa). Bu "ish qancha davom etadi" EMAS —
    /// ishlayotgan ishchi uni muntazam uzaytiradi. Bu "ishchi qulaganini
    /// qancha vaqtda sezamiz".
    /// </summary>
    public int LeaseMinutes { get; private init; } = 5;

    /// <summary>
    /// Ijara qancha vaqtda bir uzaytiriladi (sekund).
    ///
    /// ⚠️ <see cref="LeaseMinutes"/> DAN SEZILARLI KICHIK bo'lishi SHART.
    /// </summary>
    public int RenewSeconds { get; private init; } = 60;

    /// <summary>
    /// Tungi oyna oxiriga shundan kam qolganda YANGI ish boshlanmaydi
    /// (daqiqa). Sabab <c>RecordingCompositionWindow</c> izohida.
    /// </summary>
    public int StartCutoffMinutes { get; private init; } = 30;

    /// <summary>
    /// Ishga tushishda shundan eski ishchi papkalar o'chiriladi (soat).
    ///
    /// ⚠️ ENG UZUN KODLASHDAN SEZILARLI KATTA bo'lishi SHART: aks holda
    /// tozalash hozir ishlayotgan ishning papkasini olib tashlardi.
    /// </summary>
    public int ScratchMaxAgeHours { get; private init; } = 24;

    /// <summary>
    /// <c>SIGTERM</c> dan keyin <c>SIGKILL</c> gacha kutish (sekund).
    ///
    /// ⚠️ Konteynerning <c>stop_grace_period</c> i bundan KATTA bo'lishi
    /// SHART, aks holda Docker bizni ffmpeg'dan oldin o'ldiradi va YETIM
    /// protsess qoladi.
    /// </summary>
    public int StopGraceSeconds { get; private init; } = 10;

    /// <summary>Bir aylanishda ko'pi bilan nechta yozuvning xom fayli tozalanadi.</summary>
    public int PurgeBatchSize { get; private init; } = 20;

    // ---------------------------------------------------------------- hosila

    public TimeSpan Poll => TimeSpan.FromSeconds(PollSeconds);

    public TimeSpan StartCutoff => TimeSpan.FromMinutes(StartCutoffMinutes);

    public TimeSpan ScratchMaxAge => TimeSpan.FromHours(ScratchMaxAgeHours);

    public RecordingCompositionSettings Composition => new(
        Lease: TimeSpan.FromMinutes(LeaseMinutes),
        RenewEvery: TimeSpan.FromSeconds(RenewSeconds),
        PurgeBatchSize: PurgeBatchSize);

    public FfmpegComposerSettings Ffmpeg => new(
        ScratchPath: ScratchPath,
        FfmpegPath: FfmpegComposerSettings.Default.FfmpegPath,
        FfprobePath: FfmpegComposerSettings.Default.FfprobePath,
        StopGrace: TimeSpan.FromSeconds(StopGraceSeconds));

    /// <summary>Konfiguratsiyadan o'qiydi; yo'q yoki buzuq qiymat standartga tushadi.</summary>
    public static CompositionOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var defaults = new CompositionOptions();

        return new CompositionOptions
        {
            Enabled = Flag(configuration, $"{SectionName}:Enabled", defaults.Enabled),

            ScratchPath = Text(
                configuration, $"{SectionName}:ScratchPath", defaults.ScratchPath),

            PollSeconds = Number(
                configuration, $"{SectionName}:PollSeconds", defaults.PollSeconds, 5, 3600),

            // ★ PASTKI CHEGARA 2 DAQIQA: undan qisqa ijara uzaytirish
            //   oralig'iga (60 s) juda yaqin bo'lib qolardi va bitta
            //   kechikkan uzaytirish qatorni boshqa ishchiga berardi.
            LeaseMinutes = Number(
                configuration, $"{SectionName}:LeaseMinutes", defaults.LeaseMinutes, 2, 60),

            RenewSeconds = Number(
                configuration, $"{SectionName}:RenewSeconds", defaults.RenewSeconds, 10, 600),

            StartCutoffMinutes = Number(
                configuration, $"{SectionName}:StartCutoffMinutes",
                defaults.StartCutoffMinutes, 0, 240),

            ScratchMaxAgeHours = Number(
                configuration, $"{SectionName}:ScratchMaxAgeHours",
                defaults.ScratchMaxAgeHours, 1, 168),

            StopGraceSeconds = Number(
                configuration, $"{SectionName}:StopGraceSeconds",
                defaults.StopGraceSeconds, 1, 120),

            PurgeBatchSize = Number(
                configuration, $"{SectionName}:PurgeBatchSize", defaults.PurgeBatchSize, 1, 500),
        };
    }

    private static int Number(IConfiguration configuration, string key, int fallback, int min, int max) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static bool Flag(IConfiguration configuration, string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value) ? value : fallback;

    private static string Text(IConfiguration configuration, string key, string fallback) =>
        configuration[key] is { Length: > 0 } value ? value.Trim() : fallback;
}
