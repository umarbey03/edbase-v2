using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// Kirish/chiqish use-case'lari.
/// HTTP haqida HECH NARSA bilmaydi — faqat domain xatolarini ko'taradi.
/// </summary>
public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    IJwtTokenService tokens) : IAuthService
{
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        var user = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        // MUHIM: foydalanuvchi topilmasa ham parol tekshiriladi (dummy hash bilan).
        // Sabab: aks holda javob vaqtidagi farq orqali qaysi email ro'yxatda
        // borligini aniqlash mumkin (user enumeration / timing attack).
        var hash = user?.PasswordHash ?? DummyHash;
        var ok = await hasher.VerifyAsync(request.Password ?? string.Empty, hash, ct);

        if (user is null || !ok)
            throw new UnauthorizedException("Email yoki parol noto'g'ri.");

        if (!user.IsActive)
            throw new ForbiddenException("Profil faol emas. O'quv bo'limi bilan bog'laning.");

        return Build(user);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var parsed = tokens.ValidateRefreshToken(refreshToken)
            ?? throw new UnauthorizedException("Sessiya muddati tugagan. Qaytadan kiring.");

        var user = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Id == parsed.UserId, ct)
            ?? throw new UnauthorizedException("Foydalanuvchi topilmadi.");

        // Token bekor qilinganmi (parol almashtirilgan / rol o'zgargan / chiqilgan)
        if (user.TokenVersion != parsed.TokenVersion)
            throw new UnauthorizedException("Sessiya bekor qilingan. Qaytadan kiring.");

        if (!user.IsActive)
            throw new ForbiddenException("Profil faol emas.");

        return Build(user);
    }

    public async Task LogoutAllAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        user.InvalidateTokens();
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserDto> GetCurrentAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        return Map(user);
    }

    private AuthResponse Build(User user) =>
        new(tokens.CreateAccessToken(user), tokens.CreateRefreshToken(user), Map(user));

    private static UserDto Map(User u) =>
        new(u.Id, u.FullName, u.Email, u.Role.ToString());

    /// <summary>Timing attack'ga qarshi qiyoslash uchun haqiqiy ko'rinishdagi BCrypt hash.</summary>
    private const string DummyHash =
        "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
}
