using System.Globalization;

namespace Zinnur.IntegrationTests.Infrastructure;

/// <summary>
/// Testlar uchun TAKRORLANMAYDIGAN telefon raqamlari.
///
/// ★ NIMA UCHUN KERAK BO'LIB QOLDI (2026-08-13): xodim yaratishda telefon
/// MAJBURIY bo'ldi (kirish faqat telefon orqali). Har test yordamchisi
/// o'zi raqam o'ylab topsa, ular ertami-kechmi to'qnashardi va
/// `EnsurePhoneFreeAsync` 409 qaytarib, testlar "flaky" bo'lardi — ya'ni
/// yashil natija hech nima isbotlamasdi (bu loyihada bir marta bo'lgan:
/// Redis kalit makoni).
///
/// ★ HISOBLAGICH JARAYON DAVOMIDA YAGONA (<see cref="Interlocked"/>).
/// Bu YETARLI, chunki har test sinfi O'Z Postgres bazasini oladi —
/// unikallik faqat bitta baza ichida talab qilinadi, hisoblagich esa
/// butun jarayon uchun bitta, ya'ni undan ham qattiqroq kafolat beradi.
///
/// ★ DIAPAZON ATAYLAB `+998 90 5xxxxxx` DAN BOSHLANADI. Kod bazasida
/// qo'lda yozilgan raqamlar `+99890 111xxxx`, `+99890 222xxxx` va
/// `+99890 000000x` (seed) hududlarida — ular bilan kesishmasin.
/// Qo'lda raqam yozadigan yangi test ham shu qoidaga amal qilsin:
/// <c>5</c> bilan boshlanadigan diapazon FAQAT shu generator uchun.
/// </summary>
public static class TestPhones
{
    /// <summary>Generator diapazonining boshi (`+998 90 5000001` dan).</summary>
    private const int Start = 5_000_000;

    private static int _counter = Start;

    /// <summary>
    /// Navbatdagi raqam: <c>+998905000001</c>, <c>+998905000002</c>, …
    ///
    /// Qiymat <c>User.NormalizePhone</c> dan o'zgarishsiz o'tadi
    /// (12 raqam -&gt; oldiga faqat <c>+</c> qo'yiladi), ya'ni test kutgan
    /// satr bazadagi <c>PhoneNormalized</c> bilan AYNAN bir xil bo'ladi.
    /// </summary>
    public static string Next() =>
        "+99890" + Interlocked
            .Increment(ref _counter)
            .ToString("D7", CultureInfo.InvariantCulture);
}
