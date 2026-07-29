using Zinnur.Domain.Entities;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>Platforma sessiyasi uchun JWT'lar.</summary>
public interface IJwtTokenService
{
    /// <summary>Qisqa muddatli (default 15 daqiqa) kirish tokeni.</summary>
    string CreateAccessToken(User user);

    /// <summary>Uzoq muddatli (default 14 kun) yangilash tokeni.</summary>
    string CreateRefreshToken(User user);

    /// <summary>
    /// Refresh tokenni tekshiradi. Yaroqli bo'lsa (userId, tokenVersion) qaytaradi.
    /// <c>tokenVersion</c> bazadagi qiymat bilan solishtirilishi SHART —
    /// mos kelmasa token bekor qilingan hisoblanadi.
    /// </summary>
    (long UserId, int TokenVersion)? ValidateRefreshToken(string token);
}
