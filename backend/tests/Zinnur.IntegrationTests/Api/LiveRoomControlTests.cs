using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.IntegrationTests.Recordings;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// XONANI YOPISH VA O'QUVCHINI CHIQARISH (2026-10-31)
/// ════════════════════════════════════════════════════════════════════════
///
/// LiveKit SOXTA (<see cref="RoomControlSpy"/>): bizga uning javobi emas,
/// BIZNING xatti-harakatimiz kerak — dars tugaganda qaysi xonalar yopildi,
/// kim chiqarildi, chiqarilgan o'quvchi qayta token oladimi.
/// </summary>
public sealed class LiveRoomControlTests(RoomControlFactory factory)
    : IClassFixture<RoomControlFactory>
{
    /// <summary>
    /// 🔴 Dars tugadi — asosiy xona VA ochiq ichki guruh xonalari yopiladi,
    /// ichki guruh holati tozalanadi.
    /// </summary>
    [Fact]
    public async Task EndingTheLesson_DeletesMainAndBreakoutRooms()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rcend");
        var sessionId = await RecordingWorld.AddSessionAsync(factory, world.GroupId, SessionStatus.Live);
        var room = await RoomOfAsync(sessionId);

        var cache = factory.Services.GetRequiredService<ICacheService>();
        await cache.SetAsync(
            BreakoutService.StateKey(sessionId),
            new BreakoutState(
                DateTimeOffset.UtcNow,
                null,
                [new BreakoutRoomState(1, [world.Student.Id], false), new BreakoutRoomState(2, [], false)]),
            TimeSpan.FromMinutes(10));

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/end", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        factory.Rooms.Deleted.Should().Contain(room)
            .And.Contain($"{room}-b1")
            .And.Contain($"{room}-b2");
        (await cache.GetAsync<BreakoutState>(BreakoutService.StateKey(sessionId))).Should().BeNull();
    }

    /// <summary>
    /// Ustoz o'quvchini chiqaradi: LiveKit'dan uziladi va shu darsga qayta
    /// token OLMAYDI (sahifani yangilab qaytib kira olmasin).
    /// </summary>
    [Fact]
    public async Task HostRemovesStudent_AndStudentCannotRejoin()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rcrem");
        var sessionId = await RecordingWorld.AddSessionAsync(factory, world.GroupId, SessionStatus.Live);
        var room = await RoomOfAsync(sessionId);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        // Chiqarishdan oldin token beriladi — test haqiqiy farqni ko'rsatsin.
        (await TokenAsync(student, sessionId)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await RemoveAsync(teacher, sessionId, world.Student.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));
        factory.Rooms.Removed.Should().Contain($"{room}|{world.Student.Id}");

        (await TokenAsync(student, sessionId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Xodimni chiqarib bo'lmaydi; o'quvchi boshqani chiqara olmaydi.</summary>
    [Fact]
    public async Task OnlyHostCanRemove_AndOnlyStudents()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rcrule");
        var sessionId = await RecordingWorld.AddSessionAsync(factory, world.GroupId, SessionStatus.Live);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        (await RemoveAsync(teacher, sessionId, world.Curator.Id)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "kuratorni darsdan chiqarib bo'lmaydi");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        (await RemoveAsync(student, sessionId, world.Teacher.Id)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Guruhdan chiqarilgan o'quvchi o'sha paytdagi JONLI darsdan ham uziladi.
    /// </summary>
    [Fact]
    public async Task RemovingFromGroup_DisconnectsFromLiveLesson()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rcgrp");
        var sessionId = await RecordingWorld.AddSessionAsync(factory, world.GroupId, SessionStatus.Live);
        var room = await RoomOfAsync(sessionId);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await WorldBuilder.RemoveMemberAsync(admin, world.GroupId, world.Student.Id);

        response.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(response));
        factory.Rooms.Removed.Should().Contain($"{room}|{world.Student.Id}");
    }

    /// <summary>
    /// 🔴 TOKEN TORAYTIRILGAN (2026-10-31): brauzer tokenida `roomAdmin` HECH
    /// KIMDA yo'q, ma'lumot kanali faqat hostda. O'quvchi baribir efirga
    /// chiqadi (kamera/mikrofon).
    /// </summary>
    [Fact]
    public async Task JoinTokens_HaveNoRoomAdmin_AndDataOnlyForHost()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rctok");
        var sessionId = await RecordingWorld.AddSessionAsync(factory, world.GroupId, SessionStatus.Live);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var studentGrant = await GrantAsync(student, sessionId);
        var teacherGrant = await GrantAsync(teacher, sessionId);

        studentGrant.GetProperty("roomAdmin").GetBoolean().Should().BeFalse();
        studentGrant.GetProperty("canPublishData").GetBoolean().Should().BeFalse();
        studentGrant.GetProperty("canPublish").GetBoolean().Should().BeTrue();

        teacherGrant.GetProperty("roomAdmin").GetBoolean().Should().BeFalse(
            "boshqaruv backend orqali — brauzer tokeni host tekshiruvini chetlab o'tmasin");
        teacherGrant.GetProperty("canPublishData").GetBoolean().Should().BeTrue();
    }

    // ================================================================= yordamchilar

    private static async Task<System.Text.Json.JsonElement> GrantAsync(HttpClient client, long sessionId)
    {
        var response = await TokenAsync(client, sessionId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("token").GetString()!;

        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        using var claims = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));

        return claims.RootElement.GetProperty("video").Clone();
    }

    private Task<string> RoomOfAsync(long sessionId) =>
        factory.WithDbAsync(async db =>
            (await db.LiveSessions.FindAsync(sessionId))!.RoomName);

    private static Task<HttpResponseMessage> TokenAsync(HttpClient client, long sessionId) =>
        client.PostAsync(new Uri($"/api/v1/live-sessions/{sessionId}/token", UriKind.Relative), null);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, long sessionId, long userId) =>
        client.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/participants/{userId}/remove", UriKind.Relative), null);
}

/// <summary>LiveKit xona boshqaruvi soxtasi bilan fixture.</summary>
public sealed class RoomControlFactory : ZinnurApiFactory
{
    public RoomControlSpy Rooms { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILiveKitRoomControl>();
            services.AddSingleton<ILiveKitRoomControl>(Rooms);
        });
    }
}

/// <summary>Chaqiruvlarni yozib boradi, doim muvaffaqiyat qaytaradi.</summary>
public sealed class RoomControlSpy : ILiveKitRoomControl
{
    public ConcurrentQueue<string> Deleted { get; } = new();

    /// <summary><c>xona|identity</c> ko'rinishida.</summary>
    public ConcurrentQueue<string> Removed { get; } = new();

    public Task<RoomControlResult> MuteTrackAsync(
        string roomName, string identity, ParticipantMediaSource source, CancellationToken ct = default) =>
        Task.FromResult(RoomControlResult.Ok());

    public Task<RoomControlResult> RemoveParticipantAsync(
        string roomName, string identity, CancellationToken ct = default)
    {
        Removed.Enqueue($"{roomName}|{identity}");
        return Task.FromResult(RoomControlResult.Ok());
    }

    public Task<RoomControlResult> DeleteRoomAsync(string roomName, CancellationToken ct = default)
    {
        Deleted.Enqueue(roomName);
        return Task.FromResult(RoomControlResult.Ok());
    }
}
