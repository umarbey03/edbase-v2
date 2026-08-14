using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Common;

/// <summary>
/// Foydalanuvchi yozgan xabar matnini tozalash va XAVFSIZ kesish.
///
/// NIMA UCHUN ALOHIDA: loyihada ikki xil chat bor —
/// jonli darsdagi <see cref="Entities.ChatMessage"/> (500 belgi) va
/// kurator bilan shaxsiy yozishma <see cref="Entities.DirectMessage"/>
/// (2000 belgi). Qoida bir xil, faqat chegara boshqa. Ikki joyda
/// takrorlansa surrogat juftlik himoyasi bittasida unutilardi — va aynan
/// shu tur xato faqat 500-belgisi emojiga tushgan xabarda ko'rinadi,
/// ya'ni testda emas, PRODUKSIYADA topiladi.
/// </summary>
public static class MessageText
{
    /// <summary>
    /// Bo'shliqni kesadi, bo'sh matnni rad etadi va uzunini
    /// <paramref name="maxLength"/> gacha qirqadi.
    /// </summary>
    /// <exception cref="DomainException">Matn bo'sh.</exception>
    public static string Normalize(string? raw, int maxLength)
    {
        var text = NormalizeOptional(raw, maxLength);

        if (text.Length == 0)
            throw new DomainException("Xabar bo'sh bo'lishi mumkin emas.");

        return text;
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴 BO'SH MATNGA RUXSAT BERADIGAN VARIANT (R16b) — ONGLI QAROR
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Bugungacha loyihada "bo'sh xabar" degan tushuncha UMUMAN yo'q edi:
    /// <see cref="Normalize"/> bo'sh matnni Domain darajasida rad etardi va
    /// uchala chat ham matnni MAJBURIY deb bilardi.
    ///
    /// ★ NIMA UCHUN INVARIANT O'ZGARDI: R16b talabi — "telegram chat kabi
    /// ... rasm, fayl yuklash". Telegram'da izohsiz surat MUTLAQO ODATIY
    /// holat. Agar matn majburiy bo'lib qolsa, klient rasmni jo'natish
    /// uchun bo'shliq yoki nuqta yozib yuborishga majbur bo'lardi — ya'ni
    /// qoida buzilmasdi, faqat MA'NOSIZ ma'lumot bilan chetlab o'tilardi.
    ///
    /// 🔴 INVARIANT BEKOR QILINMADI, KO'CHIRILDI. "Xabarda hech nima
    /// bo'lmasligi mumkin emas" qoidasi kuchida qoladi, faqat endi u
    /// MATNGA emas, MAZMUNGA tegishli:
    ///
    ///     matn BO'SH bo'lsa -> kamida BITTA biriktirma bo'lishi SHART.
    ///
    /// Bu shart <see cref="Entities.GroupChatMessage.CreateWithAttachments"/>
    /// da tekshiriladi va u YAGONA joy: oddiy
    /// <see cref="Entities.GroupChatMessage.Create"/> avvalgidek bo'sh
    /// matnni rad etadi, ya'ni "biriktirmasiz bo'sh xabar" yozishning yo'li
    /// YO'Q. Aks holda bu metod ochiq eshik bo'lardi: bir kuni kimdir uni
    /// oddiy matn yo'lida ishlatib, chatga bo'm-bo'sh qatorlar oqib
    /// kirardi.
    ///
    /// ⚠️ <c>null</c> QAYTMAYDI, BO'SH SATR qaytadi. Sabab amaliy:
    /// <c>GroupChatMessage.Body</c> ustuni NOT NULL bo'lib qoladi va DTO
    /// hamda frontend uchun <c>string</c> bo'lib qolaveradi. Uni
    /// <c>string?</c> ga aylantirish o'qish yo'llarining HAMMASIGA
    /// (sahifalash, ko'chirma, realtime tekshiruvi) null-tekshiruv
    /// qo'shishni talab qilardi — bittasi unutilsa bir kuni
    /// <c>NullReferenceException</c> chatning butun sahifasini yiqitardi.
    /// </summary>
    public static string NormalizeOptional(string? raw, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var text = (raw ?? string.Empty).Trim();

        if (text.Length <= maxLength)
            return text;

        // XAVFSIZ KESISH — emoji ikkiga bo'linib qolmasin.
        //
        // C# satri UTF-16 kod birliklaridan iborat. Emoji (va BMP'dan
        // tashqari boshqa belgilar) IKKI kod birligi — surrogat juftlik —
        // bilan ifodalanadi. Oddiy `text[..max]` juftlikning o'rtasidan
        // kesib, YOLG'IZ surrogat qoldirishi mumkin.
        //
        // Oqibati: bunday satrni Postgres'ga yozishda u `U+FFFD` ga
        // aylanadi, qat'iy kodlashda esa `EncoderFallbackException` bilan
        // yiqiladi. Ya'ni chegarasi emojiga to'g'ri kelgan xabar chatni
        // buzardi.
        var cut = maxLength;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;

        return text[..cut];
    }
}
