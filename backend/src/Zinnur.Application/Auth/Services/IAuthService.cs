using Zinnur.Application.Auth.Dtos;

namespace Zinnur.Application.Auth.Services;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Barcha qurilmalardagi sessiyalarni bekor qiladi (TokenVersion++).</summary>
    Task LogoutAllAsync(long userId, CancellationToken ct = default);

    Task<UserDto> GetCurrentAsync(long userId, CancellationToken ct = default);
}
