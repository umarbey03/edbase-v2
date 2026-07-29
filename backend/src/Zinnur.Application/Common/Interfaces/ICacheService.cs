namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Taqsimlangan kesh (Redis).
/// Jarayon xotirasidagi kesh TAQIQLANADI — ikkinchi instance qo'shilganda
/// ma'lumot bir-biriga mos kelmay qoladi.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Atomar hisoblagich (rate-limit uchun). Yangi qiymatni qaytaradi.
    /// Birinchi oshirishda <paramref name="ttl"/> o'rnatiladi.
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
