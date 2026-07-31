using Npgsql;
using Zinnur.Migration.Reporting;

namespace Zinnur.Migration.Pipeline;

/// <summary>
/// ========================================================================
/// SXEMA QO'RIQCHISI — "reja bilan baza bir-biriga mos keladimi"
/// ========================================================================
///
/// ★ NIMA UCHUN KERAK: ko'chirish rejasi (<c>MigrationPlan</c>) ustun
/// nomlarini SATR sifatida yozadi. v2 rivojlanib turibdi: ustun qayta
/// nomlansa yoki yangi MAJBURIY ustun qo'shilsa, reja eskirib qoladi.
/// Busiz buni faqat ko'chirish PAYTIDA, birinchi <c>INSERT</c> da
/// bilardik — ya'ni tunda, to'xtash oynasining o'rtasida.
///
/// Ikki tomonlama tekshiruv:
///   1. Rejadagi HAR ustun bazada bormi (yozib bo'lmaydigan ustun yo'q);
///   2. Bazadagi HAR majburiy (NOT NULL, standart qiymatsiz) ustun
///      rejada bormi (unutilgan majburiy ustun yo'q).
///
/// Ikkinchisi muhimroq: birinchisi baribir <c>INSERT</c> da chiqardi,
/// ikkinchisi esa JIMGINA noto'g'ri standart qiymat yozardi.
/// </summary>
internal static class SchemaGuard
{
    public static async Task<bool> CheckAsync(
        NpgsqlConnection target,
        IReadOnlyList<TableSpec> plan,
        MigrationReport report,
        Reporter reporter,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var actual = await ReadColumnsAsync(target, ct).ConfigureAwait(false);
        var ok = true;

        foreach (var spec in plan)
        {
            if (!actual.TryGetValue(spec.TargetTable, out var columns))
            {
                report.Fail($"Maqsad bazada `{spec.TargetTable}` jadvali yo'q (reja: {spec.Name}).");
                ok = false;
                continue;
            }

            foreach (var column in spec.Columns)
            {
                if (!columns.ContainsKey(column.Name))
                {
                    report.Fail($"`{spec.TargetTable}`.`{column.Name}` ustuni bazada yo'q — reja eskirgan.");
                    ok = false;
                }
            }

            var planned = spec.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var (name, required) in columns)
            {
                if (required && !planned.Contains(name))
                {
                    report.Fail(
                        $"`{spec.TargetTable}`.`{name}` MAJBURIY ustun, lekin ko'chirish rejasida yo'q.");
                    ok = false;
                }
            }
        }

        if (ok) reporter.Ok("Sxema qo'riqchisi: reja va maqsad baza mos.");
        return ok;
    }

    /// <summary>Jadval -> (ustun -> majburiymi) xaritasi.</summary>
    private static async Task<Dictionary<string, Dictionary<string, bool>>> ReadColumnsAsync(
        NpgsqlConnection target,
        CancellationToken ct)
    {
        const string Sql = """
            SELECT table_name, column_name,
                   (is_nullable = 'NO' AND column_default IS NULL
                    AND is_identity = 'NO' AND is_generated = 'NEVER') AS required
            FROM information_schema.columns
            WHERE table_schema = 'public'
            """;

        var result = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

        await using var cmd = new NpgsqlCommand(Sql, target);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var table = reader.GetString(0);
            if (!result.TryGetValue(table, out var columns))
            {
                columns = new Dictionary<string, bool>(StringComparer.Ordinal);
                result[table] = columns;
            }

            columns[reader.GetString(1)] = !reader.IsDBNull(2) && reader.GetBoolean(2);
        }

        return result;
    }
}
