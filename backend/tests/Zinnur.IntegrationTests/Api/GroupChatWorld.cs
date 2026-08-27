using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Guruh chati endpointlarining test shartnomasi — HTTP javoblarining
/// shakli va ularga murojaat qiluvchi yordamchi metodlar.
///
/// ★ NIMA UCHUN ALOHIDA FAYL: bu shakllardan IKKI test sinfi foydalanadi
/// (ruxsat/oqim testlari va realtime testlari). Har sinfda qayta yozilsa,
/// birida maydon nomi o'zgarib qolar va test "sababsiz" yiqilardi.
///
/// ★ ENUM MAYDONLARI ATAYLAB <c>string</c>: API enum'ni SATR ko'rinishida
/// qaytaradi (<c>"Curator"</c>, raqam emas). Test turini <c>GroupChatChannel</c>
/// qilib qo'ysam, System.Text.Json satrni jimgina enum'ga o'girib berardi va
/// shartnomaning aynan shu qismi — satrmi yoki raqammi — sinovsiz qolardi.
/// </summary>
internal sealed record GroupChatPageResponse(
    long GroupId,
    string GroupName,
    string Channel,
    IReadOnlyList<string> AvailableChannels,
    IReadOnlyList<GroupChatMessageResponse> Items,
    bool HasMore,
    long? NextBeforeId,
    int UnreadCount);

internal sealed record GroupChatMessageResponse(
    long Id,
    long GroupId,
    string Channel,
    long SenderId,
    string SenderName,
    string SenderRole,
    string Body,
    DateTimeOffset SentAt);

/// <summary>
/// "Chatlar" ro'yxatining bitta qatori.
///
/// ★ <c>GroupType</c> — SATR (yuqoridagi umumiy qoida): API enum'ni
/// <c>"Group"</c> / <c>"Individual"</c> ko'rinishida qaytaradi. Kurator
/// TURIDAGI guruh bu ro'yxatda HECH QACHON ko'rinmaydi.
/// </summary>
internal sealed record GroupChatThreadResponse(
    long GroupId,
    string GroupName,
    string Channel,
    long? LastMessageId,
    string? LastMessagePreview,
    string? LastMessageSenderName,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    /* ===== R38 · filtr uchun qo'shilgan ustunlar ===== */
    string GroupType,
    long? CategoryId,
    string? CategoryName);

internal sealed record GroupChatReadResponse(
    long GroupId,
    string Channel,
    long LastReadMessageId,
    int UnreadCount,
    bool Changed);

/// <summary>Endpointlarga murojaatning YAGONA joyi (URL satrlari bir marta yoziladi).</summary>
internal static class GroupChatApi
{
    private const string Root = "/api/v1/group-chat";

    public static Uri MessagesUrl(
        long groupId, GroupChatChannel? channel = null, int? take = null, long? beforeId = null)
    {
        var url = $"{Root}/groups/{groupId}/messages";
        var query = new List<string>(3);

        if (channel is { } value) query.Add($"channel={value}");
        if (take is { } size) query.Add($"take={size}");
        if (beforeId is { } cursor) query.Add($"beforeId={cursor}");

        if (query.Count > 0) url += "?" + string.Join('&', query);

        return new Uri(url, UriKind.Relative);
    }

    public static string SendUrl(long groupId) => $"{Root}/groups/{groupId}/messages";

    public static string ReadUrl(long groupId) => $"{Root}/groups/{groupId}/read";

