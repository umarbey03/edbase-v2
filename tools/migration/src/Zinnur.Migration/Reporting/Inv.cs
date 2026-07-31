namespace Zinnur.Migration.Reporting;

/// <summary>
/// Madaniyatdan MUSTAQIL matn yig'ish.
///
/// ★ NIMA UCHUN KERAK: hisobot matnlari uzun va bir necha bo'lakdan
/// iborat. <c>string.Create(CultureInfo.InvariantCulture, $"..." + "...")</c>
/// yozib bo'lmaydi (birlashtirilgan satr interpolyatsiya ishlovchisiga
/// o'tmaydi), <c>$"..."</c> ni shundoq qoldirish esa CA1305 ga urilardi:
/// server lokali turkcha bo'lsa sonlar <c>1.234,56</c> ko'rinishida
/// chiqib, hisobotni skript bilan o'qib bo'lmasdi.
///
/// <see cref="S"/> har bo'lakni AYNI mantiq bilan formatlaydi va
/// natijalar oddiy satr sifatida birlashtiriladi.
/// </summary>
internal static class Inv
{
    public static string S(FormattableString text) => FormattableString.Invariant(text);
}
