using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Auth.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Kirish/chiqish. Controller YUPQA: validatsiya -> servis -> javob.
/// Biznes mantiq <see cref="IAuthService"/> ichida.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>
    /// Parol topishga (brute force) qarshi rate-limit siyosatining nomi.
    ///
    /// Nom SHU YERDA e'lon qilinadi, `Program.cs` esa siyosatni AYNAN shu
    /// const bilan ro'yxatdan o'tkazadi. NEGA const: nom ikki joyda oddiy
    /// satr bo'lsa, atributni umuman unutish JIMGINA "cheklovsiz endpoint"
    /// beradi va buni hech kim sezmaydi. Aynan shu bo'lgan edi — siyosat
    /// e'lon qilingan, hech qayerga qo'llanmagan va bitta IP'dan 1500 ta
    /// kirish so'rovi to'siqsiz o'tgan.
    /// </summary>
    public const string LoginRateLimitPolicy = "auth";

    /// <summary>
    /// Token yangilash uchun ALOHIDA siyosat (kengroq budjet).
    ///
    /// NEGA KIRISH BILAN BIR XIL EMAS — tahdid modeli boshqa. Parolni
    /// taxmin qilib bo'ladi, refresh tokenni esa yo'q: u HS256 bilan
    /// imzolangan va `TokenVersion` ga bog'langan. Bu yerdagi cheklov
    /// faqat bazani bekorga urishga qarshi.
    ///
    /// Aksincha, umumiy budjet ZARAR qilardi: bitta maktab bitta NAT IP
    /// orqasida turadi va kirish tokeni har 15 daqiqada yangilanadi.
    /// 100 o'quvchi ≈ 7 yangilash/daqiqa, ustiga dars boshidagi kirishlar —
    /// birinchi dars soatidayoq hamma tizimdan uchib chiqardi.
    /// </summary>
    public const string RefreshRateLimitPolicy = "auth-refresh";

    /// <summary>Email va parol bilan kirish.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, ct));

    /// <summary>Kirish tokenini yangilash.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RefreshRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken ct) =>
        Ok(await auth.RefreshAsync(request.RefreshToken, ct));

    /// <summary>
    /// Barcha qurilmalardan chiqish.
    /// TokenVersion oshiriladi — mavjud tokenlarning HAMMASI darhol bekor bo'ladi.
    /// (Eski tizimda "chiqish" faqat cookie'ni o'chirardi, token 14 kun yashardi.)
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAllAsync(CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Joriy foydalanuvchi ma'lumoti.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) =>
        Ok(await auth.GetCurrentAsync(CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
