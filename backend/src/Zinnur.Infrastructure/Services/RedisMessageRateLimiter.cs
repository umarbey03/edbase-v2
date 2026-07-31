using System.Globalization;
using StackExchange.Redis;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IMessageRateLimiter"/> ning Redis ("token bucket") amalga
/// oshirilishi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN "TOKEN BUCKET", QAT'IY OYNA (fixed window) EMAS:
/// qat'iy oynada butun budjet oynaning birinchi millisekundida sarflanishi
/// mumkin — 30 ta xabar bir zumda ketadi va Telegram buni sekundlik
/// chegaraning buzilishi deb qabul qiladi (429 + vaqtincha bloklash).
/// Token bucket esa tokenlarni TEKIS to'ldiradi: o'rtacha tezlik ham,
/// bir zumdagi eng katta portlash ham nazorat ostida.
///
/// ★ NIMA UCHUN LUA: o'qish → hisoblash → yozish ketma-ketligi ATOMAR
/// bo'lishi shart. Uch buyruq alohida yuborilsa, ikki instance orasidagi
/// poygada ikkalasi ham "token bor" deb ko'rib, chegara ikki barobar
/// oshib ketardi — ya'ni cheklovning o'zi ma'nosini yo'qotardi.
///
/// ★ VAQT REDIS'DAN OLINADI (<c>TIME</c>), konteynerdan emas: ikki
/// instance soati bir necha sekundga farq qilsa, to'ldirish hisobi
/// buzilib, chegara jimgina oshib ketardi. Redis — YAGONA soat.
///
/// ★ KALIT MAKONI (<c>Redis:KeyPrefix</c>) MAJBURIY: bitta Redis'ni ikki
/// muhit baham ko'rsa (dev/staging, integratsiya testlari) chegara
/// hisoblagichi aralashib ketardi — sabab <see cref="RedisCacheService"/>
/// izohida batafsil.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RedisMessageRateLimiter : IMessageRateLimiter
{
    /// <summary>
    /// Token bucket. Qaytaradi: <c>0</c> — ruxsat; <c>&gt;0</c> — necha
    /// millisekunddan keyin bitta token tayyor bo'ladi.
    /// </summary>
    private const string TokenBucketScript =
        """
        local capacity = tonumber(ARGV[1])
        local refillPerSecond = tonumber(ARGV[2])
        local ttlMs = tonumber(ARGV[3])

        local time = redis.call('TIME')
        local nowMs = (tonumber(time[1]) * 1000) + math.floor(tonumber(time[2]) / 1000)

        local bucket = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(bucket[1])
        local ts = tonumber(bucket[2])

        if tokens == nil or ts == nil then
            tokens = capacity
            ts = nowMs
        end

        local elapsed = nowMs - ts
        if elapsed < 0 then elapsed = 0 end

        tokens = math.min(capacity, tokens + (elapsed * refillPerSecond / 1000))

        local waitMs = 0
        if tokens >= 1 then
            tokens = tokens - 1
        else
            waitMs = math.ceil(((1 - tokens) * 1000) / refillPerSecond)
            if waitMs < 1 then waitMs = 1 end
        end

        redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', nowMs)
        redis.call('PEXPIRE', KEYS[1], ttlMs)

        return waitMs
        """;

    /// <summary>Telegram bot uchun rasmiy global chegara ~30 xabar/sekund.</summary>
    public const int DefaultPermitsPerSecond = 25;

    /// <summary>Bir zumda ruxsat etilgan eng katta portlash.</summary>
    public const int DefaultBurst = 30;

    private readonly IDatabase _db;
    private readonly string _prefix;
    private readonly int _permitsPerSecond;
    private readonly int _burst;

    /// <summary>
    /// Kalitning yashash muddati: chelak to'lgach yozuv keraksiz bo'ladi
    /// (keyingi so'rovda u baribir "to'la" deb qayta yaratiladi).
    /// To'lish vaqtidan bir necha barobar uzun olingan.
    /// </summary>
    private readonly TimeSpan _ttl;

    public RedisMessageRateLimiter(
        IConnectionMultiplexer redis,
        string? keyPrefix = null,
        int permitsPerSecond = DefaultPermitsPerSecond,
        int burst = DefaultBurst)
    {
        ArgumentNullException.ThrowIfNull(redis);

        _db = redis.GetDatabase();
        _prefix = string.IsNullOrWhiteSpace(keyPrefix)
            ? RedisCacheService.DefaultPrefix
            : keyPrefix.Trim();

        // Buzuq konfiguratsiya butun navbatni to'xtatib qo'ymasin: 0 yoki
        // manfiy tezlikda hech qachon token bo'lmasdi va xabarlar mangu
        // kutardi. Yuqori chegara ham bor — Telegram baribir 30/s dan
        // ko'pini qabul qilmaydi.
        _permitsPerSecond = Math.Clamp(permitsPerSecond, 1, 1000);
        _burst = Math.Clamp(burst, 1, 10_000);

        _ttl = TimeSpan.FromSeconds(Math.Max(10d, (double)_burst / _permitsPerSecond * 10));
    }

    /// <inheritdoc />
    public async Task<RateLimitDecision> TryAcquireAsync(
        NotificationChannel channel, CancellationToken ct = default)
    {
        // StackExchange.Redis API'si `CancellationToken` qabul qilmaydi
        // (buyruq allaqachon socket'ga yozilgan bo'lishi mumkin), shuning
        // uchun token faqat chaqiruvdan OLDIN tekshiriladi —
        // `RedisCacheService` dagi bilan bir xil kelishuv.
        ct.ThrowIfCancellationRequested();

        var keys = new RedisKey[] { Key(channel) };

        var values = new RedisValue[]
        {
            _burst,
            _permitsPerSecond,
            (long)_ttl.TotalMilliseconds,
        };

        var result = await _db
            .ScriptEvaluateAsync(TokenBucketScript, keys, values)
            .ConfigureAwait(false);

        var waitMs = (long)result;

        return waitMs <= 0
            ? RateLimitDecision.Pass
            : new RateLimitDecision(Allowed: false, TimeSpan.FromMilliseconds(waitMs));
    }

    /// <summary>Chegara HAR KANAL uchun alohida: Telegram limiti SMS limitiga daxlsiz.</summary>
    private string Key(NotificationChannel channel) =>
        string.Create(CultureInfo.InvariantCulture, $"{_prefix}:notify:ratelimit:{channel}");
}
