using System.Globalization;
using Npgsql;
using Zinnur.Domain.Enums;
using Zinnur.Migration.Reporting;

namespace Zinnur.Migration.Pipeline;

/// <summary>
/// ========================================================================
/// TEKSHIRISH (RECONCILIATION) — VOSITANING ENG MUHIM QISMI
/// ========================================================================
///
/// ★ NIMA UCHUN KO'CHIRISHNING O'ZIDAN MUHIMROQ: ma'lumot ko'chirishda
/// eng qimmat xato — "hammasi o'tdi" degan YOLG'ON hisobot. Bir necha yuz
/// qator jimgina tushib qolsa buni oylab hech kim sezmaydi — o'quvchi
/// "mening to'lovim ko'rinmayapti" deb kelguncha, va o'shanda eski baza
/// allaqachon o'chirilgan bo'lishi mumkin.
///
/// Shuning uchun bu yerda BEShTA mustaqil tekshiruv bor va ularning
/// birortasi ham "vosita nima deb o'ylayapti" ga tayanmaydi — hammasi
/// BAZANING O'ZIDAN o'qiladi:
///
///   1. SANOQ:  manba = ko'chgan + o'tkazilgan,  maqsad = ko'chgan;
///   2. PUL:    manba yig'indisi = ko'chgan + ko'chmagan pul,
///              maqsad yig'indisi = ko'chgan pul (TIYINIGACHA);
///   3. HAFTA KUNLARI: haqiqiy dars SANALARI guruh jadvaliga mos keladimi
///      (bir kunlik siljish shu yerda ushlanadi);
///   4. CHAT: har (guruh, kanal) juftligi ALOHIDA sanaladi — ikki oqim
///      qo'shilib ketgani jami raqamda KO'RINMAYDI;
///   5. IDENTITY: ketma-ketliklar MAX(Id) dan oldinda turibdimi.
///
/// Bittasi ham buzilsa vosita XATO KODI bilan tugaydi.
/// </summary>
internal sealed class Reconciler(
    NpgsqlConnection source,
    NpgsqlConnection target,
    MigrationReport report,
    Reporter reporter)
{
    /// <summary>
    /// Pul yig'indilari: (kalit, manba SQL, maqsad SQL).
    ///
    /// ★ IKKI TENGLIK TEKSHIRILADI:
    ///   manba  = ko'chgan + ko'chmagan/tuzatilgan  (hech bir tiyin izsiz yo'qolmagan)
    ///   maqsad = ko'chgan                          (yozilgani aynan yozilgan)
    /// Ikkalasi birga bo'lgandagina "pul mos keldi" deyish mumkin.
    ///
    /// ★ "KO'CHMAGAN/TUZATILGAN" USTUNI MANFIY ham bo'lishi mumkin: u
    /// yo'qotish emas, manba va maqsad yig'indilari o'rtasidagi AYIRMA.
    /// Masalan eski tizim qaytarilgan pulni manfiy summa bilan yozgan, v2
    /// esa musbat summa + <c>Refund</c> turi bilan yozadi — ayirma manfiy
    /// bo'ladi va aynan shu qiymat tenglikni saqlab qoladi.
    /// </summary>
    private static readonly (string Key, string SourceSql, string TargetSql)[] MoneyChecks =
    [
        (
            "Payments.Amount",
            "SELECT COALESCE(SUM(amount), 0) FROM payments",
            "SELECT COALESCE(SUM(\"Amount\"), 0) FROM \"Payments\""
        ),
        (
            "Payments.PaidAmount",
            "SELECT COALESCE(SUM(paid_amount), 0) FROM payments",
            "SELECT COALESCE(SUM(\"PaidAmount\"), 0) FROM \"Payments\""
        ),
        (
            "PaymentTransactions.Amount",
            "SELECT COALESCE(SUM(amount), 0) FROM payment_transactions",
            "SELECT COALESCE(SUM(\"Amount\"), 0) FROM \"PaymentTransactions\""
        ),
        (
            "StudentAccounts.Balance",
            "SELECT COALESCE(SUM(balance), 0) FROM users",
            "SELECT COALESCE(SUM(\"Balance\"), 0) FROM \"StudentAccounts\""
        ),
        (
            "Tariffs.Amount",
            "SELECT COALESCE(SUM(amount), 0) FROM tariffs",
            "SELECT COALESCE(SUM(\"Amount\"), 0) FROM \"Tariffs\""
        ),
    ];

    private static readonly string[] WeekdayNames =
        ["Yakshanba", "Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba"];

    public async Task RunAsync(IReadOnlyList<TableSpec> plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        reporter.Section("4-BOSQICH — TEKSHIRISH (manba va maqsadni solishtirish)");

        await CountsAsync(plan, ct).ConfigureAwait(false);
        await MoneyAsync(ct).ConfigureAwait(false);
        await WeekdaysAsync(ct).ConfigureAwait(false);
        await ChatChannelsAsync(ct).ConfigureAwait(false);
        await IdentitySequences.VerifyAsync(target, report, reporter, ct).ConfigureAwait(false);
    }

    // ================================================================== 1. SANOQ

    private async Task CountsAsync(IReadOnlyList<TableSpec> plan, CancellationToken ct)
    {
        var bad = 0;

        // (a) Har reja qadami uchun: manba = ko'chgan + o'tkazilgan.
        foreach (var tally in report.Tables)
        {
            if (tally.Source == tally.Mapped + tally.Skipped) continue;

            report.Fail(Inv.S($"`{tally.Name}`: manbada {tally.Source}, lekin ko'chgan {tally.Mapped} + o'tkazilgan {tally.Skipped} ")
                + Inv.S($"= {tally.Mapped + tally.Skipped}. Farq {tally.Source - tally.Mapped - tally.Skipped} qator — ")
                + "ular hech qayerda hisobga olinmagan.");
            bad++;
        }

        // (b) Har MAQSAD jadval uchun: bazadagi haqiqiy son = ko'chgan son.
        //     Bir jadvalga ikki reja qadami yozishi mumkin (masalan
        //     `SubmissionFiles`), shuning uchun yig'indi olinadi.
        var byTarget = report.Tables
            .GroupBy(t => t.TargetTable, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in byTarget)
        {
            var expected = group.Sum(t => t.Mapped);
            var actual = await TableCopier.ScalarLongAsync(
                target,
                string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM \"{group.Key}\""),
                ct).ConfigureAwait(false);

            foreach (var tally in group) tally.Target = actual;

            if (expected == actual) continue;

            report.Fail(Inv.S($"`{group.Key}`: vosita {expected} qator yozdim dedi, bazada esa {actual} qator. ")
                + "Farq — dublikat kalit (ON CONFLICT) yoki tashqi yozuv.");
            bad++;
        }

        if (bad == 0) reporter.Ok("Sanoq mos: manba = ko'chgan + o'tkazilgan, maqsad = ko'chgan.");
    }

    // ================================================================== 2. PUL

    private async Task MoneyAsync(CancellationToken ct)
    {
        var bad = 0;

        foreach (var (key, sourceSql, targetSql) in MoneyChecks)
        {
            var sourceTotal = await TableCopier.ScalarMoneyAsync(source, sourceSql, ct).ConfigureAwait(false);
            var targetTotal = await TableCopier.ScalarMoneyAsync(target, targetSql, ct).ConfigureAwait(false);
            var migrated = report.Money(key);
            var skipped = report.SkippedMoney(key);

            if (sourceTotal != migrated + skipped)
            {
                report.Fail(Inv.S($"PUL `{key}`: manbada {MigrationReport.Format(sourceTotal)}, ")
                    + Inv.S($"ko'chgan {MigrationReport.Format(migrated)} + ko'chmagan/tuzatilgan {MigrationReport.Format(skipped)} ")
                    + Inv.S($"= {MigrationReport.Format(migrated + skipped)}. ")
                    + Inv.S($"Hisobga olinmagan: {MigrationReport.Format(sourceTotal - migrated - skipped)}."));
                bad++;
            }

            if (targetTotal != migrated)
            {
                report.Fail(Inv.S($"PUL `{key}`: vosita {MigrationReport.Format(migrated)} yozdim dedi, ")
                    + Inv.S($"bazada esa {MigrationReport.Format(targetTotal)}. ")
                    + Inv.S($"Farq: {MigrationReport.Format(targetTotal - migrated)}."));
                bad++;
            }

            reporter.Line(Inv.S($"  {key,-30} manba {MigrationReport.Format(sourceTotal),18} | ")
                + Inv.S($"ko'chgan {MigrationReport.Format(migrated),18} | ")
                + Inv.S($"ko'chmagan/tuzat. {MigrationReport.Format(skipped),14} | ")
                + Inv.S($"maqsad {MigrationReport.Format(targetTotal),18}"));
        }

        if (bad == 0) reporter.Ok("Pul yig'indilari TIYINIGACHA mos keldi.");
    }

    // ================================================================== 3. HAFTA KUNLARI

    /// <summary>
    /// ========================================================================
    /// BIR KUNLIK SILJISH TEKSHIRUVI — HAQIQIY DARS SANALARI BILAN
    /// ========================================================================
    ///
    /// ★ MUAMMO: eski Python <c>date.weekday()</c> da DUSHANBA = 0,
    /// .NET <see cref="DayOfWeek"/> da esa YAKSHANBA = 0. Konvertatsiyasiz
    /// barcha guruhlarning dars kunlari bir kun oldinga siljirdi va
    /// jadval "to'g'ri" ko'rinib turaverardi — buni na FK, na CHECK
    /// ushlamasdi.
    ///
    /// ★ NIMA UCHUN FORMULANI QAYTA HISOBLASH YETARLI EMAS: u faqat
    /// "kod o'zi yozgan narsani o'zi tasdiqlashi" bo'lardi. Shuning uchun
    /// tekshiruv HAQIQIY DARS SANALARIDAN boradi:
    ///
    ///   • har jonli darsning Toshkent bo'yicha hafta kuni olinadi;
    ///   • u guruhning ko'chirilgan <c>Weekdays</c> ro'yxatida bormi
    ///     (TO'G'RI moslik);
    ///   • BIR KUN ORQAGA surilgan holda mos kelarmidi (SILJIGAN moslik).
    ///
    /// Agar siljigan mosliklar to'g'risidan KO'P bo'lsa — bu aynan
    /// off-by-one xatosining IMZOSI va ko'chirish MUVAFFAQIYATSIZ deb
    /// belgilanadi.
    ///
    /// Postgres <c>EXTRACT(DOW)</c> ham YAKSHANBA = 0 — ya'ni .NET bilan
    /// AYNI konvensiya, qo'shimcha o'girish kerak emas.
    /// </summary>
    private async Task WeekdaysAsync(CancellationToken ct)
    {
        // --- (a) guruh jadvallari: manba (Python) -> maqsad (.NET) ---
        var sourceWeekdays = new Dictionary<long, short[]>();

        await using (var cmd = new NpgsqlCommand(
            "SELECT id, COALESCE(teacher_weekdays, '{}') FROM groups ORDER BY id", source))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                sourceWeekdays[reader.GetInt64(0)] = (short[])reader.GetValue(1);
        }

        var mismatched = 0;

        await using (var cmd = new NpgsqlCommand(
            "SELECT \"Id\", \"Weekdays\" FROM \"Groups\" ORDER BY \"Id\"", target))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var actual = ((int[])reader.GetValue(1)).Order().ToArray();

                if (!sourceWeekdays.TryGetValue(id, out var python)) continue;

                var expected = python
                    .Where(d => d is >= 0 and <= 6)
                    .Select(d => (d + 1) % 7)
                    .Distinct()
                    .Order()
                    .ToArray();

                if (actual.SequenceEqual(expected)) continue;

                mismatched++;
                report.Fail(Inv.S($"HAFTA KUNI `Groups`.`Id`={id}: kutilgan [{string.Join(", ", expected)}], ")
                    + Inv.S($"bazada [{string.Join(", ", actual)}] (eski Python [{string.Join(", ", python)}])."));
            }
        }

        if (mismatched == 0 && sourceWeekdays.Count > 0)
            reporter.Ok("Guruh jadvallari: (python + 1) % 7 konvertatsiyasi barcha guruhlarda to'g'ri.");

        // --- (b) HAQIQIY DARS SANALARI ---
        const string Sql = """
            SELECT s."Id",
                   g."Id",
                   (s."ScheduledStart" AT TIME ZONE 'Asia/Tashkent')::date          AS local_date,
                   EXTRACT(DOW FROM (s."ScheduledStart" AT TIME ZONE 'Asia/Tashkent'))::int AS dow,
                   g."Weekdays"
            FROM "LiveSessions" s
            JOIN "Groups" g ON g."Id" = s."GroupId"
            WHERE array_length(g."Weekdays", 1) > 0
            ORDER BY s."Id"
            """;

        long direct = 0, shifted = 0, neither = 0;
        var samples = new List<string>();

        await using (var cmd = new NpgsqlCommand(Sql, target))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var sessionId = reader.GetInt64(0);
                var groupId = reader.GetInt64(1);
                var date = reader.GetFieldValue<DateTime>(2);
                var dow = reader.GetInt32(3);
                var weekdays = (int[])reader.GetValue(4);

                var isDirect = Array.IndexOf(weekdays, dow) >= 0;
                var isShifted = Array.IndexOf(weekdays, (dow + 6) % 7) >= 0;

                if (isDirect) direct++;
                else if (isShifted) shifted++;
                else neither++;

                if (samples.Count < 8)
                {
                    samples.Add(Inv.S($"dars #{sessionId} (guruh {groupId}): {date:yyyy-MM-dd} = {WeekdayNames[dow]} (DOW={dow}), ")
                        + Inv.S($"guruh jadvali [{string.Join(", ", weekdays.Select(w => WeekdayNames[w]))}] -> ")
                        + (isDirect ? "MOS" : isShifted ? "BIR KUN SILJIGAN" : "umuman mos emas"));
                }
            }
        }

        foreach (var sample in samples) reporter.Line("  " + sample);

        reporter.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"  Jami: mos {direct}, bir kun siljigan {shifted}, mos emas {neither}."));

        if (shifted > direct)
        {
            report.Fail(Inv.S($"HAFTA KUNI SILJIGAN: {shifted} ta dars sanasi guruh jadvaliga BIR KUN SURILGANDA mos keladi, ")
                + Inv.S($"to'g'ridan-to'g'ri esa faqat {direct} tasi. Bu Python(Dushanba=0) -> .NET(Yakshanba=0) ")
                + "konvertatsiyasi bajarilmaganining imzosi.");
        }
        else if (direct > 0)
        {
            reporter.Ok(string.Create(
                CultureInfo.InvariantCulture,
                $"Hafta kunlari SILJIMAGAN: {direct} ta dars sanasi guruh jadvaliga to'g'ridan-to'g'ri mos keldi."));
        }

        if (neither > 0)
        {
            report.Warn(Inv.S($"{neither} ta dars guruh jadvalidagi kunlarga umuman to'g'ri kelmadi. ")
                + "Bu odatda qo'lda ko'chirilgan yoki jadval o'zgargandan keyingi darslar — "
                + "ko'chirish xatosi EMAS, lekin ko'z bilan tekshirilsin.");
        }
    }

    // ================================================================== 4. CHAT OQIMLARI

    /// <summary>
    /// ========================================================================
    /// GURUH CHATINING IKKI OQIMI QO'SHILIB KETMAGANINI ISBOTLASH
    /// ========================================================================
    ///
    /// ★ ENG JIM ZARAR: eski ilovada o'quvchi USTOZGA va KURATORGA
    /// ALOHIDA yozadi (<c>chat_messages.channel</c> = <c>teacher</c> /
    /// <c>assistant</c>). Kanal tashlab yuborilsa ikki oqim bitta bo'lib
    /// qoladi va ustoz o'quvchining KURATORGA atalgan savollarini o'qib
    /// qoladi. Buni na FK, na CHECK ushlaydi.
    ///
    /// ★ NIMA UCHUN JAMI SON YETARLI EMAS: oqimlar qo'shilganda XABARLAR
    /// SONI O'ZGARMAYDI — jami raqam baribir to'g'ri chiqaveradi. Faqat
    /// (guruh, kanal) kesimida sanaganda buzilish ko'rinadi.
    ///
    /// Ikki tenglik tekshiriladi (har juftlik uchun alohida):
    ///   manba(guruh, kanal)  = ko'chgan + o'tkazilgan
    ///   maqsad(guruh, kanal) = ko'chgan
    /// </summary>
    private async Task ChatChannelsAsync(CancellationToken ct)
    {
        // --- manba: kanal AYNI xaritalash bilan aniqlanadi ---
        const string SourceSql = """
            SELECT cm.group_id,
                   CASE WHEN lower(btrim(COALESCE(cm.channel, ''))) IN ('assistant', 'curator')
                        THEN 1 ELSE 0 END AS channel,
                   COUNT(*)
            FROM chat_messages cm
            JOIN users u ON u.id = cm.sender_id
            GROUP BY 1, 2
            ORDER BY 1, 2
            """;

        const string TargetSql = """
            SELECT "GroupId", "Channel", COUNT(*)
            FROM "GroupChatMessages"
            GROUP BY 1, 2
            ORDER BY 1, 2
            """;

        var sourceCounts = await ReadChannelsAsync(source, SourceSql, ct).ConfigureAwait(false);
        var targetCounts = await ReadChannelsAsync(target, TargetSql, ct).ConfigureAwait(false);

        var bad = 0;
        var keys = sourceCounts.Keys
            .Concat(targetCounts.Keys)
            .Concat(report.Channels.Keys)
            .Distinct()
            .OrderBy(k => k.GroupId)
            .ThenBy(k => k.Channel);

        reporter.Line("  guruh | kanal   | manba | ko'chgan | o'tkazilgan | maqsad");

        foreach (var key in keys)
        {
            var src = sourceCounts.GetValueOrDefault(key);
            var tgt = targetCounts.GetValueOrDefault(key);
            report.Channels.TryGetValue(key, out var tally);
            var migrated = tally?.Migrated ?? 0;
            var skipped = tally?.Skipped ?? 0;

            var channelName = key.Channel == (int)GroupChatChannel.Curator ? "Curator" : "Teacher";

            reporter.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"  {key.GroupId,5} | {channelName,-7} | {src,5} | {migrated,8} | {skipped,11} | {tgt,6}"));

            if (src != migrated + skipped)
            {
                report.Fail(Inv.S($"CHAT (guruh {key.GroupId}, {channelName}): manbada {src}, ")
                    + Inv.S($"ko'chgan {migrated} + o'tkazilgan {skipped} = {migrated + skipped}."));
                bad++;
            }

            if (tgt != migrated)
            {
                report.Fail(Inv.S($"CHAT (guruh {key.GroupId}, {channelName}): vosita {migrated} yozdim dedi, bazada {tgt}. ")
                    + "Ikki oqim qo'shilib ketgan bo'lishi mumkin.");
                bad++;
            }
        }

        if (bad == 0)
            reporter.Ok("Chat oqimlari AJRALGANICHA qoldi: har (guruh, kanal) juftligi alohida mos keldi.");
    }

    private static async Task<Dictionary<(long GroupId, int Channel), long>> ReadChannelsAsync(
        NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        var result = new Dictionary<(long, int), long>();

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result[(reader.GetInt64(0), reader.GetInt32(1))] = reader.GetInt64(2);

        return result;
    }
}
