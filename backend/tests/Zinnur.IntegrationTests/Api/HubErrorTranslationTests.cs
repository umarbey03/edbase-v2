using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.GroupChat.Services;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.WebApi.Hubs;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// HUB XATOLARI KLIENTGA SABAB BILAN YETADIMI (REGRESSIYA QULFI)
/// ========================================================================
///
/// ── QANDAY NOSOZLIKNI QO'RIQLAYDI ──────────────────────────────────────
///
/// SignalR klientga FAQAT <see cref="HubException"/> ning matnini uzatadi.
/// Hub metodi boshqa istisno tashlasa, prod'da
/// (<c>EnableDetailedErrors=false</c>) klient
/// <c>"An unexpected error occurred invoking 'JoinThread'"</c> degan UMUMIY
/// satrni oladi. Ya'ni:
///
///     "ruxsatingiz yo'q"  ==  "juda tez yozyapsiz"  ==  haqiqiy 500
///
/// UI sababni ayta olmaydi, foydalanuvchi qayta-qayta urinaveradi.
/// Shuning uchun hub'lar use-case istisnolarini <see cref="HubErrors"/>
/// orqali <see cref="HubException"/> ga o'giradi. Bu sinf o'sha
/// o'girishning HAQIQATAN ishlashini qulflaydi.
///
/// ── ★ NIMA UCHUN TEST <c>EnableDetailedErrors</c> GA BOG'LIQ EMAS ──────
///
/// Bu eng nozik nuqta. Dev muhitida <c>EnableDetailedErrors=true</c> va
/// tarjima BUTUNLAY olib tashlansa ham klient xato MATNINI ko'raveradi —
/// ya'ni "xabar matnini" tekshiradigan test dev'da YASHIL bo'lib, prod'dagi
/// haqiqiy nosozlikni o'tkazib yuborardi.
///
/// Bu yerda tekshiriladigan narsa MATN emas, ISTISNONING TURI:
/// <c>ThrowExactlyAsync&lt;HubException&gt;</c>. Hub metodi to'g'ridan-to'g'ri
/// (SignalR quvurisiz) chaqiriladi, shuning uchun natijaga
/// <c>EnableDetailedErrors</c>, transport turi yoki muhit UMUMAN ta'sir
/// qilmaydi. Tarjima olib tashlansa metod <c>ForbiddenException</c>
/// tashlaydi va <c>ThrowExactly</c> darhol qizaradi.
///
/// Matn ham tekshiriladi, lekin QO'SHIMCHA sifatida: turi to'g'ri bo'lib
/// matni bo'sh qolsa foydalanuvchi baribir sababni bilmasdi.
///
/// ── QABUL QILINGAN YAGONA FARAZ ────────────────────────────────────────
///
/// "SignalR <see cref="HubException"/> matnini uzatadi, boshqasini
/// uzatmaydi" — bu FRAMEWORK xatti-harakati va bu yerda sinalmaydi
/// (uni sinash ASP.NET Core'ni sinash bo'lardi). Koordinator uni jonli
/// WebSocket klienti bilan tasdiqlagan. Test qo'riqlaydigan narsa —
/// BIZNING kodimiz o'sha shartnomani bajaradimi.
///
/// ── VAKOLATCHI (MOCK) YO'Q ─────────────────────────────────────────────
///
/// Istisnolarni HAQIQIY use-case HAQIQIY Postgres/Redis ustida tashlaydi:
/// begona o'quvchi, mavjud bo'lmagan guruh, haqiqiy Redis tezlik
/// hisoblagichi. Soxta servis "ForbiddenException tashla" deb sozlansa,
/// test faqat <see cref="HubErrors"/> ni tekshirardi — hub'ning o'sha
/// yordamchini CHAQIRISHI esa sinovsiz qolardi (aynan shu joyda regressiya
/// bo'lishi mumkin).
/// </summary>
public sealed class HubErrorTranslationTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= GroupChatHub.JoinThread

    /// <summary>
    /// ★ ASOSIY HOLAT: begona odam obuna bo'lmoqchi.
    ///
    /// Use-case <c>ForbiddenException</c> tashlaydi. Klient <b>HubException</b>
    /// va o'zbekcha sababni olishi SHART — aks holda "chat ochilmadi" degan
    /// tushunarsiz holatga tushardi.
    /// </summary>
    [Fact]
    public async Task JoinThread_WhenNoAccess_ClientGetsHubExceptionWithReason()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxjt");
        var outsider = await WorldBuilder.CreateAsync(factory, "hxjt2");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, outsider.Student.Id);

        Func<Task> act = async () => await hub.JoinThread(world.GroupId, null);

        var error = (await act.Should().ThrowExactlyAsync<HubException>(
            "SignalR faqat HubException matnini klientga uzatadi")).Which;

        error.Message.Should().Be("Bu guruh chatiga ruxsatingiz yo'q.");
        error.InnerException.Should().BeOfType<ForbiddenException>(
            "asl sabab logda (va Sentry'da) ko'rinishi kerak");
    }

    /// <summary>Mavjud bo'lmagan guruh — <c>NotFoundException</c> yo'li.</summary>
    [Fact]
    public async Task JoinThread_WhenGroupMissing_ClientGetsHubException()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxjtm");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, world.Student.Id);

        Func<Task> act = async () => await hub.JoinThread(987_654_321L, null);

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.Message.Should().Contain("topilmadi");
        error.InnerException.Should().BeOfType<NotFoundException>();
    }

    /// <summary>
    /// Noma'lum kanal raqami — <c>ValidationException</c> yo'li.
    ///
    /// Hub argumenti <c>enum</c> bo'lgani uchun klient ixtiyoriy raqam
    /// yubora oladi (JSON protokolida u shunchaki son).
    /// </summary>
    [Fact]
    public async Task JoinThread_WhenChannelUnknown_ClientGetsHubException()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxjtc");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, world.Student.Id);

        Func<Task> act = async () =>
            await hub.JoinThread(world.GroupId, (GroupChatChannel)77);

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.InnerException.Should().BeOfType<ValidationException>();
    }

    /// <summary>
    /// ★ IJOBIY NAZORAT — testlar BEKORGA yashil emasligining dalili.
    ///
    /// Loyihada bu tuzoq allaqachon bo'lgan: N+1 hisoblagichi hech nima
    /// yozmagani uchun test "0 == 0" bilan o'tib ketgan. Bu yerda ham
    /// tarmoq (Context, Groups, DI) noto'g'ri ulangan bo'lsa, YUQORIDAGI
    /// testlar "har doim istisno" sababli yashil bo'lib turardi. Shu test
    /// muvaffaqiyatli yo'l HAQIQATAN ishlashini isbotlaydi.
    /// </summary>
    [Fact]
    public async Task JoinThread_WhenAllowed_SubscribesAndReturnsAccess()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxjok");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, world.Student.Id);

        var access = await hub.JoinThread(world.GroupId, GroupChatChannel.Curator);

        access.GroupId.Should().Be(world.GroupId);
        access.Channel.Should().Be(GroupChatChannel.Curator);

        HubHarness.Groups(hub).Added.Should().ContainSingle()
            .Which.Should().Contain(
                world.GroupId.ToString(CultureInfo.InvariantCulture),
                "obuna (guruh, kanal) xonasiga qo'shilishi kerak");
    }

    // ================================================================= GroupChatHub.SendMessage

    /// <summary>Yozish huquqi yo'q — <c>ForbiddenException</c> -> HubException.</summary>
    [Fact]
    public async Task SendMessage_WhenNoAccess_ClientGetsHubExceptionWithReason()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxsm");
        var outsider = await WorldBuilder.CreateAsync(factory, "hxsm2");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, outsider.Student.Id);

        Func<Task> act = async () =>
            await hub.SendMessage(world.GroupId, null, "Begona xabar");

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.Message.Should().Be("Bu guruh chatiga ruxsatingiz yo'q.");
        error.InnerException.Should().BeOfType<ForbiddenException>();
    }

    /// <summary>
    /// ★ TEZLIK CHEGARASI — bu funksiyaning ENG KO'RINADIGAN holati.
    ///
    /// Foydalanuvchi tez yozganda "biroz kuting" deb ko'rsatish kerak.
    /// Tarjimasiz u "nimadir xato ketdi" ko'rib, YANA bosardi va chegara
    /// oynasi cheksiz uzayardi.
    ///
    /// Test sababning HAQIQATAN yetkazilayotganini isbotlaydi: xabar
    /// "ruxsat yo'q" matnidan BOSHQA bo'lishi tekshiriladi — ya'ni ikki
    /// turli nosozlik klientga ikki turli sabab bilan boradi.
    /// </summary>
    [Fact]
    public async Task SendMessage_WhenFlooding_ClientGetsRateLimitReason()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxrl");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, world.Student.Id);

        // Chegara: 10 sekundlik oynada 10 xabar (`GroupChatService`).
        for (var i = 0; i < 10; i++)
        {
            await hub.SendMessage(
                world.GroupId,
                GroupChatChannel.Teacher,
                string.Create(CultureInfo.InvariantCulture, $"Savol {i}"));
        }

        Func<Task> act = async () =>
            await hub.SendMessage(world.GroupId, GroupChatChannel.Teacher, "Ortiqcha");

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.InnerException.Should().BeOfType<TooManyRequestsException>();
        error.Message.Should().Contain("tez yozyapsiz");
        error.Message.Should().NotBe("Bu guruh chatiga ruxsatingiz yo'q.",
            "ikki xil nosozlik klientga BIR XIL ko'rinmasligi kerak");
    }

    /// <summary>Bo'sh matn — Domain qoidasi (<c>DomainException</c>) yo'li.</summary>
    [Fact]
    public async Task SendMessage_WhenBodyEmpty_ClientGetsHubException()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxemp");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, world.Student.Id);

        Func<Task> act = async () =>
            await hub.SendMessage(world.GroupId, GroupChatChannel.Teacher, "   ");

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.Message.Should().Be("Xabar bo'sh bo'lishi mumkin emas.");
        error.InnerException.Should().BeAssignableTo<DomainException>();
    }

    // ================================================================= GroupChatHub.LeaveThread

    /// <summary>
    /// <c>LeaveThread</c> — YAGONA use-case'siz metod, va bu ATAYLAB.
    ///
    /// U faqat SignalR obunasini uzadi. Ruxsat tekshirilmaydi va tekshirilishi
    /// SHART EMAS: obunani uzish hech qanday ma'lumot ochmaydi, tekshiruv esa
    /// zarar keltirardi — ruxsati bekor qilingan foydalanuvchi (guruhdan
    /// chiqarilgan o'quvchi) eski oqimdan CHIQA OLMAY qolardi va xabar olishda
    /// davom etardi.
    ///
    /// Shuning uchun bu yerda "HubException kutamiz" demaymiz; test qoidani
    /// yozib qo'yadi: metod hech qanday holatda YIQILMAYDI.
    /// </summary>
    [Fact]
    public async Task LeaveThread_ForInaccessibleThread_NeverThrows()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxlv");
        var outsider = await WorldBuilder.CreateAsync(factory, "hxlv2");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.GroupChat(scope, outsider.Student.Id);

        Func<Task> act = async () =>
            await hub.LeaveThread(world.GroupId, GroupChatChannel.Teacher);

        await act.Should().NotThrowAsync();

        HubHarness.Groups(hub).Removed.Should().ContainSingle();
    }

    // ================================================================= LiveClassHub

    /// <summary>
    /// ★ TOPILGAN TESHIK: <see cref="LiveClassHub.JoinSession"/> da tarjima
    /// UMUMAN yo'q edi (izohda esa "klientga HubException ketadi" deb
    /// yozilgan edi).
    ///
    /// Ya'ni darsga kira olmagan o'quvchi "Bu darsga ruxsatingiz yo'q"
    /// o'rniga umumiy xato ko'rardi — aynan guruh chatidagi tuzatilgan
    /// nosozlikning o'zi, faqat pul/baho bilan bog'liq oqimda.
    /// </summary>
    [Fact]
    public async Task LiveJoinSession_WhenNoAccess_ClientGetsHubExceptionWithReason()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxls");
        var outsider = await WorldBuilder.CreateAsync(factory, "hxls2");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, new DateTimeOffset(2026, 3, 2, 14, 0, 0, TimeSpan.Zero));

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.LiveClass(scope, outsider.Student.Id);

        Func<Task> act = async () => await hub.JoinSession(sessionId);

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.Message.Should().Be("Bu darsga ruxsatingiz yo'q.");
        error.InnerException.Should().BeOfType<ForbiddenException>();
    }

    /// <summary>Mavjud bo'lmagan dars — <c>NotFoundException</c> yo'li.</summary>
    [Fact]
    public async Task LiveJoinSession_WhenSessionMissing_ClientGetsHubException()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxlsm");

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.LiveClass(scope, world.Student.Id);

        Func<Task> act = async () => await hub.JoinSession(876_543_210L);

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.Message.Should().Contain("topilmadi");
        error.InnerException.Should().BeOfType<NotFoundException>();
    }

    /// <summary>
    /// ★ IJOBIY NAZORAT + bo'sh xabar yo'li BITTA testda.
    ///
    /// Avval darsga HAQIQATAN qo'shiladi (ya'ni yuqoridagi testlar
    /// "hamma narsa yiqiladi" sababli yashil emas), keyin bo'sh matn
    /// yuboriladi — u <c>DomainException</c> beradi va u ham
    /// <see cref="HubException"/> ga o'girilishi kerak.
    /// </summary>
    [Fact]
    public async Task LiveJoinSession_WhenMember_SucceedsAndEmptyMessageIsTranslated()
    {
        var world = await WorldBuilder.CreateAsync(factory, "hxlok");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

        using var scope = factory.Services.CreateScope();
        using var hub = HubHarness.LiveClass(scope, world.Student.Id);

        var result = await hub.JoinSession(sessionId);

        result.Session.Id.Should().Be(sessionId);
        result.Count.Should().BePositive("o'zi ro'yxatda bo'lishi kerak");

        Func<Task> act = async () => await hub.SendMessage(sessionId, "  ", clientId: null);

        var error = (await act.Should().ThrowExactlyAsync<HubException>()).Which;

        error.Message.Should().Be("Xabar bo'sh bo'lishi mumkin emas.");
        error.InnerException.Should().BeAssignableTo<DomainException>();
    }

    // ================================================================= qamrov qulfi

    /// <summary>
    /// ★ YANGI HUB METODI TARJIMANI CHETLAB O'TA OLMASIN.
    ///
    /// Yuqoridagi testlar MAVJUD metodlarni qoplaydi. Ertaga kimdir
    /// <c>PinMessage</c> qo'shsa va use-case istisnosini tarjimasiz
    /// qoldirsa — hech qanday test qizarmasdi, chunki test o'sha metod
    /// haqida bilmaydi.
    ///
    /// Shu sabab ommaviy metodlar ro'yxati QOTIRILADI: yangi metod
    /// qo'shilishi shu testni yiqitadi va muallif uni shu faylga test
    /// yozib "ochishi" kerak bo'ladi.
    /// </summary>
    [Fact]
    public void GroupChatHub_PublicMethods_AreCoveredByThisSuite() =>
        PublicMethodNames(typeof(GroupChatHub)).Should().BeEquivalentTo(
            new[] { "JoinThread", "LeaveThread", "SendMessage", "OnDisconnectedAsync" },
            "yangi hub metodi uchun xato tarjimasi testi ham yozilishi shart");

    /// <inheritdoc cref="GroupChatHub_PublicMethods_AreCoveredByThisSuite"/>
    [Fact]
    public void LiveClassHub_PublicMethods_AreCoveredByThisSuite() =>
        PublicMethodNames(typeof(LiveClassHub)).Should().BeEquivalentTo(
            new[] { "JoinSession", "LeaveSession", "SendMessage", "RaiseHand", "OnDisconnectedAsync" },
            "yangi hub metodi uchun xato tarjimasi testi ham yozilishi shart");

    /// <summary>Hub'ning O'ZI e'lon qilgan ommaviy metodlari (meros olinganlarsiz).</summary>
    private static IEnumerable<string> PublicMethodNames(Type hubType) =>
        hubType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name);
}

