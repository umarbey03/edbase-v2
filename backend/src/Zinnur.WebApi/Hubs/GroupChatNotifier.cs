using Microsoft.AspNetCore.SignalR;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Application.GroupChat.Services;

namespace Zinnur.WebApi.Hubs;

/// <summary>
/// <see cref="IGroupChatNotifier"/> ning SignalR amalga oshirilishi.
///
/// Bu sinf ATAYLAB WebApi qatlamida: SignalR turlari (`IHubContext`) shu
/// yerda yashaydi va `Zinnur.Application` ularni bilmasligi kerak —
/// <c>LiveSessionNotifier</c> bilan bir xil naqsh.
///
/// ★ XABAR FAQAT `(GroupId, Channel)` XONASIGA ketadi. Butun guruhga
/// yuborilsa, kurator oqimidagi savol ustozning ekranida chiqib qolardi —
/// ya'ni butun kanal izolyatsiyasi realtime yo'lida bekor bo'lardi.
/// </summary>
public sealed class GroupChatNotifier(
    IHubContext<GroupChatHub> hub,
    ILogger<GroupChatNotifier> logger) : IGroupChatNotifier
{
    /// <inheritdoc />
    public async Task MessageSentAsync(GroupChatMessageDto message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await hub.Clients
                .Group(GroupChatHub.ThreadName(message.GroupId, message.Channel))
                .SendAsync("GroupChatMessage", message, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ★ Yutib yuboramiz (port kelishuvi): xabar BAZADA allaqachon
            // saqlangan. Bu yerdan istisno chiqsa endpoint 500 qaytarardi,
            // foydalanuvchi "yuborilmadi" deb o'ylab qayta bosardi va AYNI
            // xabar ikki marta yozilardi. Yetkazilmagani darajasi: qarshi
            // tomon sahifani yangilaganda xabarni baribir ko'radi.
            GroupChatLog.BroadcastFailed(
                logger, ex, message.GroupId, message.Channel.ToString());
        }
    }
}
