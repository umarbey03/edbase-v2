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