// ========================================================================= test infratuzilmasi

/// <summary>
/// Hub'ni SignalR quvurisiz, lekin HAQIQIY bog'liqliklar bilan ko'taradi.
///
/// ★ NIMA UCHUN QUVUR YO'Q: quvurni qo'shish testni <c>EnableDetailedErrors</c>,
/// transport va handshake'ga bog'lardi — aynan shu bog'liqlik yomon testni
/// dev'da yashil qilib qo'yardi (sinf izohiga qarang). Bu yerda hub metodi
/// to'g'ridan-to'g'ri chaqiriladi va ISTISNO TURI tekshiriladi.
///
/// Servislar DI'dan olinadi (soxta emas): ruxsat, tezlik chegarasi va domain
/// qoidalari haqiqiy Postgres/Redis ustida bajariladi.
/// </summary>
internal static class HubHarness
{
    public static GroupChatHub GroupChat(IServiceScope scope, long userId, string role = "Student")
    {
        ArgumentNullException.ThrowIfNull(scope);

        var provider = scope.ServiceProvider;

        return new GroupChatHub(
            provider.GetRequiredService<IGroupChatService>(),
            provider.GetRequiredService<ILogger<GroupChatHub>>())
        {
            Context = new TestHubCallerContext(userId, role),
            Groups = new RecordingGroupManager(),
            Clients = new TestHubCallerClients(),
        };
    }

