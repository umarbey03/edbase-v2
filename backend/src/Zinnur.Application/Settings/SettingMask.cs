namespace Zinnur.Application.Settings;

/// <summary>
/// ========================================================================
/// SIR QIYMATNI MASKALASH
/// ========================================================================
///
/// ★ NIMA UCHUN BU KRITIK: admin paneli BRAUZERDA ochiladi. API javobi
/// brauzer keshiga, DevTools tarixiga, reverse-proxy loglariga va (xato
/// yuz bersa) Sentry'ga tushishi mumkin. Sir bir marta shu yo'llarning
/// biriga tushsa, uni "qaytarib olib bo'lmaydi" — almashtirishdan boshqa
/// chora qolmaydi. Shuning uchun sir TO'LIQ holda API javobiga HECH QACHON
/// chiqmaydi (loyihadagi <c>SentryScrubber</c> bilan bir xil mantiq:
/// qiymat o'chiriladi, lekin maydonning O'ZI ko'rinib turadi).
///
/// ★ NIMA UCHUN BOSHIDAN EMAS, FAQAT OXIRIDAN 4 BELGI:
/// <c>sk_live_…</c> kabi ochiq prefiks BA'ZI xizmatlarda bor, lekin bizdagi
/// sirlarning ko'pi — xom HMAC kalitlari (<c>Jwt:Secret</c>,
/// <c>LiveKit:ApiSecret</c>). Ularda "prefiks" degan tushuncha yo'q: boshidan
/// 8 belgi ko'rsatish TO'G'RIDAN-TO'G'RI kalit materialining bir qismini
/// oshkor qilish bo'lardi. Oxirgi 4 belgi esa admin "ha, men o'rnatgan kalit
/// shu" deb tanib olishi uchun yetarli va bruteforce uchun ma'nosiz.
///
/// ★ UZUNLIK HAM BERILMAYDI: kalit uzunligi hujumchi uchun foydali ma'lumot,
/// admin uchun esa hech qanday qiymati yo'q.
/// </summary>
public static class SettingMask
{
    /// <summary>Yashirilgan qism o'rniga qo'yiladigan belgi.</summary>
    public const string Hidden = "••••••••";

    /// <summary>Oxirida ochiq qoldiriladigan belgilar soni.</summary>
    private const int TailLength = 4;

    /// <summary>
    /// Qiymatni juda qisqa bo'lsa BUTUNLAY yashiradi: 6 belgilik sirdan 4
    /// tasini ko'rsatish deyarli hammasini ko'rsatish demakdir.
    /// </summary>
    private const int MinLengthForTail = 12;

    /// <summary>
    /// Sirning maskalangan ko'rinishi. Qiymat yo'q bo'lsa <c>null</c> —
    /// panel bunda "o'rnatilmagan" deb ko'rsatadi.
    /// </summary>
    public static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return value.Length < MinLengthForTail
            ? Hidden
            : string.Concat(Hidden, value.AsSpan(value.Length - TailLength));
    }
}
