using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Persistence;
using Zinnur.Infrastructure.Services;

namespace Zinnur.IntegrationTests.Notifications;

/// <summary>
/// TEZLIK CHEGARASI — Redis'dagi token bucket.
///
/// ★ NIMA UCHUN HAQIQIY REDIS: chegaraning butun ma'nosi shundaki, u
/// JARAYONDAN TASHQARIDA turadi. Xotiradagi soxta (fake) bilan sinalgan
/// test aynan tekshirilishi kerak bo'lgan narsani — ikki instance bitta
/// chelakdan ichishini — isbotlay olmasdi.
/// </summary>
public sealed class OutboxRateLimitTests(ThrottledOutboxFactory factory)
    : IClassFixture<ThrottledOutboxFactory>
{
    /// <summary>Portlash budjeti tugagach rad etiladi va kutish vaqti beriladi.</summary>
    [Fact]
    public async Task TokenBucket_AllowsBurstThenDenies()
    {
        var limiter = NewLimiter(permitsPerSecond: 1, burst: 3);

        for (var i = 0; i < 3; i++)
        {
            var granted = await limiter.TryAcquireAsync(NotificationChannel.Telegram);
            granted.Allowed.Should().BeTrue($"{i + 1}-xabar portlash budjetiga sig'adi");
        }

        var denied = await limiter.TryAcquireAsync(NotificationChannel.Telegram);

        denied.Allowed.Should().BeFalse();
        denied.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero,
            "worker qancha kutishni bilishi kerak");
    }

    /// <summary>
    /// ★ ENG MUHIM TEST: IKKI INSTANCE bitta chelakdan ichadi.
    ///
    /// Ikkita alohida limiter obyekti — bu ikki API konteyneri modeli.
    /// Chegara jarayon xotirasida bo'lganda ikkalasi ham to'liq budjetga
    /// ega bo'lardi va Telegram'ning 30/s chegarasi jimgina ikki barobar
    /// oshib ketardi (429 va vaqtincha bloklash bilan tugaydi).
    /// </summary>
    [Fact]
    public async Task TokenBucket_IsSharedBetweenInstances()
    {
        var prefix = NewPrefix();

        var first = NewLimiter(permitsPerSecond: 1, burst: 2, prefix);
        var second = NewLimiter(permitsPerSecond: 1, burst: 2, prefix);

        (await first.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeTrue();
        (await second.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeTrue();

        // Budjet UMUMIY — uchinchi so'rov qaysi instance'dan kelishidan
        // qat'i nazar rad etiladi.
        (await first.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeFalse();
        (await second.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeFalse();
    }

    /// <summary>
    /// ★ KALIT MAKONI (<c>Redis:KeyPrefix</c>) hurmat qilinadi: bitta
    /// Redis'ni ikki muhit baham ko'rsa hisoblagichlar ARALASHMAYDI.
    /// (O'tgan sessiyada aynan makonsiz kalit tufayli 9 test yiqilgan.)
    /// </summary>
    [Fact]
    public async Task TokenBucket_WithDifferentKeyPrefixes_IsIndependent()
    {
        var production = NewLimiter(permitsPerSecond: 1, burst: 1, NewPrefix());
        var staging = NewLimiter(permitsPerSecond: 1, burst: 1, NewPrefix());

        (await production.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeTrue();
        (await production.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeFalse();

        (await staging.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeTrue(
            "boshqa makondagi chegara birinchisidan mustaqil bo'lishi kerak");
    }

    /// <summary>Chelak vaqt o'tishi bilan TO'LADI (qat'iy oyna emas).</summary>
    [Fact]
    public async Task TokenBucket_RefillsOverTime()
    {
        // 100 token/sekund = har 10 ms da bitta.
        var limiter = NewLimiter(permitsPerSecond: 100, burst: 1);

        (await limiter.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeTrue();
        (await limiter.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeFalse();

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        (await limiter.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeTrue(
            "kutilgandan keyin token qayta tiklanishi kerak");
    }

    /// <summary>Har kanal o'z chelagi bilan — Telegram limiti boshqasiga daxlsiz.</summary>
    [Fact]
    public async Task TokenBucket_IsPerChannel()
    {
        var limiter = NewLimiter(permitsPerSecond: 1, burst: 1);

        (await limiter.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeTrue();
        (await limiter.TryAcquireAsync(NotificationChannel.Telegram)).Allowed.Should().BeFalse();
    }

    // ------------------------------------------------------------ navbat bilan birga

    /// <summary>
    /// ★ CHEGARAGA URILGAN XABAR URINISH SARFLAMAYDI.
    ///
    /// Fixture chegarasi 1 xabar/sekund: birinchi xabar ketadi, qolganlari
    /// KEYINGA SURILADI. Ular `Pending` bo'lib qoladi, hisoblagichi 0 —
    /// ya'ni "yuborilmadi" deb hisoblanmaydi. Aks holda uzilish paytida
    /// xabarlar yuborilmasdan turib `Failed` bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Dispatch_WhenRateLimitIsExhausted_PostponesWithoutSpendingAttempts()
    {
        factory.Spy.Reset();

        var keys = new[] { NewKey(), NewKey(), NewKey() };

        foreach (var key in keys)
            await EnqueueAsync(key);

        var result = await factory.DispatchAsync();

        result.Delivered.Should().BeGreaterThanOrEqualTo(1);
        result.Postponed.Should().BeGreaterThanOrEqualTo(1,
            "chegara to'lgach qolgan xabarlar keyinga surilishi kerak");

        var postponed = new List<MessageOutbox>();

        foreach (var key in keys)
        {
            var row = await factory.FindAsync(key);
            if (row!.Status == OutboxStatus.Pending) postponed.Add(row);
        }

        postponed.Should().NotBeEmpty();
        postponed.Should().OnlyContain(m => m.AttemptCount == 0,
            "chegara sababli kutish — bu urinish emas");
    }

    // ------------------------------------------------------------ yordamchi

    private static string NewKey() => $"ratelimit:{Guid.NewGuid():N}";

    private static string NewPrefix() => $"rl-{Guid.NewGuid():N}"[..12];

    private RedisMessageRateLimiter NewLimiter(
        int permitsPerSecond, int burst, string? prefix = null) =>
        new(
            factory.Services.GetRequiredService<IConnectionMultiplexer>(),
            prefix ?? NewPrefix(),
            permitsPerSecond,
            burst);

    private Task<int> EnqueueAsync(string key) =>
        factory.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<INotificationOutbox>()
                .EnqueueAsync(OutboxFactory.Request(key));

            return await sp.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        });
}

/// <summary>
/// Chegarasi ATAYLAB juda past API (1 xabar/sekund) — navbat chegaraga
/// urilganda nima qilishini tekshirish uchun.
/// </summary>
public sealed class ThrottledOutboxFactory : OutboxFactory
{
    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Notifications:Enabled", "false"),
        new("Notifications:RateLimit:PerSecond", "1"),
        new("Notifications:RateLimit:Burst", "1"),
    ];
}
