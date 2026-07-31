using System.Globalization;

namespace Zinnur.Migration.Reporting;

/// <summary>
/// Yakuniy hisobotni chiqaruvchi.
///
/// ★ TARTIB ATAYLAB SHUNDAY: eng yomon xabar ENG OXIRIDA turadi.
/// Operator tunda, charchagan holda ekranning PASTIGA qaraydi —
/// "muvaffaqiyatli" yozuvi u yerda bo'lsa, uning tepasidagi 200 qatorlik
/// jadval o'qilmay qolishi mumkin. Shuning uchun XULOSA eng oxirgi
/// qator bo'ladi.
///
/// ★ IKKI RO'YXAT ALOHIDA:
///   • O'TKAZIB YUBORILGAN — qator MAQSAD BAZAGA TUSHMADI (yo'qotish);
///   • TUZATILGAN — qator tushdi, lekin qiymati o'zgartirildi (taxmin).
/// Ular bir ro'yxatda bo'lsa "necha qator yo'qoldi" degan savolga javob
/// berib bo'lmasdi.
/// </summary>
internal static class ReportPrinter
{
    public static void Print(MigrationReport report, Reporter reporter)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(reporter);

        PrintTables(report, reporter);
        PrintGrouped(reporter, "O'TKAZIB YUBORILGAN QATORLAR (maqsad bazaga TUSHMADI)",
            report.IssuesByReason(), report.Issues);
        PrintGrouped(reporter, "TUZATILGAN QIYMATLAR (qator ko'chdi, qiymati o'zgardi)",
            report.FixesByReason(), report.Fixes);
        PrintList(reporter, "DIQQAT TALAB QILADIGAN HOLATLAR", report.Warnings);
        PrintList(reporter, "XATOLAR — KO'CHIRISH MUVAFFAQIYATSIZ", report.Failures);
    }

    private static void PrintTables(MigrationReport report, Reporter reporter)
    {
        if (report.Tables.Count == 0) return;

        reporter.Section("JADVALLAR BO'YICHA SANOQ");
        reporter.Line("  manba | ko'chgan | o'tkazilgan | maqsad | qadam");
        reporter.Line("  " + new string('-', 68));

        long source = 0, mapped = 0, skipped = 0;

        foreach (var t in report.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            source += t.Source;
            mapped += t.Mapped;
            skipped += t.Skipped;

            var flag = t.Source == t.Mapped + t.Skipped ? " " : "!";

            reporter.Line(string.Create(
                CultureInfo.InvariantCulture,
                $" {flag}{t.Source,6} | {t.Mapped,8} | {t.Skipped,11} | {t.Target,6} | {t.Name}"));
        }

        reporter.Line("  " + new string('-', 68));
        reporter.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"  {source,6} | {mapped,8} | {skipped,11} |        | JAMI"));
    }

    private static void PrintGrouped(
        Reporter reporter,
        string title,
        IEnumerable<(string Table, string Reason, int Count)> grouped,
        IReadOnlyList<RowIssue> rows)
    {
        var list = grouped.ToList();
        if (list.Count == 0) return;

        reporter.Section(title);

        foreach (var (table, reason, count) in list)
        {
            reporter.Line(string.Create(CultureInfo.InvariantCulture, $"  {count,6} x  {table}: {reason}"));

            // Namuna: sabab bo'yicha bir nechta ANIQ qator ko'rsatiladi —
            // "50 ta qator tushdi" degan raqamdan ko'ra "id=317, telefon
            // +998901234567" ancha foydali (uni bazadan darhol topsa bo'ladi).
            var samples = rows
                .Where(r => string.Equals(r.Table, table, StringComparison.Ordinal)
                            && string.Equals(r.Reason, reason, StringComparison.Ordinal))
                .Take(MigrationReport.SampleLimit == 0 ? 1 : 3);

            foreach (var sample in samples)
            {
                reporter.Line(string.Create(
                    CultureInfo.InvariantCulture,
                    $"           id={sample.SourceId}{(sample.Detail is null ? string.Empty : "  " + sample.Detail)}"));
            }
        }
    }

    private static void PrintList(Reporter reporter, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;

        reporter.Section(title);

        for (var i = 0; i < items.Count && i < MigrationReport.SampleLimit * 5; i++)
            reporter.Line("  - " + items[i]);

        if (items.Count > MigrationReport.SampleLimit * 5)
        {
            reporter.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"  ... yana {items.Count - (MigrationReport.SampleLimit * 5)} ta."));
        }
    }
}
