namespace Zinnur.Application.Notifications;

/// <summary>
/// Qayta urinish jadvali — EKSPONENSIAL backoff.
///
/// ══════════════════════════════════════════════════════════════════════
/// JADVAL (qaror, 2026-07-30) — urinish DARHOL, keyin 1m → 5m → 15m → 60m:
///
///   1-urinish   navbatga tushishi bilan (kechikishsiz)
///   2-urinish   +1 daqiqa      — qisqa tarmoq uzilishi
///   3-urinish   +5 daqiqa      — Telegram tomonidagi kichik uzilish
///   4-urinish   +15 daqiqa     — jiddiyroq uzilish
///   5-urinish   +60 daqiqa     — oxirgi imkoniyat
///   keyin       Failed
///
/// NIMA UCHUN AYNAN SHUNDAY:
///   * BIRINCHI URINISH KECHIKTIRILMAYDI — xabarlarning aksariyati birinchi
///     urinishda ketadi. "15 daqiqada dars boshlanadi" eslatmasi 1 daqiqa
///     kechiksa ma'nosini yo'qotadi.
///   * O'SISH TEZ (×3..5) — Telegram uzilishi odatda soniyalar yoki
///     daqiqalar. Sekin o'sish (30s, 1m, 2m) navbatni bekorga aylantirib,
///     uzilish davomida ming marta urinardi va o'zimizni cheklovga urardik.
///   * JAMI ~1.3 SOAT (1+5+15+60) — undan uzoq kutish ma'nosiz: dars haqidagi
///     eslatma allaqachon eskirgan bo'ladi.
///   * 5 URINISHDAN keyin <c>Failed</c> — "zaharli xabar" (poison message)
///     navbatni abadiy band qilmasin. Eski tizimda cheksiz urinish tufayli
///     bitta yaroqsiz chat_id butun eslatma oqimini sekinlashtirardi.
///
/// TASODIFIY QO'SHIMCHA (jitter) YO'Q — ataylab: navbat qatorlari
/// <c>SKIP LOCKED</c> bilan olinadi va ular baribir bir vaqtda emas, ketma-ket
/// yuboriladi (tezlik chegarasi ham bor). Jitter faqat kutish vaqtini
/// oldindan aytib bo'lmaydigan qilib, testni ham murakkablashtirardi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public static class OutboxRetryPolicy
{
    /// <summary>
    /// Urinishlar orasidagi kutish. Indeks — MUVAFFAQIYATSIZ urinishlar soni
    /// (1-chi yiqilishdan keyin <c>Delays[0]</c>).
    /// </summary>
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    ];

    /// <summary>Jami urinishlar soni (birinchisi + qayta urinishlar).</summary>
    public static int MaxAttempts => Delays.Length + 1;

    /// <summary>
    /// Navbatdagi urinishgacha qancha kutish kerak.
    /// </summary>
    /// <param name="failedAttempts">Shu paytgacha necha marta yiqilgan (1 dan boshlanadi).</param>
    /// <returns>
    /// Kutish muddati; <c>null</c> — urinishlar tugadi, xabar
    /// <see cref="OutboxStatus.Failed"/> ga o'tkazilsin.
    /// </returns>
    public static TimeSpan? NextDelay(int failedAttempts)
    {
        // Ehtiyot: hisoblagich buzuq bo'lsa ham birinchi jadval qiymatidan
        // boshlanadi (manfiy kutish yoki indeks xatosi bo'lmaydi).
        if (failedAttempts < 1) return Delays[0];

        return failedAttempts <= Delays.Length ? Delays[failedAttempts - 1] : null;
    }
}
