using System.Globalization;
using Npgsql;
using Zinnur.Domain.Entities;
using Zinnur.Migration.Mapping;
using Zinnur.Migration.Reporting;

namespace Zinnur.Migration.Pipeline;

/// <summary>
/// ========================================================================
/// TAYYORGARLIK TEKSHIRUVI — FAQAT O'QIYDI, HECH NARSA YOZMAYDI
/// ========================================================================
///
/// ★ NIMA UCHUN ALOHIDA BOSQICH: ko'chirish TUNDA, tizim yopilgan
/// holatda bajariladi. To'siqni ko'chirishning o'rtasida topish —
/// eng yomon holat: yarim ko'chgan baza, charchagan operator va
/// ortga qaytish uchun qolgan bir necha soat.
///
/// Shuning uchun MANBA bilan bog'liq hamma narsa OLDINDAN, ish kunida,
/// ishlab turgan tizimga UMUMAN tegmasdan tekshiriladi:
///
///   1. Kerakli jadvallar bormi;
///   2. Vaqt ustunlari HAQIQATAN <c>timestamptz</c> mi (agar naive
///      <c>timestamp</c> bo'lsa, barcha dars vaqtlari 5 soat siljirdi);
///   3. Telefon DUBLIKATLARI (v2 da filtrlangan unikal indeks);
///   4. Elektron pochta dublikatlari (kichik harfga o'tgandan keyin);
///   5. Kvitansiya raqami dublikatlari;
///   6. Kursga bog'lanmagan modullar (v2 da <c>CourseId</c> majburiy);
///   7. Eski ERKIN SATR ustunlaridagi TANILMAGAN qiymatlar.
///
/// 7-band alohida qimmatli: u "ko'chirish qancha qator yo'qotadi" degan
/// savolga KO'CHIRISHDAN OLDIN javob beradi.
/// </summary>
internal static class Preflight
{
    /// <summary>Manbada bo'lishi SHART bo'lgan jadvallar.</summary>
    private static readonly string[] RequiredTables =
    [
        "users", "courses", "modules", "module_lessons", "groups", "group_members",
        "lessons", "attendance", "chat_messages", "dm_messages",
        "assignments", "submissions", "submission_files", "lesson_progress",
        "tests", "test_questions", "test_options", "test_attempts", "test_answers",
        "tariffs", "student_discounts", "payments", "payment_transactions", "payment_audit",
    ];

    /// <summary>
    /// Vaqt ustunlari — ular <c>timestamptz</c> bo'lishi SHART.
    /// (jadval, ustun) juftlari.
    /// </summary>
    private static readonly (string Table, string Column)[] InstantColumns =
    [
        ("lessons", "scheduled_start"),
        ("lessons", "scheduled_end"),
        ("attendance", "joined_at"),
        ("chat_messages", "created_at"),
        ("payments", "created_at"),
        ("users", "created_at"),
    ];

    public static async Task<bool> RunAsync(
        NpgsqlConnection source,
        MigrationState state,
        MigrationReport report,
        Reporter reporter,
        bool allowOrphanModules,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);

        reporter.Section("1-BOSQICH — TAYYORGARLIK TEKSHIRUVI (manba faqat o'qiladi)");

        var ok = await CheckTablesAsync(source, report, reporter, ct).ConfigureAwait(false);
        if (!ok) return false;                       // jadvallarsiz qolgan tekshiruvlar ma'nosiz

        ok &= await CheckTimestampsAsync(source, report, reporter, ct).ConfigureAwait(false);
        await CheckPhonesAsync(source, state, report, reporter, ct).ConfigureAwait(false);
        ok &= await CheckEmailsAsync(source, state, report, reporter, ct).ConfigureAwait(false);
        await CheckReceiptsAsync(source, state, reporter, ct).ConfigureAwait(false);
        ok &= await CheckOrphanModulesAsync(source, report, reporter, allowOrphanModules, ct).ConfigureAwait(false);
        await CheckUnknownValuesAsync(source, report, reporter, ct).ConfigureAwait(false);

