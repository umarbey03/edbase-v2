using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// KURATOR YOZISHMASI — RUXSAT VA OQIM
/// ========================================================================
///
/// Bu modulning eng qimmat xatosi ruxsatda bo'lardi: shaxsiy yozishma
/// begonaga ochilib qolishi. Shuning uchun testlarning yarmi aynan
/// RUXSAT MATRITSASI:
///
///   • o'quvchi boshqa kurator bilan yozisha olmaydi;
///   • kurator o'ziga biriktirilmagan o'quvchi bilan yozisha olmaydi;
///   • begona o'quvchi suhbatga umuman kira olmaydi;
///   • admin ham avtomatik kira olmaydi (shaxsiy yozishma).
/// </summary>
public sealed class DirectMessageEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= suhbatlar

    /// <summary>
    /// O'quvchiga guruh orqali biriktirilgan kurator ro'yxatda ko'rinadi —
    /// hali BITTA ham xabar yozilmagan bo'lsa ham. Aks holda o'quvchi
    /// birinchi savolini yozadigan joy topa olmasdi.
    /// </summary>
    [Fact]
    public async Task Conversations_ForStudent_ContainsCurator_EvenWithNoMessages()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dm");

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var conversations = await client.GetFromJsonAsync<List<ConversationResponse>>(
            "/api/v1/messages/conversations");

        conversations.Should().ContainSingle();
        conversations![0].PeerId.Should().Be(world.Curator.Id);
        conversations[0].PeerRole.Should().Be(nameof(UserRole.Assistant));
        conversations[0].LastMessageId.Should().BeNull();
        conversations[0].UnreadCount.Should().Be(0);
    }

    /// <summary>Kuratorsiz o'quvchida ro'yxat BO'SH — 404 emas (ekran baribir ochiladi).</summary>
    [Fact]
    public async Task Conversations_ForStudentWithoutCurator_IsEmpty()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var loner = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "dmsolo");

        using var client = await WorldBuilder.ClientAsync(factory, loner);

        var conversations = await client.GetFromJsonAsync<List<ConversationResponse>>(
            "/api/v1/messages/conversations");

        conversations.Should().BeEmpty();
    }

    /// <summary>Kurator o'z o'quvchilarini ko'radi, guruh nomi bilan.</summary>
    [Fact]
    public async Task Conversations_ForCurator_ListsAssignedStudents()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dmcur");
        var second = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "dmcur2");

        using var client = await WorldBuilder.ClientAsync(factory, world.Curator);

        var conversations = await client.GetFromJsonAsync<List<ConversationResponse>>(
            "/api/v1/messages/conversations");

        conversations.Should().HaveCount(2);
        conversations!.Select(c => c.PeerId).Should().BeEquivalentTo(
            new[] { world.Student.Id, second.Id });
        conversations.Should().OnlyContain(c => c.GroupName == world.GroupName);
    }

    // ================================================================= oqim

    /// <summary>
    /// To'liq oqim: o'quvchi savol yozadi -> kuratorda o'qilmagan paydo
    /// bo'ladi -> kurator o'qiydi va javob beradi -> o'quvchida
    /// o'qilmagan paydo bo'ladi.
    /// </summary>
    [Fact]
    public async Task FullRoundTrip_QuestionThenAnswer_TracksUnreadOnBothSides()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dmflow");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        using var curator = await WorldBuilder.ClientAsync(factory, world.Curator);

        // 1) O'quvchi savol yozadi
        var sent = await SendAsync(student, world.Curator.Id, "Vazifani tushunmadim");
        sent.Mine.Should().BeTrue();
        sent.Body.Should().Be("Vazifani tushunmadim");

        // 2) Kuratorda o'qilmagan bor
        var curatorThread = await ThreadAsync(curator, world.Student.Id);
        curatorThread.UnreadCount.Should().Be(1);
        curatorThread.Items.Should().ContainSingle();
        curatorThread.Items[0].Mine.Should().BeFalse("xabarni o'quvchi yozgan");

        // 3) ★ O'QISH HOLATNI O'ZGARTIRMAYDI — sanoq hamon 1.
        var again = await ThreadAsync(curator, world.Student.Id);
        again.UnreadCount.Should().Be(1, "GET xavfsiz amal — o'qilgan deb belgilamaydi");

        // 4) Aniq "o'qildi" amali
        var read = await curator.PostAsync(
            new Uri($"/api/v1/messages/conversations/{world.Student.Id}/read", UriKind.Relative),
            content: null);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var readResult = await read.Content.ReadFromJsonAsync<MarkReadResponse>();
        readResult!.MarkedCount.Should().Be(1);
        readResult.UnreadCount.Should().Be(0);

        // IDEMPOTENT: ikkinchi marta hech nima belgilanmaydi
        var readTwice = await curator.PostAsync(
            new Uri($"/api/v1/messages/conversations/{world.Student.Id}/read", UriKind.Relative),
            content: null);

        (await readTwice.Content.ReadFromJsonAsync<MarkReadResponse>())!.MarkedCount.Should().Be(0);

        // 5) Kurator javob beradi -> endi O'QUVCHIDA o'qilmagan
        await SendAsync(curator, world.Student.Id, "Darsni qayta ko'ring");

        var studentThread = await ThreadAsync(student, world.Curator.Id);
        studentThread.UnreadCount.Should().Be(1);
        studentThread.Items.Should().HaveCount(2);

        // Tartib: ESKIDAN YANGIGA
        studentThread.Items[0].Body.Should().Be("Vazifani tushunmadim");
        studentThread.Items[1].Body.Should().Be("Darsni qayta ko'ring");

        // ★ "Ikki belgi": o'quvchining xabarini kurator o'qigan
        studentThread.Items[0].ReadByPeer.Should().BeTrue();
    }

    /// <summary>
    /// ★ KURSORLI SAHIFALASH: yangi sahifa oxirgi xabarlarni beradi,
    /// `beforeId` bilan eskiroqlariga o'tiladi va xabar TAKRORLANMAYDI.
    /// </summary>
    [Fact]
    public async Task Thread_PaginatesByCursor_WithoutDuplicates()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dmpage");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        for (var i = 1; i <= 7; i++)
            await SendAsync(student, world.Curator.Id, "Xabar " + i);

        var firstPage = await ThreadAsync(student, world.Curator.Id, take: 3);

        firstPage.Items.Should().HaveCount(3);
        firstPage.Items.Select(m => m.Body).Should().Equal("Xabar 5", "Xabar 6", "Xabar 7");
        firstPage.HasMore.Should().BeTrue();
        firstPage.NextBeforeId.Should().NotBeNull();

        var secondPage = await ThreadAsync(
            student, world.Curator.Id, take: 3, beforeId: firstPage.NextBeforeId);

        secondPage.Items.Select(m => m.Body).Should().Equal("Xabar 2", "Xabar 3", "Xabar 4");
        secondPage.HasMore.Should().BeTrue();

        var lastPage = await ThreadAsync(
            student, world.Curator.Id, take: 3, beforeId: secondPage.NextBeforeId);

        lastPage.Items.Select(m => m.Body).Should().Equal("Xabar 1");
        lastPage.HasMore.Should().BeFalse();
        lastPage.NextBeforeId.Should().BeNull();
    }

    [Fact]
    public async Task Send_EmptyBody_ReturnsError()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dmempty");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await student.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{world.Curator.Id}/messages",
            new { body = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "bo'sh xabar Domain qoidasini buzadi");
    }

    /// <summary>Mavjud bo'lmagan dars kontekst sifatida yuborilsa — 400, 500 emas.</summary>
    [Fact]
    public async Task Send_WithUnknownLessonContext_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dmlesson");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await student.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{world.Curator.Id}/messages",
            new { body = "Savol", moduleLessonId = 999_999L });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================= ruxsat

    /// <summary>★ O'quvchi BOSHQA guruhning kuratori bilan yozisha olmaydi.</summary>
    [Fact]
    public async Task Send_ToForeignCurator_ReturnsForbidden()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "dmmine");
        var other = await WorldBuilder.CreateAsync(factory, "dmforeign");

        using var student = await WorldBuilder.ClientAsync(factory, mine.Student);

        var response = await student.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{other.Curator.Id}/messages",
            new { body = "Salom" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>★ Kurator o'ziga biriktirilmagan o'quvchi bilan yozisha olmaydi.</summary>
    [Fact]
    public async Task Thread_CuratorReadingUnassignedStudent_ReturnsForbidden()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "dmcura");
        var other = await WorldBuilder.CreateAsync(factory, "dmcurb");

        using var curator = await WorldBuilder.ClientAsync(factory, mine.Curator);

        var response = await curator.GetAsync(new Uri(
            $"/api/v1/messages/conversations/{other.Student.Id}/messages", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>★ Begona o'quvchi suhbatni O'QIY OLMAYDI.</summary>
    [Fact]
    public async Task Thread_AsOutsiderStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dmout");
        var outsider = await WorldBuilder.CreateAsync(factory, "dmout2");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        await SendAsync(student, world.Curator.Id, "Shaxsiy savol");

        using var stranger = await WorldBuilder.ClientAsync(factory, outsider.Student);

        var response = await stranger.GetAsync(new Uri(
            $"/api/v1/messages/conversations/{world.Curator.Id}/messages", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ★ ADMIN HAM ISTISNO EMAS. Shaxsiy yozishma ikki kishilik: uni
    /// "hamma narsani ko'radigan" rol ham JIMGINA o'qiy olmasligi kerak.
    /// Nazorat kerak bo'lsa — alohida, oshkora audit endpointi bilan.
    /// </summary>
    [Fact]
    public async Task Thread_AsAdmin_IsAlsoForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "dmadmin");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        await SendAsync(student, world.Curator.Id, "Shaxsiy savol");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.GetAsync(new Uri(
            $"/api/v1/messages/conversations/{world.Curator.Id}/messages", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Conversations_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/v1/messages/conversations", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= yordamchi

    private static async Task<MessageResponse> SendAsync(
        HttpClient client, long peerId, string body)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{peerId}/messages", new { body });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<MessageResponse>())!;
    }

    private static async Task<ThreadResponse> ThreadAsync(
        HttpClient client, long peerId, int take = 50, long? beforeId = null)
    {
        var url = $"/api/v1/messages/conversations/{peerId}/messages?take={take}"
                  + (beforeId is { } cursor ? $"&beforeId={cursor}" : string.Empty);

        var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<ThreadResponse>())!;
    }

    private sealed record ConversationResponse(
        long PeerId,
        string PeerName,
        string PeerRole,
        string? GroupName,
        long? LastMessageId,
        string? LastMessagePreview,
        DateTimeOffset? LastMessageAt,
        bool? LastMessageMine,
        int UnreadCount);

    private sealed record ThreadResponse(
        long PeerId,
        string PeerName,
        List<MessageResponse> Items,
        bool HasMore,
        long? NextBeforeId,
        int UnreadCount);

    private sealed record MessageResponse(
        long Id,
        long SenderId,
        string SenderName,
        bool Mine,
        string Body,
        long? ModuleLessonId,
        string? ModuleLessonName,
        DateTimeOffset SentAt,
        bool ReadByPeer);

    private sealed record MarkReadResponse(int MarkedCount, int UnreadCount);
}
