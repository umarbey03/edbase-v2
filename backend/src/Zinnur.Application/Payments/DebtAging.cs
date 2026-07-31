using Zinnur.Domain.Finance;

namespace Zinnur.Application.Payments;

/// <summary>
/// ========================================================================
/// QARZ YOSHI — SOF FUNKSIYA
/// ========================================================================
///
/// Kassir uchun eng muhim ko'rsatkich "jami qarz" emas, balki QAYSI QARZ
/// ESKIRIB KETYAPTI: bir oylik qarz odatda o'z-o'zidan yopiladi, uch oylik
/// qarz esa deyarli hech qachon yopilmaydi. Shuning uchun qarz to'rt
/// guruhga bo'linadi: <c>0-30 / 31-60 / 61-90 / 90+</c> kun.
///
/// ★ YOSH HISOB OYINING BIRINCHI KUNIDAN sanaladi
/// (<c>kun = asOf − oyning 1-kuni</c>). Nima uchun to'lov sanasidan emas:
/// qarzning yoshi — bu "qachondan beri to'lanmagan" degani, va oy hisobi
/// oyning boshida ochiladi. Yozuvning <c>CreatedAt</c> i olinsa, kech
/// ochilgan oy sun'iy ravishda "yosh" bo'lib chiqardi.
///
/// ★ NIMA UCHUN ALOHIDA SINF (servis ichida emas): chegara qoidasi —
/// AYNAN o'sha joy, xato oson yashiringan joy. "30 kun qaysi guruhda?"
/// degan savolga javob unit test bilan qotirilgan (<c>&lt;= 30</c>, ya'ni
/// 30 hali birinchi guruhda, 31 esa ikkinchisida). Servis ichida bo'lsa
/// buni tekshirish uchun haqiqiy baza va aniq sanalar kerak bo'lardi.
/// <see cref="BillingSelection"/> bilan bir xil naqsh.
/// </summary>
public static class DebtAging
{
    /// <summary>
    /// Guruhlar — TARTIBI MUHIM: indeks <see cref="IndexOf(int)"/> dan
    /// qaytadigan qiymat bilan bir xil.
    /// </summary>
    public static IReadOnlyList<DebtAgingBucket> Buckets { get; } =
    [
        new("0-30", 0, 30),
        new("31-60", 31, 60),
        new("61-90", 61, 90),
        new("90+", 91, null),
    ];

    /// <summary>
    /// Qarzning yoshi — kunlarda. Kelajakda ochilgan oy uchun MANFIY
    /// bo'lishi mumkin (hisob oldindan ochilgan) va bu xato emas.
    /// </summary>
    public static int AgeInDays(DateOnly asOf, BillingPeriod period) =>
        asOf.DayNumber - period.FirstDay().DayNumber;

    /// <summary>
    /// Yoshiga qarab guruh indeksi.
    ///
    /// ★ CHEGARALAR: 30 kun — hali <c>0-30</c> da, 31 — <c>31-60</c> da;
    /// 60/61 va 90/91 ham xuddi shunday. Ya'ni taqqoslash <c>&lt;=</c>,
    /// <c>&lt;</c> EMAS — aks holda har guruhning oxirgi kuni keyingi
    /// guruhga o'tib ketardi va "90 kunlik qarz" hisoboti bir kunga
    /// yolg'on bo'lardi.
    ///
    /// ★ MANFIY YOSH (kelajak oyi) eng yangi guruhga tushadi: shunda
    /// guruhlar yig'indisi umumiy qarzga AYNAN teng bo'ladi va hech bir
    /// qator hisobdan tushib qolmaydi.
    /// </summary>
    public static int IndexOf(int ageInDays) =>
        ageInDays <= 30 ? 0
        : ageInDays <= 60 ? 1
        : ageInDays <= 90 ? 2
        : 3;

    /// <summary>Davr satridan (<c>YYYY-MM</c>) to'g'ridan-to'g'ri guruh indeksi.</summary>
    public static int IndexOf(DateOnly asOf, string period) =>
        IndexOf(AgeInDays(asOf, BillingPeriod.Parse(period)));
}

/// <summary>
/// Bitta guruhning ta'rifi.
/// </summary>
/// <param name="Key">UI va eksportda ko'rinadigan kalit: <c>0-30</c>, <c>90+</c>.</param>
/// <param name="MinDays">Quyi chegara (kun), KIRADI.</param>
/// <param name="MaxDays">Yuqori chegara (kun), KIRADI. <c>null</c> — cheksiz (<c>90+</c>).</param>
public sealed record DebtAgingBucket(string Key, int MinDays, int? MaxDays);
