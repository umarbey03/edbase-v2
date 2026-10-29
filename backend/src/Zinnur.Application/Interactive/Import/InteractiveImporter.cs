using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Interactive.Content;
using Zinnur.Application.Media;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Interactive.Import;

/// <summary>Moslashtirish fayli (`--map`): birlik → mavjud kurs darsi.</summary>
public sealed class UnitLessonMap
{
    /// <summary>Kurs nomi (masalan, "ATF") — <see cref="CourseId"/> berilmasa.</summary>
    [JsonPropertyName("course")]
    public string? Course { get; init; }

    [JsonPropertyName("courseId")]
    public long? CourseId { get; init; }

    [JsonPropertyName("units")]
    public Dictionary<string, UnitLessonRef> Units { get; init; } = new(StringComparer.Ordinal);

    public static UnitLessonMap Parse(string json) =>
        JsonSerializer.Deserialize<UnitLessonMap>(json)
        ?? throw new JsonException("Moslashtirish fayli bo'sh.");
}

/// <summary>
/// Birlik qaysi darsga bog'lanadi: <see cref="LessonId"/> (aniq) yoki modul nomi +
/// darsning modul ichidagi o'rni (<see cref="LessonPosition"/>) yoki turi
/// (<see cref="LessonKind"/> = "Exam" — moduldagi imtihon darsi).
/// </summary>
public sealed class UnitLessonRef
{
    [JsonPropertyName("lessonId")]
    public long? LessonId { get; init; }

    [JsonPropertyName("module")]
    public string? Module { get; init; }

    [JsonPropertyName("lessonPosition")]
    public int? LessonPosition { get; init; }

    [JsonPropertyName("lessonKind")]
    public string? LessonKind { get; init; }
}

public sealed record ImportOptions(
    string BookDir,
    string BookCode,
    long? CourseId,
    UnitLessonMap? Map,
    bool DryRun,
    IReadOnlyList<ForbiddenTermAllow> Allow,
    long? ActorId = null);

