using System.Globalization;
using System.Text.Json.Nodes;
using Zinnur.Application.Interactive.Content;
using static Zinnur.Application.Interactive.Content.UnitJson;

namespace Zinnur.Application.Interactive.Grading;

/// <summary>Tekshiriladigan element turi.</summary>
public enum ItemKind
{
    Tanla,
    Tf,
    Ula,
    Ovlash,
    Jadval,
    Toldir,
    Tartibla,
    TovushTest,
    Diktant,
}

/// <summary>
/// Kontentdagi bitta tekshiriladigan element (mashq, tovush testi yoki daftar
/// topshirig'i) va uning "savollar" soni.
/// </summary>
public sealed record GradableItem(string Key, ItemKind Kind, JsonObject Node, JsonObject? Block, int Total);

/// <summary>
/// O'quvchi yuborgan javob. Qaysi maydon kerakligi tur bo'yicha:
/// tanla/toldir/tovush testi — <see cref="Index"/> + <see cref="Choice"/>;
/// tf — <see cref="Index"/> + <see cref="Value"/>; ula — <see cref="Left"/> +
/// <see cref="Right"/>; ovlash — <see cref="Word"/>; jadval — <see cref="Cells"/>;
/// tartibla — <see cref="Index"/> + <see cref="Words"/>; diktant —
/// <see cref="Index"/> + <see cref="Text"/>.
/// </summary>
public sealed record CheckAnswer(
    int? Index = null,
    int? Choice = null,
    bool? Value = null,
    string? Left = null,
    string? Right = null,
    int? Word = null,
    IReadOnlyDictionary<string, string>? Cells = null,
    IReadOnlyList<string>? Words = null,
    string? Text = null);

/// <summary>
/// Bitta tekshiruv natijasi.
/// </summary>
/// <param name="Correct">Javob to'g'rimi.</param>
/// <param name="Slot">
/// Birinchi urinish hisobida qaysi "katak" yopildi: savol indeksi ("0"),
/// `ula` da chap element ("L…"), `ovlash` da normallashgan so'z. Noto'g'ri
/// `ula`/`ovlash` javobida — <c>null</c> (faqat xato soni oshadi).
/// </param>
/// <param name="Right">Bir zarbada baholanadigan element (`jadval`) uchun to'g'rilar soni.</param>
/// <param name="Reveal">O'quvchiga shu savolning to'g'ri javobi (javobdan KEYIN).</param>
public sealed record CheckVerdict(bool Correct, string? Slot, int? Right, JsonObject Reveal);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// JAVOB TEKSHIRUVI — SOF FUNKSIYA, FAQAT SERVERDA (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ Kalit kontentdan (`dars.json`) o'qiladi; klientda kalit yo'q.
/// ★ Javob ochilishi — faqat javobdan keyin va faqat SHU savolning javobi:
///   butun mashq kaliti hech qachon bir so'rovda chiqmaydi.
/// 🔴 Noto'g'ri shakldagi javob (indeks chegaradan tashqari, maydon yo'q) —
///   <see cref="InvalidAnswerException"/>, xato emas "noto'g'ri javob": aks holda
///   buzuq so'rov birinchi urinishni "yeb" qo'yardi.
/// </summary>
public static class AnswerChecker
{
    /// <summary>Element kalitini kontentda topadi (mashq id, "A4:test" yoki daftar "W3").</summary>
    public static GradableItem? Find(JsonObject unit, string itemKey)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(itemKey);

        if (itemKey.EndsWith(":test", StringComparison.Ordinal))
        {
            var blockId = itemKey[..^":test".Length];
            var block = Blocks(unit).Select(b => b.Block)
                .FirstOrDefault(b => Str(b, "id") == blockId && Str(b, "tur") == "tovush");

            return block is null
                ? null
                : new GradableItem(itemKey, ItemKind.TovushTest, block, block, Arr(block, "test").Count);
        }

        foreach (var (block, ex) in Exercises(unit))
        {
            if (Str(ex, "id") != itemKey) continue;
            return Describe(itemKey, ex, block);
        }

        var work = Objects(unit, "mashq_daftari").FirstOrDefault(w => Str(w, "id") == itemKey);
        if (work is not null)
        {
            return Str(work, "tur") switch
            {
                "diktant" => new GradableItem(itemKey, ItemKind.Diktant, work, null, Strings(work, "sozlar").Count),
                "ula" or "toldir" or "tanla" => Describe(itemKey, work, null),
                _ => null, // ovoz/ochiq — ustoz tekshiradi
            };
        }

