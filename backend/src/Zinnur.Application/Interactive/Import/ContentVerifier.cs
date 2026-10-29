using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zinnur.Application.Interactive.Content;
using Zinnur.Application.Interactive.Grading;
using static Zinnur.Application.Interactive.Content.UnitJson;

namespace Zinnur.Application.Interactive.Import;

/// <summary>Papkadagi bitta audio fayl.</summary>
public sealed record AudioFile(string Id, string Path, string Sha256, long SizeBytes);

/// <summary>Bitta birlik papkasini tekshirish natijasi (bazaga tegmaydi).</summary>
public sealed class UnitScan
{
    public required string Code { get; init; }

    public required string Directory { get; init; }

    public int Number { get; set; }

    public string TitleUz { get; set; } = string.Empty;

    public string? TitleAr { get; set; }

    /// <summary>`dars.json` matni (UTF-8 BOMsiz, baytma-bayt).</summary>
    public string? Raw { get; set; }

    public string? Sha256 { get; set; }

    public JsonObject? Json { get; set; }

    public string? Guide { get; set; }

    public string? GuideSha256 { get; set; }

    /// <summary>Kontentda havola qilingan audio (id → fayl).</summary>
    public Dictionary<string, AudioFile> Audio { get; } = new(StringComparer.Ordinal);

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public Dictionary<string, int> Stats { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// `verify` — KONTENT PAPKASINI TEKSHIRISH (2026-10-29), bazaga TEGMAYDI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ XATO (import to'xtaydi): JSON buzuq, renderer bilmaydigan blok/mashq
///   turi, takror blok id, javob indeksi chegaradan tashqarida, `ula` da 2
///   dan kam juftlik, `jadval.bosh` jadvaldan tashqarida, `ovlash` javobi
///   matnda yo'q, audio havolasi faylsiz/kichik/mp3 emas, o'quvchi matnida
///   allow-ro'yxatda yo'q taqiqlangan atama.
/// ★ OGOHLANTIRISH (faqat hisobot, kontent o'zgartirilmaydi): blok vaqti
///   80 emas, test 8–10 savol emas, `tinglash` 3 dan kam, daftar 6–8 emas
///   (imtihon — istisno), ishlatilmagan audio, xodim matnidagi atama.
/// 🔴 Kontent papkasi FAQAT O'QILADI.
/// </summary>
public static class ContentVerifier
{
    public const string ExamCode = "imtihon";

    public static readonly IReadOnlySet<string> BlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "isinish", "grammatika", "harf", "tovush", "yozish", "yakun", "vazifa", "tabir", "dialog", "oqish",
    };

    public static readonly IReadOnlySet<string> ExerciseTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "tanla", "tf", "ula", "ovlash", "jadval", "toldir", "tartibla",
    };

