using System.Data;
using System.Globalization;
using Zinnur.Migration.Reporting;

namespace Zinnur.Migration.Pipeline;

/// <summary>
/// Xaritalash funksiyasiga beriladigan bitta qator konteksti:
/// manba qiymatlari + ko'chirish xotirasi + hisobot.
///
/// ★ NIMA UCHUN O'RAM (<c>IDataRecord</c> to'g'ridan-to'g'ri emas):
/// eski bazada deyarli har ustun <c>NULL</c> bo'lishi mumkin, va
/// <c>reader.GetString(i)</c> <c>NULL</c> da yiqiladi. Har xaritalashda
/// <c>IsDBNull</c> tekshiruvini qo'lda yozish — 25 jadval x 15 ustun =
/// 375 ta unutilishi mumkin bo'lgan joy. Bu yerda u BITTA joyda.
/// </summary>
internal sealed class RowContext(MigrationState state, MigrationReport report, string sourceTable)
{
    private IDataRecord _record = null!;

    public MigrationState State { get; } = state;

    public MigrationReport Report { get; } = report;

    public string SourceTable { get; } = sourceTable;

    internal void Bind(IDataRecord record) => _record = record;

    // ---------------------------------------------------------------- o'qish

    public bool IsNull(int i) => _record.IsDBNull(i);

    public long Id => _record.GetInt64(0);

    public long Int64(int i) => _record.GetInt64(i);

    public long? Int64OrNull(int i) => _record.IsDBNull(i) ? null : _record.GetInt64(i);

    public int Int32(int i) => _record.GetInt32(i);

    public int? Int32OrNull(int i) => _record.IsDBNull(i) ? null : _record.GetInt32(i);

    public bool Bool(int i, bool fallback = false) => _record.IsDBNull(i) ? fallback : _record.GetBoolean(i);

    public string? Text(int i) => _record.IsDBNull(i) ? null : _record.GetString(i);

    public decimal Money(int i, decimal fallback = 0m) =>
        _record.IsDBNull(i) ? fallback : _record.GetDecimal(i);

    public decimal? MoneyOrNull(int i) => _record.IsDBNull(i) ? null : _record.GetDecimal(i);

    /// <summary>
    /// <c>timestamptz</c> -> UTC <see cref="DateTimeOffset"/>.
    ///
    /// ★ VAQT MINTAQASI HAQIDA: eski tizim dars vaqtlarini
    /// <c>_local_to_utc()</c> orqali ANIQ UTC instant sifatida yozgan
    /// (<c>app/services/scheduler.py</c>) va ustunlar <c>TIMESTAMPTZ</c>.
    /// Ya'ni bazada instant turadi, mahalliy "devor-vaqti" emas —
    /// shuning uchun bu yerda HECH QANDAY siljitish QILINMAYDI.
    /// Agar 5 soatlik tuzatish qo'shilsa, aynan shu barcha dars
    /// vaqtlarini buzardi.
    /// </summary>
    public DateTimeOffset Instant(int i) =>
        _record.IsDBNull(i)
            ? DateTimeOffset.UnixEpoch
            : new DateTimeOffset(_record.GetDateTime(i), TimeSpan.Zero).ToUniversalTime();

    public DateTimeOffset? InstantOrNull(int i) =>
        _record.IsDBNull(i) ? null : Instant(i);

    /// <summary><c>date</c> ustuni — vaqt mintaqasi umuman qatnashmaydi.</summary>
    public DateOnly Date(int i, DateOnly fallback) =>
        _record.IsDBNull(i) ? fallback : DateOnly.FromDateTime(_record.GetDateTime(i));

    public DateOnly? DateOrNull(int i) =>
        _record.IsDBNull(i) ? null : DateOnly.FromDateTime(_record.GetDateTime(i));

    public TimeOnly Time(int i, TimeOnly fallback) =>
        _record.IsDBNull(i) ? fallback : TimeOnly.FromTimeSpan((TimeSpan)_record.GetValue(i));

    public T[] Array<T>(int i) =>
        _record.IsDBNull(i) ? [] : (T[])_record.GetValue(i);

    // ---------------------------------------------------------------- yozuv

    /// <summary>Qatorni o'tkazib yuborish sababini yozadi va <c>null</c> qaytaradi.</summary>
    public object?[]? Skip(string reason, string? detail = null)
    {
        Report.Skip(SourceTable, Id, reason, detail);
        return null;
    }

    /// <summary>Qator ko'chdi, lekin qiymat TUZATILDI — hisobotga chiqadi.</summary>
    public void Fixed(string reason, string? detail = null) =>
        Report.Fix(SourceTable, Id, reason, detail);

    // ---------------------------------------------------------------- yordamchi

    /// <summary>
    /// Matnni maqsad ustuni chegarasiga sig'diradi.
    ///
    /// ★ SURROGAT JUFTLIK HIMOYASI: emoji ikkita <c>char</c> dan iborat.
    /// Chegara aynan o'rtasiga tushsa yolg'iz surrogat qoladi va Postgres
    /// uni <c>U+FFFD</c> ga aylantiradi (yoki Npgsql xato beradi). Ayni
    /// qoida v2 <c>MessageText</c> da ham bor.
    /// </summary>
    public static string? Clip(string? value, int max)
    {
        if (value is null) return null;
        if (value.Length <= max) return value;

        var cut = max;
        if (char.IsHighSurrogate(value[cut - 1])) cut--;
        return value[..cut];
    }

    /// <summary>Son yoki sanani hisobot uchun matnga o'giradi (CA1305).</summary>
    public static string Str<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);
}
