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

    /// <summary>Kalit makoni berilmasa ishlatiladigan standart qiymat.</summary>
    public const string DefaultPrefix = "zinnur";

    private readonly IDatabase _db;

    /// <summary>
    /// Kalitlar oldiga qo'yiladigan MAKON (namespace).
    ///
    /// ★ NIMA UCHUN KERAK: bitta Redis bir nechta muhitga xizmat qilishi
    /// mumkin (dev/staging, integratsiya testlari, ikkinchi instance). Kalitlar
    /// yalang'och bo'lsa turli bazalardagi BIR XIL raqamli Id'lar bir-birining
    /// yozuviga tushadi: `auth:state:4` bir bazada faol o'quvchi, ikkinchisida
    /// o'chirilgan xodim bo'lishi mumkin. Bu jimgina xato — hech qayerda
    /// ko'rinmaydi, faqat "nega bu foydalanuvchi kira olmayapti?" degan savol
    /// qoladi.
    ///
    /// Integratsiya testlari aynan shu holatga tushdi: har test sinfi O'Z
    /// Postgres bazasini oladi, Redis esa UMUMIY.
    /// </summary>
    private readonly string _prefix;

    public RedisCacheService(IConnectionMultiplexer redis, string? keyPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _db = redis.GetDatabase();
        _prefix = string.IsNullOrWhiteSpace(keyPrefix) ? DefaultPrefix : keyPrefix.Trim();
    }

    private string Qualify(string key) =>
        string.Create(CultureInfo.InvariantCulture, $"{_prefix}:{key}");

    /// <summary>Rate-limit kalitini bir xil ko'rinishda yasaydi (DRY — hub va API bir xil kalit ishlatsin).</summary>
    public static string RateLimitKey(string action, long userId) =>
        string.Create(CultureInfo.InvariantCulture, $"ratelimit:{action}:{userId}");

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var value = await _db.StringGetAsync(Qualify(key)).ConfigureAwait(false);
        var raw = (string?)value;

        return raw is null ? default : JsonSerializer.Deserialize<T>(raw, SerializerOptions);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(value, SerializerOptions);
        await _db.StringSetAsync(Qualify(key), json, ttl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.KeyDeleteAsync(Qualify(key)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // TTL millisekundda: sekundga yaxlitlansa 2 sekundlik oyna 1 ga tushib qolardi.
        var ttlMs = (long)Math.Max(1d, ttl.TotalMilliseconds);

        var keys = new RedisKey[] { Qualify(key) };
        var values = new RedisValue[] { ttlMs };

        var result = await _db.ScriptEvaluateAsync(IncrementScript, keys, values).ConfigureAwait(false);

        return (long)result;
    }
}
