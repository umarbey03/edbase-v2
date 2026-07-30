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

    /// <summary>Foydalanuvchi kiritgan ko'rinish (bo'shliq, qavs, defis bo'lishi mumkin).</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Qidiruv va taqqoslash uchun YAGONA ko'rinish: <c>+998901234567</c>.
    /// FILTRLI UNIKAL indeks shu ustunda — telefon bo'yicha izlash bitta
    /// indeksli <c>WHERE</c> bo'ladi.
    ///
    /// NIMA UCHUN ALOHIDA USTUN: eski tizimda <c>Phone</c> qanday kiritilgan
    /// bo'lsa shunday saqlanardi va taqqoslash uchun HAR kirishda barcha
    /// foydalanuvchilar xotiraga yuklanib, Python siklida normalizatsiya
    /// qilinardi (<c>users_svc.find_student_by_phone</c>). 100 ming yozuvda
    /// bu har so'rovda sekundlar demakdir. Endi normalizatsiya YOZUVDA bir
    /// marta bajariladi.
    ///
    /// <see cref="SetPhone"/> dan boshqa yo'l bilan o'zgartirilmaydi —
    /// shuning uchun ikki ustun bir-biriga mos kelmay qolishi mumkin emas.
    /// </summary>
    public string? PhoneNormalized { get; private set; }

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

    /// <summary>
    /// Telefonni o'rnatadi va <see cref="PhoneNormalized"/> ni AVTOMATIK hisoblaydi.
    /// Telefonni o'zgartirishning yagona yo'li — normalizatsiyani unutib bo'lmaydi.
    /// </summary>
    public void SetPhone(string? rawPhone)
    {
        var normalized = NormalizePhone(rawPhone);

        // Raqamsiz matn ("-", "yo'q") telefonsiz deb qaraladi.
        Phone = normalized is null ? null : rawPhone?.Trim();
        PhoneNormalized = normalized;
    }

    /// <summary>
    /// Telefonni taqqoslash uchun bir ko'rinishga keltiradi: faqat raqamlar + <c>+</c>.
    /// O'zbekiston uchun 9 xonali lokal raqam (<c>901234567</c>) ga <c>998</c>
    /// prefiksi qo'shiladi, <c>0998...</c> ko'rinishidagi boshdagi nol olib tashlanadi.
    /// Raqam topilmasa <c>null</c> qaytaradi.
    /// </summary>
    public static string? NormalizePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone)) return null;

        Span<char> digits = stackalloc char[MaxPhoneDigits];
        var length = 0;

        foreach (var ch in rawPhone)
        {
            if (!char.IsAsciiDigit(ch)) continue;
            if (length == MaxPhoneDigits) return null;      // haddan tashqari uzun -> yaroqsiz
            digits[length++] = ch;
        }

        if (length == 0) return null;

        var value = new string(digits[..length]);

        return length switch
        {
            9 => "+998" + value,                            // 901234567    -> +998901234567
            13 when value[0] == '0' => "+" + value[1..],     // 0998901234567 -> +998901234567
            _ => "+" + value,
        };
    }

    /// <summary>E.164 chegarasi 15 raqam; zaxira bilan 18.</summary>
    private const int MaxPhoneDigits = 18;
}
