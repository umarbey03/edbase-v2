using Zinnur.Application.Auth.Dtos;

namespace Zinnur.Application.Auth.Services;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// TASDIQLANGAN Telegram ID bo'yicha kirish (Mini App — FAZA 5.1).
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// ★ NIMA UCHUN AYNAN SHU YERDA, alohida servisda EMAS
    ///
    /// Token yaratish, `ver` (sessiya versiyasi) va `refresh` mexanizmi
    /// SHU SINFDA. Telegram uchun ikkinchi, parallel kirish yo'li yozilsa,
    /// birida tuzatilgan zaiflik ikkinchisida ochiq qolardi — bu
    /// autentifikatsiya kodidagi eng ko'p uchraydigan xato turi.
    /// Shuning uchun Telegram moduli imzoni TEKSHIRADI, tokenni esa
    /// AYNI shu joydan oladi.
    ///
    /// ★ CHAQIRUVCHI IMZONI ALLAQACHON TEKSHIRGAN bo'lishi SHART:
    /// bu metod `telegramUserId` ga so'zsiz ishonadi. Uni HTTP dan
    /// to'g'ridan-to'g'ri olib chaqirish TAQIQLANADI — yagona chaqiruvchi
    /// `ITelegramMiniAppAuth`, u esa `initData` imzosini tekshiradi.
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="telegramUserId">`initData` imzosi bilan tasdiqlangan Telegram ID.</param>
    Task<AuthResponse> LoginWithTelegramAsync(long telegramUserId, CancellationToken ct = default);

    /// <summary>Barcha qurilmalardagi sessiyalarni bekor qiladi (TokenVersion++).</summary>
    Task LogoutAllAsync(long userId, CancellationToken ct = default);

    Task<UserDto> GetCurrentAsync(long userId, CancellationToken ct = default);
}
