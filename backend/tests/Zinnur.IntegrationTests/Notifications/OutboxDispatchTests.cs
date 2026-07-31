using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.IntegrationTests.Notifications;

/// <summary>
/// Navbatni YUBORISH: holat o'tishlari, backoff va commit-then-send.
///
/// Fon worker'i o'chirilgan (sabab <see cref="OutboxFactory"/> da) — aylanish
/// shu yerdan chaqiriladi va natija darhol tekshiriladi.
/// </summary>
public sealed class OutboxDispatchTests(OutboxFactory factory) : IClassFixture<OutboxFactory>
{
    [Fact]
    public async Task Dispatch_DeliversPendingMessage_AndMarksSent()
    {
        factory.Spy.Reset();

        var key = NewKey();
        await EnqueueAsync(key);

        var result = await factory.DispatchAsync();

        result.Delivered.Should().BeGreaterThanOrEqualTo(1);

        var row = await factory.FindAsync(key);

        row.Should().NotBeNull();
        row!.Status.Should().Be(OutboxStatus.Sent);
        row.SentAt.Should().NotBeNull();
        row.AttemptCount.Should().Be(0, "muvaffaqiyatli yuborish urinish sarflamaydi");
    }

    /// <summary>Yuborilgan xabar IKKINCHI marta olinmaydi.</summary>
    [Fact]
    public async Task Dispatch_TwiceForSameMessage_SendsOnlyOnce()
    {
        factory.Spy.Reset();

        var key = NewKey();
        var id = await EnqueueAsync(key);

        await factory.DispatchAsync();
        await factory.DispatchAsync();

        factory.Spy.Sent.Count(m => m.Id == id).Should().Be(1);
    }

    // ------------------------------------------------------------ commit-then-send

    /// <summary>
    /// ★ XABAR YUBORILAYOTGAN PAYTDA yozuv BAZADA (kommit qilingan) bo'ladi.
    ///
    /// Josus holatni YANGI scope'dan — boshqa ulanishdan — o'qiydi. Demak
    /// ko'rilgan narsa haqiqatan kommit bo'lgan ma'lumot. Bu tartib
    /// teskari bo'lsa (send-then-commit), josus umuman qator topa olmasdi.
    /// </summary>
    [Fact]
    public async Task Dispatch_AtSendTime_MessageIsAlreadyCommitted()
    {
        factory.Spy.Reset();

        var key = NewKey();
        var id = await EnqueueAsync(key);

        await factory.DispatchAsync();

        factory.Spy.StateWhenSent.Should().ContainKey(id);

        var snapshot = factory.Spy.StateWhenSent[id];

        snapshot.Status.Should().Be(OutboxStatus.Pending,
            "yuborish payti yozuv hali `Pending` — `Sent` faqat kanal qabul qilgach yoziladi");
        snapshot.SentAt.Should().BeNull();
    }