    public static async Task<GroupChatPageResponse> MessagesAsync(
        HttpClient client,
        long groupId,
        GroupChatChannel? channel = null,
        int? take = null,
        long? beforeId = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.GetAsync(MessagesUrl(groupId, channel, take, beforeId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<GroupChatPageResponse>())!;
    }

    /// <summary>
    /// "Chatlar" ro'yxati.
    /// </summary>
    /// <param name="type">R38 filtri: <c>Group</c> yoki <c>Individual</c>.</param>
    /// <param name="categoryId">R38 filtri: o'quv yo'nalishi.</param>
    public static async Task<IReadOnlyList<GroupChatThreadResponse>> ThreadsAsync(
        HttpClient client, GroupType? type = null, long? categoryId = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.GetAsync(ThreadsUrl(type, categoryId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content
            .ReadFromJsonAsync<IReadOnlyList<GroupChatThreadResponse>>())!;
    }

    /// <summary>
    /// R38 · ro'yxat manzili (filtr bilan). Ajratilgan, chunki 400 kutadigan
    /// testlar javob KODINI tekshiradi va yuqoridagi metod 200 talab qiladi.
    /// </summary>
    public static Uri ThreadsUrl(GroupType? type = null, long? categoryId = null)
    {
        var url = $"{Root}/threads";
        var query = new List<string>(2);

        if (type is { } value) query.Add($"type={value}");
        if (categoryId is { } id) query.Add($"categoryId={id}");

        if (query.Count > 0) url += "?" + string.Join('&', query);

        return new Uri(url, UriKind.Relative);
    }

    public static async Task<GroupChatMessageResponse> SendAsync(
        HttpClient client, long groupId, string body, GroupChatChannel? channel = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync(
            SendUrl(groupId),
            new { channel = channel?.ToString(), body });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<GroupChatMessageResponse>())!;
    }

    public static async Task<GroupChatReadResponse> MarkReadAsync(
        HttpClient client,
        long groupId,
        GroupChatChannel? channel = null,
        long? upToMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync(
            ReadUrl(groupId),
            new { channel = channel?.ToString(), upToMessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<GroupChatReadResponse>())!;
    }

    /// <summary>
    /// Mavjud ustoz/kuratorga YANA bitta guruh qo'shadi va unga YANGI
    /// o'quvchi a'zo qiladi.
    ///
    /// ★ N+1 testi uchun MAJBURIY: bitta guruhda so'rovlar soni har qanday
    /// amalga oshirishda bir xil bo'ladi. Farq FAQAT ikkinchi guruh
    /// qo'shilganda ko'rinadi — naif kod u yerda so'rovlar sonini ikkiga
    /// ko'paytiradi.
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 NEGA `world.Student` EMAS, YANGI O'QUVCHI (2026-08-22)
    ///
    /// Ilgari bu metod AYNI o'quvchini ikkinchi guruhga ham qo'shardi.
    /// 2026-08-17 dan bu MUMKIN EMAS: "o'quvchi bir vaqtda faqatgina
    /// bitta o'qituvchi guruhida bo'lishi mumkin" (loyiha egasi,
    /// <c>GroupService.AddMemberAsync</c>) — chaqiruv 409 qaytaradi.
    ///
    /// ★ TESTLARNING MA'NOSI SAQLANADI, chunki bu metoddan foydalanadigan
    ///   testlar ikkinchi guruhni USTOZ / KURATOR / ADMIN ko'zi bilan
    ///   ko'radi, o'quvchi ko'zi bilan emas. Xodim esa istagancha ko'p
    ///   guruhga ega bo'la oladi — "40 guruhli ustoz" aynan shu.
    ///
    /// ⚠️ SHU SABABLI O'QUVCHI SHOXINI IKKI GURUH BILAN SINAB BO'LMAYDI.
    ///   U endi domen bo'yicha ERISHIB BO'LMAYDIGAN holat, ya'ni uni
    ///   sinash — mavjud bo'lmagan xatti-harakatni sinash bo'lardi.
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    public static async Task<long> AddGroupAsync(
        Infrastructure.ZinnurApiFactory factory, StudentWorld world, string prefix)
    {
        ArgumentNullException.ThrowIfNull(world);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync("/api/v1/groups", new
        {
            name = $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}",
            startDate = "2026-01-05",
            weekdays = new[] { "Tuesday", "Thursday" },
            startTime = "17:00:00",
            teacherId = world.Teacher.Id,
            assistantId = world.Curator.Id,
            courseMonths = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(response));

        var created = (await response.Content.ReadFromJsonAsync<CreatedGroupResponse>())!;

        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix);

        var member = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{created.Group.Id}/members", new { studentId = student.Id });

        member.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(member));

        return created.Group.Id;
    }

    private sealed record CreatedGroupResponse(GroupIdBrief Group);

    private sealed record GroupIdBrief(long Id);
}
