using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zinnur.Application;
using Zinnur.Application.Interactive.Content;
using Zinnur.Application.Interactive.Import;
using Zinnur.Infrastructure;

namespace Zinnur.ContentTool;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ZIN-NUR KONTENT CLI — interaktiv darslik (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
///   verify &lt;kitob-papkasi&gt; [--book ATF-1] [--allow fayl]
///       Bazaga tegmaydi. Har `dars.json` + audio + atamalar tekshiriladi.
///
///   import &lt;kitob-papkasi&gt; --book ATF-1 [--course-id N] [--map fayl] [--dry-run] [--allow fayl]
///       Idempotent: o'zgarmagan birlik — "unchanged", audio qayta yuklanmaydi.
///
/// Chiqish kodi: 0 — muvaffaqiyat, 1 — kontent xatosi, 2 — buyruq noto'g'ri.
/// 🔴 Kontent papkasi FAQAT O'QILADI (prodda `:ro` bilan ulanadi).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("verify" or "import"))
        {
            await Console.Error.WriteLineAsync(
                "Foydalanish:\n"
                + "  verify <kitob-papkasi> [--book ATF-1] [--allow fayl]\n"
                + "  import <kitob-papkasi> --book ATF-1 [--course-id N] [--map fayl] [--dry-run] [--allow fayl]");
            return 2;
        }

        var command = args[0];
        var bookDir = Path.GetFullPath(args[1]);
        var options = ParseOptions(args.Skip(2).ToArray());

        if (!Directory.Exists(bookDir))
        {
            await Console.Error.WriteLineAsync($"Papka topilmadi: {bookDir}");
            return 2;
        }

        var bookCode = options.GetValueOrDefault("book") ?? Path.GetFileName(bookDir);
        var allow = await ReadAllowAsync(options.GetValueOrDefault("allow")
            ?? Path.Combine(AppContext.BaseDirectory, "forbidden-terms.allow.json"));

        return command == "verify"
            ? await VerifyAsync(bookDir, bookCode, allow)
            : await ImportAsync(bookDir, bookCode, allow, options);
    }

    private static async Task<int> VerifyAsync(string bookDir, string bookCode, IReadOnlyList<ForbiddenTermAllow> allow)
    {
        var scans = await InteractiveImporter.VerifyAsync(bookDir, bookCode, allow);
        var errors = 0;

        foreach (var scan in scans)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{scan.Code,-10} {(scan.Errors.Count == 0 ? "OK " : "XATO")}  {Stats(scan.Stats)}"));
            foreach (var e in scan.Errors) Console.WriteLine("    ✗ " + e);
            foreach (var w in scan.Warnings) Console.WriteLine("    ! " + w);
            errors += scan.Errors.Count;
        }

        var manifest = Path.Combine(Path.GetTempPath(), $"zinnur-{bookCode}-manifest.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(
            scans.Select(s => new
            {
                s.Code,
                s.Sha256,
                s.GuideSha256,
                audio = s.Audio.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToDictionary(a => a.Id, a => a.Sha256),
                s.Stats,
            }),
            Indented));

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"\n{scans.Count} birlik, {errors} xato, {scans.Sum(s => s.Warnings.Count)} ogohlantirish. Manifest: {manifest}"));

        return errors == 0 ? 0 : 1;
    }

    private static async Task<int> ImportAsync(
        string bookDir, string bookCode, IReadOnlyList<ForbiddenTermAllow> allow, Dictionary<string, string?> options)
    {
        UnitLessonMap? map = null;
        if (options.GetValueOrDefault("map") is { } mapPath)
            map = UnitLessonMap.Parse(await File.ReadAllTextAsync(mapPath));

        long? courseId = options.GetValueOrDefault("course-id") is { } raw
            ? long.Parse(raw, CultureInfo.InvariantCulture)
            : null;

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production,
        });
        builder.Configuration.AddEnvironmentVariables();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        using var host = builder.Build();

        // Ish vaqtidagi sozlamalar (ombor manzili paneldan o'zgartirilgan bo'lishi
        // mumkin) hosted service orqali yuklanadi — shuning uchun host ishga tushiriladi.
        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<InteractiveImporter>();

            var report = await importer.ImportAsync(new ImportOptions(
                bookDir, bookCode, courseId, map, options.ContainsKey("dry-run"), allow));

            foreach (var unit in report.Units)
            {
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{unit.Code,-10} {unit.Status,-12} v{unit.Version?.ToString(CultureInfo.InvariantCulture) ?? "-"}  dars={unit.LessonId?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));
                foreach (var e in unit.Errors) Console.WriteLine("    ✗ " + e);
            }

            foreach (var e in report.Errors) Console.WriteLine("✗ " + e);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"\n{(report.DryRun ? "[dry-run] " : string.Empty)}{report.Units.Count} birlik: {report.Unchanged} unchanged; audio: {report.AudioUploaded} yuklandi, {report.AudioReused} bor edi. Natija: {(report.Ok ? "ok" : "failed")} (run #{report.RunId?.ToString(CultureInfo.InvariantCulture) ?? "-"})"));

            return report.Ok ? 0 : 1;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;

            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
            result[key] = value;
        }

        return result;
    }

    private static async Task<IReadOnlyList<ForbiddenTermAllow>> ReadAllowAsync(string path)
    {
        if (!File.Exists(path)) return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<ForbiddenTermAllow>>(json, Web) ?? [];
    }

    private static string Stats(Dictionary<string, int> stats) =>
        string.Join(", ", stats.Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} {kv.Value}")));
}