    public static LiveClassHub LiveClass(IServiceScope scope, long userId, string role = "Student")
    {
        ArgumentNullException.ThrowIfNull(scope);

        var provider = scope.ServiceProvider;

        return new LiveClassHub(
            provider.GetRequiredService<ILiveSessionService>(),
            provider.GetRequiredService<IPresenceService>(),
            provider.GetRequiredService<ICacheService>(),
            provider.GetRequiredService<IChatMessageWriter>(),
            provider.GetRequiredService<ILogger<LiveClassHub>>())
        {
            Context = new TestHubCallerContext(userId, role),
            Groups = new RecordingGroupManager(),
            Clients = new TestHubCallerClients(),
        };
    }

    /// <summary>Hub qaysi SignalR xonalariga qo'shilgani/chiqqanini o'qish uchun.</summary>
    public static RecordingGroupManager Groups(Hub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);

        return (RecordingGroupManager)hub.Groups;
    }
}

/// <summary>
/// Ulanish konteksti: JWT'dan keyingi claim'lar bilan bir xil shakl
/// (<c>NameIdentifier</c> / <c>Name</c> / <c>Role</c>) — hub aynan shularni
/// o'qiydi.
///
/// ★ <see cref="ConnectionAborted"/> uchun <c>CancellationTokenSource</c>
/// ATAYLAB YARATILMAYDI: u <c>IDisposable</c> maydon bo'lardi va sinfni
/// <c>IDisposable</c> qilishga majburlardi (CA1001). Testda uzilishni
/// modellashtirish kerak emas.
/// </summary>
internal sealed class TestHubCallerContext : HubCallerContext
{
    private readonly Dictionary<object, object?> _items = [];

