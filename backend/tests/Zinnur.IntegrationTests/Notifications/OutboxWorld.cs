using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Notifications;

/// <summary>
/// Notifikatsiya navbati uchun umumiy fixture.
///
/// ── NIMA UCHUN FON WORKER'I O'CHIRILGAN ────────────────────────────────
/// <c>Notifications:Enabled=false</c>: testlar aylanishni O'ZI chaqiradi va
/// natijani darhol tekshiradi. Worker parallel ishlab tursa, u test
/// yozgan xabarni "o'g'irlab" yuborishi mumkin edi va test tasodifiy
/// (flaky) bo'lardi — "gohida o'tadi, gohida yo'q" degan eng yomon holat.
/// Worker'ning O'ZI yupqa sikl, uning mantiqi <c>IOutboxDispatcher</c> da
/// va aynan shu yerda sinaladi.
///
/// ── NIMA UCHUN HAQIQIY YUBORUVCHI EMAS, JOSUS ──────────────────────────
/// Tekshiriladigan narsa transport emas, NAVBAT XATTI-HARAKATI: qachon
/// yuborishga buyuriladi, qanday holatda buyuriladi va yiqilganda nima
/// bo'ladi. Josus har chaqiruvda BAZANI YANGI scope'dan o'qiydi — bu
/// commit-then-send ning bevosita dalili
/// (<c>LiveSessionEndBroadcastTests</c> dagi bilan bir xil uslub).
/// </summary>
public class OutboxFactory : ZinnurApiFactory
{
    public MessageSenderSpy Spy { get; } = new();

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Notifications:Enabled", "false"),

        // Tezlik chegarasi bu sinflarda YO'LDAN OLINGAN (chegaraning o'zini
        // `OutboxRateLimitTests` tekshiradi). Aks holda har test Redis'dagi
        // umumiy chelakka bog'lanib qolardi.
        new("Notifications:RateLimit:PerSecond", "1000"),
        new("Notifications:RateLimit:Burst", "1000"),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            // Haqiqiy (log yozuvchi) yuboruvchini OLIB TASHLAYMIZ — aks holda
            // qaysi biri ishlashini ro'yxat tartibi hal qilardi.
            services.RemoveAll<IMessageSender>();

            services.AddSingleton(sp =>
            {
                Spy.UseScopes(sp.GetRequiredService<IServiceScopeFactory>());
                return Spy;
            });

            services.AddSingleton<IMessageSender>(sp => sp.GetRequiredService<MessageSenderSpy>());
        });
    }

    /// <summary>Scope ichida navbat servislari bilan ishlash.</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Bitta aylanishni chaqiradi (worker o'rniga).</summary>
    public Task<OutboxDispatchResult> DispatchAsync(int batchSize = 50) =>
        WithScopeAsync(sp => sp.GetRequiredService<IOutboxDispatcher>()
            .DispatchAsync(batchSize, TimeSpan.FromMinutes(2)));

    /// <summary>Kalit bo'yicha navbat qatorini yangi kontekstdan o'qiydi.</summary>
    public Task<MessageOutbox?> FindAsync(string idempotencyKey) =>
        WithDbAsync(db => db.MessageOutbox
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.IdempotencyKey == idempotencyKey));

    /// <summary>Testlar uchun tayyor so'rov (kalit har chaqiruvda yangi).</summary>
    public static NotificationRequest Request(string idempotencyKey, string body = "Sinov xabari") =>
        new()
        {
            Channel = NotificationChannel.Telegram,
            RecipientAddress = "123456789",
            TemplateKey = "test_message",
            Body = body,
            IdempotencyKey = idempotencyKey,
        };
}

/// <summary>
/// Yuborish chaqiruvlarini va chaqirilgan PAYTDAGI baza holatini yozib
/// boradigan josus.
/// </summary>
public sealed class MessageSenderSpy : IMessageSender
{
    private IServiceScopeFactory? _scopeFactory;

    internal void UseScopes(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Telegram;

    /// <summary>Yuborishga berilgan xabarlar (tartib bilan).</summary>
    public ConcurrentQueue<OutboxMessage> Sent { get; } = new();

    /// <summary>
    /// Xabar yuborilgan PAYTDA bazada ko'ringan holat — commit-then-send
    /// dalili. Yangi scope'dan o'qiladi, ya'ni bu haqiqatan KOMMIT
    /// bo'lgan ma'lumot, so'rovning o'z keshi emas.
    /// </summary>
    public ConcurrentDictionary<long, OutboxSnapshot> StateWhenSent { get; } = new();

    /// <summary>Javobni test boshqaradi (standart — muvaffaqiyat).</summary>
    public Func<OutboxMessage, MessageSendResult> Behavior { get; set; } = _ => MessageSendResult.Ok;

    /// <summary>
    /// Xabar yuborilayotgan PAYTDA bazani tekshirish uchun ilgak.
    /// Yangi scope'ning konteksti beriladi — ya'ni ko'rilgan narsa
    /// haqiqatan KOMMIT bo'lgan ma'lumot.
    /// </summary>
    public Func<OutboxMessage, ApplicationDbContext, Task>? Inspect { get; set; }

    /// <summary>Har test o'z holatidan boshlasin (fixture sinf bo'ylab umumiy).</summary>
    public void Reset()
    {
        Sent.Clear();
        StateWhenSent.Clear();
        Behavior = _ => MessageSendResult.Ok;
        Inspect = null;
    }

    public async Task<MessageSendResult> SendAsync(
        OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Sent.Enqueue(message);

        if (_scopeFactory is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var row = await db.MessageOutbox
                .AsNoTracking()
                .Where(m => m.Id == message.Id)
                .Select(m => new OutboxSnapshot(m.Status, m.AttemptCount, m.SentAt))
                .FirstOrDefaultAsync(ct);

            if (row is not null)
                StateWhenSent[message.Id] = row;

            if (Inspect is not null)
                await Inspect(message, db);
        }

        return Behavior(message);
    }
}

/// <summary>Xabar yuborilayotgan paytdagi baza holati.</summary>
public sealed record OutboxSnapshot(OutboxStatus Status, int AttemptCount, DateTimeOffset? SentAt);
