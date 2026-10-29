using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zinnur.Application.Interactive.Content;

/// <summary>
/// `dars.json` ni o'qish uchun kichik yordamchilar (2026-10-29).
///
/// ★ Kontent sxemasi tashqi (kitob muallifi yozadi), shuning uchun C# sinflariga
///   qattiq bog'lanmaydi: `JsonNode` bilan o'qiladi, kutilmagan shakl —
///   bo'sh qiymat (proyeksiyada tushib qoladi), istisno emas.
/// </summary>
public static class UnitJson
{
    public static JsonObject Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        return JsonNode.Parse(raw) as JsonObject
            ?? throw new JsonException("dars.json ildizi obyekt bo'lishi kerak.");
    }

    public static string? Str(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static int? Number(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    public static bool? Flag(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    public static JsonArray Arr(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonArray a ? a : [];

    public static JsonObject? Obj(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonObject child ? child : null;

    public static IEnumerable<JsonObject> Objects(JsonNode? node, string key) =>
        Arr(node, key).OfType<JsonObject>();

    public static List<string> Strings(JsonNode? node, string key) =>
        [.. Arr(node, key).Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
            .Where(s => s is not null)
            .Select(s => s!)];

    public static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Barcha darslar (A, B…) bo'yicha bloklar — kitobdagi tartibda.</summary>
    public static IEnumerable<(string Part, JsonObject Block)> Blocks(JsonObject unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (unit["darslar"] is not JsonObject parts) yield break;

        foreach (var (part, value) in parts)
        {
            foreach (var block in Objects(value, "bloklar"))
                yield return (part, block);
        }
    }

    /// <summary>Blok ichidagi mashqlar (blok bilan birga — `ovlash` blok matnini ishlatadi).</summary>
    public static IEnumerable<(JsonObject Block, JsonObject Exercise)> Exercises(JsonObject unit) =>
        Blocks(unit).SelectMany(b => Objects(b.Block, "mashqlar").Select(e => (b.Block, e)));

    /// <summary>`ovlash` uchun blok matnining so'zlari — klient va server AYNI ro'yxatni ko'radi.</summary>
    public static List<string> ReadingWords(JsonObject block) =>
        [.. string.Join(' ', Strings(block, "matn"))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>So'zdan tinish belgilarini olib tashlaydi (prototipdagi <c>clean</c>).</summary>
    public static string CleanWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return new string([.. word.Where(ch => ch is not ('.' or ',' or '!' or '?' or '؟' or '،'
            or '«' or '»' or ':'))]);
    }
}