/// <summary>Birlik natijasi: "unchanged", "new", "new-version" yoki "error".</summary>
public sealed record UnitImportResult(
    string Code,
    string Status,
    int? Version,
    long? LessonId,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ImportReport(
    string BookCode,
    bool DryRun,
    bool Ok,
    long? RunId,
    int AudioUploaded,
    int AudioReused,
    IReadOnlyList<UnitImportResult> Units,
    IReadOnlyList<string> Errors)
{
    public int Unchanged => Units.Count(u => u.Status == "unchanged");
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// `import` — DARSLIKNI BAZAGA VA OMBORGA YUKLASH (2026-10-29), IDEMPOTENT
/// ════════════════════════════════════════════════════════════════════════
///
/// 1. `verify` (<see cref="ContentVerifier"/>) — bitta XATO bo'lsa butun import
///    to'xtaydi (yarim kitob yuklanmaydi).
/// 2. Audio: fayl SHA-256 bo'yicha; <c>InteractiveAudio</c> da bor bo'lsa —
///    o'tkazib yuboriladi. ★ AVVAL OMBOR, KEYIN BAZA: baza hech qachon yo'q
///    faylga ishora qilmaydi.
/// 3. Kitob upsert; har birlik uchun `dars.json` va qo'llanma xeshlari joriy
///    versiya bilan solishtiriladi: teng — "unchanged", farq — yangi versiya
///    (eskisi o'chirilmaydi). Versiyalar bitta <c>SaveChanges</c> da, joriy
///    versiya ko'rsatkichi — undan keyin (aylanma bog'lanish sababli).
/// 4. `--map` berilsa — birlik darsga bog'lanadi; xaritada yo'q birlik —
///    ogohlantirish, mavjud bog'lanish o'zgarmaydi. Xarita YO'Q bo'lsa
///    bog'lanishlarga umuman tegilmaydi (birliklar "bog'lanmagan" qoladi).
/// 5. Saqlangandan keyin: bazadagi `ContentRaw` xeshi qayta hisoblanadi —
///    yozilgan narsa aynan o'qilgan narsa.
/// 6. <c>InteractiveImportRuns</c> ga hisobot. Ikkinchi ishga tushirish
///    "N × unchanged, 0 audio yuklandi" berishi kerak (qabul mezoni).
/// </summary>
public sealed class InteractiveImporter(IApplicationDbContext db, IMediaStorage storage, TimeProvider clock)
{
    public const string AudioFolder = "interactive-audio";

    public static async Task<IReadOnlyList<UnitScan>> VerifyAsync(
        string bookDir, string bookCode, IReadOnlyList<ForbiddenTermAllow> allow, CancellationToken ct = default)
    {
        var scans = new List<UnitScan>();
        foreach (var dir in ContentVerifier.UnitDirectories(bookDir))
            scans.Add(await ContentVerifier.ScanAsync(dir, bookCode, allow, ct));

        return scans;
    }

    public async Task<ImportReport> ImportAsync(ImportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var started = clock.GetUtcNow();
        var scans = await VerifyAsync(options.BookDir, options.BookCode, options.Allow, ct);
        var errors = new List<string>();

        if (scans.Count == 0) errors.Add($"«{options.BookDir}» da birlik (dars.json) topilmadi.");
        errors.AddRange(scans.SelectMany(s => s.Errors.Select(e => $"{s.Code}: {e}")));

        var lessonIds = await ResolveMapAsync(options, scans, errors, ct);

        if (errors.Count > 0)
        {
            var failed = Results(scans, _ => "error", null, lessonIds);
            var failedRun = await WriteRunAsync(options, started, "failed", failed, errors, ct);
            return new ImportReport(options.BookCode, options.DryRun, false, failedRun, 0, 0, failed, errors);
        }

        // ---------------------------------------------------------- audio
        var files = scans.SelectMany(s => s.Audio.Values)
            .GroupBy(a => a.Sha256, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var shas = files.Keys.ToList();
        var known = await db.InteractiveAudio.AsNoTracking()
            .Where(a => shas.Contains(a.Sha256))
            .Select(a => a.Sha256)
            .ToListAsync(ct);
        var missing = shas.Except(known, StringComparer.Ordinal).ToList();

        if (!options.DryRun && missing.Count > 0)
        {
            foreach (var sha in missing)
            {
                var file = files[sha];
                await using var stream = File.OpenRead(file.Path);
                var key = await storage.SaveAsync(
                    new MediaUpload(AudioFolder, "mp3", "audio/mpeg", stream, file.SizeBytes), ct);

                db.InteractiveAudio.Add(new InteractiveAudio
                {
                    Sha256 = sha,
                    ObjectKey = key,
                    SizeBytes = file.SizeBytes,
                    ContentType = "audio/mpeg",
                    CreatedAt = clock.GetUtcNow(),
                });

                // Har fayldan keyin saqlanadi: import yarmida uzilsa, yuklangan
                // fayllar qayta yuklanmaydi (keyingi ishga tushirish ularni "bor" deb ko'radi).
                await db.SaveChangesAsync(ct);
            }
        }

        // ---------------------------------------------------------- kitob va birliklar
        var book = await db.InteractiveBooks.Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Code == options.BookCode, ct);

        var (title, subtitle) = ReadBookTitle(options.BookDir, options.BookCode);

        if (book is null)
        {
            book = new InteractiveBook
            {
                Code = options.BookCode,
                Title = title,
                Subtitle = subtitle,
                CourseId = options.CourseId,
                Position = await db.InteractiveBooks.CountAsync(ct),
                CreatedAt = clock.GetUtcNow(),
            };
            db.InteractiveBooks.Add(book);
        }
        else
        {
            book.Title = title;
            book.Subtitle = subtitle;
            if (options.CourseId is not null) book.CourseId = options.CourseId;
            book.UpdatedAt = clock.GetUtcNow();
        }

        var run = new InteractiveImportRun
        {
            BookCode = options.BookCode,
            StartedAt = started,
            ActorId = options.ActorId,
            DryRun = options.DryRun,
            Result = "running",
            Report = "{}",
        };
        if (!options.DryRun)
        {
            db.InteractiveImportRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        var unitIds = book.Units.Select(u => u.Id).ToList();
        var current = await db.InteractiveUnitVersions.AsNoTracking()
            .Where(v => unitIds.Contains(v.UnitId))
            .GroupBy(v => v.UnitId)
            .Select(g => new
            {
                UnitId = g.Key,
                Max = g.Max(v => v.Version),
            })
            .ToDictionaryAsync(x => x.UnitId, x => x.Max, ct);

        var currentVersionIds = book.Units.Where(u => u.CurrentVersionId != null).Select(u => u.CurrentVersionId!.Value).ToList();
        var currentShas = await db.InteractiveUnitVersions.AsNoTracking()
            .Where(v => currentVersionIds.Contains(v.Id))
            .Select(v => new { v.Id, v.ContentSha256, v.TeacherGuideSha256 })
            .ToDictionaryAsync(v => v.Id, ct);

        var statuses = new Dictionary<string, (string Status, int? Version)>(StringComparer.Ordinal);
        var written = new List<(InteractiveUnitVersion Version, string Sha)>();
        var position = 0;

        foreach (var scan in scans)
        {
            var unit = book.Units.FirstOrDefault(u => u.Code == scan.Code);
            var isNew = unit is null;

            if (unit is null)
            {
                unit = new InteractiveUnit { Code = scan.Code, TitleUz = scan.TitleUz, CreatedAt = clock.GetUtcNow() };
                book.Units.Add(unit);
            }

            unit.Number = scan.Number;
            unit.TitleUz = Truncate(scan.TitleUz, InteractiveUnit.MaxTitleLength);
            unit.TitleAr = scan.TitleAr is null ? null : Truncate(scan.TitleAr, InteractiveUnit.MaxTitleLength);
            unit.Position = position++;

            if (lessonIds.TryGetValue(scan.Code, out var lessonId)) unit.ModuleLessonId = lessonId;

            var same = unit.CurrentVersionId is { } cv
                && currentShas.TryGetValue(cv, out var cur)
                && string.Equals(cur.ContentSha256, scan.Sha256, StringComparison.Ordinal)
                && string.Equals(cur.TeacherGuideSha256, scan.GuideSha256, StringComparison.Ordinal);

            if (same)
            {
                statuses[scan.Code] = ("unchanged", current.GetValueOrDefault(unit.Id));
                continue;
            }

            var next = (isNew ? 0 : current.GetValueOrDefault(unit.Id)) + 1;
            var version = new InteractiveUnitVersion
            {
                Unit = unit,
                Version = next,
                ContentRaw = scan.Raw!,
                ContentSha256 = scan.Sha256!,
                Content = scan.Raw!,
                TeacherGuideMd = scan.Guide,
                TeacherGuideSha256 = scan.GuideSha256,
                AudioManifest = JsonSerializer.Serialize(
                    scan.Audio.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToDictionary(a => a.Id, a => a.Sha256)),
                Stats = JsonSerializer.Serialize(scan.Stats),
                SourcePath = Truncate(scan.Directory, InteractiveUnitVersion.MaxSourcePathLength),
                ImportRunId = options.DryRun ? null : run.Id,
                CreatedAt = clock.GetUtcNow(),
            };

            db.InteractiveUnitVersions.Add(version);
            written.Add((version, scan.Sha256!));
            statuses[scan.Code] = (isNew ? "new" : "new-version", next);
        }

        if (options.DryRun)
        {
            var dry = Results(scans, c => statuses[c].Status, c => statuses[c].Version, lessonIds);
            return new ImportReport(options.BookCode, true, true, null, missing.Count, known.Count, dry, []);
        }

        // ⚠️ IKKI QADAM: birlik ↔ versiya aylanma bog'langan (`CurrentVersionId`),
        //   EF uni bitta `SaveChanges` da yoza olmaydi. Avval kitob, birliklar va
        //   yangi versiyalar, keyin ko'rsatkich. Ikkinchi qadam yiqilsa birlik eski
        //   (to'liq) versiyani ko'rsatib qoladi — yarim holat o'quvchiga chiqmaydi.
        await db.SaveChangesAsync(ct);

        foreach (var (version, _) in written)
            version.Unit!.CurrentVersionId = version.Id;

        await db.SaveChangesAsync(ct);

        // ---------------------------------------------------------- yozilganini tekshirish
        var ids = written.Select(w => w.Version.Id).ToList();
        var stored = await db.InteractiveUnitVersions.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.ContentRaw })
            .ToDictionaryAsync(v => v.Id, v => v.ContentRaw, ct);

        foreach (var (version, sha) in written)
        {
            var actual = ContentVerifier.Sha(new System.Text.UTF8Encoding(false).GetBytes(stored[version.Id]));
            if (!string.Equals(actual, sha, StringComparison.Ordinal))
                errors.Add($"Versiya {version.Id}: bazadagi matn xeshi fayldan farq qiladi.");
        }

        var results = Results(scans, c => statuses[c].Status, c => statuses[c].Version, lessonIds);
        var ok = errors.Count == 0;

        run.Result = ok ? "ok" : "failed";
        run.FinishedAt = clock.GetUtcNow();
        run.Report = JsonSerializer.Serialize(new { units = results, audioUploaded = missing.Count, audioReused = known.Count, errors });
        await db.SaveChangesAsync(ct);

        return new ImportReport(options.BookCode, false, ok, run.Id, missing.Count, known.Count, results, errors);
    }

    // ================================================================ moslashtirish

    private async Task<Dictionary<string, long>> ResolveMapAsync(
        ImportOptions options, IReadOnlyList<UnitScan> scans, List<string> errors, CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (options.Map is not { } map) return result;

        var courseId = map.CourseId ?? options.CourseId;
        if (courseId is null && map.Course is { } courseName)
        {
            courseId = await db.Courses.AsNoTracking()
                .Where(c => c.Name == courseName)
                .Select(c => (long?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (courseId is null) errors.Add($"Xarita: «{courseName}» kursi topilmadi.");
        }

        var lessons = courseId is null
            ? []
            : await db.ModuleLessons.AsNoTracking()
                .Where(l => l.Module!.CourseId == courseId)
                .Select(l => new { l.Id, Module = l.Module!.Name, l.Position, l.Kind })
                .ToListAsync(ct);

        foreach (var (code, target) in map.Units)
        {
            if (scans.All(s => s.Code != code))
            {
                errors.Add($"Xarita: «{code}» birligi papkada yo'q.");
                continue;
            }

            long? lessonId = target.LessonId;

            if (lessonId is { } direct)
            {
                if (!await db.ModuleLessons.AsNoTracking().AnyAsync(l => l.Id == direct, ct))
                    errors.Add($"Xarita: {code} → dars #{direct} topilmadi.");
            }
            else
            {
                var inModule = lessons.Where(l => string.Equals(l.Module, target.Module, StringComparison.Ordinal)).ToList();
                var match = string.Equals(target.LessonKind, nameof(LessonKind.Exam), StringComparison.OrdinalIgnoreCase)
                    ? inModule.Where(l => l.Kind == LessonKind.Exam).OrderByDescending(l => l.Position).FirstOrDefault()
                    : inModule.FirstOrDefault(l => l.Position == target.LessonPosition);

                lessonId = match?.Id;
                if (lessonId is null)
                {
                    errors.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Xarita: {code} → «{target.Module}» modulida {target.LessonPosition?.ToString(CultureInfo.InvariantCulture) ?? target.LessonKind} dars topilmadi."));
                }
            }

            if (lessonId is { } id)
            {
                if (result.ContainsValue(id)) errors.Add($"Xarita: dars #{id} ga ikki birlik bog'lanmoqda.");
                else result[code] = id;
            }
        }

        foreach (var scan in scans.Where(s => !map.Units.ContainsKey(s.Code)))
            scan.Warnings.Add("Xaritada yo'q — dars bog'lanishi o'zgarmadi (bog'lanmagan bo'lsa o'quvchiga ko'rinmaydi).");

        return result;
    }

    // ================================================================ yordamchilar

    private async Task<long?> WriteRunAsync(
        ImportOptions options, DateTimeOffset started, string result,
        IReadOnlyList<UnitImportResult> units, IReadOnlyList<string> errors, CancellationToken ct)
    {
        if (options.DryRun) return null;

        var run = new InteractiveImportRun
        {
            BookCode = options.BookCode,
            StartedAt = started,
            FinishedAt = clock.GetUtcNow(),
            ActorId = options.ActorId,
            DryRun = false,
            Result = result,
            Report = JsonSerializer.Serialize(new { units, errors }),
        };
        db.InteractiveImportRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    private static List<UnitImportResult> Results(
        IReadOnlyList<UnitScan> scans,
        Func<string, string> status,
        Func<string, int?>? version,
        Dictionary<string, long> lessons) =>
        [.. scans.Select(s => new UnitImportResult(
            s.Code,
            s.Errors.Count > 0 ? "error" : status(s.Code),
            version?.Invoke(s.Code),
            lessons.TryGetValue(s.Code, out var l) ? l : null,
            s.Errors,
            s.Warnings))];

    private static (string Title, string? Subtitle) ReadBookTitle(string bookDir, string code)
    {
        var path = Path.Combine(bookDir, "kurs.json");
        if (!File.Exists(path)) return (code, null);

        try
        {
            var json = UnitJson.Parse(File.ReadAllText(path));
            return (Truncate(UnitJson.Str(json, "title") ?? code, InteractiveBook.MaxTitleLength),
                UnitJson.Str(json, "sarlavha") is { } s ? Truncate(s, InteractiveBook.MaxTitleLength) : null);
        }
        catch (JsonException)
        {
            return (code, null);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
