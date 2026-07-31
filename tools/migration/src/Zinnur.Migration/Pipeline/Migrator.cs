using System.Globalization;
using Npgsql;
using Zinnur.Migration.Plan;
using Zinnur.Migration.Reporting;

namespace Zinnur.Migration.Pipeline;

/// <summary>
/// ========================================================================
/// KO'CHIRISH QUVURI — REJANI TARTIB BILAN BAJARADI
/// ========================================================================
///
/// Uchta narsani kafolatlaydi:
///
///   1. BOG'LIQLIK TARTIBI. Reja
///      (foydalanuvchi -> kurs -> guruh -> a'zolik -> dars -> davomat ->
///      chat -> o'quv -> moliya) shu yerda ketma-ket bajariladi va har
///      bola jadval ota id'sini <see cref="MigrationState"/> dan
///      tekshiradi. FK xatosi bilan yiqilish PRINSIPIAL ravishda mumkin
///      emas — ota ko'chmagan bola jimgina emas, SABAB BILAN o'tkazib
///      yuboriladi.
///
///   2. QAYTA YURGIZSA BO'LADI. Har <c>INSERT</c>
///      <c>ON CONFLICT DO NOTHING</c>, ya'ni yarim yo'lda uzilgan
///      ko'chirish BOSHIDAN qayta ishga tushiriladi va allaqachon
///      yozilganlar tashlab ketiladi. Alohida "davom ettirish" holati
///      yoki nazorat jadvali KERAK EMAS.
///
///   3. ID KETMA-KETLIGI. Eski id'lar saqlangani uchun identity
///      ketma-ketliklari ko'chirish OXIRIDA majburiy to'g'rilanadi
///      (<see cref="IdentitySequences"/>) — busiz birinchi yangi
///      foydalanuvchi ro'yxatdan o'tolmasdi.
/// </summary>
internal sealed class Migrator(
    NpgsqlConnection source,
    NpgsqlConnection target,
    MigrationOptions options,
    MigrationState state,
    MigrationReport report,
    Reporter reporter)
{
    public async Task RunAsync(IReadOnlyList<TableSpec> plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        reporter.Section("3-BOSQICH — KO'CHIRISH");

        foreach (var spec in plan)
        {
            var copier = new TableCopier(source, target, state, report, reporter, options.BatchSize);
            var tally = await copier.CopyAsync(spec, ct).ConfigureAwait(false);

            reporter.Step(string.Create(
                CultureInfo.InvariantCulture,
                $"{spec.Name}: yozildi {tally.Mapped}, o'tkazib yuborildi {tally.Skipped}"));
        }

        await LinkCuratorGroupsAsync(ct).ConfigureAwait(false);
        await IdentitySequences.ResetAsync(target, reporter, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// ========================================================================
    /// IKKINCHI QADAM: GURUH -> KURATOR GURUHI HAVOLASI
    /// ========================================================================
    ///
    /// ★ NIMA UCHUN ALOHIDA QADAM, ko'chirish paytida emas:
    /// <c>Groups.CuratorGroupId</c> <c>Groups</c> ning O'ZIGA havola
    /// qiladi. Guruhlar id bo'yicha ko'chadi, ya'ni id=10 guruh id=57
    /// kurator guruhiga ishora qilsa, yozish paytida 57 hali YO'Q va FK
    /// xatosi chiqardi. Ikkinchi qadamda esa barcha guruhlar joyida.
    ///
    /// ★ Bu qadam ham IDEMPOTENT: <c>UPDATE</c> takrorlanganda natija
    /// o'zgarmaydi.
    ///
    /// ★★ HAVOLA JIMGINA TASHLAB KETILMAYDI: u yo'qolsa kurator
    /// guruhining o'quvchilari BO'SH bo'lib qolardi (v2 da kurator
    /// guruhida to'g'ridan-to'g'ri a'zo bo'lmaydi — o'quvchilar
    /// bog'langan ustoz guruhlaridan keladi), ya'ni butun kurator oqimi
    /// ishlamay qolardi va buni hech qanday xato ko'rsatmasdi.
    /// </summary>
    private async Task LinkCuratorGroupsAsync(CancellationToken ct)
    {
        const string Sql = """
            SELECT id, curator_group_id FROM groups
            WHERE curator_group_id IS NOT NULL
            ORDER BY id
            """;

        var links = new List<(long GroupId, long CuratorGroupId)>();

        await using (var cmd = new NpgsqlCommand(Sql, source))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                links.Add((reader.GetInt64(0), reader.GetInt64(1)));
        }

        var applied = 0;
        var dropped = 0;

        foreach (var (groupId, curatorGroupId) in links)
        {
            if (!state.Has("groups", groupId) || !state.Has("groups", curatorGroupId))
            {
                dropped++;
                report.Fix("groups", groupId, "Kurator guruhi havolasi bo'shatildi (guruhlardan biri ko'chmagan)",
                    RowContext.Str(curatorGroupId));
                continue;
            }

            await using var update = new NpgsqlCommand(
                "UPDATE \"Groups\" SET \"CuratorGroupId\" = $2 WHERE \"Id\" = $1", target);
            update.Parameters.AddWithValue(groupId);
            update.Parameters.AddWithValue(curatorGroupId);
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            applied++;
        }

        reporter.Ok(string.Create(
            CultureInfo.InvariantCulture,
            $"Kurator guruhi havolalari: {applied} ta qo'yildi, {dropped} ta bo'shatildi."));
    }

    /// <summary>
    /// Maqsad baza BO'SH ekanini tekshiradi.
    ///
    /// ★ NIMA UCHUN STANDART HOLDA MAJBURIY: bu vositaning eng xavfli
    /// xatosi — uni bexosdan ISHLAB TURGAN bazaga qaratish. Bo'sh baza
    /// talabi shu xatoni birinchi soniyadayoq to'xtatadi.
    /// Uzilgan ko'chirishni davom ettirish uchun
    /// <c>--allow-nonempty-target</c> OSHKOR beriladi.
    /// </summary>
    public async Task<bool> CheckTargetEmptyAsync(IReadOnlyList<TableSpec> plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var tables = plan.Select(s => s.TargetTable).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var nonEmpty = new List<string>();

        foreach (var table in tables)
        {
            var count = await TableCopier.ScalarLongAsync(
                target,
                string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM \"{table}\""),
                ct).ConfigureAwait(false);

            if (count > 0)
                nonEmpty.Add(string.Create(CultureInfo.InvariantCulture, $"{table}={count}"));
        }

        if (nonEmpty.Count == 0)
        {
            reporter.Ok("Maqsad baza bo'sh.");
            return true;
        }

        var message = "Maqsad bazada ALLAQACHON ma'lumot bor: " + string.Join(", ", nonEmpty);

        if (options.AllowNonEmptyTarget)
        {
            report.Warn(message + " (--allow-nonempty-target berilgan — uzilgan ko'chirish davom ettirilmoqda)");
            reporter.Warn(message + " — davom etilmoqda (mavjud qatorlar TEGILMAYDI).");
            return true;
        }

        report.Fail(
            message
            + ". Bu ISHLAB TURGAN baza bo'lishi mumkin. Ataylab davom ettirmoqchi bo'lsangiz "
            + "`--allow-nonempty-target` bering (mavjud qatorlar o'zgarmaydi).");

        reporter.Error(message);
        return false;
    }
}
