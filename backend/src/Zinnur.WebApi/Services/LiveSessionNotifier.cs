using Microsoft.AspNetCore.SignalR;
using Zinnur.Application.Common.Interfaces;
using Zinnur.WebApi.Hubs;

namespace Zinnur.WebApi.Services;

/// <summary>
/// <see cref="ILiveSessionNotifier"/> ning SignalR amalga oshirishi.
///
/// Bu klass ATAYLAB WebApi qatlamida: SignalR turlari (`IHubContext`) shu
/// yerda yashaydi va `Zinnur.Application` ularni bilmasligi kerak. Xuddi
/// <see cref="ChatMessageWriter"/> kabi — Application port e'lon qiladi,
/// WebApi uni bajaradi.
/// </summary>
public sealed class LiveSessionNotifier(
    IHubContext<LiveClassHub> hub,
    ILogger<LiveSessionNotifier> logger) : ILiveSessionNotifier
{
    /// <inheritdoc />
    public async Task SessionEndedAsync(long sessionId, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients
                .Group(LiveClassHub.GroupName(sessionId))
                .SendAsync("SessionEnded", new SessionEndedEvent(sessionId), ct)
                .ConfigureAwait(false);

            ApiLog.SessionEndBroadcast(logger, sessionId);
        }
        catch (Exception ex)
        {
            // ★ Yutib yuboramiz (port kelishuvi): dars BAZADA allaqachon
            // yakunlangan. Bu yerdan istisno chiqsa endpoint 500 qaytarardi va
            // ustoz "yakunlanmadi" deb qayta bosardi — holat esa aslida
            // to'g'ri. Xabar yetmagani darajasi: o'quvchi sahifani yangilaganda
            // baribir tugagan darsni ko'radi.
            ApiLog.SessionEndBroadcastFailed(logger, ex, sessionId);
        }
    }
}
