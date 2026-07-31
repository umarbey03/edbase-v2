using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.WebApi.Hubs;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// GURUH CHATI — REAL VAQTDAGI YETKAZISH VA SO'ROVLAR SONI
/// ========================================================================
///
/// Bu yerda SignalR TRANSPORTI sinalmaydi (u framework'ning ishi), lekin
/// undan oldingi HAMMA narsa sinaladi:
///
///   • xabar QAYSI xonaga yuborildi — kanal izolyatsiyasining realtime
///     tomoni (ustoz `Curator` xonasiga umuman qo'shilmasligi kerak, ya'ni
///     xona nomida kanal BO'LISHI shart);
///   • xabar yuborilgan PAYTDA u bazada bormi — commit-then-send;
///   • tarqatish yiqilsa so'rov yiqiladimi — yo'q, yiqilmasligi kerak;
///   • "Chatlar" ro'yxati N+1 qilyaptimi.
///
/// Buning uchun <c>IHubContext&lt;GroupChatHub&gt;</c> yozib boruvchi
/// soxta bilan almashtiriladi. HAQIQIY <c>GroupChatNotifier</c> esa
/// JOYIDA qoladi — ya'ni test uning kodini (xona nomi, hodisa nomi,
/// istisnoni yutish) haqiqatan bajaradi.
/// </summary>
public sealed class GroupChatRealtimeTests(GroupChatRealtimeFactory factory)
    : IClassFixture<GroupChatRealtimeFactory>
{
    /// <summary>
    /// ★ XONA NOMIDA KANAL BOR — realtime izolyatsiyaning asosi.
    ///
    /// Xabar butun guruhga (`gchat-{groupId}`) yuborilsa, ustoz o'quvchining
    /// kuratorga atalgan savolini EKRANIDA ko'rardi — ruxsat tekshiruvi
    /// benuqson bo'lsa ham, chunki u REST yo'lida turadi.
    /// </summary>
    [Fact]
    public async Task Send_BroadcastsToChannelSpecificRoom()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcrt");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        factory.Hub.Clear();

        await GroupChatApi.SendAsync(
            student, world.GroupId, "Ustozga", GroupChatChannel.Teacher);
        await GroupChatApi.SendAsync(
            student, world.GroupId, "Kuratorga", GroupChatChannel.Curator);

        var broadcasts = factory.Hub.Take();

        broadcasts.Should().HaveCount(2);
        broadcasts.Should().OnlyContain(b => b.Method == "GroupChatMessage");

        var teacherRoom = broadcasts.Single(b => b.Message.Body == "Ustozga").RoomName;
        var curatorRoom = broadcasts.Single(b => b.Message.Body == "Kuratorga").RoomName;

        teacherRoom.Should().NotBe(curatorRoom,
            "ikki oqim ikki xil SignalR xonasiga tushishi SHART");

        teacherRoom.Should().Contain(
            world.GroupId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        curatorRoom.Should().Contain(
            world.GroupId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// ★ COMMIT-THEN-SEND. Tarqatish paytida xabar BAZADA bo'lishi shart.
    ///
    /// Teskarisi jimgina buziladi: saqlash yiqilsa ekranlarda savol turardi,
    /// bazada esa yo'q edi — o'quvchi javob kutardi, ustoz esa keyingi
    /// ochganda hech nima ko'rmasdi. Bu tartib shu test bilan QULFLANADI:
    /// kimdir qatorlarni almashtirsa test yiqiladi.
    /// </summary>
    [Fact]
    public async Task Send_PersistsMessageBeforeBroadcasting()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcrtc");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        factory.Hub.Clear();

        var sent = await GroupChatApi.SendAsync(student, world.GroupId, "Saqlangan bo'lsin");

        var broadcast = factory.Hub.Take().Should().ContainSingle().Subject;

        broadcast.Message.Id.Should().Be(sent.Id);
        broadcast.Message.Id.Should().BePositive("tarqatilayotgan xabarda haqiqiy Id bo'lishi kerak");
        broadcast.BodyInDatabase.Should().Be("Saqlangan bo'lsin",
            "xabar tarqatilgan PAYTDA bazada bo'lishi shart (commit-then-send)");
    }

    /// <summary>
    /// ★ TARQATISH YIQILSA SO'ROV YIQILMAYDI.
    ///
    /// Xabar bazada saqlangan. Bu yerdan 500 qaytsa, foydalanuvchi
    /// "yuborilmadi" deb o'ylab qayta bosardi va AYNI savol ikki marta
    /// yozilardi. Yetkazilmagani darajasi: qarshi tomon sahifani
    /// yangilaganda xabarni baribir ko'radi.
    /// </summary>
    [Fact]
    public async Task Send_WhenBroadcastFails_StillReturnsCreatedAndPersists()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcrtf");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        factory.Hub.Clear();
        factory.Hub.FailBroadcast = true;

        try
        {
            var response = await student.PostAsJsonAsync(
                GroupChatApi.SendUrl(world.GroupId), new { body = "Tarqatish yiqiladi" });

            response.StatusCode.Should().Be(HttpStatusCode.Created,
                await WorldBuilder.Body(response));
        }
        finally
        {
            factory.Hub.FailBroadcast = false;
        }

        var page = await GroupChatApi.MessagesAsync(student, world.GroupId);

        page.Items.Select(m => m.Body).Should().Equal("Tarqatish yiqiladi");
    }

    /// <summary>
    /// ★ N+1 YO'Q: "Chatlar" ro'yxati guruhlar soniga BOG'LIQ EMAS.
    ///
    /// Naif amalga oshirish har guruh (yoki har oqim) uchun alohida
    /// "oxirgi xabar" va "o'qilmaganlar" so'rovini qiladi — 40 guruhli
    /// ustozda bu 80+ borish-kelish va sekundlab ochiladigan ekran.
    ///
    /// Test buni TO'G'RIDAN-TO'G'RI o'lchaydi: guruh chati jadvallariga
    /// tegadigan SQL buyruqlari sanaladi, keyin ikkinchi guruh qo'shilib,
    /// sanoq AYNAN o'sha bo'lishi tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Threads_QueryCount_DoesNotGrowWithGroupCount()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcn1");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        await GroupChatApi.SendAsync(
            student, world.GroupId, "Birinchi guruh, ustoz", GroupChatChannel.Teacher);
        await GroupChatApi.SendAsync(
            student, world.GroupId, "Birinchi guruh, kurator", GroupChatChannel.Curator);

        factory.StartCounting();
        var oneGroup = await GroupChatApi.ThreadsAsync(student);
        var withOneGroup = factory.StopCounting();

        oneGroup.Where(t => t.GroupId == world.GroupId).Should().HaveCount(2);

        // ★ HISOBLAGICHNING O'ZI ISHLAYOTGANINI ISBOTLAYMIZ.
        //
        // Bunsiz test BEKORGA yashil bo'lardi: hisoblagich hech nima
        // yozmasa "0 == 0" chiqadi va N+1 bemalol o'tib ketardi. (Bu
        // aynan shu testning birinchi variantida sodir bo'ldi.)
        withOneGroup.Should().NotBeEmpty("hisoblagich SQL buyruqlarini ko'rishi kerak");

        // --- ikkinchi guruh ---
        var secondGroupId = await GroupChatApi.AddGroupAsync(factory, world, "gcn1b");

        await GroupChatApi.SendAsync(
            student, secondGroupId, "Ikkinchi guruh, ustoz", GroupChatChannel.Teacher);
        await GroupChatApi.SendAsync(
            student, secondGroupId, "Ikkinchi guruh, kurator", GroupChatChannel.Curator);

        factory.StartCounting();
        var twoGroups = await GroupChatApi.ThreadsAsync(student);
        var withTwoGroups = factory.StopCounting();

        twoGroups.Where(t => t.GroupId == world.GroupId || t.GroupId == secondGroupId)
            .Should().HaveCount(4, "ikki guruh x ikki oqim");

        withTwoGroups.Count.Should().Be(withOneGroup.Count,
            "guruh qo'shilishi so'rovlar sonini oshirmasligi kerak (N+1 yo'q)");

        // Absolyut chegara ham qo'yiladi: kelajakda "bir xil, lekin 12 ta"
        // holatiga tushib qolmaslik uchun.
        withTwoGroups.Count.Should().BeLessThanOrEqualTo(4);
    }

    /// <summary>
    /// O'qilmaganlar sanog'i BIR NECHA guruh va oqim bo'ylab to'g'ri
    /// hisoblanadi — bitta so'rovda hisoblanishi uni buzmasligi kerak.
    ///
    /// Bu N+1 testining ikkinchi yarmi: tezlikni tekshirgan test
    /// NATIJANI ham tekshirmasa, "tez va noto'g'ri" yechim yashil bo'lardi.
    /// </summary>
    [Fact]
    public async Task Threads_UnreadCount_IsPerGroupAndPerChannel()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcunr2");
        var secondGroupId = await GroupChatApi.AddGroupAsync(factory, world, "gcunr2b");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        using var curator = await WorldBuilder.ClientAsync(factory, world.Curator);

        // 1-guruh: ustoz 2 ta, kurator 1 ta yozdi
        await GroupChatApi.SendAsync(teacher, world.GroupId, "U1");
        await GroupChatApi.SendAsync(teacher, world.GroupId, "U2");
        await GroupChatApi.SendAsync(curator, world.GroupId, "K1");

        // 2-guruh: ustoz 1 ta yozdi
        await GroupChatApi.SendAsync(teacher, secondGroupId, "U3");

        var threads = await GroupChatApi.ThreadsAsync(student);

        Unread(threads, world.GroupId, GroupChatChannel.Teacher).Should().Be(2);
        Unread(threads, world.GroupId, GroupChatChannel.Curator).Should().Be(1);
        Unread(threads, secondGroupId, GroupChatChannel.Teacher).Should().Be(1);
        Unread(threads, secondGroupId, GroupChatChannel.Curator).Should().Be(0);

        // Bitta oqimni o'qiymiz — FAQAT o'sha nolga tushadi.
        await GroupChatApi.MarkReadAsync(student, world.GroupId, GroupChatChannel.Teacher);

        var after = await GroupChatApi.ThreadsAsync(student);

        Unread(after, world.GroupId, GroupChatChannel.Teacher).Should().Be(0);
        Unread(after, world.GroupId, GroupChatChannel.Curator).Should().Be(1,
            "boshqa oqimning belgisi tegilmasligi kerak");
        Unread(after, secondGroupId, GroupChatChannel.Teacher).Should().Be(1,
            "boshqa GURUHNING belgisi ham tegilmasligi kerak");

        // Oxirgi xabar ko'chirmasi ham to'g'ri oqimdan olinadi.
        Thread(after, world.GroupId, GroupChatChannel.Curator)
            .LastMessagePreview.Should().Be("K1");
    }

    private static int Unread(
        IReadOnlyList<GroupChatThreadResponse> threads, long groupId, GroupChatChannel channel) =>
        Thread(threads, groupId, channel).UnreadCount;

    private static GroupChatThreadResponse Thread(
        IReadOnlyList<GroupChatThreadResponse> threads, long groupId, GroupChatChannel channel) =>
        threads.Single(t => t.GroupId == groupId && t.Channel == channel.ToString());
}

