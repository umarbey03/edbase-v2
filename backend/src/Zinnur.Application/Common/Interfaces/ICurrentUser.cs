using Zinnur.Domain.Enums;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>Joriy so'rovdagi foydalanuvchi (HTTP kontekstidan olinadi).</summary>
public interface ICurrentUser
{
    long? UserId { get; }
    UserRole? Role { get; }
    bool IsAuthenticated { get; }
}
