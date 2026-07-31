using System.Globalization;

namespace Zinnur.Migration;

/// <summary>Vosita qaysi bosqichlarni bajarishi.</summary>
[Flags]
internal enum MigrationPhase
{
    None = 0,

    /// <summary>Faqat o'qish: manbani tekshirish va to'siqlarni sanash.</summary>
    Preflight = 1,

    /// <summary>Ma'lumotni ko'chirish.</summary>
    Migrate = 2,

    /// <summary>Solishtirish hisoboti.</summary>
    Verify = 4,

    All = Preflight | Migrate | Verify,
}

/// <summary>
/// Buyruq satri sozlamalari.
///
/// ★ ULANISH SATRLARI FAQAT OSHKOR BERILADI (argument yoki muhit
/// o'zgaruvchisi). Vosita hech qanday <c>.env</c> faylini O'QIMAYDI va
/// standart ulanish satri YO'Q — aks holda kimdir uni bexosdan ishlab
/// turgan bazaga qaratib yuborishi mumkin edi.
/// </summary>
internal sealed class MigrationOptions
{
    public required string SourceConnection { get; init; }

    public required string TargetConnection { get; init; }

    public MigrationPhase Phases { get; init; } = MigrationPhase.All;

    /// <summary>
    /// Bitta <c>INSERT</c> dagi qatorlar soni.
    ///
    /// 1000 — standart. Sabab: Postgres bitta so'rovda 65 535 tagacha
    /// parametr qabul qiladi. Eng keng jadvalimiz ~16 ustunli, ya'ni
    /// 1000 x 16 = 16 000 parametr — chegaradan uzoq, lekin tarmoq
    /// aylanishlari soni 1000 barobar kam. 10 000 qilinsa eng keng
    /// jadvalda chegaraga yaqinlashardi va xato faqat PROD hajmida
    /// chiqardi.
    /// </summary>
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Maqsad bazada qator borligiga ruxsat (uzilgan ko'chirishni DAVOM
    /// ettirish uchun). Standart holda vosita bo'sh baza talab qiladi —
    /// bu ishlab turgan bazaga bexosdan yozib yuborishdan himoya.
    /// </summary>
    public bool AllowNonEmptyTarget { get; init; }

    /// <summary>
    /// Kursga bog'lanmagan modullar (<c>modules.course_id IS NULL</c>)
    /// bo'lsa ham davom etish. Ular v2 ga ko'cha OLMAYDI (u yerda
    /// <c>Modules.CourseId</c> majburiy) va ULAR BILAN BIRGA butun daraxt
    /// (darslar, vazifalar, testlar, progress) tushib qoladi.
    /// Shuning uchun standart holda vosita to'xtaydi.
    /// </summary>
    public bool AllowOrphanModules { get; init; }

    public static MigrationOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? source = Environment.GetEnvironmentVariable("ZINNUR_LEGACY_DB");
        string? target = Environment.GetEnvironmentVariable("ZINNUR_V2_DB");
        var phases = MigrationPhase.All;
        var batch = 1000;
        var allowNonEmpty = false;
        var allowOrphan = false;

        foreach (var arg in args)
        {
            if (TryValue(arg, "--source=", out var v)) source = v;
            else if (TryValue(arg, "--target=", out v)) target = v;
            else if (TryValue(arg, "--batch=", out v)) batch = int.Parse(v, CultureInfo.InvariantCulture);
            else if (TryValue(arg, "--only=", out v)) phases = ParsePhase(v);
            else if (string.Equals(arg, "--allow-nonempty-target", StringComparison.Ordinal)) allowNonEmpty = true;
            else if (string.Equals(arg, "--allow-orphan-modules", StringComparison.Ordinal)) allowOrphan = true;
            else throw new ArgumentException($"Noma'lum argument: {arg}", nameof(args));
        }

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Manba ulanishi ko'rsatilmagan (--source=... yoki ZINNUR_LEGACY_DB).", nameof(args));

        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Maqsad ulanishi ko'rsatilmagan (--target=... yoki ZINNUR_V2_DB).", nameof(args));

        if (batch is < 1 or > 10_000)
            throw new ArgumentException("--batch 1..10000 oralig'ida bo'lsin.", nameof(args));

        return new MigrationOptions
        {
            SourceConnection = source,
            TargetConnection = target,
            Phases = phases,
            BatchSize = batch,
            AllowNonEmptyTarget = allowNonEmpty,
            AllowOrphanModules = allowOrphan,
        };
    }

    private static bool TryValue(string arg, string prefix, out string value)
    {
        if (arg.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static MigrationPhase ParsePhase(string raw) => raw.ToLowerInvariant() switch
    {
        "preflight" => MigrationPhase.Preflight,
        "migrate" => MigrationPhase.Migrate,
        "verify" => MigrationPhase.Verify,
        "all" => MigrationPhase.All,
        _ => throw new ArgumentException($"--only qiymati noto'g'ri: {raw}", nameof(raw)),
    };

    public const string Usage = """
        zinnur-migrate — eski Zin-Nur platformasidan v2 ga ma'lumot ko'chirish

          --source=<ulanish satri>      eski (Python) baza. FAQAT O'QILADI.
          --target=<ulanish satri>      v2 (.NET) baza. Bo'sh bo'lishi kerak.
          --only=preflight|migrate|verify|all   (standart: all)
          --batch=<son>                 bitta INSERT dagi qatorlar (standart 1000)
          --allow-nonempty-target       uzilgan ko'chirishni davom ettirish
          --allow-orphan-modules        kursi yo'q modullar bo'lsa ham davom etish

        Muhit o'zgaruvchilari: ZINNUR_LEGACY_DB, ZINNUR_V2_DB

        Chiqish kodi: 0 — hisobot toza; 1 — mos kelmovchilik; 2 — ishga tushmadi.
        """;
}
