using Zinnur.Application.Common.Models;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>LiveKit uchun imzolangan kirish tokeni yaratadi.</summary>
public interface ILiveKitTokenService
{
    /// <summary>HS256 JWT qaytaradi (LiveKit `video` grant'lari bilan).</summary>
    string CreateAccessToken(LiveKitTokenRequest request);

    /// <summary>Frontend ulanadigan LiveKit server manzili (ws:// yoki wss://).</summary>
    string ServerUrl { get; }
}
