using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Domain.Entities;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.WebApi.Hubs;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// JONLI DARS CHATI — TARQATISH SHARTNOMASI (REGRESSIYA QULFI)
/// ========================================================================
///
/// ── QANDAY NOSOZLIKNI QO'RIQLAYDI ──────────────────────────────────────
///
/// Loyiha egasi "chatda kechikish bor" deb shikoyat qildi. O'lchov ko'rsatdi:
/// sim bo'ylab kechikish 5-7 ms, ya'ni server AYBDOR EMAS. Haqiqiy sabab
/// tarqatilayotgan xabarning KALITIDA edi:
///
///   `Clients.Group(...).SendAsync("ChatMessage", new ChatMessageDto(0, ...))`
///
/// — ya'ni HAR xabar `Id = 0` bilan ketardi (baza raqamini fon xizmati
/// beradi, tarqatish esa undan OLDIN bo'ladi). Klient takrorlarni
/// identifikator bo'yicha filtrlaydi, shuning uchun BIRINCHI xabardan keyin
/// 0 "ko'rilgan" ro'yxatiga tushib, o'sha darsdagi KEYINGI HAMMA xabar
/// jimgina tashlanardi. Ekranda xabar sahifa yangilangandagina (REST tarixi,
/// haqiqiy Id bilan) paydo bo'lardi — foydalanuvchi buni "kechikish" deb
/// his qilgan.
///
/// ── NIMA UCHUN BU YUKLAMA TESTIDA KO'RINMAGAN ──────────────────────────
///
/// `tests/load/signalr-load.mjs` SIMNI o'lchaydi: nechta hodisa keldi va
/// qancha vaqtda. Hodisalar HAMMASI kelardi — ular EKRANGA chiqmasdi.
/// Shu sababli yuklama testi ham, backend testlari ham yashil edi.
/// Endi ikkalasi ham kalitni tekshiradi.
///
/// ── NIMA TEKSHIRILADI ──────────────────────────────────────────────────
///
/// Hub metodi SignalR quvurisiz, HAQIQIY servislar (Postgres + Redis) bilan
/// chaqiriladi — `HubErrorTranslationTests` dagi bilan bir xil yondashuv.
/// Klient proksisi esa tarqatilgan DTO'ni YOZIB oladi, ya'ni tasdiqlar
/// simdagi haqiqiy yuk ustida bajariladi.
/// </summary>
public sealed class LiveChatBroadcastTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>
    /// ★ ASOSIY QULF: har tarqatilgan xabarning kaliti NOYOB bo'lishi shart.
    ///
    /// Kalit takrorlansa (eski holatda — doim <c>Id = 0</c>) klient ikkinchi
    /// va undan keyingi xabarlarni "allaqachon ko'rilgan" deb tashlaydi.
    /// Ya'ni bu tasdiq buzilishi = chat ekranda qotib qolishi.
    /// </summary>
    [Fact]
    public async Task SendMessage_GivesEveryBroadcastAUniqueKey()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lcbu");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new ChatRecordingClients();
        var writer = new ChatWriterSpy(clients);
        using var hub = BuildHub(scope, world.Student.Id, clients, writer);

        await hub.JoinSession(sessionId);

        await hub.SendMessage(sessionId, "birinchi", "kalit-bir");
        await hub.SendMessage(sessionId, "ikkinchi", "kalit-ikki");
        await hub.SendMessage(sessionId, "uchinchi", "kalit-uch");

        clients.ChatMessages.Should().HaveCount(3);

        clients.ChatMessages.Should().OnlyContain(
            m => !string.IsNullOrEmpty(m.ClientId),
            "kalitsiz xabarni klient boshqasidan ajrata olmaydi");

        clients.ChatMessages.Select(m => m.ClientId).Should().OnlyHaveUniqueItems(
            "klient takrorlarni kalit bo'yicha filtrlaydi — kalit takrorlansa "
            + "birinchisidan keyingi HAMMA xabar ekranga chiqmay yo'qoladi");
    }

    /// <summary>
    /// Klient bergan kalit O'ZGARISHSIZ qaytadi.
    ///
    /// Bunga OPTIMISTIK ko'rsatish tayanadi: yuboruvchi xabarini darhol
    /// ekranga chiqaradi va o'z broadcast'i qaytganda uni AYNI kalit bo'yicha
    /// tanib, ikkinchi marta chizmaydi. Kalit o'zgartirilsa yuboruvchi o'z
    /// xabarini IKKI MARTA ko'rardi.
    /// </summary>
    [Fact]
    public async Task SendMessage_EchoesTheKeyTheClientChose()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lcbe");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new ChatRecordingClients();
        var writer = new ChatWriterSpy(clients);
        using var hub = BuildHub(scope, world.Student.Id, clients, writer);

        await hub.JoinSession(sessionId);
        await hub.SendMessage(sessionId, "salom", "abc-123");

        clients.ChatMessages.Should().ContainSingle()
            .Which.ClientId.Should().Be("abc-123");
    }

    /// <summary>
    /// Klientga ISHONILMAYDI: shakli buzuq kalit qabul qilinmaydi, lekin
    /// xabar ham yo'qolmaydi — server o'z kalitini qo'yadi.
    ///
    /// Kalit qaytarilmasa (bo'sh qolsa) nosozlik AYNAN qaytadi, shuning
    /// uchun "bo'sh emas" sharti ham shu yerda qulflanadi.
    /// </summary>
    [Theory]
    [InlineData("lcbsa", "")]
    [InlineData("lcbsb", "kalit bilan bo'sh joy")]
    [InlineData("lcbsc", "<script>alert(1)</script>")]
    public async Task SendMessage_WhenKeyIsUnsafe_ServerSubstitutesItsOwn(
        string prefix, string badKey)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new ChatRecordingClients();
        var writer = new ChatWriterSpy(clients);
        using var hub = BuildHub(scope, world.Student.Id, clients, writer);

        await hub.JoinSession(sessionId);
        await hub.SendMessage(sessionId, "salom", badKey);

        var broadcast = clients.ChatMessages.Should().ContainSingle().Subject;

        broadcast.ClientId.Should().NotBeNullOrEmpty("kalitsiz broadcast bo'lmaydi");
        broadcast.ClientId.Should().NotBe(badKey, "shubhali kalit qaytarilmaydi");
    }

    /// <summary>
    /// ★ TEZLIK CHEGARASI QISQA "PORTLASH"GA YO'L QO'YADI.
    ///
    /// Eski chegara "1 xabar / 2 sekund" edi va odam tabiiy yozadigan ketma-ket
    /// ikki qatorning ikkinchisini rad etardi — bu ham "chat sekin" bo'lib
    /// his qilinardi. Endi oynada bir nechta xabarga ruxsat bor, o'rtacha
    /// tezlik esa o'zgarmagan.
    /// </summary>
    [Fact]
    public async Task SendMessage_AllowsAShortBurst()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lcbb");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new ChatRecordingClients();
        var writer = new ChatWriterSpy(clients);
        using var hub = BuildHub(scope, world.Student.Id, clients, writer);

        await hub.JoinSession(sessionId);

        Func<Task> burst = async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                var suffix = i.ToString(CultureInfo.InvariantCulture);
                await hub.SendMessage(sessionId, "qator " + suffix, "burst-" + suffix);
            }
        };

        await burst.Should().NotThrowAsync(
            "ketma-ket yozilgan qisqa qatorlar rad etilmasligi kerak");

        clients.ChatMessages.Should().HaveCount(3);
    }

    /// <summary>
    /// ★ CHEGARAGA URILGANDA FOYDALANUVCHI SABABNI KO'RADI.
    ///
    /// SignalR klientga FAQAT <see cref="HubException"/> matnini uzatadi.
    /// Boshqa istisno bo'lsa (yoki umuman jim tashlansa) o'quvchi xabari
    /// qayerga ketganini bilmasdi — bu ham "kechikish" bo'lib ko'rinardi.
    /// Bu yerda MATN emas, ISTISNO TURI va sababning bor-yo'qligi qulflanadi.
    /// </summary>
    [Fact]
    public async Task SendMessage_WhenFlooding_ClientGetsVisibleReason()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lcbf");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new ChatRecordingClients();
        var writer = new ChatWriterSpy(clients);
        using var hub = BuildHub(scope, world.Student.Id, clients, writer);

        await hub.JoinSession(sessionId);

        // Chegara oshib ketguncha yozamiz. Yuqori chegara ataylab katta
        // olingan: test aniq songa emas, XATTI-HARAKATGA bog'lanadi.
        HubException? rejection = null;

        for (var i = 0; i < 50 && rejection is null; i++)
        {
            var suffix = i.ToString(CultureInfo.InvariantCulture);
            try
            {
                await hub.SendMessage(sessionId, "toshqin " + suffix, "flood-" + suffix);
            }
            catch (HubException ex)
            {
                rejection = ex;
            }
        }

        rejection.Should().NotBeNull("cheksiz yozishga ruxsat bo'lmasligi kerak");

        // `!` ishlatilmaydi — yuqoridagi tasdiq allaqachon qulflagan, lekin
        // kompilyatorga buni bildirishning eng tinch yo'li shu.
        var reason = rejection?.Message ?? string.Empty;

        reason.Should().NotBeNullOrWhiteSpace(
            "SignalR faqat HubException matnini uzatadi — sabab shu matnda bo'lishi shart");
        reason.Should().Contain("tez",
            "foydalanuvchi nima uchun rad etilganini o'qiy olishi kerak");
    }

    /// <summary>
    /// ★ TARQATISH — BAZAGA YOZISHDAN OLDIN.
    ///
    /// Bu tartib butun chat tezligining asosi: baza yozuvi (yoki navbat)
    /// tarqatishdan oldin bo'lsa, har xabar DB kechikishi qadar kechikardi.
    /// Josus xabar navbatga tushgan PAYTDA nechta broadcast bo'lganini
    /// o'qiydi — <c>LiveSessionEndBroadcastTests</c> dagi naqsh, faqat
    /// teskari yo'nalishda.
    /// </summary>
    [Fact]
    public async Task SendMessage_BroadcastsBeforeQueueingForDatabase()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lcbq");
        var sessionId = await ScheduleSessionAsync(world.GroupId);

        using var scope = factory.Services.CreateScope();
        var clients = new ChatRecordingClients();
        var writer = new ChatWriterSpy(clients);
        using var hub = BuildHub(scope, world.Student.Id, clients, writer);

        await hub.JoinSession(sessionId);
        await hub.SendMessage(sessionId, "tartib muhim", "tartib-1");

        writer.BroadcastsSeenAtEnqueue.Should().Equal([1],
            "xabar navbatga qo'yilganda u ALLAQACHON tarqatilgan bo'lishi kerak");
    }

    // ------------------------------------------------------------------ yordamchi

    private Task<long> ScheduleSessionAsync(long groupId) =>
        WorldBuilder.AddScheduledSessionAsync(
            factory, groupId, new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Hub'ni SignalR quvurisiz, lekin HAQIQIY bog'liqliklar bilan ko'taradi.
    /// <see cref="IChatMessageWriter"/> ATAYLAB josus bilan almashtiriladi:
    /// haqiqiy yozuvchi fon xizmati bo'lgani uchun test undan "qachon
    /// chaqirilding" degan savolga javob ola olmasdi.
    /// </summary>
    private static LiveClassHub BuildHub(
        IServiceScope scope,
        long userId,
        ChatRecordingClients clients,
        IChatMessageWriter writer)
    {
        var provider = scope.ServiceProvider;

        return new LiveClassHub(
            provider.GetRequiredService<ILiveSessionService>(),
            provider.GetRequiredService<IPresenceService>(),
            provider.GetRequiredService<ICacheService>(),
            writer,
            provider.GetRequiredService<ILogger<LiveClassHub>>())
        {
            Context = new TestHubCallerContext(userId, "Student"),
            Groups = new RecordingGroupManager(),
            Clients = clients,
        };
    }
}

