using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// Platforma sessiyasi uchun JWT'lar (LiveKit tokeni bilan aralashtirmang).
///
/// ENG MUHIM QISM — <see cref="TokenVersionClaim"/> (`ver`):
/// tokenda foydalanuvchining <c>TokenVersion</c> qiymati yuriladi.
/// Parol almashtirilganda / rol o'zgarganda / "hamma qurilmadan chiqish"da
/// bu son bazada oshiriladi va eski tokenlar SHU ONDA yaroqsiz bo'ladi.
/// Eski tizimda "Chiqish" faqat cookie'ni o'chirardi — o'g'irlangan token
/// 14 kun ishlayverardi va uni bekor qilishning imkoni yo'q edi.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    /// <summary>Sessiya versiyasi claim'i. WebApi ham SHU nomni tekshiradi.</summary>
    public const string TokenVersionClaim = "ver";

    /// <summary>Token turi: kirish tokeni refresh sifatida ishlatilmasin.</summary>
    public const string TokenUseClaim = "token_use";

    /// <summary>
    /// Rol claim'i. Qisqa "role" nomi standart — <c>JwtBearer</c> default
    /// sozlamalarida u avtomatik <c>ClaimTypes.Role</c> ga o'giriladi.
    /// Agar WebApi'da <c>MapInboundClaims = false</c> qilinsa,
    /// <c>TokenValidationParameters.RoleClaimType = "role"</c> qo'yilishi shart.
    /// </summary>
    public const string RoleClaim = "role";

    private const string AccessTokenUse = "access";
    private const string RefreshTokenUse = "refresh";

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _refreshValidation;

    // MapInboundClaims = false: aks holda `sub` claim'i o'qishda uzun
    // `...nameidentifier` URI'siga aylanadi va biz uni topa olmay qolamiz.
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        _refreshValidation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,

            // Default 5 daqiqalik "skew" refresh tokeni uchun keraksiz kenglik.
            ClockSkew = TimeSpan.FromSeconds(30),

            // Faqat HS256 — `alg: none` yoki boshqa algoritmga o'tkazish hujumi yopiladi.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };
    }

    /// <inheritdoc />
    public string CreateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Kirish tokeni qisqa umrli: o'g'irlansa ham 15 daqiqada o'ladi.
        // Uzun umrli huquq faqat refresh tokenda va u har ishlatilganda
        // baza bilan (TokenVersion) solishtiriladi.
        var claims = new List<Claim>(6)
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)),
            new(TokenVersionClaim, user.TokenVersion.ToString(CultureInfo.InvariantCulture)),
            new(TokenUseClaim, AccessTokenUse),
            new(RoleClaim, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Name, user.FullName),
        };

        return Create(claims, TimeSpan.FromMinutes(_options.AccessMinutes));
    }

    /// <inheritdoc />
    public string CreateRefreshToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Refresh tokenda rol/ism YO'Q — u faqat "kim" va "qaysi versiya"ni
        // bildiradi. Qolgani baza bilan tekshiriladi (rol tokenda qotib qolmasin).
        var claims = new List<Claim>(4)
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)),
            new(TokenVersionClaim, user.TokenVersion.ToString(CultureInfo.InvariantCulture)),
            new(TokenUseClaim, RefreshTokenUse),
        };

        return Create(claims, TimeSpan.FromDays(_options.RefreshDays));
    }

    /// <inheritdoc />
    public (long UserId, int TokenVersion)? ValidateRefreshToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        ClaimsPrincipal principal;

        try
        {
            principal = _handler.ValidateToken(token, _refreshValidation, out _);
        }
        catch (SecurityTokenException)
        {
            // Muddati o'tgan / imzo noto'g'ri / shakli buzuq — hammasi bir xil
            // natija: "qaytadan kiring". Sabab klientga OSHKOR qilinmaydi.
            return null;
        }
        catch (ArgumentException)
        {
            // JWT umuman bo'lmagan satr yuborilgan.
            return null;
        }

        // Kirish tokenini refresh sifatida ishlatishga yo'l qo'yilmaydi.
        if (!string.Equals(ClaimValue(principal, TokenUseClaim), RefreshTokenUse, StringComparison.Ordinal))
            return null;

        var subject = ClaimValue(principal, JwtRegisteredClaimNames.Sub);
        var version = ClaimValue(principal, TokenVersionClaim);

        if (!long.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return null;

        if (!int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokenVersion))
            return null;

        return (userId, tokenVersion);
    }

    /// <summary>
    /// `FindFirstValue` — ASP.NET Identity kengaytmasi; Infrastructure qatlami
    /// ASP.NET'ga bog'lanmasligi uchun (Clean Architecture) shu yerda o'zi yozildi.
    /// </summary>
    private static string? ClaimValue(ClaimsPrincipal principal, string type) =>
        principal.FindFirst(type)?.Value;

    private string Create(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        // JwtSecurityTokenHandler `DateTime` (UTC) kutadi — DateTimeOffset emas.
        var now = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            SigningCredentials = _credentials,
        };

        return _handler.CreateEncodedJwt(descriptor);
    }
}
