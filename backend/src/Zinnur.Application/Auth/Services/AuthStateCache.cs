using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// <see cref="IAuthStateCache"/> — Redis + baza.
///
/// KESH MUDDATI QISQA (<see cref="Ttl"/>), lekin u ASOSIY mexanizm EMAS:
/// chiqish/o'chirishda kesh ANIQ tozalanadi, ya'ni o'zgarish darhol kuchga
/// kiradi. Muddat faqat "kimdir tozalashni unutdi" holati uchun tarmoq
/// (safety net) — cheksiz keshda o'chirilgan foydalanuvchi abadiy kira olardi.
/// </summary>
public sealed class AuthStateCache(
    IApplicationDbContext db,
    ICacheService cache) : IAuthStateCache
{
    /// <summary>
    /// 60 sekund: jonli darsda SignalR va HTTP chaqiruvlari zich keladi, har
    /// biriga bitta `SELECT` qo'shish 200 foydalanuvchida sezilarli yuk beradi.
    /// Tozalash aniq bo'lgani uchun bu muddat kechikish emas, zaxira.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    public async Task<UserAuthState?> GetAsync(long userId, CancellationToken ct = default)
    {
        var key = KeyFor(userId);

        var cached = await cache.GetAsync<UserAuthState>(key, ct).ConfigureAwait(false);
        if (cached is not null) return cached;

        var state = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserAuthState(u.TokenVersion, u.IsActive))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // Topilmagan foydalanuvchi KESHLANMAYDI: aks holda o'chirilgan
        // (yoki hali yaratilmagan) Id bo'yicha keladigan so'rovlar keshni
        // to'ldirib yuborardi.
        if (state is null) return null;

        await cache.SetAsync(key, state, Ttl, ct).ConfigureAwait(false);
        return state;
    }

    public Task InvalidateAsync(long userId, CancellationToken ct = default) =>
        cache.RemoveAsync(KeyFor(userId), ct);

    private static string KeyFor(long userId) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:state:{userId}");
}