/// <summary>
/// Tarqatilgan <c>ChatMessage</c> hodisalarini yozib boruvchi klient to'plami.
///
/// ★ FAQAT hub HAQIQATAN ishlatadigan yo'llar qo'llab-quvvatlanadi
/// (<c>Group</c>, <c>OthersInGroup</c>) — <c>TestHubCallerClients</c> dagi
/// bilan bir xil sabab: kimdir tarqatishni "hammaga" o'zgartirsa test
/// jimgina o'tib ketmasin.
/// </summary>
internal sealed class ChatRecordingClients : IHubCallerClients
{
    private readonly List<ChatMessageDto> _chatMessages = [];

    private readonly RecordingProxy _proxy;

    public ChatRecordingClients() => _proxy = new RecordingProxy(this);

    /// <summary>Xonaga tarqatilgan chat xabarlari — kelish tartibida.</summary>
    public IReadOnlyList<ChatMessageDto> ChatMessages => _chatMessages;

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
    /// Faqat <c>ChatMessage</c> yoziladi: hub bu yo'ldan <c>PresenceChanged</c>
    /// ni ham yuboradi va u sanoqni yolg'on to'ldirardi.
    /// </summary>
    private void Record(string method, object?[] args)
    {
        if (!string.Equals(method, "ChatMessage", StringComparison.Ordinal)) return;
        if (args.Length > 0 && args[0] is ChatMessageDto dto) _chatMessages.Add(dto);
    }

    private sealed class RecordingProxy(ChatRecordingClients owner) : IClientProxy
    {
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Record(method, args);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Navbatga qo'yishni yozib boruvchi josus.
///
/// Har chaqiruvda O'SHA PAYTDAGI broadcast sonini eslab qoladi — shu bilan
/// "avval tarqat, keyin saqla" tartibi isbotlanadi.
/// </summary>
internal sealed class ChatWriterSpy(ChatRecordingClients clients) : IChatMessageWriter
{
    private readonly List<int> _broadcastsSeenAtEnqueue = [];

    private readonly List<string> _enqueued = [];

    /// <summary>Har navbatga qo'yish paytida ko'ringan broadcast soni.</summary>
    public IReadOnlyList<int> BroadcastsSeenAtEnqueue => _broadcastsSeenAtEnqueue;

    /// <summary>Bazaga yozish uchun navbatga tushgan matnlar.</summary>
    public IReadOnlyList<string> Enqueued => _enqueued;

    public ValueTask EnqueueAsync(ChatMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _broadcastsSeenAtEnqueue.Add(clients.ChatMessages.Count);
        _enqueued.Add(message.Body);
        return ValueTask.CompletedTask;
    }
}
