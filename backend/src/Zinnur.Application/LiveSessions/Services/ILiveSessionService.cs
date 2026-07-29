using Zinnur.Application.LiveSessions.Dtos;

namespace Zinnur.Application.LiveSessions.Services;

public interface ILiveSessionService
{
    Task<IReadOnlyList<LiveSessionDto>> ListForUserAsync(long userId, CancellationToken ct = default);

    Task<LiveSessionDto> GetAsync(long sessionId, long userId, CancellationToken ct = default);

    Task<LiveSessionDto> StartAsync(long sessionId, long userId, CancellationToken ct = default);

    Task<LiveSessionDto> EndAsync(long sessionId, long userId, CancellationToken ct = default);

    /// <summary>LiveKit'ga ulanish uchun token. Ruxsat shu yerda tekshiriladi.</summary>
    Task<LiveKitJoinDto> CreateJoinTokenAsync(long sessionId, long userId, CancellationToken ct = default);

    Task<IReadOnlyList<ChatMessageDto>> GetRecentMessagesAsync(
        long sessionId, long userId, int take = 50, CancellationToken ct = default);

    /// <summary>Ishtirokchi darsga kirdi/chiqdi — davomatni yangilaydi.</summary>
    Task RegisterJoinAsync(long sessionId, long userId, CancellationToken ct = default);

    Task RegisterLeaveAsync(long sessionId, long userId, CancellationToken ct = default);
}
