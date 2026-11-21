using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// HAMJAMIYAT — Mustaqil o'quvchilarning UMUMIY chati (2026-11-21)
/// ════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi: <i>"mustaqil o'quvchilarni hammasini bir chatga jamlasak,
/// bu yerda fikr almashib turishar edi"</i>. Kurator bilan shaxsiy yozishma
/// (<see cref="DirectMessage"/>) O'ZGARMAYDI — bu ikkinchi, umumiy xona.
///
/// ★ ALOHIDA JADVAL, guruh chati EMAS: guruh chati a'zolikni guruhdan oladi;
///   Mustaqil o'quvchilarni soxta guruhga qo'shish davomat, reyting va
///   darslar mantiqini buzardi.
/// ★ O'CHIRISH — YUMSHOQ: moderator o'chirgan xabar o'rnida "o'chirildi"
///   qoladi (javoblar zanjiri uzilmasin), matni esa tozalanadi.
/// </summary>
public class CommunityMessage : BaseEntity
{
    public const int MaxBodyLength = 1000;

    public long AuthorId { get; set; }
    public User? Author { get; set; }

    public string Body { get; set; } = string.Empty;

    public long? ReplyToId { get; set; }
    public CommunityMessage? ReplyTo { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public long? DeletedById { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    public static CommunityMessage Create(long authorId, string? body, long? replyToId, DateTimeOffset now)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new DomainException("Xabar bo'sh.");
        if (text.Length > MaxBodyLength)
            throw new DomainException($"Xabar {MaxBodyLength} belgidan oshmasin.");

        return new CommunityMessage
        {
            AuthorId = authorId,
            Body = text,
            ReplyToId = replyToId,
            CreatedAt = now,
        };
    }

    public void Delete(long actorId, DateTimeOffset now)
    {
        if (IsDeleted) return;
        DeletedAt = now;
        DeletedById = actorId;
        Body = string.Empty;
        UpdatedAt = now;
    }
}

/// <summary>Hamjamiyatda yozishdan vaqtincha to'xtatilgan o'quvchi (moderator qarori).</summary>
public class CommunityMute : BaseEntity
{
    public const int MaxReasonLength = 300;

    public long UserId { get; set; }
    public User? User { get; set; }

    public DateTimeOffset MutedUntil { get; set; }
    public string? Reason { get; set; }
    public long MutedById { get; set; }

    public bool ActiveAt(DateTimeOffset now) => MutedUntil > now;
}

/// <summary>Foydalanuvchi hamjamiyatda qaysi xabargacha o'qigan (o'qilmaganlar soni uchun).</summary>
public class CommunityReadState : BaseEntity
{
    public long UserId { get; set; }
    public long LastReadMessageId { get; set; }
}