// ========================================================================= test infratuzilmasi

/// <summary>
/// SignalR hub konteksti YOZIB BORUVCHI soxta bilan almashtirilgan API.
///
/// ★ NIMA UCHUN <c>IGroupChatNotifier</c> emas, aynan <c>IHubContext</c>
/// almashtiriladi: port almashtirilsa, HAQIQIY <c>GroupChatNotifier</c>
/// umuman bajarilmasdi va uning eng nozik qismlari — xona nomi hamda
/// istisnoni yutish — sinovsiz qolardi.
///
/// N+1 hisoblagichi ham SHU fixture'da: har qo'shimcha fixture yangi test
/// bazasi va yangi migratsiya yugurishi degani, ikkalasi esa bitta modulni
/// tekshiradi.
/// </summary>
public sealed class GroupChatRealtimeFactory : ZinnurApiFactory
{
    private readonly GroupChatCommandCounter _commands = new();

    public RecordingHubContext Hub { get; } = new();

    /// <summary>Shu fixture BAZASIDAGI guruh chati so'rovlarini sanashni boshlaydi.</summary>
    public void StartCounting() => _commands.Start(DatabaseName);

    /// <summary>Sanashni to'xtatadi va yig'ilgan SQL matnlarini qaytaradi.</summary>
    public IReadOnlyList<string> StopCounting() => _commands.Stop();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            // `AddSignalR` `IHubContext<>` ni OCHIQ generic sifatida yozadi;
            // yopiq (aniq tur uchun) ro'yxat undan ustun turadi.
            services.RemoveAll<IHubContext<GroupChatHub>>();

