using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;

namespace Zinnur.WebApi.Hubs;

/// <summary>
/// <see cref="INotificationNotifier"/> ning SignalR amalga oshirilishi.
///
/// Bu sinf ATAYLAB WebApi qatlamida: SignalR turlari (<c>IHubContext</c>)
/// shu yerda yashaydi va <c>Zinnur.Application</c> ularni bilmasligi kerak
/// — <see cref="GroupChatNotifier"/> va <c>LiveSessionNotifier</c> bilan
/// AYNI naqsh.
/// </summary>
public sealed class NotificationNotifier(
    IHubContext<NotificationHub> hub,
    ILogger<NotificationNotifier> logger) : INotificationNotifier
{
    /// <inheritdoc />
    public async Task NotificationCreatedAsync(
        long userId, NotificationDto notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            // ═════════════════════════════════════════════════════════════
            // ★ `Clients.User(...)` — `Clients.Group(...)` EMAS.
            //
            // Bu repozitoriydagi YAGONA shunday chaqiruv. U SignalR ning
            // ichki "user -> ulanishlar" jadvalidan foydalanadi: ro'yxatni
            // biz yuritmaymiz, ulanish qo'shilganda/uzilganda SignalR o'zi
            // to'ldiradi. Guruh yo'lini takrorlash (masalan "user-42"
            // nomli guruh + `OnConnected` da qo'lda qo'shish) qo'shimcha
            // holat va qo'shimcha xato manbai bo'lardi — ayniqsa Redis
            // backplane bilan, ya'ni ulanishlar bir necha instance'ga
            // tarqalganda.
            //
            // ★ IDENTIFIKATOR SATR va u `ClaimTypes.NameIdentifier` dagi
            //   qiymat bilan AYNAN MOS bo'lishi kerak (`DefaultUserIdProvider`
            //   shu claim'ni o'qiydi). `InvariantCulture` — `long` ni
            //   formatlashda madaniyat guruh ajratgichini qo'shib
            //   yubormasligi uchun ("1 234" kabi qiymat hech qanday
            //   ulanishga mos kelmasdi va xato ham chiqmasdi).
            // ═════════════════════════════════════════════════════════════
            await hub.Clients
                .User(userId.ToString(CultureInfo.InvariantCulture))
                .SendAsync("NotificationCreated", notification, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 🔴 YUTIB YUBORAMIZ (port kelishuvi) — `GroupChatNotifier`
            // dagi AYNI sabab, lekin bu yerda oqibati OG'IRROQ: chaqiruvchi
            // BAHOLASH endpointi. Istisno yuqoriga chiqsa endpoint 500
            // qaytarardi, ustoz esa "saqlanmadi" deb o'ylab QAYTA baholardi
            // — ya'ni transport nosozligi ustozning ishini ikkilantirardi.
            //
            // Yetkazilmaganining narxi kichik: bildirishnoma BAZADA
            // allaqachon bor, o'quvchi sahifani yangilaganda uni ko'radi.
            NotificationLog.BroadcastFailed(logger, ex, userId, notification.Id);
        }
    }
}

/// <summary>
/// Bildirishnoma kanalining jurnali (manba-generatorli
/// <c>LoggerMessage</c> — <see cref="GroupChatLog"/> bilan bir xil uslub).
/// </summary>
internal static partial class NotificationLog
{
    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Warning,
        Message = "Bildirishnoma saqlandi, lekin tarqatilmadi: user={UserId} notification={NotificationId}")]
    public static partial void BroadcastFailed(
        ILogger logger, Exception exception, long userId, long notificationId);
}