    /// <summary>
    /// ★ BIZNES O'ZGARISHI ham yuborish paytida ALLAQACHON bazada bo'ladi.
    ///
    /// Eski tizimda aksincha edi: xabar avval ketardi va saqlash yiqilsa
    /// o'quvchi bo'lmagan hodisa haqida xabar olardi.
    /// </summary>
    [Fact]
    public async Task Dispatch_AtSendTime_BusinessChangeIsAlreadyCommitted()
    {
        factory.Spy.Reset();

        var key = NewKey();

        var sessionId = await factory.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var outbox = sp.GetRequiredService<INotificationOutbox>();

            var groupId = await db.Groups.Select(g => g.Id).FirstAsync();
            var now = DateTimeOffset.UtcNow;

            var session = new LiveSession
            {
                GroupId = groupId,
                Title = "Navbat testi",
                Type = SessionType.Teacher,
                Status = SessionStatus.Scheduled,
                ScheduledStart = now,
                ScheduledEnd = now.AddMinutes(80),
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);

            await outbox.EnqueueAsync(OutboxFactory.Request(key));
            await db.SaveChangesAsync();

            return session.Id;
        });

        var seen = false;

        factory.Spy.Inspect = async (_, db) =>
            seen = await db.LiveSessions.AnyAsync(s => s.Id == sessionId);

        await factory.DispatchAsync();

        seen.Should().BeTrue(
            "xabar yuborilayotganda unga sabab bo'lgan o'zgarish bazada bo'lishi SHART");
    }

    // ------------------------------------------------------------ backoff

    /// <summary>
    /// ★ VAQTINCHALIK XATO: xabar `Pending` qoladi, urinish oshadi va
    /// keyingi urinish JADVAL bo'yicha (1 daqiqa) kelajakka suriladi.
    /// </summary>
    [Fact]
    public async Task Dispatch_WhenSendFails_SchedulesRetryWithBackoff()
    {
        factory.Spy.Reset();
        factory.Spy.Behavior = _ => MessageSendResult.Retry("Telegram javob bermadi");

        var key = NewKey();
        await EnqueueAsync(key);

        var before = DateTimeOffset.UtcNow;
        var result = await factory.DispatchAsync();

        result.Rejected.Should().BeGreaterThanOrEqualTo(1);

        var row = await factory.FindAsync(key);

        row!.Status.Should().Be(OutboxStatus.Pending);
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Contain("Telegram");

        // Birinchi yiqilishdan keyin 1 daqiqa (OutboxRetryPolicy).
        row.NextAttemptAt.Should().BeCloseTo(
            before.AddMinutes(1), TimeSpan.FromSeconds(30));
    }

    /// <summary>Keyingi urinish vaqti kelmagan xabar OLINMAYDI.</summary>
    [Fact]
    public async Task Dispatch_BeforeNextAttemptTime_SkipsMessage()
    {
        factory.Spy.Reset();
        factory.Spy.Behavior = _ => MessageSendResult.Retry("vaqtinchalik");

        var key = NewKey();
        var id = await EnqueueAsync(key);

        await factory.DispatchAsync();      // 1-urinish yiqildi, +1 daqiqaga surildi

        factory.Spy.Reset();
        await factory.DispatchAsync();      // darhol yana aylantiramiz

        factory.Spy.Sent.Should().NotContain(m => m.Id == id,
            "kutish muddati tugamaguncha xabar qayta olinmasligi kerak");
    }

    /// <summary>
    /// ★ ZAHARLI XABAR (poison message) navbatni ABADIY band qilmaydi:
    /// urinishlar tugagach yozuv `Failed` ga o'tadi va boshqa olinmaydi.
    /// </summary>
    [Fact]
    public async Task Dispatch_AfterAllAttempts_MarksFailedAndStopsTrying()
    {
        factory.Spy.Reset();
        factory.Spy.Behavior = _ => MessageSendResult.Retry("xizmat javob bermayapti");

        var key = NewKey();
        var id = await EnqueueAsync(key);

        for (var attempt = 0; attempt < OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            await factory.DispatchAsync();

            // "Vaqt o'tdi" — kutish muddatini yopamiz, aks holda test
            // haqiqiy 1+5+15+60 daqiqani kutishi kerak bo'lardi.
            await MakeDueAsync(key);
        }

        var row = await factory.FindAsync(key);

        row!.Status.Should().Be(OutboxStatus.Failed);
        row.AttemptCount.Should().Be(OutboxRetryPolicy.MaxAttempts);

        var sentBefore = factory.Spy.Sent.Count(m => m.Id == id);

        await factory.DispatchAsync();

        factory.Spy.Sent.Count(m => m.Id == id).Should().Be(sentBefore,
            "`Failed` holatidagi xabar boshqa olinmaydi");
    }

    /// <summary>
    /// QAYTARIB BO'LMAYDIGAN xato (bot bloklangan, chat topilmadi) —
    /// darhol `Failed`. Bunday xabar uchun 5 marta urinish Telegram
    /// chegarasini bekorga yeyishdan boshqa narsa bermasdi.
    /// </summary>
    [Fact]
    public async Task Dispatch_WhenErrorIsPermanent_FailsImmediately()
    {
        factory.Spy.Reset();
        factory.Spy.Behavior = _ => MessageSendResult.Permanent("bot bloklangan");

        var key = NewKey();
        await EnqueueAsync(key);

        await factory.DispatchAsync();

        var row = await factory.FindAsync(key);

        row!.Status.Should().Be(OutboxStatus.Failed);
        row.AttemptCount.Should().Be(1, "bitta urinish yetarli — qaytarish ma'nosiz");
        row.LastError.Should().Contain("bloklangan");
    }

    /// <summary>
    /// Yuboruvchi port shartnomasini buzib istisno tashlasa ham navbat
    /// TO'XTAMAYDI: xato vaqtinchalik deb qayd etiladi.
    /// </summary>
    [Fact]
    public async Task Dispatch_WhenSenderThrows_TreatsItAsTemporaryFailure()
    {
        factory.Spy.Reset();
        factory.Spy.Behavior = _ => throw new InvalidOperationException("kutilmagan nosozlik");

        var key = NewKey();
        await EnqueueAsync(key);

        var act = async () => await factory.DispatchAsync();

        await act.Should().NotThrowAsync();

        var row = await factory.FindAsync(key);

        row!.Status.Should().Be(OutboxStatus.Pending);
        row.AttemptCount.Should().Be(1);
    }

    // ------------------------------------------------------------ yordamchi

    private static string NewKey() => $"dispatch:{Guid.NewGuid():N}";

    /// <summary>Xabarni navbatga yozadi va uning id'sini qaytaradi.</summary>
    private async Task<long> EnqueueAsync(string key)
    {
        await factory.WithScopeAsync(async sp =>
        {
            var outbox = sp.GetRequiredService<INotificationOutbox>();
            var db = sp.GetRequiredService<ApplicationDbContext>();

            await outbox.EnqueueAsync(OutboxFactory.Request(key));

            return await db.SaveChangesAsync();
        });

        var row = await factory.FindAsync(key);

        return row!.Id;
    }

    /// <summary>Kutish muddatini yopadi ("vaqt o'tdi" degan faraz).</summary>
    private Task<int> MakeDueAsync(string key) =>
        factory.WithDbAsync(db => db.MessageOutbox
            .Where(m => m.IdempotencyKey == key && m.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.NextAttemptAt, DateTimeOffset.UtcNow)));
}