    public static readonly IReadOnlySet<string> WorkbookTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "ochiq", "ovoz", "diktant", "ula", "toldir", "tanla",
    };

    private const int MinAudioBytes = 5 * 1024;
    private const int LessonMinutes = 80;

    /// <summary>Kitob papkasidagi birliklar (birlik_NN, imtihon) — tartib bilan.</summary>
    public static IReadOnlyList<string> UnitDirectories(string bookDir)
    {
        ArgumentNullException.ThrowIfNull(bookDir);

        return [.. System.IO.Directory.EnumerateDirectories(bookDir)
            .Where(d => File.Exists(Path.Combine(d, "dars.json")))
            .OrderBy(d => Path.GetFileName(d) == ExamCode ? 1 : 0)
            .ThenBy(d => Path.GetFileName(d), StringComparer.Ordinal)];
    }

    public static async Task<UnitScan> ScanAsync(
        string unitDir, string bookCode, IReadOnlyList<ForbiddenTermAllow> allow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unitDir);
        ArgumentNullException.ThrowIfNull(allow);

        var scan = new UnitScan { Code = Path.GetFileName(unitDir), Directory = unitDir };

        var bytes = await File.ReadAllBytesAsync(Path.Combine(unitDir, "dars.json"), ct);
        scan.Sha256 = Sha(bytes);

        try
        {
            scan.Raw = new UTF8Encoding(false, true).GetString(bytes);
            scan.Json = UnitJson.Parse(scan.Raw);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or JsonException)
        {
            scan.Errors.Add("dars.json UTF-8 JSON emas: " + ex.Message);
            return scan;
        }

        var guidePath = Path.Combine(unitDir, "ustoz_qollanmasi.md");
        if (File.Exists(guidePath))
        {
            var guideBytes = await File.ReadAllBytesAsync(guidePath, ct);
            scan.Guide = Encoding.UTF8.GetString(guideBytes);
            scan.GuideSha256 = Sha(guideBytes);
        }

        var meta = Obj(scan.Json, "meta");
        scan.Number = scan.Code == ExamCode ? InteractiveUnitNumber.Exam : Number(meta, "birlik") ?? 0;
        scan.TitleUz = Str(meta, "nom_uz") ?? scan.Code;
        scan.TitleAr = Str(meta, "nom_ar");

        if (scan.Number <= 0) scan.Errors.Add("meta.birlik — musbat son bo'lishi kerak.");

        CheckStructure(scan, scan.Json);
        await CheckAudioAsync(scan, scan.Json, ct);
        CheckTerms(scan, scan.Json, bookCode, allow);

        return scan;
    }

    // ================================================================ tuzilma

    private static void CheckStructure(UnitScan scan, JsonObject unit)
    {
        var isExam = scan.Code == ExamCode;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int blocks = 0, exercises = 0, minutes = 0;

        if (unit["darslar"] is not JsonObject parts || parts.Count == 0)
        {
            scan.Errors.Add("`darslar` bo'sh.");
            return;
        }

        foreach (var (part, block) in Blocks(unit))
        {
            blocks++;
            var id = Str(block, "id");
            var tur = Str(block, "tur") ?? string.Empty;
            var where = $"{part}/{id}";

            if (string.IsNullOrWhiteSpace(id)) scan.Errors.Add($"{where}: blok id yo'q.");
            else if (!ids.Add(id)) scan.Errors.Add($"{where}: blok id takrorlangan.");

            if (!BlockTypes.Contains(tur)) scan.Errors.Add($"{where}: noma'lum blok turi «{tur}».");

            minutes += Number(block, "vaqt") ?? 0;

            if (tur == "tovush")
            {
                foreach (var t in Objects(block, "test"))
                    CheckChoice(scan, where + " test", t);
            }

            foreach (var ex in Objects(block, "mashqlar"))
            {
                exercises++;
                CheckExercise(scan, ids, block, ex, where);
            }
        }

        if (!isExam && minutes != LessonMinutes)
            scan.Warnings.Add($"Bloklar vaqti {minutes} daqiqa ({LessonMinutes} kutilgan).");

        var test = Objects(Obj(unit, "test"), "savollar").ToList();
        for (var i = 0; i < test.Count; i++)
        {
            var q = test[i];
            var where = $"test #{i + 1}";

            if (Flag(q, "ochiq") == true) continue;
            if (Str(q, "javob_matn") is not null) continue;
            if (Arr(q, "variantlar").Count > 0) CheckChoice(scan, where, q);
            else scan.Errors.Add($"{where}: variantlar+javob, javob_matn yoki ochiq bo'lishi kerak.");
        }

        if (!isExam && test.Count is < 8 or > 10)
            scan.Warnings.Add($"Mini-test {test.Count} savol (8–10 kutilgan).");
        if (!isExam && test.Count(q => Str(q, "kon") == "tinglash") < 3)
            scan.Warnings.Add("Mini-testda 3 dan kam tinglash savoli.");

        var workbook = Objects(unit, "mashq_daftari").ToList();
        foreach (var w in workbook)
        {
            var id = Str(w, "id") ?? "?";
            var tur = Str(w, "tur") ?? string.Empty;

            if (!ids.Add(id)) scan.Errors.Add($"daftar {id}: id takrorlangan.");
            if (!WorkbookTypes.Contains(tur)) scan.Errors.Add($"daftar {id}: noma'lum tur «{tur}».");
            if (Number(w, "sessiya") is not (>= 1 and <= 3)) scan.Warnings.Add($"daftar {id}: sessiya 1–3 emas.");

            if (tur == "ula") CheckPairs(scan, $"daftar {id}", w);
            if (tur == "toldir") CheckFill(scan, $"daftar {id}", w);
            if (tur == "diktant" && Strings(w, "sozlar").Count != Arr(w, "audio").Count)
                scan.Errors.Add($"daftar {id}: diktant so'zlari va audio soni teng emas.");
        }

        if (!isExam && workbook.Count is < 6 or > 8)
            scan.Warnings.Add($"Mashq daftari {workbook.Count} topshiriq (6–8 kutilgan).");

        scan.Stats["bloklar"] = blocks;
        scan.Stats["mashqlar"] = exercises;
        scan.Stats["elementlar"] = AnswerChecker.All(unit).Sum(i => i.Total);
        scan.Stats["test"] = test.Count;
        scan.Stats["daftar"] = workbook.Count;
    }

    private static void CheckExercise(UnitScan scan, HashSet<string> ids, JsonObject block, JsonObject ex, string where)
    {
        var id = Str(ex, "id");
        var tur = Str(ex, "tur") ?? string.Empty;
        where = $"{where} {id}";

        if (string.IsNullOrWhiteSpace(id)) scan.Errors.Add($"{where}: mashq id yo'q.");
        else if (!ids.Add(id)) scan.Errors.Add($"{where}: mashq id takrorlangan.");

        if (!ExerciseTypes.Contains(tur))
        {
            scan.Errors.Add($"{where}: noma'lum mashq turi «{tur}».");
            return;
        }

        switch (tur)
        {
            case "tanla":
                foreach (var q in Objects(ex, "savollar")) CheckChoice(scan, where, q);
                break;

            case "tf":
                if (Objects(ex, "gaplar").Any(g => Flag(g, "javob") is null))
                    scan.Errors.Add($"{where}: tf javobi true/false bo'lishi kerak.");
                break;

            case "ula":
                CheckPairs(scan, where, ex);
                break;

            case "toldir":
                CheckFill(scan, where, ex);
                break;

            case "ovlash":
                var words = ReadingWords(block).Select(w => ArabicNorm.Normalize(CleanWord(w))).ToHashSet(StringComparer.Ordinal);
                foreach (var answer in Strings(ex, "javoblar"))
                {
                    if (!words.Contains(ArabicNorm.Normalize(answer)))
                        scan.Errors.Add($"{where}: ovlash javobi «{answer}» blok matnida topilmadi.");
                }

                break;

            case "jadval":
                var rows = Arr(ex, "qatorlar").OfType<JsonArray>().ToList();
                foreach (var cell in Arr(ex, "bosh").OfType<JsonArray>())
                {
                    var r = cell.Count == 2 ? (int?)cell[0] : null;
                    var c = cell.Count == 2 ? (int?)cell[1] : null;
                    if (r is null || c is null || r < 0 || r >= rows.Count || c < 0 || c >= rows[r.Value].Count)
                        scan.Errors.Add($"{where}: jadval.bosh katagi jadvaldan tashqarida.");
                }

                break;
        }
    }

    private static void CheckChoice(UnitScan scan, string where, JsonObject q)
    {
        var count = Arr(q, "variantlar").Count;
        if (Number(q, "javob") is not { } key || key < 0 || key >= count)
            scan.Errors.Add($"{where}: javob indeksi variantlar chegarasida emas.");
    }

    private static void CheckPairs(UnitScan scan, string where, JsonObject ex)
    {
        var pairs = Arr(ex, "juftlar").OfType<JsonArray>().ToList();
        if (pairs.Count < 2) scan.Errors.Add($"{where}: ula — kamida 2 juftlik kerak.");
        if (pairs.Any(p => p.Count != 2 || AsString(p[0]) is null || AsString(p[1]) is null))
            scan.Errors.Add($"{where}: ula juftligi [chap, o'ng] shaklida emas.");
    }

    private static void CheckFill(UnitScan scan, string where, JsonObject ex)
    {
        foreach (var g in Objects(ex, "gaplar"))
        {
            var key = Str(g, "javob");
            if (!Strings(g, "variantlar").Contains(key ?? string.Empty, StringComparer.Ordinal))
                scan.Errors.Add($"{where}: toldir javobi variantlar ichida emas.");
        }
    }

    // ================================================================ audio

    private static async Task CheckAudioAsync(UnitScan scan, JsonObject unit, CancellationToken ct)
    {
        var referenced = AudioIds(unit);
        var dir = Path.Combine(scan.Directory, "audio");
        var files = System.IO.Directory.Exists(dir)
            ? System.IO.Directory.EnumerateFiles(dir, "*.mp3")
                .ToDictionary(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var id in referenced.Order(StringComparer.Ordinal))
        {
            if (!files.TryGetValue(id, out var path))
            {
                scan.Errors.Add($"audio «{id}»: fayl yo'q.");
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);
            if (bytes.Length < MinAudioBytes)
                scan.Errors.Add($"audio «{id}»: {bytes.Length} bayt (≥ 5 KB kutilgan).");
            else if (!LooksLikeMp3(bytes))
                scan.Errors.Add($"audio «{id}»: mp3 sarlavhasi topilmadi.");

            scan.Audio[id] = new AudioFile(id, path, Sha(bytes), bytes.Length);
        }

        var unused = files.Keys.Where(k => !referenced.Contains(k)).Order(StringComparer.Ordinal).ToList();
        if (unused.Count > 0)
            scan.Warnings.Add($"Ishlatilmagan audio: {string.Join(", ", unused)}.");

        scan.Stats["audio"] = scan.Audio.Count;
    }

    /// <summary>Kontentdagi barcha audio havolalari (`audio`, `audio_toliq` — satr yoki massiv).</summary>
    public static HashSet<string> AudioIds(JsonNode? node)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        Collect(node, result);
        return result;

        static void Collect(JsonNode? node, HashSet<string> into)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (key, value) in o)
                    {
                        if (key is "audio" or "audio_toliq")
                        {
                            if (AsString(value) is { Length: > 0 } single) into.Add(single);
                            else if (value is JsonArray arr)
                            {
                                foreach (var s in arr.Select(AsString).Where(s => !string.IsNullOrEmpty(s)))
                                    into.Add(s!);
                            }
                        }
                        else
                        {
                            Collect(value, into);
                        }
                    }

                    break;

                case JsonArray a:
                    foreach (var item in a) Collect(item, into);
                    break;
            }
        }
    }

    private static bool LooksLikeMp3(byte[] bytes) =>
        (bytes.Length > 3 && bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3')
        || (bytes.Length > 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0);

    // ================================================================ atamalar

    private static void CheckTerms(UnitScan scan, JsonObject unit, string bookCode, IReadOnlyList<ForbiddenTermAllow> allow)
    {
        Walk(unit, string.Empty, (path, text) =>
        {
            var staff = path.EndsWith(".ustozga", StringComparison.Ordinal);

            foreach (var term in ForbiddenTerms.Find(text))
            {
                var allowed = allow.Any(a =>
                    string.Equals(a.Book, bookCode, StringComparison.Ordinal)
                    && string.Equals(a.Unit, scan.Code, StringComparison.Ordinal)
                    && string.Equals(a.Term, term, StringComparison.Ordinal));

                if (staff) scan.Warnings.Add($"Xodim matnida atama «{term}» ({path}) — o'quvchiga chiqmaydi.");
                else if (allowed) scan.Warnings.Add($"Ruxsat etilgan atama «{term}» ({path}).");
                else scan.Errors.Add($"Taqiqlangan atama «{term}» ({path}).");
            }
        });

        foreach (var term in ForbiddenTerms.Find(scan.Guide))
            scan.Warnings.Add($"Ustoz qo'llanmasida atama «{term}» — o'quvchiga chiqmaydi.");
    }

    private static void Walk(JsonNode? node, string path, Action<string, string> visit)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o) Walk(value, path + "." + key, visit);
                break;
            case JsonArray a:
                foreach (var item in a) Walk(item, path + "[]", visit);
                break;
            case JsonValue v when v.TryGetValue<string>(out var s):
                visit(path, s);
                break;
        }
    }

    public static string Sha(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>Birlik raqamlari.</summary>
public static class InteractiveUnitNumber
{
    public const int Exam = Domain.Entities.InteractiveUnit.ExamNumber;
}