        return null;
    }

    /// <summary>Kontentdagi barcha tekshiriladigan elementlar (progress jami uchun).</summary>
    public static IEnumerable<GradableItem> All(JsonObject unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        foreach (var (_, block) in Blocks(unit))
        {
            if (Str(block, "tur") == "tovush" && Arr(block, "test").Count > 0)
                yield return new GradableItem($"{Str(block, "id")}:test", ItemKind.TovushTest, block, block, Arr(block, "test").Count);

            foreach (var ex in Objects(block, "mashqlar"))
            {
                if (Describe(Str(ex, "id") ?? string.Empty, ex, block) is { } item)
                    yield return item;
            }
        }
    }

    public static CheckVerdict Check(GradableItem item, CheckAnswer answer, MatchTokenCodec match, long versionId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(match);

        return item.Kind switch
        {
            ItemKind.Tanla => Choice(Objects(item.Node, "savollar").ToList(), answer),
            ItemKind.TovushTest => Choice(Objects(item.Node, "test").ToList(), answer),
            ItemKind.Toldir => Fill(item, answer),
            ItemKind.Tf => TrueFalse(item, answer),
            ItemKind.Ula => Match(item, answer, match, versionId),
            ItemKind.Ovlash => Hunt(item, answer),
            ItemKind.Jadval => Table(item, answer),
            ItemKind.Tartibla => Order(item, answer),
            ItemKind.Diktant => Dictation(item, answer),
            _ => throw new InvalidAnswerException("Bu mashq turi tekshirilmaydi."),
        };
    }

    // ================================================================ turlar

    private static GradableItem? Describe(string key, JsonObject ex, JsonObject? block)
    {
        return Str(ex, "tur") switch
        {
            "tanla" => new(key, ItemKind.Tanla, ex, block, Arr(ex, "savollar").Count),
            "tf" => new(key, ItemKind.Tf, ex, block, Arr(ex, "gaplar").Count),
            "ula" => new(key, ItemKind.Ula, ex, block, Arr(ex, "juftlar").Count),
            "ovlash" => new(key, ItemKind.Ovlash, ex, block, HuntTargets(ex).Count),
            "jadval" => new(key, ItemKind.Jadval, ex, block, OpenCells(ex).Count),
            "toldir" => new(key, ItemKind.Toldir, ex, block, Arr(ex, "gaplar").Count),
            "tartibla" => new(key, ItemKind.Tartibla, ex, block, Arr(ex, "gaplar").Count),
            _ => null,
        };
    }

    private static CheckVerdict Choice(List<JsonObject> questions, CheckAnswer answer)
    {
        var (index, q) = Question(questions, answer.Index);
        var options = Strings(q, "variantlar");

        if (answer.Choice is not { } choice || choice < 0 || choice >= options.Count)
            throw new InvalidAnswerException("Variant tanlanmagan yoki mavjud emas.");

        var key = Number(q, "javob") ?? -1;

        return new CheckVerdict(
            choice == key,
            Slot(index),
            null,
            new JsonObject { ["correctChoice"] = key });
    }

    private static CheckVerdict Fill(GradableItem item, CheckAnswer answer)
    {
        var (index, q) = Question(Objects(item.Node, "gaplar").ToList(), answer.Index);
        var options = Strings(q, "variantlar");

        if (answer.Choice is not { } choice || choice < 0 || choice >= options.Count)
            throw new InvalidAnswerException("Variant tanlanmagan yoki mavjud emas.");

        var key = Str(q, "javob");
        var correctChoice = options.FindIndex(o => string.Equals(o, key, StringComparison.Ordinal));

        return new CheckVerdict(
            string.Equals(options[choice], key, StringComparison.Ordinal),
            Slot(index),
            null,
            new JsonObject { ["correctChoice"] = correctChoice, ["javob"] = key });
    }

    private static CheckVerdict TrueFalse(GradableItem item, CheckAnswer answer)
    {
        var (index, q) = Question(Objects(item.Node, "gaplar").ToList(), answer.Index);

        if (answer.Value is not { } value)
            throw new InvalidAnswerException("To'g'ri yoki noto'g'ri tanlanmagan.");

        var key = Flag(q, "javob") ?? false;

        return new CheckVerdict(
            value == key,
            Slot(index),
            null,
            new JsonObject { ["correctValue"] = key, ["tuzatish"] = Str(q, "tuzatish") });
    }

    private static CheckVerdict Match(GradableItem item, CheckAnswer answer, MatchTokenCodec match, long versionId)
    {
        var count = Arr(item.Node, "juftlar").Count;

        int? left = null, right = null;
        for (var i = 0; i < count; i++)
        {
            if (string.Equals(match.Token(versionId, item.Key, 'L', i), answer.Left, StringComparison.Ordinal)) left = i;
            if (string.Equals(match.Token(versionId, item.Key, 'R', i), answer.Right, StringComparison.Ordinal)) right = i;
        }

        if (left is null || right is null)
            throw new InvalidAnswerException("Juftlik elementlari topilmadi (sahifani yangilang).");

        var correct = left == right;

        return new CheckVerdict(correct, correct ? answer.Left : null, null, []);
    }

    private static CheckVerdict Hunt(GradableItem item, CheckAnswer answer)
    {
        var words = item.Block is null ? [] : ReadingWords(item.Block);

        if (answer.Word is not { } index || index < 0 || index >= words.Count)
            throw new InvalidAnswerException("So'z tanlanmagan yoki mavjud emas.");

        var normalized = ArabicNorm.Normalize(CleanWord(words[index]));
        var correct = HuntTargets(item.Node).Contains(normalized);

        return new CheckVerdict(correct, correct ? normalized : null, null, []);
    }

    private static CheckVerdict Table(GradableItem item, CheckAnswer answer)
    {
        var cells = answer.Cells ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = Arr(item.Node, "qatorlar").OfType<JsonArray>().ToList();
        var reveal = new JsonObject();
        var right = 0;

        foreach (var (r, c) in OpenCells(item.Node))
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{r}:{c}");
            var expected = AsString(rows[r][c]) ?? string.Empty;
            cells.TryGetValue(key, out var given);
            var ok = ArabicNorm.Same(given, expected);
            if (ok) right++;
            reveal[key] = new JsonObject { ["correct"] = ok, ["javob"] = expected };
        }

        return new CheckVerdict(right == reveal.Count, null, right, new JsonObject { ["cells"] = reveal });
    }

    private static CheckVerdict Order(GradableItem item, CheckAnswer answer)
    {
        var (index, q) = Question(Objects(item.Node, "gaplar").ToList(), answer.Index);

        if (answer.Words is not { Count: > 0 } words)
            throw new InvalidAnswerException("So'zlar tartibi yuborilmagan.");

        var key = Str(q, "javob");

        return new CheckVerdict(
            ArabicNorm.Same(string.Join(' ', words), key),
            Slot(index),
            null,
            new JsonObject { ["javob"] = key });
    }

    private static CheckVerdict Dictation(GradableItem item, CheckAnswer answer)
    {
        var words = Strings(item.Node, "sozlar");

        if (answer.Index is not { } index || index < 0 || index >= words.Count)
            throw new InvalidAnswerException("Savol topilmadi.");

        return new CheckVerdict(
            ArabicNorm.Same(answer.Text, words[index]) && !string.IsNullOrWhiteSpace(answer.Text),
            Slot(index),
            null,
            new JsonObject { ["javob"] = words[index] });
    }

    // ================================================================ yordamchilar

    private static (int Index, JsonObject Question) Question(List<JsonObject> questions, int? index)
    {
        if (index is not { } i || i < 0 || i >= questions.Count)
            throw new InvalidAnswerException("Savol topilmadi.");

        return (i, questions[i]);
    }

    private static string Slot(int index) => index.ToString(CultureInfo.InvariantCulture);

    /// <summary>`ovlash` kaliti — normallashgan, takrorsiz.</summary>
    public static HashSet<string> HuntTargets(JsonObject exercise) =>
        Strings(exercise, "javoblar").Select(ArabicNorm.Normalize).ToHashSet(StringComparer.Ordinal);

    /// <summary>`jadval` ning to'ldiriladigan kataklari (`bosh` dan tashqari).</summary>
    public static List<(int Row, int Col)> OpenCells(JsonObject exercise)
    {
        var given = Arr(exercise, "bosh").OfType<JsonArray>()
            .Where(p => p.Count == 2)
            .Select(p => ((int?)p[0] ?? -1, (int?)p[1] ?? -1))
            .ToHashSet();

        var result = new List<(int, int)>();
        var r = 0;
        foreach (var row in Arr(exercise, "qatorlar").OfType<JsonArray>())
        {
            for (var c = 0; c < row.Count; c++)
            {
                if (!given.Contains((r, c))) result.Add((r, c));
            }

            r++;
        }

        return result;
    }
}

/// <summary>Javob shakli buzuq (400) — birinchi urinish hisobiga kirmaydi.</summary>
public sealed class InvalidAnswerException(string message) : Exception(message);