        return ok;
    }

    // ---------------------------------------------------------------- 1. jadvallar

    private static async Task<bool> CheckTablesAsync(
        NpgsqlConnection source, MigrationReport report, Reporter reporter, CancellationToken ct)
    {
        const string Sql = """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            """;

        var present = new HashSet<string>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(Sql, source))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                present.Add(reader.GetString(0));
        }

        var missing = RequiredTables.Where(t => !present.Contains(t)).ToList();
        foreach (var table in missing)
            report.Fail($"Manbada `{table}` jadvali YO'Q — ulanish satri to'g'rimi?");

        if (missing.Count == 0) reporter.Ok("Manbadagi barcha kerakli jadvallar joyida.");
        return missing.Count == 0;
    }

    // ---------------------------------------------------------------- 2. vaqt mintaqasi

    /// <summary>
    /// ★★ ENG JIM XAVFLARDAN BIRI. Eski ilova vaqtni
    /// <c>_local_to_utc()</c> orqali ANIQ UTC instant qilib yozadi va
    /// ustunlar <c>timestamptz</c>. Shu tufayli ko'chirishda hech qanday
    /// siljitish KERAK EMAS.
    ///
    /// Lekin bu FARAZ, va u xato bo'lsa (ustun naive <c>timestamp</c>
    /// bo'lsa) barcha dars vaqtlari 5 soatga siljirdi — o'quvchilar
    /// darsga kelmasdi va sababini hech kim tushunmasdi. Shuning uchun
    /// faraz KO'CHIRISHDAN OLDIN bazaning o'zidan TEKSHIRILADI.
    /// </summary>
    private static async Task<bool> CheckTimestampsAsync(
        NpgsqlConnection source, MigrationReport report, Reporter reporter, CancellationToken ct)
    {
        const string Sql = """
            SELECT table_name, column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public' AND data_type LIKE 'timestamp%'
            """;

        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(Sql, source))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                types[reader.GetString(0) + "." + reader.GetString(1)] = reader.GetString(2);
        }

        var bad = 0;
        foreach (var (table, column) in InstantColumns)
        {
            if (!types.TryGetValue(table + "." + column, out var type)) continue;

            if (!string.Equals(type, "timestamp with time zone", StringComparison.Ordinal))
            {
                report.Fail(
                    $"`{table}`.`{column}` turi `{type}` — kutilgani `timestamp with time zone`. "
                    + "Vaqt mintaqasiz saqlangan bo'lsa ko'chirishda 5 soatlik siljish YUZ BERADI; "
                    + "ko'chirishdan oldin qaysi mintaqada yozilgani ANIQLANISHI shart.");
                bad++;
            }
        }

        if (bad == 0)
            reporter.Ok("Vaqt ustunlari `timestamptz` — siljitish KERAK EMAS (tekshirildi, farazga tayanilmadi).");

        return bad == 0;
    }

    // ---------------------------------------------------------------- 3. telefon dublikatlari

    /// <summary>
    /// ★★ TELEFON DUBLIKATLARI JIMGINA YUTILMAYDI.
    ///
    /// v2 da <c>Users.PhoneNormalized</c> ustunida FILTRLANGAN UNIKAL
    /// indeks bor: <c>+998 90 123 45 67</c> va <c>998901234567</c> BIR XIL
    /// raqam. Eski bazada bunday cheklov yo'q edi va dublikat butunlay
    /// tabiiy (aka-uka bir raqam bergan, ota-ona raqami ikki bolaga
    /// yozilgan).
    ///
    /// Normalizatsiya v2 ning O'Z kodi (<see cref="User.NormalizePhone"/>)
    /// bilan qilinadi — bu yerda ikkinchi nusxa yozilsa, ko'chirilgan
    /// raqamlar ilova qidiruviga tushmay qolardi.
    ///
    /// Har dublikat guruh HISOBOTGA CHIQADI: eng kichik <c>id</c> raqamni
    /// oladi, qolganlarida <c>PhoneNormalized</c> <c>NULL</c> bo'ladi
    /// (<c>Phone</c> ko'rinishda QOLADI, ya'ni xodim uni panelda ko'radi).
    /// </summary>
    private static async Task CheckPhonesAsync(
        NpgsqlConnection source, MigrationState state, MigrationReport report,
        Reporter reporter, CancellationToken ct)
    {
        const string Sql = """
            SELECT id, phone FROM users
            WHERE phone IS NOT NULL AND btrim(phone) <> ''
            ORDER BY id
            """;

        var byNormalized = new Dictionary<string, List<(long Id, string Raw)>>(StringComparer.Ordinal);

        await using (var cmd = new NpgsqlCommand(Sql, source))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var raw = reader.GetString(1);
                var normalized = User.NormalizePhone(raw);
                if (normalized is null) continue;    // raqamsiz matn — telefonsiz deb qaraladi

                if (!byNormalized.TryGetValue(normalized, out var list))
                {
                    list = [];
                    byNormalized[normalized] = list;
                }

                list.Add((reader.GetInt64(0), raw));
            }
        }

        var groups = 0;
        var losers = 0;

        foreach (var (normalized, users) in byNormalized.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (users.Count < 2) continue;

            groups++;
            var winner = users[0];                  // ro'yxat id bo'yicha tartiblangan

            for (var i = 1; i < users.Count; i++)
            {
                state.PhoneDuplicateLosers.Add(users[i].Id);
                losers++;
            }

            var ids = string.Join(", ", users.Select(u => u.Id.ToString(CultureInfo.InvariantCulture)));
            var raws = string.Join(" | ", users.Select(u => u.Raw));

            report.Warn(
                Inv.S($"TELEFON DUBLIKATI {normalized}: id={ids} (asl ko'rinishlar: {raws}). ")
                + Inv.S($"Normallashtirilgan raqamni id={winner.Id} oladi, ")
                + "qolganlarida PhoneNormalized NULL bo'ladi (Phone ko'rinishda saqlanadi).");
        }

        if (groups == 0)
        {
            reporter.Ok("Telefon dublikati topilmadi.");
        }
        else
        {
            reporter.Warn(Inv.S($"TELEFON DUBLIKATI: {groups} ta raqam, {losers} ta foydalanuvchida PhoneNormalized NULL bo'ladi ")
                + "(ro'yxat yakuniy hisobotda).");
        }
    }

    // ---------------------------------------------------------------- 4. pochta dublikatlari

    /// <summary>
    /// Elektron pochta v2 da KICHIK HARFDA va UNIKAL. Eski bazada
    /// <c>UNIQUE</c> bor edi, lekin REGISTRGA SEZGIR: <c>Ali@x.uz</c> va
    /// <c>ali@x.uz</c> ikki xil qator bo'la olardi. v2 ga ikkalasi ham
    /// ko'cha OLMAYDI.
    ///
    /// ★ BU HOLAT XATO (<c>Fail</c>), OGOHLANTIRISH EMAS: foydalanuvchi
    /// ko'chmasa uning BUTUN daraxti (guruh a'zoligi, davomat, to'lovlar)
    /// ham tushib qoladi. To'g'ri yechim — eski bazada pochtani qo'lda
    /// tuzatib, keyin ko'chirish.
    /// </summary>
    private static async Task<bool> CheckEmailsAsync(
        NpgsqlConnection source, MigrationState state, MigrationReport report,
        Reporter reporter, CancellationToken ct)
    {
        const string Sql = """
            SELECT lower(btrim(email)) AS key, array_agg(id ORDER BY id) AS ids
            FROM users
            WHERE email IS NOT NULL AND btrim(email) <> ''
            GROUP BY 1
            HAVING COUNT(*) > 1
            ORDER BY 1
            """;

        var groups = 0;

        await using (var cmd = new NpgsqlCommand(Sql, source))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                var ids = (long[])reader.GetValue(1);
                groups++;

                for (var i = 1; i < ids.Length; i++)
                    state.EmailDuplicateLosers.Add(ids[i]);

                var idList = string.Join(", ", ids.Select(v => v.ToString(CultureInfo.InvariantCulture)));

                report.Fail(
                    Inv.S($"POCHTA DUBLIKATI `{key}`: id={idList}. ")
                    + Inv.S($"v2 da pochta kichik harfda va UNIKAL — faqat id={ids[0]} ko'chadi, ")
                    + "qolganlari BUTUN daraxti bilan tushib qoladi. Eski bazada tuzating.");
            }
        }

        if (groups == 0) reporter.Ok("Elektron pochta dublikati topilmadi.");
        else reporter.Error(string.Create(CultureInfo.InvariantCulture, $"POCHTA DUBLIKATI: {groups} ta guruh."));

        return groups == 0;
    }

    // ---------------------------------------------------------------- 5. kvitansiya dublikatlari

    private static async Task CheckReceiptsAsync(
        NpgsqlConnection source, MigrationState state, Reporter reporter, CancellationToken ct)
    {
        const string Sql = """
            SELECT array_agg(id ORDER BY id) AS ids
            FROM payment_transactions
            WHERE receipt_no IS NOT NULL AND btrim(receipt_no) <> ''
            GROUP BY btrim(receipt_no)
            HAVING COUNT(*) > 1
            """;

        var groups = 0;

        await using (var cmd = new NpgsqlCommand(Sql, source))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var ids = (long[])reader.GetValue(0);
                groups++;

                for (var i = 1; i < ids.Length; i++)
                    state.ReceiptDuplicateLosers.Add(ids[i]);
            }
        }

        if (groups == 0)
            reporter.Ok("Kvitansiya raqami dublikati topilmadi.");
        else
            reporter.Warn(string.Create(CultureInfo.InvariantCulture,
                $"KVITANSIYA DUBLIKATI: {groups} ta raqam — takrorlanganlarida ReceiptNo bo'shatiladi (asli izohga yoziladi)."));
    }

    // ---------------------------------------------------------------- 6. kursi yo'q modullar

    private static async Task<bool> CheckOrphanModulesAsync(
        NpgsqlConnection source, MigrationReport report, Reporter reporter,
        bool allow, CancellationToken ct)
    {
        var orphans = await TableCopier.ScalarLongAsync(
            source, "SELECT COUNT(*) FROM modules WHERE course_id IS NULL", ct).ConfigureAwait(false);

        if (orphans == 0)
        {
            reporter.Ok("Kursga bog'lanmagan modul yo'q.");
            return true;
        }

        var lessons = await TableCopier.ScalarLongAsync(
            source,
            "SELECT COUNT(*) FROM module_lessons ml JOIN modules m ON m.id = ml.module_id WHERE m.course_id IS NULL",
            ct).ConfigureAwait(false);

        var message = Inv.S($"KURSSIZ MODUL: {orphans} ta modul va ular ichidagi {lessons} ta kurs darsi. ")
            + "v2 da `Modules.CourseId` MAJBURIY — bu modullar va ULAR BILAN BIRGA butun daraxt "
            + "(darslar, vazifalar, testlar, progress) KO'CHMAYDI. "
            + "To'g'ri yechim: eski bazada modullarga kurs biriktirib, keyin ko'chirish.";

        if (allow)
        {
            report.Warn(message + " (--allow-orphan-modules berilgani uchun davom etilmoqda)");
            reporter.Warn(message);
            return true;
        }

        report.Fail(message);
        reporter.Error(message);
        return false;
    }

    // ---------------------------------------------------------------- 7. tanilmagan qiymatlar

    /// <summary>
    /// Eski bazadagi ERKIN SATR ustunlaridagi barcha turli qiymatlarni
    /// o'qib, ularning har birini <see cref="LegacyMap"/> tanishini
    /// tekshiradi.
    ///
    /// ★ NIMA UCHUN BU ENG FOYDALI TEKSHIRUV: u "ko'chirish qancha qator
    /// yo'qotadi" degan savolga KO'CHIRISHDAN OLDIN, ish kunida javob
    /// beradi. Tanilmagan qiymat topilsa yechim oddiy — yo eski bazada
    /// qiymat tuzatiladi, yo <see cref="LegacyMap"/> ga bitta qator
    /// qo'shiladi.
    /// </summary>
    private static async Task CheckUnknownValuesAsync(
        NpgsqlConnection source, MigrationReport report, Reporter reporter, CancellationToken ct)
    {
        var checks = new (string Table, string Column, Func<string?, bool> Known)[]
        {
            ("users", "role::text", v => LegacyMap.TryRole(v, out _)),
            ("groups", "group_type", v => LegacyMap.TryGroupType(v, out _)),
            ("groups", "status", v => LegacyMap.TryGroupActive(v, out _)),
            ("group_members", "status", v => LegacyMap.TryMemberStatus(v, out _)),
            ("lessons", "type::text", v => LegacyMap.TrySessionType(v, out _)),
            ("lessons", "status::text", v => LegacyMap.TrySessionStatus(v, out _)),
            ("attendance", "status::text", v => LegacyMap.TryAttendanceStatus(v, out _)),
            ("chat_messages", "channel", v => { LegacyMap.Channel(v, out var known); return known; }),
            ("submissions", "status", v => LegacyMap.TrySubmissionStatus(v, out _)),
            ("test_attempts", "status", v => LegacyMap.TryAttemptStatus(v, out _)),
            ("tests", "kind", v => LegacyMap.TryTestKind(v, out _)),
            ("payments", "status", v => LegacyMap.TryPaymentStatus(v, out _)),
            ("payments", "method", v => { LegacyMap.Method(v, out var known); return known; }),
            ("payment_transactions", "type", v => LegacyMap.TryTransactionKind(v, out _)),
            ("payment_transactions", "method", v => { LegacyMap.Method(v, out var known); return known; }),
            ("student_discounts", "kind", v => LegacyMap.TryDiscountKind(v, out _)),
        };

        var unknown = 0;

        foreach (var (table, column, known) in checks)
        {
            // `payment_transactions.type = 'due'` ATAYLAB ko'chirilmaydi —
            // uni "tanilmagan qiymat" sifatida ko'rsatish yolg'on signal
            // bo'lardi. Shuning uchun alohida hisoblanadi.
            var sql = string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT {column} AS v, COUNT(*) FROM {table} GROUP BY 1 ORDER BY 2 DESC");

            await using var cmd = new NpgsqlCommand(sql, source);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var value = reader.IsDBNull(0) ? null : reader.GetString(0);
                var count = reader.GetInt64(1);

                if (known(value)) continue;

                if (string.Equals(table, "payment_transactions", StringComparison.Ordinal)
                    && string.Equals(column, "type", StringComparison.Ordinal)
                    && string.Equals(value?.Trim(), "due", StringComparison.OrdinalIgnoreCase))
                {
                    reporter.Line(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  [ma'lum] payment_transactions.type='due': {count} qator ATAYLAB ko'chirilmaydi."));
                    continue;
                }

                unknown++;
                var shown = value ?? "NULL";

                report.Warn(
                    Inv.S($"TANILMAGAN QIYMAT `{table}`.`{column}` = `{shown}` ({count} qator). ")
                    + "Bu qatorlar ko'chmaydi yoki standart qiymat oladi — Mapping/LegacyMap.cs ga qo'shing.");
            }
        }

        if (unknown == 0)
            reporter.Ok("Eski satrli ustunlardagi barcha qiymatlar tanildi.");
        else
            reporter.Warn(string.Create(CultureInfo.InvariantCulture,
                $"TANILMAGAN QIYMATLAR: {unknown} ta (ro'yxat yakuniy hisobotda)."));
    }
}
