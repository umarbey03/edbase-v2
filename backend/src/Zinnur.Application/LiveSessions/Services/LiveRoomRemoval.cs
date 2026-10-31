using System.Globalization;
using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Application.LiveSessions.Services;

/// <summary>
/// "Ustoz bu o'quvchini SHU darsdan chiqardi" belgisi (2026-10-31).
///
/// ★ NIMA UCHUN KERAK: LiveKit'dan chiqarish (<c>RemoveParticipant</c>)
/// bir martalik uzish — o'quvchi sahifani yangilasa yangi token so'rardi va
/// qaytib kirardi. Belgi token berish yo'llarini (asosiy xona va ichki
/// guruh) shu dars davomida yopadi.
///
/// ★ NIMA UCHUN REDIS (baza emas): bu DARS ICHIDAGI qaror — ertangi darsga
/// tegmasligi kerak va tarix sifatida saqlanmaydi. TTL eng uzun darsdan
/// uzun (<see cref="Ttl"/>), dars tugagach token baribir berilmaydi.
///
/// ★ NIMA UCHUN ALOHIDA STATIK SINF: belgini IKKI servis o'qiydi
/// (<c>LiveSessionService</c>, <c>BreakoutService</c>) va kalit shakli bitta
/// joyda bo'lishi shart — aks holda biri yozgan kalitni ikkinchisi topmasdi.
/// </summary>
public static class LiveRoomRemoval
{
    /// <summary>Belgi yashash muddati — <c>BreakoutService</c> holati bilan AYNI (8 soat).</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(8);

    /// <summary>Chiqarilgan o'quvchiga ko'rsatiladigan matn (403).</summary>
    public const string RemovedMessage =
        "Ustoz sizni bu darsdan chiqardi. Savol bo'lsa ustoz yoki o'quv bo'limiga murojaat qiling.";

    public static string Key(long sessionId, long userId) => string.Create(
        CultureInfo.InvariantCulture, $"live:removed:{sessionId}:{userId}");

    public static Task MarkRemovedAsync(ICacheService cache, long sessionId, long userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cache);

        return cache.SetAsync(Key(sessionId, userId), new LiveRoomRemovalMark(true), Ttl, ct);
    }

    public static async Task<bool> IsRemovedAsync(
        ICacheService cache, long sessionId, long userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cache);

        var mark = await cache.GetAsync<LiveRoomRemovalMark>(Key(sessionId, userId), ct).ConfigureAwait(false);

        return mark?.Removed == true;
    }
}

/// <summary>
/// <see cref="LiveRoomRemoval"/> kesh qiymati. Oddiy <c>bool</c> EMAS: <c>GetAsync&lt;bool&gt;</c>
/// yo'q kalitda <c>false</c> qaytaradi va "yozilmagan" bilan "false yozilgan"
/// farqlanmaydi — obyekt bilan <c>null</c> aniq ma'no beradi.
/// </summary>
public sealed record LiveRoomRemovalMark(bool Removed);
