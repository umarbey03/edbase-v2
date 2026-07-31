using System.Globalization;
using Npgsql;
using Zinnur.Migration;
using Zinnur.Migration.Pipeline;
using Zinnur.Migration.Plan;
using Zinnur.Migration.Reporting;

// ============================================================================
// zinnur-migrate — ESKI ZIN-NUR (Python/FastAPI) -> v2 (.NET) MA'LUMOT KO'CHIRISH
// ============================================================================
//
// ★ VOSITA BIR MARTALIK, LEKIN QAYTA YURGIZSA BO'LADIGAN. Ko'chirish
// tunda, rejalashtirilgan to'xtash oynasida bajariladi. Har qanday
// uzilishdan keyin vositani BOSHIDAN qayta ishga tushirish XAVFSIZ —
// yozilgan qatorlar ikkinchi marta yozilmaydi.
//
// ★ CHIQISH KODLARI (skript uchun):
//     0 — hisobot toza, ko'chirish qabul qilinishi mumkin;
//     1 — mos kelmovchilik topildi (ma'lumot yo'qolgan yoki sanoq buzilgan);
//     2 — vosita umuman ishga tusha olmadi (argument, ulanish, sxema).
//
// ★ MANBAGA HECH QACHON YOZILMAYDI. Manba ulanishi ochilgach darhol
// `SET default_transaction_read_only = on` qo'yiladi — bu vosita ichidagi
// istalgan xato (noto'g'ri SQL, kelajakdagi tahrir) eski, ISHLAB TURGAN
// bazaga tegib ketishini BAZANING O'ZI darajasida to'sadi.
// ============================================================================

const int ExitOk = 0;
const int ExitMismatch = 1;
const int ExitFailed = 2;

if (args.Length == 1 && (args[0] is "--help" or "-h"))
{
    Console.WriteLine(MigrationOptions.Usage);
    return ExitOk;
}

MigrationOptions options;
try
{
    options = MigrationOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("XATO: " + ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(MigrationOptions.Usage);
    return ExitFailed;
}

var reporter = new Reporter(Console.Out);
var report = new MigrationReport();
var state = new MigrationState();
var plan = MigrationPlan.Build();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
    Console.Error.WriteLine("To'xtatilmoqda... (qayta yurgizish XAVFSIZ)");
};

var ct = cancellation.Token;

reporter.Section("zinnur-migrate — ESKI TIZIMDAN v2 GA MA'LUMOT KO'CHIRISH");
reporter.Line(string.Create(
    CultureInfo.InvariantCulture,
    $"  Boshlandi: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | paket: {options.BatchSize} qator"));

try
{
    await using var source = new NpgsqlConnection(options.SourceConnection);
    await using var target = new NpgsqlConnection(options.TargetConnection);

    await source.OpenAsync(ct).ConfigureAwait(false);
    await target.OpenAsync(ct).ConfigureAwait(false);

    // ★ MANBANI FAQAT O'QISHGA QULFLASH — vositaning eng muhim himoyasi.
    await using (var readOnly = new NpgsqlCommand("SET default_transaction_read_only = on", source))
        await readOnly.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

    reporter.Ok("Manba ulanishi FAQAT O'QISH rejimiga qo'yildi (default_transaction_read_only = on).");

    // ---------------------------------------------------------------- 1. tayyorgarlik
    if (options.Phases.HasFlag(MigrationPhase.Preflight))
    {
        var ready = await Preflight.RunAsync(
            source, state, report, reporter, options.AllowOrphanModules, ct).ConfigureAwait(false);

        if (!ready && options.Phases.HasFlag(MigrationPhase.Migrate))
        {
            reporter.Error("Tayyorgarlik tekshiruvi o'tmadi — KO'CHIRISH BOSHLANMADI.");
            ReportPrinter.Print(report, reporter);
            return ExitMismatch;
        }
    }
    else if (options.Phases.HasFlag(MigrationPhase.Migrate))
    {
        // Dublikat ro'yxatlarisiz ko'chirish unikal indeks xatosiga
        // yiqilardi, shuning uchun ular baribir hisoblanadi.
        reporter.Warn("Tayyorgarlik o'tkazib yuborildi — dublikat ro'yxatlari baribir tayyorlanmoqda.");
        await Preflight.RunAsync(source, state, report, reporter, allowOrphanModules: true, ct)
            .ConfigureAwait(false);
    }

    var migrator = new Migrator(source, target, options, state, report, reporter);

    // ---------------------------------------------------------------- 2. sxema va bo'shlik
    if (options.Phases.HasFlag(MigrationPhase.Migrate))
    {
        reporter.Section("2-BOSQICH — MAQSAD BAZA TEKSHIRUVI");

        var schemaOk = await SchemaGuard.CheckAsync(target, plan, report, reporter, ct).ConfigureAwait(false);
        var emptyOk = await migrator.CheckTargetEmptyAsync(plan, ct).ConfigureAwait(false);

        if (!schemaOk || !emptyOk)
        {
            reporter.Error("Maqsad baza tayyor emas — KO'CHIRISH BOSHLANMADI.");
            ReportPrinter.Print(report, reporter);
            return ExitMismatch;
        }

        await migrator.RunAsync(plan, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 3. tekshirish
    if (options.Phases.HasFlag(MigrationPhase.Verify) && options.Phases.HasFlag(MigrationPhase.Migrate))
    {
        var reconciler = new Reconciler(source, target, report, reporter);
        await reconciler.RunAsync(plan, ct).ConfigureAwait(false);
    }
    else if (options.Phases.HasFlag(MigrationPhase.Verify))
    {
        reporter.Warn("Tekshirish faqat ko'chirish bilan birga ma'noga ega (--only=all yurgizing).");
    }
}
catch (OperationCanceledException)
{
    reporter.Error("To'xtatildi. Vositani BOSHIDAN qayta yurgizish xavfsiz.");
    return ExitFailed;
}
catch (NpgsqlException ex)
{
    reporter.Error("Baza xatosi: " + ex.Message);
    reporter.Line("  Hech narsa buzilmadi — vositani qayta yurgizish xavfsiz.");
    return ExitFailed;
}
catch (InvalidOperationException ex)
{
    reporter.Error("Ichki xato (reja va sxema mos emas): " + ex.Message);
    return ExitFailed;
}

ReportPrinter.Print(report, reporter);

reporter.Section("XULOSA");

if (report.Failures.Count > 0)
{
    reporter.Error(Inv.S($"KO'CHIRISH QABUL QILINMAYDI: {report.Failures.Count} ta mos kelmovchilik. ")
        + "Yuqoridagi ro'yxatni o'qing, sababini bartaraf eting va vositani QAYTA yurgizing.");
    return ExitMismatch;
}

if (report.Warnings.Count > 0)
{
    reporter.Warn(Inv.S($"Sanoq va pul MOS KELDI, lekin {report.Warnings.Count} ta holat diqqat talab qiladi ")
        + "(telefon dublikatlari, tanilmagan qiymatlar). Ularni loyiha egasi ko'rib chiqsin.");
}

reporter.Ok("Sanoq mos, pul mos, hafta kunlari siljimagan, chat oqimlari ajralgan.");
return ExitOk;
