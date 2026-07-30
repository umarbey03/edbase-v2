using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>Jonli darsdagi chat xabari.</summary>
public class ChatMessage : BaseEntity
{
    /// <summary>Maksimal uzunlik — server tomonda majburiy kesiladi.</summary>
    public const int MaxBodyLength = 500;

    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }

    public long SenderId { get; set; }

    /// <summary>
    /// Yuboruvchi ismi xabar bilan BIRGA saqlanadi (denormalizatsiya).
    /// Sabab: chat tarixini o'qishda 200 ta xabar uchun `users` jadvaliga
    /// JOIN qilish shart emas — bu 200 kishilik darsda sezilarli tejash.
    /// </summary>
    public required string SenderName { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Kiruvchi matnni tozalaydi va tekshiradi.</summary>
    public static string NormalizeBody(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();

        if (text.Length == 0)
            throw new DomainException("Xabar bo'sh bo'lishi mumkin emas.");

        if (text.Length <= MaxBodyLength)
            return text;

        // XAVFSIZ KESISH — emoji ikkiga bo'linib qolmasin.
        //
        // C# satri UTF-16 kod birliklaridan iborat. Emoji (va BMP'dan tashqari
        // boshqa belgilar) IKKI kod birligi — surrogat juftlik — bilan
        // ifodalanadi. Oddiy `text[..500]` juftlikning o'rtasidan kesib,
        // YOLG'IZ surrogat qoldirishi mumkin.
        //
        // Oqibati: bunday satrni Postgres'ga yozishda u `U+FFFD` (almashtirish
        // belgisi) ga aylanadi, qat'iy kodlashda esa `EncoderFallbackException`
        // bilan yiqiladi. Ya'ni 500-belgisi emojiga to'g'ri kelgan xabar chatni
        // buzardi.
        //
        // Yechim: chegara yolg'iz yuqori surrogatga tushsa — bitta belgi orqaga.
        var cut = MaxBodyLength;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;

        return text[..cut];
    }
}
