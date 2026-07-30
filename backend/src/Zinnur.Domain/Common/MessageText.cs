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
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var text = (raw ?? string.Empty).Trim();

        if (text.Length == 0)
            throw new DomainException("Xabar bo'sh bo'lishi mumkin emas.");

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
