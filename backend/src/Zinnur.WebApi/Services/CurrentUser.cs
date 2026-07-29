using System.Globalization;
using System.Security.Claims;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Enums;

namespace Zinnur.WebApi.Services;

/// <summary>
/// Joriy so'rovdagi foydalanuvchi — JWT claim'laridan o'qiladi.
/// Application qatlami HTTP'ni bilmaydi, shuning uchun bu adapter WebApi'da.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public long? UserId =>
        long.TryParse(
            Principal?.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var id)
            ? id
            : null;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role)
            ? role
            : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
}
