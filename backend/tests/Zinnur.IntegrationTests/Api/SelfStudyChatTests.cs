using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL — 24/7 KURATOR CHATI (2026-11-18)
/// ════════════════════════════════════════════════════════════════════
///
/// Tekshiriladi: o'quvchi kuratoriga yozadi va u Telegram'da turtki oladi
/// (ketma-ket xabarlarga BITTA), ovozli xabarning davomiyligi saqlanadi.
/// </summary>
public sealed class SelfStudyChatTests(StorageBackedApiFactory factory)
    : IClassFixture<StorageBackedApiFactory>
{
    [Fact]
    public async Task Curator_gets_one_telegram_nudge_per_burst()
    {
        var (student, curator) = await SetUpAsync("ssc-chat-nudge");
        var chatId = ProfileWorldBuilder.NextTelegramId();
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == curator.Id);
            user.LinkTelegram(chatId, "ssc_kurator", DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });

        using var client = await WorldBuilder.ClientAsync(factory, student);
        for (var i = 0; i < 3; i++)
        {
            var sent = await client.PostAsJsonAsync(
                $"/api/v1/messages/conversations/{curator.Id}/messages", new { body = $"Savol {i}" });
            sent.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(sent));
        }

        var nudges = await factory.WithDbAsync(db => db.MessageOutbox
            .CountAsync(m => m.TemplateKey == TelegramTemplates.DirectMessageReceived
                          && m.RecipientUserId == curator.Id));
        nudges.Should().Be(1, "ketma-ket xabarlar uchun bitta turtki");
    }

    [Fact]
    public async Task Voice_message_keeps_its_duration()
    {
        var (student, curator) = await SetUpAsync("ssc-chat-voice");
        using var client = await WorldBuilder.ClientAsync(factory, student);

        // Eng kichik Ogg sarlavhasi — tur MAZMUNDAN aniqlanadi.
        var bytes = new byte[256];
        Encoding.ASCII.GetBytes("OggS").CopyTo(bytes, 0);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        content.Add(file, "files", "ovoz.ogg");
        content.Add(new StringContent("42"), "durationSec");

        var response = await client.PostAsync(
            $"/api/v1/messages/conversations/{curator.Id}/messages/attachments", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(response));

        var saved = await factory.WithDbAsync(db => db.DirectMessageAttachments
            .Where(a => a.Message!.StudentId == student.Id)
            .Select(a => new { a.Kind, a.DurationSec })
            .SingleAsync());
        saved.Kind.Should().Be(AttachmentKind.Audio);
        saved.DurationSec.Should().Be(42);
    }

    [Fact]
    public async Task Student_can_send_only_one_voice_message_per_day()
    {
        var (student, curator) = await SetUpAsync("ssc-chat-limit");
        using var client = await WorldBuilder.ClientAsync(factory, student);

        var first = await PostVoiceAsync(client, curator.Id, 30);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(first));

        var quota = await client.GetFromJsonAsync<QuotaRow>("/api/v1/messages/voice-quota");
        quota!.PerDay.Should().Be(1);
        quota.UsedToday.Should().Be(1);
        quota.MaxSeconds.Should().Be(90);

        var second = await PostVoiceAsync(client, curator.Id, 30);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "kuniga bitta ovozli xabar");

        // Matn cheklanmaydi.
        var text = await client.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{curator.Id}/messages", new { body = "Matnli savol" });
        text.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(text));

        // Kurator javobi cheklanmaydi.
        using var staff = await WorldBuilder.ClientAsync(factory, curator);
        (await PostVoiceAsync(staff, student.Id, 200)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostVoiceAsync(staff, student.Id, 200)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Student_voice_longer_than_limit_is_rejected()
    {
        var (student, curator) = await SetUpAsync("ssc-chat-long");
        using var client = await WorldBuilder.ClientAsync(factory, student);

        var tooLong = await PostVoiceAsync(client, curator.Id, 150);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noDuration = await PostVoiceAsync(client, curator.Id, null);
        noDuration.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<HttpResponseMessage> PostVoiceAsync(HttpClient client, long peerId, int? seconds)
    {
        var bytes = new byte[256];
        Encoding.ASCII.GetBytes("OggS").CopyTo(bytes, 0);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        content.Add(file, "files", "ovoz.ogg");
        if (seconds is { } value)
            content.Add(new StringContent(value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "durationSec");

        return await client.PostAsync($"/api/v1/messages/conversations/{peerId}/messages/attachments", content);
    }

    private sealed record QuotaRow(int? PerDay, int UsedToday, int MaxSeconds);

    private async Task<(TestUser Student, TestUser Curator)> SetUpAsync(string prefix)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var curator = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-kurator");
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-mustaqil");

        var courseId = await factory.WithDbAsync(async db =>
        {
            var course = new Zinnur.Domain.Entities.Course { Name = $"{prefix} kurs {Guid.NewGuid():N}"[..40] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            return course.Id;
        });

        var plan = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
        {
            name = prefix + " oylik",
            courseId,
            months = 1,
            price = 299_000m,
        });
        plan.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(plan));
        var planId = (await plan.Content.ReadFromJsonAsync<IdRow>())!.Id;

        (await admin.PostAsJsonAsync("/api/v1/subscriptions", new { studentId = student.Id, planId, method = "Cash" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var assign = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{student.Id}", new { curatorId = curator.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(assign));

        return (student, curator);
    }

    private sealed record IdRow(long Id);
}