            services.AddSingleton(sp =>
            {
                Hub.UseScopes(sp.GetRequiredService<IServiceScopeFactory>());
                return Hub;
            });
            services.AddSingleton<IHubContext<GroupChatHub>>(
                sp => sp.GetRequiredService<RecordingHubContext>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _commands.Dispose();

        base.Dispose(disposing);
    }
}

/// <summary>Bitta tarqatish haqidagi yozuv.</summary>
/// <param name="RoomName">SignalR xonasi (kanal izolyatsiyasining dalili).</param>
/// <param name="BodyInDatabase">
/// Tarqatish PAYTIDA bazada turgan matn — <c>null</c> bo'lsa xabar hali
/// saqlanmagan (commit-then-send buzilgan).
/// </param>
public sealed record HubBroadcast(
    string RoomName,
    string Method,
    GroupChatMessageDto Message,
    string? BodyInDatabase);

/// <summary>
/// <c>IHubContext&lt;GroupChatHub&gt;</c> o'rnini bosuvchi yozib boruvchi.
///
/// ★ FAQAT <see cref="RecordingClients.Group"/> qo'llab-quvvatlanadi.
/// Qolgan yo'llar (<c>All</c>, <c>User</c>, ...) ATAYLAB istisno ko'taradi:
/// kimdir tarqatishni butun guruhga yoki hammaga o'zgartirsa, test
/// jimgina o'tib ketmasin.
/// </summary>
public sealed class RecordingHubContext : IHubContext<GroupChatHub>
{
    private readonly ConcurrentQueue<HubBroadcast> _broadcasts = new();

