using System.Globalization;

namespace Zinnur.Migration.Reporting;

/// <summary>Bitta o'tkazib yuborilgan yoki tuzatilgan qator haqidagi yozuv.</summary>
/// <param name="Table">Manba jadval nomi.</param>
/// <param name="SourceId">Eski qator <c>id</c> si.</param>
/// <param name="Reason">Sabab — hisobotda AYNAN shu matn guruhlanadi.</param>
/// <param name="Detail">Qo'shimcha tafsilot (asl qiymat va h.k.).</param>
internal sealed record RowIssue(string Table, long SourceId, string Reason, string? Detail);

/// <summary>
/// Bitta jadval bo'yicha yakuniy sanoq.
/// <c>Source == Inserted + Skipped</c> tenglik SHART — aks holda
/// ko'chirish "muvaffaqiyatli" hisoblanmaydi.
/// </summary>
internal sealed class TableTally
{
    public required string Name { get; init; }

    public required string SourceTable { get; init; }

    public required string TargetTable { get; init; }

    /// <summary>Manbadagi (filtrga tushgan) qatorlar soni.</summary>
    public long Source { get; set; }

    /// <summary>Vosita yozishga bergan qatorlar soni.</summary>
    public long Mapped { get; set; }

    /// <summary>O'tkazib yuborilgan qatorlar soni.</summary>
    public long Skipped { get; set; }

    /// <summary>Maqsad bazadagi haqiqiy qatorlar soni (tekshiruv bosqichida).</summary>
    public long Target { get; set; }
}

/// <summary>
/// ========================================================================
/// KO'CHIRISH HISOBOTI — VOSITANING ENG MUHIM QISMI
/// ========================================================================
///
/// ★ NIMA UCHUN HISOBOT KO'CHIRISHNING O'ZIDAN MUHIMROQ: ma'lumot
/// ko'chirishda eng qimmat xato — "hammasi o'tdi" degan yolg'on. Bir necha
/// yuz qator jimgina tushib qolsa buni oylab hech kim sezmaydi (o'quvchi
/// "mening to'lovim ko'rinmayapti" deb kelguncha). Shuning uchun:
///
///   1. HAR o'tkazib yuborilgan qator SABABI bilan yoziladi;
///   2. Sanoqlar (manba / yozilgan / o'tkazilgan / maqsad) solishtiriladi;
///   3. Pul yig'indilari ALOHIDA solishtiriladi;
///   4. Mos kelmasa vosita XATO KODI bilan tugaydi.
///
/// Vosita "muvaffaqiyatli" deb faqat shu hisobot toza bo'lgandagina
/// hisoblanadi.
/// </summary>
/// <summary>
/// Bitta guruh chati OQIMI bo'yicha sanoq (guruh + kanal).
///
/// ★ NIMA UCHUN ALOHIDA SANALADI: "chat ko'chdi" degan umumiy son IKKI
/// OQIM QO'SHILIB KETGANINI YASHIRADI — jami raqam baribir to'g'ri
/// chiqaveradi. Faqat (guruh, kanal) kesimida sanaganda ustozning oqimi
/// kuratornikiga qo'shilib ketgani darhol ko'rinadi.
/// </summary>
internal sealed class ChannelTally
{
    public long Migrated { get; set; }

    public long Skipped { get; set; }
}

internal sealed class MigrationReport
{
    private readonly List<RowIssue> _issues = [];
    private readonly List<RowIssue> _fixes = [];
    private readonly Dictionary<string, TableTally> _tables = new(StringComparer.Ordinal);
    private readonly List<string> _failures = [];
    private readonly List<string> _warnings = [];
    private readonly Dictionary<string, decimal> _money = new(StringComparer.Ordinal);
    private readonly Dictionary<string, decimal> _moneySkipped = new(StringComparer.Ordinal);
    private readonly Dictionary<(long GroupId, int Channel), ChannelTally> _channels = [];

    /// <summary>Hisobotda ko'rsatiladigan namunalar chegarasi (qolgani sanaladi).</summary>
    public const int SampleLimit = 20;

    public IReadOnlyList<RowIssue> Issues => _issues;

    /// <summary>
    /// Qator KO'CHDI, lekin qiymati o'zgartirildi (taxmin, kesish, tozalash).
    /// O'tkazib yuborilganlardan ATAYLAB ajratilgan: ikkisi bir ro'yxatda
    /// bo'lsa "necha qator yo'qoldi" degan savolga javob berib bo'lmasdi.
    /// </summary>
    public IReadOnlyList<RowIssue> Fixes => _fixes;

    public IReadOnlyDictionary<(long GroupId, int Channel), ChannelTally> Channels => _channels;

