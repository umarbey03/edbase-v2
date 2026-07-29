namespace Zinnur.Application.Auth.Dtos;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record UserDto(long Id, string FullName, string Email, string Role);

public sealed record AuthResponse(string AccessToken, string RefreshToken, UserDto User);