    private readonly FeatureCollection _features = new();

    private readonly ClaimsPrincipal _user;

    public TestHubCallerContext(long userId, string role)
    {
        var id = userId.ToString(CultureInfo.InvariantCulture);

        UserIdentifier = id;

        _user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, id),
                new Claim(ClaimTypes.Name, "Test Foydalanuvchi"),
                new Claim(ClaimTypes.Role, role),
            ],
            authenticationType: "TestAuth"));
    }

    public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");

    public override string? UserIdentifier { get; }

    public override ClaimsPrincipal? User => _user;

    public override IDictionary<object, object?> Items => _items;

    public override IFeatureCollection Features => _features;

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    /// <summary>Testda ulanish uzilmaydi — chaqirilgani yozib qo'yiladi.</summary>
    public override void Abort() => Aborted = true;

    public bool Aborted { get; private set; }
}

/// <summary>Obuna qaysi xonaga tushganini yozib boruvchi guruh menejeri.</summary>
internal sealed class RecordingGroupManager : IGroupManager
{
    public List<string> Added { get; } = [];

    public List<string> Removed { get; } = [];

    public Task AddToGroupAsync(
        string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Added.Add(groupName);
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(
        string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Removed.Add(groupName);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Klientlarga tarqatishning bo'sh amalga oshirilishi.
///
/// ★ FAQAT hub HAQIQATAN ishlatadigan yo'llar qo'llab-quvvatlanadi
/// (<c>Group</c>, <c>OthersInGroup</c>). Qolganlari ATAYLAB istisno
/// tashlaydi — <see cref="GroupChatRealtimeTests"/> dagi bilan bir xil
/// sabab: kimdir tarqatishni "hammaga" o'zgartirsa test jimgina o'tib
/// ketmasin.
/// </summary>
internal sealed class TestHubCallerClients : IHubCallerClients
{
    private static readonly NoopClientProxy Proxy = new();

    public IClientProxy Caller => Proxy;

    public IClientProxy Others => Proxy;

    public IClientProxy OthersInGroup(string groupName) => Proxy;

    public IClientProxy Group(string groupName) => Proxy;

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

    private sealed class NoopClientProxy : IClientProxy
    {
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
