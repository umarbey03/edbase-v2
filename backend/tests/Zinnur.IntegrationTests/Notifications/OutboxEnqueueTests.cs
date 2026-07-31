using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.IntegrationTests.Notifications;

/// <summary>
/// Navbatga YOZISH: idempotentlik va commit-then-send.
///
/// ★ ESKI TIZIMNING XATOSI shu yerda qulflanadi. U yerda xabar avval
/// yuborilib, keyin bazaga yozilardi: server qayta ishga tushsa yoki
/// tranzaksiya orqaga qaytsa, o'quvchi BEKOR QILINGAN dars haqida xabar
/// olardi. Bu yerdagi testlar tartibni teskari — avval kommit, keyin
/// yuborish — qilib mahkamlaydi: kimdir kelajakda qatorlarni almashtirsa
/// test yiqiladi.
/// </summary>
public sealed class OutboxEnqueueTests(OutboxFactory factory) : IClassFixture<OutboxFactory>
{
    // ------------------------------------------------------------ idempotentlik

    /// <summary>
    /// ★ BIR TRANZAKSIYADA bir kalit ikki marta: ikkinchisi qo'shilmaydi.
    ///
    /// Bunsiz `SaveChanges` unikal indeksga urilib, BUTUN biznes
    /// tranzaksiyasini yiqitardi — ya'ni "ikkinchi xabar" xatosi tufayli
    /// asosiy amal ham bajarilmay qolardi.
    /// </summary>
    [Fact]
    public async Task Enqueue_SameKeyTwiceInOneTransaction_WritesSingleRow()
    {
        var key = NewKey();

        var (first, second) = await factory.WithScopeAsync(async sp =>
        {
            var outbox = sp.GetRequiredService<INotificationOutbox>();
            var db = sp.GetRequiredService<ApplicationDbContext>();

            var a = await outbox.EnqueueAsync(OutboxFactory.Request(key));
            var b = await outbox.EnqueueAsync(OutboxFactory.Request(key));

            await db.SaveChangesAsync();

            return (a, b);
        });

        first.Should().BeTrue();
        second.Should().BeFalse("bir xil kalitli ikkinchi xabar navbatga tushmasligi kerak");

        (await CountAsync(key)).Should().Be(1);
    }

    /// <summary>
    /// ★ KEYINGI so'rovda (alohida tranzaksiyada) ham takror yozilmaydi.
    /// Aynan shu holat eslatmalarni hisoblovchi fon vazifasi qayta ishga
    /// tushganda yuz beradi.
    /// </summary>
    [Fact]
    public async Task Enqueue_SameKeyInAnotherTransaction_IsRejected()
    {
        var key = NewKey();

        (await EnqueueAndSaveAsync(key)).Should().BeTrue();
        (await EnqueueAndSaveAsync(key)).Should().BeFalse();

        (await CountAsync(key)).Should().Be(1);
    }

    /// <summary>
    /// ★ OXIRGI TO'SIQ — UNIKAL INDEKS.
    ///
    /// Koddagi tekshiruv ikki instance orasidagi poygada ishlamaydi:
    /// ikkalasi ham "yo'q ekan" deb ko'rib, ikkalasi ham yozadi. Bu test
    /// tekshiruvni CHETLAB O'TIB (to'g'ridan-to'g'ri qator qo'shib),
    /// himoya BAZA darajasida ham borligini isbotlaydi.
    /// </summary>
    [Fact]
    public async Task DuplicateKey_BypassingTheServiceCheck_IsRejectedByUniqueIndex()
    {
        var key = NewKey();
        await EnqueueAndSaveAsync(key);

        var act = async () => await factory.WithDbAsync(async db =>
        {
            db.MessageOutbox.Add(new MessageOutbox
            {
                Channel = NotificationChannel.Telegram,
                RecipientAddress = "123456789",
                TemplateKey = "test_message",
                Body = "Ikkinchi nusxa",
                IdempotencyKey = key,
                NextAttemptAt = DateTimeOffset.UtcNow,
            });

            return await db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateException>(
            "takror kalitni baza rad etishi kerak");
    }

    // ------------------------------------------------------------ commit-then-send

    /// <summary>
    /// ★ KOMMITGACHA XABAR KO'RINMAYDI.
    ///
    /// Worker navbatni ALOHIDA ulanishdan o'qiydi. Agar yozuv `SaveChanges`
    /// dan oldin ko'rinadigan bo'lsa, xabar tranzaksiya bekor qilinishidan
    /// oldin yuborilib ketishi mumkin edi — bu aynan eski tizimdagi
    /// "bo'lmagan hodisa haqida xabar" holati.
    /// </summary>
    [Fact]
    public async Task Enqueue_BeforeSaveChanges_IsInvisibleToOtherConnections()
    {
        var key = NewKey();

        using var scope = factory.Services.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await outbox.EnqueueAsync(OutboxFactory.Request(key));

        (await factory.FindAsync(key)).Should().BeNull(
            "yozuv hali kommit qilinmagan — boshqa ulanish uni ko'rmasligi kerak");

        await db.SaveChangesAsync();

        (await factory.FindAsync(key)).Should().NotBeNull("kommitdan keyin ko'rinadi");
    }

    /// <summary>
    /// ★ BITTA SaveChanges — ikkalasi ham saqlanadi.
    /// Biznes o'zgarishi (jonli dars) va xabar AYNI kuzatuvchida to'planib,
    /// bitta tranzaksiyada yoziladi.
    /// </summary>
    [Fact]
    public async Task Enqueue_WithBusinessChange_IsSavedTogether()
    {
        var key = NewKey();

        var sessionId = await factory.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var outbox = sp.GetRequiredService<INotificationOutbox>();

            var session = await NewSessionAsync(db);
            db.LiveSessions.Add(session);

            await outbox.EnqueueAsync(OutboxFactory.Request(key));

            // ★ YAGONA saqlash — ikkalasi bitta tranzaksiyada.
            await db.SaveChangesAsync();

            return session.Id;
        });

        (await factory.FindAsync(key)).Should().NotBeNull();

        var sessionExists = await factory.WithDbAsync(db =>
            db.LiveSessions.AnyAsync(s => s.Id == sessionId));

        sessionExists.Should().BeTrue();
    }

