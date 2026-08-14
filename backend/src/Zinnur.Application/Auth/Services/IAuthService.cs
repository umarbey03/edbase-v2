using Zinnur.Application.Auth.Dtos;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// Sessiya ochish/yopish. TOKEN YARATISHNING YAGONA JOYI.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 IKKINCHI, PARALLEL KIRISH YO'LI YOZILMAYDI — QAT'IY QOIDA
///
/// Token yaratish, <c>ver</c> (sessiya versiyasi) va <c>refresh</c>
/// mexanizmi SHU SINFDA. Har yangi "eshik" uchun alohida servis yozilsa,
/// birida tuzatilgan zaiflik ikkinchisida ochiq qolardi — bu
/// autentifikatsiya kodidagi eng ko'p uchraydigan xato turi.
///
/// Shuning uchun HAR eshik AYNI naqshni takrorlaydi: modul EGALIKNI
/// tekshiradi (Telegram imzosi / bir martalik kod), tokenni esa AYNAN shu
/// yerdan oladi.
///
/// Bugungi eshiklar:
///   • <see cref="LoginWithTelegramAsync"/> — Mini App, <c>initData</c> imzosi;
///   • <see cref="LoginWithPhoneAsync"/>    — telefon + bir martalik kod.
///
/// ⚠️ EMAIL VA PAROL BILAN KIRISH OLIB TASHLANDI (2026-08-13, loyiha
/// egasining qarori — talab R26). <c>LoginAsync</c> metodi ham,
/// <c>POST /api/v1/auth/login</c> endpointi ham YO'Q. Parol hash'i
/// ustuni bazada qoldi, lekin uni HECH KIM o'qimaydi — sabab
/// <c>User.PasswordHash</c> izohida.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface IAuthService
{
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// TASDIQLANGAN Telegram ID bo'yicha kirish (Mini App — FAZA 5.1).
    /// </summary>
    /// <remarks>
    /// ★ CHAQIRUVCHI IMZONI ALLAQACHON TEKSHIRGAN bo'lishi SHART:
    /// bu metod <paramref name="telegramUserId"/> ga so'zsiz ishonadi. Uni
    /// HTTP dan to'g'ridan-to'g'ri olib chaqirish TAQIQLANADI — yagona
    /// chaqiruvchi <c>ITelegramMiniAppAuth</c>, u esa <c>initData</c>
    /// imzosini tekshiradi.
    /// </remarks>
    /// <param name="telegramUserId">`initData` imzosi bilan tasdiqlangan Telegram ID.</param>
    Task<AuthResponse> LoginWithTelegramAsync(long telegramUserId, CancellationToken ct = default);

    /// <summary>
    /// TASDIQLANGAN bir martalik koddan keyingi kirish (telefon oqimi).
    /// </summary>
    /// <remarks>
    /// ★ CHAQIRUVCHI KODNI ALLAQACHON TEKSHIRGAN VA ISHLATGAN bo'lishi
    /// SHART: bu metod <paramref name="userId"/> ga so'zsiz ishonadi.
    /// Yagona chaqiruvchi — <c>IPhoneLoginService.VerifyAsync</c>.
    ///
    /// 🔴 <c>userId</c> HTTP so'rovidan KELMAYDI. U kod tasdiqlangandan
    /// keyin SERVER tomonida, <c>PhoneNormalized</c> bo'yicha topiladi.
    /// So'rov tanasidagi identifikatorga ishonish — eski tizimning X-1
    /// zaifligining aynan o'zi bo'lardi.
    /// </remarks>
    Task<AuthResponse> LoginWithPhoneAsync(long userId, CancellationToken ct = default);

    /// <summary>Barcha qurilmalardagi sessiyalarni bekor qiladi (TokenVersion++).</summary>
    Task LogoutAllAsync(long userId, CancellationToken ct = default);

    Task<UserDto> GetCurrentAsync(long userId, CancellationToken ct = default);
}
