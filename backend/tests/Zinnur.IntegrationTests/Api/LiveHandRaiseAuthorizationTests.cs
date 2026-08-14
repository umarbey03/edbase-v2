using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.WebApi.Hubs;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// QO'L KO'TARISH — FAQAT O'QUVCHIDA (REGRESSIYA QULFI)
/// ========================================================================
///
/// ── QANDAY NOSOZLIKNI QO'RIQLAYDI ──────────────────────────────────────
///
/// Loyiha egasining talabi: "livechatda teacher is not needed to raise hand"
/// (R1). Tugma boshqaruv panelidan olib tashlandi, LEKIN faqat ko'rinish
/// qatlamidagi tuzatish YETARLI EMAS:
///
///  • ko'tarilgan qo'l Redis'dagi presence yozuviga TUSHADI va u yerda
///    qayta ulanishgacha qoladi;
///  • u xonadagi HAMMAGA tarqatiladi va har bir o'quvchining
///    "Qo'l ko'targanlar (N)" ro'yxatiga tushadi.
///
/// Ya'ni keshlangan eski klient yoki oddiy `curl` bilan yuborilgan BITTA
/// chaqiruv ustozning ismini butun sinfning ro'yxatida qoldirardi.
///
/// ── ★ NIMA UCHUN "O'QUVCHIMI?", "SHU DARSNING USTOZIMI?" EMAS ──────────
///
/// Qamrov ATAYLAB kengroq: qo'l ko'tarish — so'z SO'RASH signali va u
/// XODIMGA qaratilgan. O'quv bo'limi kuzatuvchisi (`Academic`) uni kimga
/// ko'taradi? Shuning uchun shart rol da'vosiga bog'landi, sessiya "host"
/// grantiga emas — buning ustiga host tekshiruvi har chaqiruvda bazaga
/// borishni talab qilardi (`LiveClassHub` sinf izohi, 5-qaror).
///
/// ── NIMA TEKSHIRILADI ──────────────────────────────────────────────────
///
/// Hub metodi SignalR quvurisiz, HAQIQIY servislar (Postgres + Redis) bilan
/// chaqiriladi — `HubErrorTranslationTests` dagi bilan bir xil yondashuv.
/// Har testda IKKI dalil tekshiriladi: tarqatish bo'lmagani VA presence
/// yozuvi o'zgarmagani. Faqat birinchisi tekshirilsa, Redis'da qolib
/// ketgan qo'l keyingi qo'shiluvchining to'liq ro'yxatida ko'rinardi.
/// </summary>
public sealed class LiveHandRaiseAuthorizationTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>Guruhning O'Z ustozi — talabning bevosita mazmuni.</summary>
    [Fact]
    public async Task RaiseHand_WhenTeacher_IsRejectedAndLeavesNoTrace()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lhrt");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new HandRecordingClients();
        using var hub = BuildHub(scope, world.Teacher.Id, nameof(UserRole.Teacher), clients);

        await hub.JoinSession(sessionId);

        Func<Task> act = async () => await hub.RaiseHand(sessionId, raised: true);

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.Message.Should().Contain("o'quvchilar",
            "SignalR faqat HubException matnini uzatadi — sabab o'qilishi kerak");

        clients.HandEvents.Should().BeEmpty("rad etilgan qo'l tarqatilmasligi kerak");

        (await HandRaisedInPresenceAsync(scope, sessionId, world.Teacher.Id))
            .Should().BeFalse("Redis'dagi yozuv ham o'zgarmasligi kerak");
    }

    /// <summary>
    /// O'quv bo'limi kuzatuvchisi — yuqoridagi QAMROV qarorining o'zi.
    ///
    /// ★ `Academic` har darsda "host" hisoblanadi
    /// (<c>LiveSessionService.IsHost</c>), ya'ni u darsga bemalol kiradi.
    /// Shart rol bo'yicha bo'lgani uchun u ham rad etiladi — bu test o'sha
    /// qarorni qulflaydi va kimdir shartni "faqat ustoz" ga toraytirsa
    /// qizaradi.
    /// </summary>
    [Fact]
    public async Task RaiseHand_WhenAcademicObserver_IsAlsoRejected()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lhra");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, "lhra");

        using var scope = factory.Services.CreateScope();
        var clients = new HandRecordingClients();
        using var hub = BuildHub(scope, academic.Id, nameof(UserRole.Academic), clients);

        await hub.JoinSession(sessionId);

        Func<Task> act = async () => await hub.RaiseHand(sessionId, raised: true);

        await act.Should().ThrowExactlyAsync<HubException>();

        clients.HandEvents.Should().BeEmpty();
    }

    /// <summary>
    /// ★ IJOBIY NAZORAT — busiz yuqoridagi ikki test "hamma narsa yiqiladi"
    /// sababli ham yashil bo'lardi.
    ///
    /// O'quvchining qo'li HAM tarqatilishi, HAM presence'ga yozilishi kerak:
    /// ChatPanel dagi "Qo'l ko'targanlar (N)" chizig'i aynan shu ikkalasidan
    /// oziqlanadi (hodisa — darhol, ro'yxat — keyin qo'shilganlar uchun).
    /// </summary>
    [Fact]
    public async Task RaiseHand_WhenStudent_StillBroadcastsAndPersists()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lhrs");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new HandRecordingClients();
        using var hub = BuildHub(scope, world.Student.Id, nameof(UserRole.Student), clients);

        await hub.JoinSession(sessionId);
        await hub.RaiseHand(sessionId, raised: true);

        var broadcast = clients.HandEvents.Should().ContainSingle().Subject;

        broadcast.UserId.Should().Be(world.Student.Id);
        broadcast.Raised.Should().BeTrue();

        (await HandRaisedInPresenceAsync(scope, sessionId, world.Student.Id))
            .Should().BeTrue();
    }

    // ------------------------------------------------------------------ yordamchi

    private Task<long> ScheduleSessionAsync(long groupId) =>
        WorldBuilder.AddScheduledSessionAsync(
            factory, groupId, new DateTimeOffset(2026, 5, 6, 19, 0, 0, TimeSpan.Zero));

    private static async Task<bool> HandRaisedInPresenceAsync(
        IServiceScope scope, long sessionId, long userId)
    {
        var presence = scope.ServiceProvider.GetRequiredService<IPresenceService>();
        var list = await presence.ListAsync(sessionId);

        return list.Any(e => e.UserId == userId && e.HandRaised);
    }

    /// <summary>
    /// <see cref="HubHarness.LiveClass"/> ustiga tarqatishni YOZIB oluvchi
    /// klient to'plami qo'yiladi — harness'ning o'z to'plami jim (bo'sh)
    /// va undan "nima tarqatildi" degan savolga javob olib bo'lmaydi.
    /// </summary>
    private static LiveClassHub BuildHub(
        IServiceScope scope, long userId, string role, HandRecordingClients clients)
    {
        var hub = HubHarness.LiveClass(scope, userId, role);
        hub.Clients = clients;
        return hub;
    }
}