    /// <summary>
    /// ★ ENG MUHIM TEST: tranzaksiya ORQAGA QAYTSA xabar ham qolmaydi.
    ///
    /// Eski tizimda xabar allaqachon yuborilgan bo'lardi va uni "qaytarib
    /// olish" imkonsiz edi. Bu yerda esa yarim holat MUMKIN EMAS: yo
    /// ikkalasi ham bor, yo ikkalasi ham yo'q.
    /// </summary>
    [Fact]
    public async Task Enqueue_WhenTransactionRollsBack_LeavesNothing()
    {
        var key = NewKey();

        var sessionId = await factory.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var outbox = sp.GetRequiredService<INotificationOutbox>();

            long createdId = 0;

            // `EnableRetryOnFailure` yoqilgani uchun qo'lda ochilgan
            // tranzaksiya ijro strategiyasi ichida bo'lishi SHART —
            // aks holda EF "user-initiated transaction" deb rad etadi.
            var strategy = db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();

                var session = await NewSessionAsync(db);
                db.LiveSessions.Add(session);

                await outbox.EnqueueAsync(OutboxFactory.Request(key));
                await db.SaveChangesAsync();

                createdId = session.Id;

                // Biznes amali yiqildi — hamma narsa qaytadi.
                await transaction.RollbackAsync();
            });

            return createdId;
        });

        (await factory.FindAsync(key)).Should().BeNull(
            "tranzaksiya qaytgach navbatda xabar QOLMASLIGI kerak");

        var sessionExists = await factory.WithDbAsync(db =>
            db.LiveSessions.AnyAsync(s => s.Id == sessionId));

        sessionExists.Should().BeFalse("biznes yozuvi ham qaytgan bo'lishi kerak");
    }

    // ------------------------------------------------------------ tekshiruvlar

    /// <summary>Qabul qiluvchisiz xabar navbatga tushmaydi (dasturchi xatosi — 500).</summary>
    [Fact]
    public async Task Enqueue_WithoutRecipient_Throws()
    {
        var act = async () => await factory.WithScopeAsync(sp =>
            sp.GetRequiredService<INotificationOutbox>().EnqueueAsync(
                OutboxFactory.Request(NewKey()) with { RecipientAddress = null }));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Chegaradan uzun matn RAD ETILADI (kesilmaydi): kesilgan tayyor matn
    /// ochiq qolgan HTML tegi bilan Telegram uchun yaroqsiz bo'lardi.
    /// </summary>
    [Fact]
    public async Task Enqueue_WithTooLongBody_Throws()
    {
        var act = async () => await factory.WithScopeAsync(sp =>
            sp.GetRequiredService<INotificationOutbox>().EnqueueAsync(
                OutboxFactory.Request(NewKey(), new string('x', NotificationText.MaxBodyLength + 1))));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------------ yordamchi

    private static string NewKey() => $"test:{Guid.NewGuid():N}";

    private Task<bool> EnqueueAndSaveAsync(string key) =>
        factory.WithScopeAsync(async sp =>
        {
            var outbox = sp.GetRequiredService<INotificationOutbox>();
            var db = sp.GetRequiredService<ApplicationDbContext>();

            var added = await outbox.EnqueueAsync(OutboxFactory.Request(key));
            await db.SaveChangesAsync();

            return added;
        });

    private Task<int> CountAsync(string key) =>
        factory.WithDbAsync(db => db.MessageOutbox.CountAsync(m => m.IdempotencyKey == key));

    private static async Task<LiveSession> NewSessionAsync(ApplicationDbContext db)
    {
        var groupId = await db.Groups.Select(g => g.Id).FirstAsync();
        var now = DateTimeOffset.UtcNow;

        return new LiveSession
        {
            GroupId = groupId,
            Title = "Navbat testi",
            Type = SessionType.Teacher,
            Status = SessionStatus.Scheduled,
            ScheduledStart = now,
            ScheduledEnd = now.AddMinutes(80),
            RoomName = LiveSession.GenerateRoomName(),
        };
    }
}
