using System.Globalization;
using System.Text;

namespace Zinnur.Application.Common.Export;

/// <summary>
/// ========================================================================
/// CSV YOZUVCHI — LOYIHADA YAGONA
/// ========================================================================
///
/// NIMA UCHUN BITTA JOYDA: eksport ikki modulda bor (test natijalari va
/// moliya hisoboti). Har biri o'z "yacheykani qo'shtirnoqqa olish" va
/// "sonni formatlash" funksiyasini yozsa, ular vaqt o'tib bir-biridan
/// ajralib ketardi — bir fayl Excel'da ochilardi, ikkinchisi esa yo'q, va
/// sabab har safar qaytadan qidirilardi.
///
/// ── NIMA UCHUN CSV, .XLSX EMAS ─────────────────────────────────────────
///
/// Eski (Python) tizim `openpyxl` bilan .xlsx berardi. v2 da CSV tanlandi:
///
///   • .xlsx uchun yangi NuGet paketi kerak (ClosedXML → DocumentFormat.
///     OpenXml + SixLabors zanjiri; EPPlus esa tijorat litsenziyasi bilan).
///     Bu bitta hisobot uchun bir necha megabayt bog'liqlik, uzunroq
///     restore va qo'shimcha ta'minot zanjiri xavfi degani.
///   • Loyihada CSV eksport ALLAQACHON bor (test natijalari) — ikkinchi
///     texnologiya kiritish "bir ishni ikki xil qilish" bo'lardi.
///   • Kassirga kerak bo'lgani — Excel'da ochilib, filtrlanadigan jadval.
///     CSV buni beradi, agar quyidagi UCH tuzoq yopilsa.
///
/// ── UCH TUZOQ VA ULARNING YECHIMI ──────────────────────────────────────
///
///  1) KODLASH. BOM'siz UTF-8 ni Excel ANSI deb o'qiydi va o'zbek harflari
///     (ʻ, ʼ) hamda kirill krakozyabraga aylanadi. Yechim: fayl boshida
///     OSHKOR <c>\uFEFF</c> escape'i (kodda ko'rinmas belgi qoldirilmaydi).
///
///  2) AJRATGICH. Excel faylni operatsion tizimning "ro'yxat ajratgichi"
///     bo'yicha bo'ladi. uz-UZ/ru-RU lokalida u <c>;</c> — ya'ni vergulli
///     fayl BITTA ustunga tushib qolardi. Yechim: <see cref="WithExcelHint"/>
///     birinchi qatorga <c>sep=,</c> direktivasini qo'yadi. Uni Excel ham,
///     LibreOffice ham tushunadi va lokaldan QAT'I NAZAR to'g'ri bo'ladi.
///     Ajratgichning O'ZI vergul bo'lib qoladi — shunda fayl pandas,
///     Google Sheets va boshqa vositalarda ham standart bo'yicha o'qiladi.
///
///  3) SONLAR. Kasr ajratgichi lokalga bog'liq: vergulli lokalda
///     <c>540000.00</c> SON emas, MATN bo'lib tushadi. Yechim:
///     <see cref="Money"/> pulni AJRATGICHSIZ butun son qilib yozadi
///     (<c>540000</c>) — bunday qiymatni hech qaysi lokal noto'g'ri
///     o'qiy olmaydi. Loyiha qoidasi ham shu: pul — butun son.
///     Foiz (<see cref="Percent"/>) bitta kasr bilan yoziladi va vergulli
///     lokalda matn bo'lib qolishi mumkin — bu ONGLI yon ta'sir: foiz
///     O'QILADI, yig'indi olinmaydi.
///
/// ★ SANA-O'XSHASH QIYMATLAR HAQIDA OGOHLANTIRISH: Excel <c>2026-07</c> ni
///   sana deb o'qib "iyul 26" ko'rinishida ko'rsatishi mumkin. QIYMAT
///   to'g'ri qoladi (o'sha oy), faqat ko'rinish formati o'zgaradi.
///   <c>="2026-07"</c> formula hiylasi ATAYLAB ishlatilmadi: u CSV-injection
///   namunasi va Excel'dan boshqa vositalarda fayl buziladi.
/// </summary>
public sealed class CsvBuilder
{
    private readonly StringBuilder _text;
    private readonly char _delimiter;