    private IServiceScopeFactory? _scopeFactory;

    /// <summary>Tarqatish paytida bazani o'qish uchun (commit-then-send dalili).</summary>
    internal void UseScopes(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Keyingi tarqatish istisno bilan yiqilsinmi.</summary>
    public bool FailBroadcast { get; set; }

    public IHubClients Clients => new RecordingClients(this);

    public IGroupManager Groups { get; } = new NoopGroupManager();

    public void Clear() => _broadcasts.Clear();

    public IReadOnlyList<HubBroadcast> Take() => [.. _broadcasts];

    private async Task RecordAsync(
        string roomName, string method, object?[] args, CancellationToken ct)
    {
        var message = args.Length > 0 ? args[0] as GroupChatMessageDto : null;

        if (message is not null)
        {
            _broadcasts.Enqueue(new HubBroadcast(
                roomName, method, message, await BodyInDatabaseAsync(message.Id, ct)));
        }

        if (FailBroadcast)
            throw new InvalidOperationException("Soxta SignalR nosozligi (test).");
    }

    /// <summary>
    /// YANGI scope — so'rovning o'z <c>DbContext</c> i emas: shu sababli
    /// o'qilgan qiymat haqiqatan BAZAGA yozilgan matn bo'ladi, kesh emas.
    /// </summary>
    private async Task<string?> BodyInDatabaseAsync(long messageId, CancellationToken ct)
    {
        if (_scopeFactory is null) return null;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.GroupChatMessages.AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => m.Body)
            .FirstOrDefaultAsync(ct);
    }

