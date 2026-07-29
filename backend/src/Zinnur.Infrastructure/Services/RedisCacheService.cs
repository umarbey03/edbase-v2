using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;
using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ICacheService"/> ning Redis amalga oshirilishi.
///
/// Jarayon xotirasidagi kesh (MemoryCache) ATAYLAB ishlatilmaydi: API bir
/// nechta konteynerda ishlaganda har biri o'z keshini ko'radi va chat
/// rate-limit'i instance soniga ko'paytirilib ketadi (2 instance = 2 barobar spam).
///
/// DIQQAT: StackExchange.Redis API'si <c>CancellationToken</c> qabul qilmaydi
/// (buyruq allaqachon socket'ga yozilgan bo'lishi mumkin — uni "bekor qilish"
/// ma'nosiz). Shuning uchun token faqat chaqiruvdan OLDIN tekshiriladi.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    /// <summary>
    /// ATOMAR hisoblagich (chat rate-limit uchun — SPEC 6.2: 1 xabar / 2 sekund).
    ///
    /// NIMA UCHUN LUA: <c>INCR</c> va <c>EXPIRE</c> alohida yuborilsa, ular
    /// orasida jarayon uzilsa kalit MANGU qolib ketadi va foydalanuvchi chatdan
    /// butunlay bloklanadi. Skript Redis'da bo'linmas (atomar) bajariladi.
    /// TTL faqat BIRINCHI oshirishda qo'yiladi — aks holda har xabar oynani
    /// cho'zib yuborib, limit hech qachon tugamasdi.
    ///
    /// StackExchange.Redis skriptni SHA bo'yicha keshlaydi va keyingi
    /// chaqiriqlarda EVALSHA yuboradi — tarmoqqa har safar matn ketmaydi.
    /// </summary>
    private const string IncrementScript =
        """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return current
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db;

    public RedisCacheService(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _db = redis.GetDatabase();
    }

    /// <summary>Rate-limit kalitini bir xil ko'rinishda yasaydi (DRY — hub va API bir xil kalit ishlatsin).</summary>
    public static string RateLimitKey(string action, long userId) =>
        string.Create(CultureInfo.InvariantCulture, $"ratelimit:{action}:{userId}");

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var value = await _db.StringGetAsync(key).ConfigureAwait(false);
        var raw = (string?)value;

        return raw is null ? default : JsonSerializer.Deserialize<T>(raw, SerializerOptions);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(value, SerializerOptions);
        await _db.StringSetAsync(key, json, ttl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // TTL millisekundda: sekundga yaxlitlansa 2 sekundlik oyna 1 ga tushib qolardi.
        var ttlMs = (long)Math.Max(1d, ttl.TotalMilliseconds);

        var keys = new RedisKey[] { key };
        var values = new RedisValue[] { ttlMs };

        var result = await _db.ScriptEvaluateAsync(IncrementScript, keys, values).ConfigureAwait(false);

        return (long)result;
    }
}
