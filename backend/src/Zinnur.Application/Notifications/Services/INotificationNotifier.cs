using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// Ilova ichidagi bildirishnomani REALTIME uzatish porti.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NAQSH <c>IGroupChatNotifier</c> / <c>ILiveSessionNotifier</c> DAN
///   AYNAN KO'CHIRILGAN: port shu yerda (Application), SignalR amalga
///   oshirilishi esa WebApi'da. <c>Zinnur.Application</c> SignalR turlarini
///   (<c>IHubContext</c>) KO'RMASLIGI kerak.
///
/// 🔴 IKKI QAT'IY QOIDA — ikkalasi ham amalga oshirishga tegishli va
///    ikkalasi ham eski tizimdagi haqiqiy nosozlikdan kelib chiqqan:
///
///  1) COMMIT-THEN-SEND. Bu metod HAR DOIM <c>SaveChanges</c> DAN KEYIN
///     chaqiriladi. Oldin chaqirilsa, tranzaksiya orqaga qaytganda
///     o'quvchining ekranida BAZADA YO'Q baho paydo bo'lardi va u sahifani
///     yangilaguncha shunday turardi.
///
///  2) ISTISNO YUTILADI. Amalga oshirish HECH QACHON istisno tashlamaydi.
///     Bu yerdan chiqqan xato baholash endpointini 500 qilardi, ustoz esa
///     "saqlanmadi" deb o'ylab QAYTA baholardi — ya'ni transport nosozligi
///     BIZNES ma'lumotini buzardi. Yetkazilmaganining narxi: o'quvchi
///     sahifani yangilaganda bahoni baribir ko'radi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface INotificationNotifier
{
    /// <summary>
    /// Yangi bildirishnomani AYNAN BITTA foydalanuvchining barcha ochiq
    /// ilovalariga uzatadi.
    ///
    /// ★ QABUL QILUVCHI <paramref name="userId"/> — SignalR "guruhi" emas.
    /// Bu repozitoriydagi BIRINCHI foydalanuvchi darajasidagi kanal:
    /// mavjud hub'lar (<c>LiveClassHub</c>, <c>GroupChatHub</c>)
    /// <c>Clients.Group</c> ishlatadi, chunki ularning qamrovi dars yoki
    /// oqim. Bu yerda esa qamrov — ODAM: u bir vaqtda telefonda (Mini App)
    /// va noutbukda ochiq turishi mumkin, ikkalasiga ham yetishi kerak.
    /// </summary>
    Task NotificationCreatedAsync(
        long userId, NotificationDto notification, CancellationToken ct = default);
}
