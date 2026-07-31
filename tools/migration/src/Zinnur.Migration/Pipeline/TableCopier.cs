using System.Globalization;
using System.Text;
using Npgsql;
using Zinnur.Migration.Reporting;

namespace Zinnur.Migration.Pipeline;

/// <summary>
/// ========================================================================
/// PAKETLI VA QAYTA YURGIZSA BO'LADIGAN KO'CHIRUVCHI
/// ========================================================================
///
/// ★ IDEMPOTENTLIK — <c>INSERT ... ON CONFLICT ("Id") DO NOTHING</c>.
/// Eski <c>id</c> lar SAQLANADI (qaror va sabablari
/// <c>docs/MA_LUMOT_KOCHIRISH.md</c> da), shuning uchun "bu qator
/// allaqachon ko'chganmi" degan savolga javob birlamchi kalitning O'ZI.
/// Alohida "ko'chirildi" jadvali yoki xotirada saqlanadigan holat KERAK
/// EMAS: vosita yarim yo'lda uzilsa, boshidan qayta yurgiziladi va
/// allaqachon yozilganlar jimgina tashlab ketiladi.
///
/// ★ NIMA UCHUN <c>DO NOTHING</c>, <c>DO UPDATE</c> EMAS: ko'chirish
/// TO'XTASH OYNASIDA bajariladi — v2 bazasida ko'chirishdan boshqa hech
/// kim yozmaydi. <c>DO UPDATE</c> bo'lsa qayta yurgizish v2 da qo'lda
/// qilingan tuzatishni bosib ketardi.
///
/// ★ NIMA UCHUN <c>COPY</c> EMAS: <c>COPY</c> tezroq, lekin
/// <c>ON CONFLICT</c> ni umuman qo'llab-quvvatlamaydi — ya'ni qayta
/// yurgizish dublikat kalit xatosi bilan yiqilardi. Bizning hajmda
/// (o'n minglab qator) paketli <c>INSERT</c> ham bir necha sekund.
/// </summary>
internal sealed class TableCopier(
    NpgsqlConnection source,
    NpgsqlConnection target,
    MigrationState state,
    MigrationReport report,
    Reporter reporter,
    int batchSize)
{
    public async Task<TableTally> CopyAsync(TableSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var tally = report.Tally(spec.Name, spec.SourceTable, spec.TargetTable);
        tally.Source = await ScalarLongAsync(source, spec.SourceCountSql, ct).ConfigureAwait(false);

        reporter.Step($"{spec.Name}: manbada {tally.Source.ToString(CultureInfo.InvariantCulture)} qator");

        if (tally.Source == 0) return tally;

        var context = new RowContext(state, report, spec.SourceTable);
        var insertSqlCache = new Dictionary<int, string>();
        var buffer = new List<object?[]>(batchSize);
        long read = 0;

        // Manba KURSOR bilan oqim sifatida o'qiladi: butun jadvalni xotiraga
        // yuklash 100 000 qatorli `attendance` da yuzlab megabayt bo'lardi.
        await using var command = new NpgsqlCommand(spec.SourceSql, source) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            read++;
            context.Bind(reader);

            var values = spec.Map(context);
            if (values is null)
            {
                tally.Skipped++;
                continue;
            }

            if (values.Length != spec.Columns.Count)
            {
                throw new InvalidOperationException(
                    $"{spec.Name}: xaritalash {values.Length} qiymat qaytardi, ustunlar soni {spec.Columns.Count}.");
            }

            buffer.Add(values);

            if (buffer.Count >= batchSize)
            {
                tally.Mapped += await FlushAsync(spec, buffer, insertSqlCache, ct).ConfigureAwait(false);
                buffer.Clear();
                reporter.Progress(spec.Name, read, tally.Source);
            }
        }

        if (buffer.Count > 0)
            tally.Mapped += await FlushAsync(spec, buffer, insertSqlCache, ct).ConfigureAwait(false);

        reporter.Progress(spec.Name, read, tally.Source);
        return tally;
    }

    /// <summary>Paketni bitta <c>INSERT</c> bilan yozadi va qatorlar sonini qaytaradi.</summary>
    private async Task<int> FlushAsync(
        TableSpec spec,
        List<object?[]> rows,
        Dictionary<int, string> sqlCache,
        CancellationToken ct)
    {
        if (!sqlCache.TryGetValue(rows.Count, out var sql))
        {
            sql = BuildInsert(spec, rows.Count);
            sqlCache[rows.Count] = sql;
        }

        await using var cmd = new NpgsqlCommand(sql, target) { CommandTimeout = 0 };

        foreach (var row in rows)
        {
            for (var c = 0; c < spec.Columns.Count; c++)
            {
                cmd.Parameters.Add(new NpgsqlParameter
                {
                    NpgsqlDbType = spec.Columns[c].Type,
                    Value = row[c] ?? DBNull.Value,
                });
            }
        }

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return rows.Count;
    }

    /// <summary>
    /// <c>INSERT INTO "T" ("A","B") VALUES ($1,$2),($3,$4) ON CONFLICT ("Id") DO NOTHING</c>.
    ///
    /// Pozitsion parametrlar (<c>$n</c>) ATAYLAB: nomlangan parametrlarda
    /// 16 000 ta noyob nom generatsiya qilish kerak bo'lardi va Npgsql
    /// ularni har safar qayta tahlil qilardi.
    /// </summary>
    private static string BuildInsert(TableSpec spec, int rowCount)
    {
        var sb = new StringBuilder(256 + (rowCount * spec.Columns.Count * 6));
        sb.Append("INSERT INTO \"").Append(spec.TargetTable).Append("\" (");

        for (var c = 0; c < spec.Columns.Count; c++)
        {
            if (c > 0) sb.Append(", ");
            sb.Append('"').Append(spec.Columns[c].Name).Append('"');
        }

        sb.Append(") VALUES ");

        var p = 1;
        for (var r = 0; r < rowCount; r++)
        {
            if (r > 0) sb.Append(", ");
            sb.Append('(');
            for (var c = 0; c < spec.Columns.Count; c++)
            {
                if (c > 0) sb.Append(", ");
                sb.Append('$').Append(p.ToString(CultureInfo.InvariantCulture));
                p++;
            }

            sb.Append(')');
        }

        sb.Append(" ON CONFLICT (").Append(spec.ConflictTarget).Append(") DO NOTHING");
        return sb.ToString();
    }

    public static async Task<long> ScalarLongAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static async Task<decimal> ScalarMoneyAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }
}
