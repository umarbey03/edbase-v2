using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.IntegrationTests.Notifications;

/// <summary>
/// Navbatdan OLISH: <c>FOR UPDATE SKIP LOCKED</c> va ko'rinmaslik muddati.
///
/// ★ NIMA UCHUN BU ALOHIDA SINALADI: loyiha gorizontal masshtablanishga
/// tayyorlanmoqda, ya'ni API bir nechta konteynerda ishlaydi va HAR BIRIDA
/// worker bo'ladi. Ikki worker bitta qatorni olsa, o'quvchi bir xil
/// eslatmani ikki marta olardi — eski tizimning aynan o'sha muammosi,
/// faqat boshqa sababdan.
///
/// Bu yerda mock yo'q: HAQIQIY Postgres, haqiqiy parallel ulanishlar.
/// Qulflash xatti-harakatini boshqa yo'l bilan isbotlab bo'lmaydi.
/// </summary>
public sealed class OutboxClaimTests(OutboxFactory factory) : IClassFixture<OutboxFactory>
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>
    /// ★ ASOSIY TEST: to'rtta "worker" bir vaqtda navbatga tashlanadi va
    /// hech biri boshqasining xabarini olmaydi.
    /// </summary>
    [Fact]
    public async Task Claim_FromParallelWorkers_NeverReturnsTheSameRowTwice()
    {
        const int Workers = 4;
        const int PerWorker = 5;

        var keys = await SeedAsync(Workers * PerWorker);

        // Scope'lar OLDINDAN ochiladi: har "worker" o'z `DbContext` i va
        // o'z ulanishi bilan ishlashi shart, aks holda bu parallel emas,
        // bitta ulanishdagi ketma-ket so'rov bo'lardi.
        var scopes = Enumerable.Range(0, Workers)
            .Select(_ => factory.Services.CreateScope())
            .ToList();

        try
        {
            var stores = scopes
                .Select(s => s.ServiceProvider.GetRequiredService<IOutboxStore>())
                .ToList();

            var batches = await Task.WhenAll(
                stores.Select(store => Task.Run(() => store.ClaimAsync(PerWorker, Lease))));

            var claimed = batches.SelectMany(b => b).Select(m => m.Id).ToList();

            claimed.Should().OnlyHaveUniqueItems(
                "bitta xabar faqat bitta worker'ga tushishi kerak");

            // ★ HAR WORKER TO'LIQ ULUSHINI OLDI — hech kim kutmadi.
            // `SKIP LOCKED` bo'lmaganda ikkinchi worker birinchisining
            // qulfi bo'shashini KUTIB turardi va paketi bo'sh qaytardi
            // (yoki bir xil qatorlarni olib, xabar ikki marta ketardi).
            batches.Should().OnlyContain(batch => batch.Count == PerWorker);
            claimed.Should().HaveCount(keys.Count);
        }
        finally
        {
            foreach (var scope in scopes) scope.Dispose();
        }
    }

    /// <summary>
    /// Band qilingan qator muddat tugamaguncha QAYTA olinmaydi — worker
    /// yuborayotgan paytda ikkinchisi uni tortib olmasin.
    /// </summary>
    [Fact]
    public async Task Claim_LeasedRow_IsHiddenUntilLeaseExpires()
    {
        var keys = await SeedAsync(1);

        var first = await ClaimAsync(10);
        var second = await ClaimAsync(10);

        var id = await IdOfAsync(keys[0]);

        first.Should().Contain(m => m.Id == id);
        second.Should().NotContain(m => m.Id == id);
    }

    /// <summary>
    /// Rejalashtirilgan xabar (<c>SendAfter</c>) VAQTI KELMAGUNCHA olinmaydi.
    /// Bu 15-daqiqalik eslatma va ertalabki digest uchun asos.
    /// </summary>
    [Fact]
    public async Task Claim_WithFutureSendAfter_SkipsMessage()
    {
        var key = $"future:{Guid.NewGuid():N}";

        await factory.WithScopeAsync(async sp =>
        {
            var outbox = sp.GetRequiredService<INotificationOutbox>();
            var db = sp.GetRequiredService<ApplicationDbContext>();

            await outbox.EnqueueAsync(OutboxFactory.Request(key) with
            {
                SendAfter = DateTimeOffset.UtcNow.AddHours(1),
            });

            return await db.SaveChangesAsync();
        });

        var claimed = await ClaimAsync(50);
        var id = await IdOfAsync(key);

        claimed.Should().NotContain(m => m.Id == id);
    }

    /// <summary>Eng uzoq kutgan xabar birinchi olinadi (navbat — navbat).</summary>
    [Fact]
    public async Task Claim_ReturnsOldestWaitingFirst()
    {
        var now = DateTimeOffset.UtcNow;

        var oldKey = $"order-old:{Guid.NewGuid():N}";
        var newKey = $"order-new:{Guid.NewGuid():N}";

        await factory.WithDbAsync(async db =>
        {
            db.MessageOutbox.Add(Row(newKey, now.AddMinutes(-1)));
            db.MessageOutbox.Add(Row(oldKey, now.AddMinutes(-30)));

            return await db.SaveChangesAsync();
        });

        var claimed = await ClaimAsync(50);

        var oldId = await IdOfAsync(oldKey);
        var newId = await IdOfAsync(newKey);

        var oldIndex = claimed.ToList().FindIndex(m => m.Id == oldId);
        var newIndex = claimed.ToList().FindIndex(m => m.Id == newId);

        oldIndex.Should().BeGreaterThanOrEqualTo(0);
        newIndex.Should().BeGreaterThanOrEqualTo(0);
        oldIndex.Should().BeLessThan(newIndex, "eng uzoq kutgan xabar oldinroq turadi");
    }

    /// <summary>
    /// ★ OLISH URINISH SARFLAMAYDI: hisoblagich faqat YIQILGANDA oshadi.
    /// Aks holda tezlik chegarasi tufayli keyinga surilgan xabar bir necha
    /// aylanishda urinishlarini yeb, umuman yuborilmasdan `Failed` bo'lardi.
    /// </summary>
    [Fact]
    public async Task Claim_DoesNotSpendAnAttempt()
    {
        var keys = await SeedAsync(1);

        await ClaimAsync(10);

        var row = await factory.FindAsync(keys[0]);

        row!.AttemptCount.Should().Be(0);
        row.Status.Should().Be(OutboxStatus.Pending);
    }

    // ------------------------------------------------------------ yordamchi

    private Task<IReadOnlyList<OutboxMessage>> ClaimAsync(int batchSize) =>
        factory.WithScopeAsync(sp =>
            sp.GetRequiredService<IOutboxStore>().ClaimAsync(batchSize, Lease));

    private async Task<List<string>> SeedAsync(int count)
    {
        var now = DateTimeOffset.UtcNow;
        var keys = new List<string>(count);

        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                var key = $"claim:{Guid.NewGuid():N}";
                keys.Add(key);
                db.MessageOutbox.Add(Row(key, now));
            }

            return await db.SaveChangesAsync();
        });

        return keys;
    }

    private Task<long> IdOfAsync(string key) =>
        factory.WithDbAsync(db => db.MessageOutbox
            .Where(m => m.IdempotencyKey == key)
            .Select(m => m.Id)
            .FirstAsync());

    private static MessageOutbox Row(string key, DateTimeOffset nextAttemptAt) => new()
    {
        Channel = NotificationChannel.Telegram,
        RecipientAddress = "123456789",
        TemplateKey = "test_message",
        Body = "Sinov xabari",
        IdempotencyKey = key,
        NextAttemptAt = nextAttemptAt,
    };
}
