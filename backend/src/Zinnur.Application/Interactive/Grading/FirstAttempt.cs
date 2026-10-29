using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zinnur.Application.Interactive.Grading;

/// <summary>
/// Birinchi urinish holati (`InteractiveItemResults.State`, `jsonb`).
/// </summary>
public sealed class FirstAttemptState
{
    /// <summary>Yopilgan kataklar: savol indeksi / `ula` chap belgisi / `ovlash` so'zi → to'g'rimi.</summary>
    [JsonPropertyName("s")]
    public Dictionary<string, bool> Slots { get; init; } = new(StringComparer.Ordinal);

    /// <summary>`ula` va `ovlash` dagi noto'g'ri bosishlar soni.</summary>
    [JsonPropertyName("m")]
    public int Mistakes { get; set; }

    public static FirstAttemptState Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new FirstAttemptState();

        try
        {
            return JsonSerializer.Deserialize<FirstAttemptState>(json) ?? new FirstAttemptState();
        }
        catch (JsonException)
        {
            return new FirstAttemptState();
        }
    }

    public string Write() => JsonSerializer.Serialize(this);
}

/// <summary>Birinchi urinish yakunlandi: ball shu ikki son bilan hisoblanadi.</summary>
public readonly record struct FirstScore(int Right, int Total);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// "BIRINCHI URINISH HISOBGA OLINADI" — SOF QOIDA (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
/// Prototipdagi <c>setScore</c> qoidasining server nusxasi:
/// <list type="bullet">
///   <item>Savolli mashq (tanla, tf, toldir, tartibla, tovush testi, diktant):
///     har savolga BIRINCHI berilgan javob yoziladi; hamma savol yopilganda
///     natija = to'g'rilar / savollar.</item>
///   <item>`ula`: to'g'ri juftlik katakni yopadi, xato — xatolar sonini
///     oshiradi; hammasi yopilganda natija = max(0, juftlar − xatolar).</item>
///   <item>`ovlash`: topilgan so'z katakni yopadi, xato bosish — xato; hammasi
///     topilganda natija = max(0, so'zlar − xatolar).</item>
///   <item>`jadval`: bir zarbada — birinchi "Tekshirish" natijasi.</item>
/// </list>
/// Yakunlangan mashqqa keyingi javoblar natijani O'ZGARTIRMAYDI (mashq qilish).
/// </summary>
public static class FirstAttempt
{
    /// <returns>Mashq shu javob bilan yakunlansa — natija, aks holda <c>null</c>.</returns>
    public static FirstScore? Apply(FirstAttemptState state, GradableItem item, CheckVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(verdict);

        switch (item.Kind)
        {
            case ItemKind.Jadval:
                return new FirstScore(verdict.Right ?? 0, item.Total);

            case ItemKind.Ula:
            case ItemKind.Ovlash:
                if (verdict.Slot is { } solved)
                    state.Slots.TryAdd(solved, true);
                else
                    state.Mistakes++;

                return state.Slots.Count >= item.Total
                    ? new FirstScore(Math.Max(0, item.Total - state.Mistakes), item.Total)
                    : null;

            default:
                if (verdict.Slot is { } slot)
                    state.Slots.TryAdd(slot, verdict.Correct);

                return state.Slots.Count >= item.Total
                    ? new FirstScore(state.Slots.Count(s => s.Value), item.Total)
                    : null;
        }
    }
}
