using Zinnur.Application.Auth.Dtos;

namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// Mini App kirishi: <c>initData</c> ni tekshiradi va MAVJUD auth oqimidan
/// token oladi.
///
/// ★ BU YERDA TOKEN YARATILMAYDI. Bu sinf faqat "kim kelayotganini"
/// isbotlaydi; token, `ver` (sessiya versiyasi) va rol tekshiruvi
/// <c>IAuthService.LoginWithTelegramAsync</c> da — ya'ni email+parol
/// bilan AYNI joyda. Ikkinchi, parallel auth yo'li YARATILMAYDI.
/// </summary>
public interface ITelegramMiniAppAuth
{
    /// <summary>
    /// Kirishga urinadi.
    /// </summary>
    /// <exception cref="Common.Exceptions.ServiceUnavailableException">
    /// Telegram sozlanmagan (bot tokeni yo'q) — 503.
    /// </exception>
    /// <exception cref="Common.Exceptions.UnauthorizedException">
    /// <c>initData</c> imzosi yaroqsiz yoki muddati o'tgan — 401.
    /// </exception>
    /// <exception cref="Common.Exceptions.ConflictException">
    /// Telegram akkaunt hech kimga bog'lanmagan — 409.
    /// </exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">
    /// O'quvchi emas yoki profil faol emas — 403.
    /// </exception>
    Task<AuthResponse> AuthenticateAsync(string? initData, CancellationToken ct = default);
}
