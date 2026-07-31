using System.Globalization;

namespace Zinnur.Migration.Reporting;

/// <summary>
/// Konsolga progress va yakuniy hisobot chiqaruvchi.
///
/// ★ NIMA UCHUN <c>ILogger</c> EMAS: bu bir martalik CLI vositasi, uning
/// yagona chiqishi — operator o'qiydigan matn. <c>ILogger</c> bo'lsa
/// DI konteyner, provayder sozlamalari va CA1848 (<c>[LoggerMessage]</c>)
/// talabi qo'shilardi — foydasi nolga teng, chunki hech qanday tashqi
/// log yig'uvchi yo'q. Ko'chirish TUNDA, bosim ostida bajariladi:
/// chiqish oddiy va bir qarashda tushunarli bo'lishi kerak.
///
/// Barcha son/sana formatlash <see cref="CultureInfo.InvariantCulture"/>
/// bilan (CA1305) — hisobot server lokalidan qat'i nazar bir xil bo'lsin.
/// </summary>
internal sealed class Reporter(TextWriter output)
{
    private readonly TextWriter _out = output;

    public void Section(string title)
    {
        _out.WriteLine();
        _out.WriteLine(new string('=', 72));
        _out.WriteLine(title);
        _out.WriteLine(new string('=', 72));
    }

    public void Line(string text) => _out.WriteLine(text);

    public void Step(string text) =>
        _out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  [{Stamp()}] {text}"));

    public void Progress(string table, long done, long total) =>
        _out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  [{Stamp()}]   {table}: {done}/{total} qator"));

    public void Ok(string text) =>
        _out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  [OK]   {text}"));

    public void Warn(string text) =>
        _out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  [DIQQAT] {text}"));

    public void Error(string text) =>
        _out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  [XATO] {text}"));

    private static string Stamp() =>
        DateTimeOffset.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
