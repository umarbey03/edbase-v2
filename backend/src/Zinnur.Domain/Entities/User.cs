using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>Platforma foydalanuvchisi (o'quvchi, ustoz, kurator, o'quv bo'limi, admin).</summary>
public class User : BaseEntity
{
    public required string FullName { get; set; }

    /// <summary>Unikal. Har doim kichik harflarda saqlanadi.</summary>
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    /// <summary>Normalizatsiya qilingan (+998...) ko'rinishda. Unikal.</summary>
    public string? Phone { get; set; }

    public long? TelegramId { get; set; }

    public UserRole Role { get; set; } = UserRole.Student;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Sessiyalarni bekor qilish hisoblagichi.
    /// JWT ichida `ver` claim'i sifatida yuriladi; mos kelmasa token rad etiladi.
    /// Parol almashtirilganda yoki rol o'zgarganda oshiriladi.
    ///
    /// NIMA UCHUN: eski tizimda "Chiqish" faqat cookie'ni o'chirardi va token
    /// 14 kun yaroqli qolardi — o'g'irlangan tokenni bekor qilishning iloji yo'q edi.
    /// </summary>
    public int TokenVersion { get; set; }

    /// <summary>Parol yoki rol o'zgarganda barcha mavjud tokenlarni bekor qiladi.</summary>
    public void InvalidateTokens() => TokenVersion++;

    public void ChangeRole(UserRole role)
    {
        if (Role == role) return;
        Role = role;
        InvalidateTokens();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPassword(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new DomainException("Parol hash'i bo'sh bo'lishi mumkin emas.");

        PasswordHash = newHash;
        InvalidateTokens();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
