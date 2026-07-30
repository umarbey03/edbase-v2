using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Jonli darsdagi chat xabari (xona oqimi).
/// Kurator bilan shaxsiy yozishma uchun <see cref="DirectMessage"/> —
/// farqi o'sha sinf izohida batafsil yozilgan.
/// </summary>
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

    /// <summary>
    /// Kiruvchi matnni tozalaydi va tekshiradi.
    ///
    /// Qoidaning O'ZI <see cref="MessageText"/> da: aynan shu tozalash
    /// (bo'shliq kesish + surrogat juftlikni buzmasdan qirqish) kurator
    /// bilan shaxsiy yozishmada ham kerak, faqat chegara boshqa. Ikki
    /// nusxa bo'lganda himoya bittasida unutilardi.
    /// </summary>
    public static string NormalizeBody(string? raw) =>
        MessageText.Normalize(raw, MaxBodyLength);
}