/// <summary>
/// Tarqatilgan <c>HandRaised</c> hodisalarini yozib boruvchi klient to'plami.
///
/// ★ FAQAT hub HAQIQATAN ishlatadigan yo'llar qo'llab-quvvatlanadi
/// (<c>Group</c>, <c>OthersInGroup</c>) — <c>ChatRecordingClients</c> dagi
/// bilan bir xil sabab: kimdir tarqatishni "hammaga" o'zgartirsa test
/// jimgina o'tib ketmasin.
/// </summary>
internal sealed class HandRecordingClients : IHubCallerClients
{
    private readonly List<HandRaisedEvent> _handEvents = [];

    private readonly RecordingProxy _proxy;

    public HandRecordingClients() => _proxy = new RecordingProxy(this);

    /// <summary>Xonaga tarqatilgan qo'l hodisalari — kelish tartibida.</summary>
    public IReadOnlyList<HandRaisedEvent> HandEvents => _handEvents;

    public IClientProxy Caller => _proxy;

    public IClientProxy Others => _proxy;

    public IClientProxy OthersInGroup(string groupName) => _proxy;

    public IClientProxy Group(string groupName) => _proxy;

    public IClientProxy All => throw Unsupported();

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
        throw Unsupported();

    public IClientProxy Client(string connectionId) => throw Unsupported();

    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw Unsupported();

    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw Unsupported();

    public IClientProxy GroupExcept(
        string groupName, IReadOnlyList<string> excludedConnectionIds) => throw Unsupported();

    public IClientProxy User(string userId) => throw Unsupported();

    public IClientProxy Users(IReadOnlyList<string> userIds) => throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("Hub xabarni FAQAT o'z xonasiga yuborishi kerak.");

    /// <summary>
    /// Faqat <c>HandRaised</c> yoziladi: hub bu yo'ldan <c>PresenceChanged</c>
    /// ni ham yuboradi va u sanoqni yolg'on to'ldirardi.
    /// </summary>
    private void Record(string method, object?[] args)
    {
        if (!string.Equals(method, "HandRaised", StringComparison.Ordinal)) return;
        if (args.Length > 0 && args[0] is HandRaisedEvent evt) _handEvents.Add(evt);
    }

    private sealed class RecordingProxy(HandRecordingClients owner) : IClientProxy
    {
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Record(method, args);
            return Task.CompletedTask;
        }
    }
}