    /// <param name="capacity">Taxminiy hajm — <c>StringBuilder</c> qayta ajratmasin.</param>
    /// <param name="delimiter">Ustun ajratgichi. Standart — vergul (RFC 4180).</param>
    public CsvBuilder(int capacity = 0, char delimiter = ',')
    {
        _delimiter = delimiter;
        _text = new StringBuilder(capacity + BomAndHintReserve);

        // ★ BOM — Excel uchun SHART (izoh sinf tepasida).
        _text.Append('\uFEFF');
    }

    /// <summary>
    /// Excel'ga ajratgichni AYTADIGAN birinchi qator (<c>sep=,</c>).
    ///
    /// Faqat kerak bo'lgan eksportda chaqiriladi: bu qator standart CSV
    /// EMAS va ba'zi vositalar uni oddiy ma'lumot qatori deb o'qiydi.
    /// Excel'da ochiladigan hisobot uchun foydasi zarardan katta, mashina
    /// o'qiydigan fayl uchun esa aksincha.
    /// </summary>
    public CsvBuilder WithExcelHint()
    {
        _text.Append("sep=").Append(_delimiter).Append(LineEnding);
        return this;
    }

    /// <summary>Bitta qator. Yacheykalar avtomatik qalqonlanadi.</summary>
    public CsvBuilder Row(params string?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0) _text.Append(_delimiter);
            _text.Append(Cell(cells[i]));
        }

        _text.Append(LineEnding);
        return this;
    }

    /// <summary>Bo'sh qator — bo'limlarni ajratish uchun.</summary>
    public CsvBuilder Blank()
    {
        _text.Append(LineEnding);
        return this;
    }

    /// <summary>
    /// Tayyor faylni beradi.
    ///
    /// <c>charset=utf-8</c> sarlavhada OSHKOR: BOM bo'lsa ham brauzer va
    /// oraliq proksi kodlashni taxmin qilishga urinmasin.
    /// </summary>
    public CsvExport ToExport(string fileName) =>
        new(fileName, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(_text.ToString()));

    /// <summary>
    /// PUL. Butun so'm — ajratgichsiz (<c>540000</c>), shuning uchun uni
    /// hech qaysi Excel lokali noto'g'ri o'qiy olmaydi. Kasr qismi faqat
    /// haqiqatan bo'lsa chiqadi (foizli chegirmaning oraliq natijasi).
    /// </summary>
    public static string Money(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Foiz — bitta kasr bilan (<c>93.8</c>). Izoh sinf tepasida.</summary>
    public static string Percent(decimal value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Butun son (dona, soni).</summary>
    public static string Count(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Sana-vaqt MAHALLIY zonada (<c>yyyy-MM-dd HH:mm</c>): hisobotni
    /// o'qiydigan odam devor-soatiga qaraydi, UTC'ga emas.
    /// </summary>
    public static string LocalTime(DateTimeOffset? instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return instant is { } at
            ? TimeZoneInfo.ConvertTime(at, timeZone)
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    /// <summary>Yacheyka: ajratgich, qo'shtirnoq va qator ko'chirishni zararsizlantiradi.</summary>
    private string Cell(string? value)
    {
        var text = value ?? string.Empty;

        return text.AsSpan().IndexOfAny(_delimiter, '"', '\n') >= 0 || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    /// <summary>
    /// <c>\r\n</c> — RFC 4180 va Excel uchun eng xavfsiz variant.
    /// Faqat <c>\n</c> bo'lsa Windows'dagi eski import yo'llari qatorni
    /// noto'g'ri bo'lishi mumkin.
    /// </summary>
    private const string LineEnding = "\r\n";

    /// <summary>BOM (1 belgi) va <c>sep=,</c> qatori uchun zaxira.</summary>
    private const int BomAndHintReserve = 16;
}