    public IReadOnlyCollection<TableTally> Tables => _tables.Values;

    /// <summary>Ko'chirishni MUVAFFAQIYATSIZ qiladigan holatlar.</summary>
    public IReadOnlyList<string> Failures => _failures;

    /// <summary>Diqqat talab qiladigan, lekin to'xtatmaydigan holatlar.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public TableTally Tally(string name, string sourceTable, string targetTable)
    {
        if (_tables.TryGetValue(name, out var existing)) return existing;

        var tally = new TableTally { Name = name, SourceTable = sourceTable, TargetTable = targetTable };
        _tables[name] = tally;
        return tally;
    }

    public void Skip(string table, long sourceId, string reason, string? detail = null) =>
        _issues.Add(new RowIssue(table, sourceId, reason, detail));

    public void Fix(string table, long sourceId, string reason, string? detail = null) =>
        _fixes.Add(new RowIssue(table, sourceId, reason, detail));

    public void Fail(string message) => _failures.Add(message);

    public void Warn(string message) => _warnings.Add(message);

    /// <summary>
    /// Pul yig'indisini qayd qiladi. Kalit ikki tomonda BIR XIL bo'ladi
    /// (masalan <c>"Payments.Amount"</c>), shunda solishtirish oddiy
    /// lug'at taqqoslashiga aylanadi.
    /// </summary>
    public void AddMoney(string key, decimal amount) =>
        _money[key] = _money.GetValueOrDefault(key) + amount;

    /// <summary>
    /// KO'CHMAGAN yoki TUZATILGAN pul — manba bilan maqsad o'rtasidagi
    /// AYIRMA (musbat ham, manfiy ham bo'lishi mumkin).
    ///
    /// ★ NIMA UCHUN ALOHIDA HISOBLANADI: pul tekshiruvining butun ma'nosi
    /// TENGLIKDA — <c>manba = ko'chgan + ko'chmagan</c>. Ko'chmagan qism
    /// sanalmasa, tenglik buzilganda "qayerda yo'qoldi" degan savolga javob
    /// qolmasdi va yagona yechim butun bazani qo'lda solishtirish bo'lardi.
    ///
    /// ★ NIMA UCHUN MANFIY QIYMAT HAM BO'LADI: ba'zi tuzatishlar summani
    /// KAMAYTIRMAY, BELGISINI o'zgartiradi (eski tizim qaytarilgan pulni
    /// manfiy yozgan, v2 esa musbat summa + <c>Refund</c> turi bilan
    /// yozadi). Bunda ayirma manfiy bo'ladi va aynan shu qiymat tenglikni
    /// SAQLAB QOLADI. Ayirmani "faqat yo'qotish" deb qarash tekshiruvni
    /// yolg'on xato berishga majbur qilardi.
    /// </summary>
    public void AddSkippedMoney(string key, decimal amount) =>
        _moneySkipped[key] = _moneySkipped.GetValueOrDefault(key) + amount;

    public decimal Money(string key) => _money.GetValueOrDefault(key);

    public decimal SkippedMoney(string key) => _moneySkipped.GetValueOrDefault(key);

    public IReadOnlyDictionary<string, decimal> MoneyTotals => _money;

    /// <summary>Guruh chatining bitta oqimidagi sanoqni oshiradi.</summary>
    public void CountChannel(long groupId, int channel, bool migrated)
    {
        var key = (groupId, channel);
        if (!_channels.TryGetValue(key, out var tally))
        {
            tally = new ChannelTally();
            _channels[key] = tally;
        }

        if (migrated) tally.Migrated++;
        else tally.Skipped++;
    }

    /// <summary>Sabab bo'yicha guruhlangan o'tkazib yuborishlar (ko'pdan kamga).</summary>
    public IEnumerable<(string Table, string Reason, int Count)> IssuesByReason() =>
        Group(_issues);

    /// <summary>Sabab bo'yicha guruhlangan tuzatishlar (ko'pdan kamga).</summary>
    public IEnumerable<(string Table, string Reason, int Count)> FixesByReason() =>
        Group(_fixes);

    private static IEnumerable<(string Table, string Reason, int Count)> Group(List<RowIssue> rows) =>
        rows
            .GroupBy(i => (i.Table, i.Reason))
            .Select(g => (g.Key.Table, g.Key.Reason, g.Count()))
            .OrderByDescending(x => x.Item3)
            .ThenBy(x => x.Table, StringComparer.Ordinal);

    /// <summary>Pulni hisobotda ko'rsatish uchun (madaniyatdan mustaqil).</summary>
    public static string Format(decimal amount) =>
        amount.ToString("N2", CultureInfo.InvariantCulture);
}
