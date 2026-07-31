namespace Zinnur.Application.Common.Export;

/// <summary>
/// Tayyor eksport fayli — controller uni <c>File(...)</c> bilan qaytaradi.
///
/// NIMA UCHUN <c>Common</c> DA, MODUL ICHIDA EMAS: eksport bir nechta
/// modulda bor (test natijalari, moliya hisoboti). Har modul o'z nusxasini
/// e'lon qilsa, bir xil shakldagi ikki tur paydo bo'lardi va controller
/// darajasida ular bir-biriga o'girib yuriladigan bo'lardi.
///
/// <c>ReadOnlyMemory&lt;byte&gt;</c> — <c>byte[]</c> EMAS: massiv qaytarilsa
/// chaqiruvchi uni JOYIDA o'zgartira olardi (record esa o'zgarmas deb
/// va'da qilyapti).
/// </summary>
public sealed record CsvExport(string FileName, string ContentType, ReadOnlyMemory<byte> Content);