    private sealed class RecordingClients(RecordingHubContext owner) : IHubClients
    {
        public IClientProxy Group(string groupName) =>
            new RecordingProxy(owner, groupName);

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

        private static NotSupportedException Unsupported() => new(
            "Guruh chati xabari FAQAT (guruh, kanal) xonasiga yuborilishi kerak.");
    }

    private sealed class RecordingProxy(RecordingHubContext owner, string roomName) : IClientProxy
    {
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default) =>
            owner.RecordAsync(roomName, method, args, cancellationToken);
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(
            string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

/// <summary>
/// Guruh chati jadvallariga tegadigan SQL buyruqlarini sanaydi.
///
/// ★ NIMA UCHUN <c>DbCommandInterceptor</c> + DI EMAS: interceptor'ni
/// <c>services.AddSingleton&lt;IInterceptor&gt;(...)</c> orqali ulash bu
/// yerda ISHLAMADI — hech qanday buyruq yozilmadi va test BEKORGA yashil
/// bo'lib turdi ("0 == 0"). Sabab: <c>DbContextOptions</c> testda ilova
/// konteyneri qurilishidan oldin tayyorlanadi. <c>DiagnosticListener</c>
/// esa EF'ning O'ZI har buyruqda chiqaradigan hodisa — hech qanday DI
/// plumbing'iga bog'liq emas.
///
/// ★ IKKI FILTR MAJBURIY:
///   1) JADVAL nomi — fon xizmatlari (outbox, jadval generatori) parallel
///      ishlaydi va ularning so'rovlari sanoqqa tushardi;
///   2) BAZA nomi — <c>DiagnosticListener</c> BUTUN jarayon uchun umumiy,
///      parallel ishlayotgan boshqa test sinflari ham shu yerga tushardi.
///      Har fixture o'z bazasiga ega, shuning uchun bu filtr aniq ajratadi.
/// </summary>
public sealed class GroupChatCommandCounter : IDisposable
{
    private readonly ConcurrentQueue<string> _texts = new();

    private readonly IDisposable _allListeners;

    private IDisposable? _efListener;

    private volatile bool _recording;

    private volatile string _database = string.Empty;

    public GroupChatCommandCounter() =>
        _allListeners = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(this));

    public void Start(string databaseName)
    {
        _texts.Clear();
        _database = databaseName;
        _recording = true;
    }

    public IReadOnlyList<string> Stop()
    {
        _recording = false;
        return [.. _texts];
    }

    public void Dispose()
    {
        _efListener?.Dispose();
        _allListeners.Dispose();
    }

    private void Record(object? payload)
    {
        if (!_recording || payload is not CommandEventData data) return;

        var text = data.Command.CommandText;

        if (!text.Contains("GroupChatMessages", StringComparison.Ordinal)
            && !text.Contains("GroupChatReads", StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(data.Command.Connection?.Database, _database, StringComparison.Ordinal))
            return;

        _texts.Enqueue(text);
    }

    private sealed class ListenerObserver(GroupChatCommandCounter owner)
        : IObserver<DiagnosticListener>
    {
        public void OnCompleted()
        {
            // Kerak emas — EF listener'i jarayon umri davomida yashaydi.
        }

        public void OnError(Exception error)
        {
            // Kerak emas.
        }

        public void OnNext(DiagnosticListener value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Name == DbLoggerCategory.Name)
                owner._efListener = value.Subscribe(new CommandObserver(owner));
        }
    }

    private sealed class CommandObserver(GroupChatCommandCounter owner)
        : IObserver<KeyValuePair<string, object?>>
    {
        public void OnCompleted()
        {
            // Kerak emas.
        }

        public void OnError(Exception error)
        {
            // Kerak emas.
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuting.Name)
                owner.Record(value.Value);
        }
    }
}
