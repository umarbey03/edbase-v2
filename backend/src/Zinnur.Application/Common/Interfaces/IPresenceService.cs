using Zinnur.Application.Common.Models;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Jonli darsdagi ishtirokchilar ro'yxati — Redis'da (hash) saqlanadi.
///
/// NIMA UCHUN REDIS: ikki API instance bo'lsa, in-memory Dictionary'da
/// har biri o'z ro'yxatini ko'radi va o'quvchilar bir-birini ko'rmaydi.
/// </summary>
public interface IPresenceService
{
    Task AddAsync(long sessionId, PresenceEntry entry, CancellationToken ct = default);

    Task RemoveAsync(long sessionId, long userId, CancellationToken ct = default);

    Task<IReadOnlyList<PresenceEntry>> ListAsync(long sessionId, CancellationToken ct = default);

    Task SetHandRaisedAsync(long sessionId, long userId, bool raised, CancellationToken ct = default);

    Task<int> CountAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Dars tugaganda butun ro'yxatni tozalaydi.</summary>
    Task ClearAsync(long sessionId, CancellationToken ct = default);
}
