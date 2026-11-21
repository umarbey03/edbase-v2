using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zinnur.Application.Community;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// HAMJAMIYAT (2026-11-21)
/// ════════════════════════════════════════════════════════════════════
///
/// Tekshiriladi:
///   - o'chiq paytda o'quvchi kirmaydi, kurator (moderator) kiradi;
///   - yoqilgach obunasi faol o'quvchi yozadi; obunasiz o'quvchi — 403;
///   - ism "Ism F.", telefon va havola rad etiladi, tezlik cheklovi;
///   - javob (reply), o'qilmaganlar soni;
///   - moderator xabarni o'chiradi va o'quvchini to'xtatadi.
/// </summary>
public sealed class CommunityTests(ZinnurApiFactory factory) : IClassFixture<ZinnurApiFactory>
{
    private const string Base = "/api/v1/community";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Community_access_messages_and_moderation()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var curator = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, "cm-kurator");
        var aziza = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "cm-a");
        var bobur = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "cm-b");
        var outsider = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "cm-tashqi");

        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"cm kurs {Guid.NewGuid():N}"[..30] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            foreach (var id in new[] { aziza.Id, bobur.Id })
            {
                db.SubscriptionPeriods.Add(new SubscriptionPeriod
                {
                    StudentId = id,
                    CourseId = course.Id,
                    StartsAt = now.AddDays(-1),
                    EndsAt = now.AddDays(29),
                    CreatedById = curator.Id,
                });
            }

            db.Users.Single(u => u.Id == aziza.Id).FullName = "Karimova Aziza";
            db.SelfStudyCuratorPoolMembers.Add(SelfStudyCuratorPoolMember.Create(curator.Id, null, curator.Id, now));
            return await db.SaveChangesAsync();
        });

        using var a = await WorldBuilder.ClientAsync(factory, aziza);
        using var b = await WorldBuilder.ClientAsync(factory, bobur);
        using var c = await WorldBuilder.ClientAsync(factory, curator);
        using var o = await WorldBuilder.ClientAsync(factory, outsider);

        // 1) O'chiq: o'quvchi kirmaydi, kurator kiradi va xonani tayyorlaydi.
        (await a.GetAsync($"{Base}/messages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var seed = await c.PostAsJsonAsync($"{Base}/messages", new { body = "Kun savoli: qaysi harf eng qiyin bo'ldi?" });
        seed.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(seed));

        var setting = await admin.PutAsJsonAsync("/api/v1/settings/community.enabled", new { value = "true" });
        setting.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(setting));

        // 2) Yoqildi: faol obunali o'quvchi ko'radi; obunasiz — yo'q.
        var access = await a.GetFromJsonAsync<CommunityAccessDto>($"{Base}/access", Json);
        access!.CanRead.Should().BeTrue();
        access.CanWrite.Should().BeTrue();
        access.IsModerator.Should().BeFalse();
        access.Unread.Should().Be(1);
        (await o.GetAsync($"{Base}/messages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var seedId = (await seed.Content.ReadFromJsonAsync<CommunityMessageDto>(Json))!.Id;
        var reply = await a.PostAsJsonAsync($"{Base}/messages", new { body = "Menga Ayn qiyin bo'ldi", replyToId = seedId });
        reply.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(reply));
        var replyDto = (await reply.Content.ReadFromJsonAsync<CommunityMessageDto>(Json))!;
        replyDto.AuthorName.Should().Be("Aziza K.");
        replyDto.ReplyTo!.Id.Should().Be(seedId);

        // 3) Tezlik cheklovi; telefon va havola — rad.
        (await a.PostAsJsonAsync($"{Base}/messages", new { body = "yana" }))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await b.PostAsJsonAsync($"{Base}/messages", new { body = "Menga yozing +998 90 123 45 67" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await b.PostAsJsonAsync($"{Base}/messages", new { body = "kanalim t.me/zzz" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var page = await b.GetFromJsonAsync<CommunityPageDto>($"{Base}/messages", Json);
        page!.Items.Should().HaveCount(2);
        page.Items[0].AuthorKind.Should().Be("Staff");

        // 4) Moderatsiya: o'quvchi boshqaning xabarini o'chira olmaydi, kurator o'chiradi.
        (await b.DeleteAsync($"{Base}/messages/{replyDto.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.DeleteAsync($"{Base}/messages/{replyDto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDelete = await b.GetFromJsonAsync<CommunityPageDto>($"{Base}/messages", Json);
        afterDelete!.Items.Single(m => m.Id == replyDto.Id).Deleted.Should().BeTrue();
        afterDelete.Items.Single(m => m.Id == replyDto.Id).Body.Should().BeEmpty();

        (await b.PutAsJsonAsync($"{Base}/mutes/{aziza.Id}", new { hours = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "o'quvchi moderator emas");
        (await c.PutAsJsonAsync($"{Base}/mutes/{aziza.Id}", new { hours = 2, reason = "qoida" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await a.GetFromJsonAsync<CommunityAccessDto>($"{Base}/access", Json))!.CanWrite.Should().BeFalse();
        await Task.Delay(TimeSpan.FromSeconds(CommunityService.MinSecondsBetween + 0.5));
        (await a.PostAsJsonAsync($"{Base}/messages", new { body = "salom" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await c.DeleteAsync($"{Base}/mutes/{aziza.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await a.PostAsJsonAsync($"{Base}/messages", new { body = "salom" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Uluğbek Xursanaliyev", "Uluğbek X.")]
    [InlineData("Xursanaliyev Ulug'bek", "Ulug'bek X.")]
    [InlineData("Karimova Aziza", "Aziza K.")]
    [InlineData("Dilnoza", "Dilnoza")]
    [InlineData("", "O'quvchi")]
    public void Display_name_hides_the_surname(string full, string expected) =>
        CommunityNames.Display(full).Should().Be(expected);
}
