using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.SelfStudy;
using Zinnur.Application.SelfStudy.Nudges;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.IntegrationTests.Jobs;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL FAOLSIZLIK ESLATMALARI — HAQIQIY BAZADA (2026-11-25)
/// ════════════════════════════════════════════════════════════════════
///
/// Soat test boshqaradi (kechki oyna va «necha kun faolsiz»). Tekshiriladi:
///   - panelda o'chiq bo'lsa hech narsa ketmaydi;
///   - 1-kun → o'quvchiga, 3-kun → kuratorga, 5-kun → tugmali savol;
///   - kuniga bitta, takror yo'q;
///   - «qiyin bo'lyapti» kuratorga darhol boradi va ro'yxatda tepaga chiqadi;
///   - «o'chirish» o'quvchi xabarlarini to'xtatadi, kuratornikini emas;
///   - begona Telegram akkaunt tugmani «bosa» olmaydi.
/// </summary>
public sealed class SelfStudyNudgeTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string EnabledKey = "selfstudy.nudges_enabled";

    [Fact]
    public async Task Ladder_runs_end_to_end_and_buttons_work()
    {
        var s = await SetUpAsync("nudge-e2e");
        await SetSettingAsync(EnabledKey, "true");

        try
        {
            // Obuna 1 kun oldin boshlangan (anchor) — bugun kechqurun 1-kun.
            var day1 = await RunAtAsync(s, idleDays: 1);
            day1.Should().BeGreaterThanOrEqualTo(1);

            var first = await NudgesAsync(s.Student.Id);
            first.Should().ContainSingle(n => n.Stage == NudgeStage.Day1 && n.RecipientId == s.Student.Id);
            var outbox = await OutboxBodyAsync($"nudge:{first.Single().Id}");
            outbox.Should().NotBeNull();
            outbox!.Value.TemplateKey.Should().Be(TelegramTemplates.SelfStudyNudge);
            outbox.Value.CallbackData.Should().Contain(TelegramTemplates.OpenAppButtonData);

            // Ayni kunda qayta yurish — takror yo'q.
            await RunAtAsync(s, idleDays: 1, minutesLater: 30);
            (await NudgesAsync(s.Student.Id)).Should().HaveCount(1);

            // 3-kun — kuratorga (o'quvchiga emas).
            await RunAtAsync(s, idleDays: 3);
            var third = await NudgesAsync(s.Student.Id);
            third.Should().ContainSingle(n => n.Stage == NudgeStage.CuratorAlert && n.RecipientId == s.Curator.Id);
            (await factory.WithDbAsync(db => db.Notifications.CountAsync(n =>
                    n.UserId == s.Curator.Id && n.Kind == NotificationKind.CuratorStudentIdle)))
                .Should().Be(1);

            // 5-kun — tugmali savol.
            await RunAtAsync(s, idleDays: 5);
            var ask = (await NudgesAsync(s.Student.Id)).Single(n => n.Stage == NudgeStage.Ask);
            (await OutboxBodyAsync($"nudge:{ask.Id}"))!.Value.CallbackData.Should().Contain($"nd:{ask.Id}:h");

            // 🔴 Begona Telegram akkaunt — bizga tegishli emas (null), hech narsa yozilmaydi.
            var stranger = await CallbackAsync(s.StrangerTelegramId, $"nd:{ask.Id}:h");
            stranger.Should().BeNull();

            // «Qiyin bo'lyapti» — kuratorga darhol, ro'yxatda belgi bilan.
            var toast = await CallbackAsync(s.TelegramId, $"nd:{ask.Id}:h");
            toast.Should().NotBeNull();
            (await CallbackAsync(s.TelegramId, $"nd:{ask.Id}:s")).Should().Contain("allaqachon");

            (await OutboxBodyAsync($"nudge_hard_staff:{ask.Id}")).Should().NotBeNull();

            using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);
            var mine = await curator.GetFromJsonAsync<List<MySelfStudyStudentDto>>("/api/v1/self-study/curators/my-students");
            var row = mine!.Single(r => r.StudentId == s.Student.Id);
            row.LastNudgeResponse.Should().Be(nameof(NudgeResponse.Hard));

            // «O'chirish» — o'quvchiga boshqa avtomatik xabar yo'q.
            (await CallbackAsync(s.TelegramId, $"nd:{ask.Id}:off")).Should().NotBeNull();
            (await factory.WithDbAsync(db => db.SelfStudyNudgePreferences
                    .AnyAsync(p => p.StudentId == s.Student.Id && p.OptedOutAt != null)))
                .Should().BeTrue();

            var before = (await NudgesAsync(s.Student.Id)).Count;
            await RunAtAsync(s, idleDays: 14);
            (await NudgesAsync(s.Student.Id)).Should().HaveCount(before, "o'chirgan o'quvchiga oxirgi xabar ham ketmaydi");

            // Qayta yoqish.
            (await CallbackAsync(s.TelegramId, NudgeTemplates.CallbackOptIn)).Should().NotBeNull();
            await RunAtAsync(s, idleDays: 14, minutesLater: 15);
            (await NudgesAsync(s.Student.Id)).Should().Contain(n => n.Stage == NudgeStage.Final);

            // Hisobot.
            using var admin = await WorldBuilder.AdminClientAsync(factory);
            var stats = await admin.GetFromJsonAsync<NudgeStatsDto>("/api/v1/self-study/nudges/stats?days=30");
            stats!.Lines.Should().Contain(l => l.Stage == nameof(NudgeStage.Day1));
            stats.CuratorAlerts.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await ResetSettingAsync(EnabledKey);
        }
    }

    [Fact]
    public async Task Disabled_or_outside_window_sends_nothing_and_activity_resets_the_chain()
    {
        var s = await SetUpAsync("nudge-off");

        // Panelda o'chiq (standart).
        (await RunAtAsync(s, idleDays: 2)).Should().Be(0);
        (await NudgesAsync(s.Student.Id)).Should().BeEmpty();

        await SetSettingAsync(EnabledKey, "true");
        try
        {
            // Oynadan tashqarida (ertalab) — hech narsa.
            (await RunAtAsync(s, idleDays: 2, localHour: 9)).Should().Be(0);
            (await NudgesAsync(s.Student.Id)).Should().BeEmpty();

            // O'quvchi bugun video ko'rdi — faolsiz emas.
            var now = LocalEvening(s.Anchor.AddDays(2));
            await factory.WithDbAsync(async db =>
            {
                var progress = new LessonProgress { StudentId = s.Student.Id, ModuleLessonId = s.LessonId };
                progress.MarkVideoWatched(now.AddHours(-1));
                db.LessonProgress.Add(progress);
                await db.SaveChangesAsync();
                return 0;
            });

            await RunAtAsync(s, idleDays: 2);
            (await NudgesAsync(s.Student.Id)).Should().BeEmpty();
        }
        finally
        {
            await ResetSettingAsync(EnabledKey);
        }
    }

    // ================================================================ qurilma

    private sealed record Setup(
        TestUser Student,
        TestUser Curator,
        long LessonId,
        long TelegramId,
        long StrangerTelegramId,
        DateTimeOffset Anchor);

    private static readonly TimeSpan Tashkent = TimeSpan.FromHours(5);

    /// <summary>Anchor kunidan <paramref name="idleDays"/> kun keyin, mahalliy 19:30 (yoki berilgan soat).</summary>
    private async Task<int> RunAtAsync(Setup s, int idleDays, int localHour = 19, int minutesLater = 0)
    {
        var at = LocalEvening(s.Anchor.AddDays(idleDays), localHour).AddMinutes(30 + minutesLater);
        var clock = new MutableTimeProvider();
        clock.Set(at);

        using var scope = factory.Services.CreateScope();
        var service = ActivatorUtilities.CreateInstance<SelfStudyNudgeService>(scope.ServiceProvider, (TimeProvider)clock);
        return await service.RunAsync();
    }

    private async Task<string?> CallbackAsync(long telegramId, string data)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISelfStudyNudgeService>();
        return await service.HandleCallbackAsync(telegramId, data);
    }

    private static DateTimeOffset LocalEvening(DateTimeOffset day, int hour = 19)
    {
        var local = day.ToOffset(Tashkent);
        return new DateTimeOffset(local.Year, local.Month, local.Day, hour, 0, 0, Tashkent).ToUniversalTime();
    }

    private Task<List<SelfStudyNudge>> NudgesAsync(long studentId) =>
        factory.WithDbAsync(db => db.SelfStudyNudges.AsNoTracking()
            .Where(n => n.StudentId == studentId).OrderBy(n => n.Id).ToListAsync());

    private Task<(string TemplateKey, string? CallbackData)?> OutboxBodyAsync(string key) =>
        factory.WithDbAsync(async db =>
        {
            var row = await db.MessageOutbox.AsNoTracking()
                .Where(m => m.IdempotencyKey == key)
                .Select(m => new { m.TemplateKey, m.CallbackData })
                .FirstOrDefaultAsync();
            return row is null ? ((string, string?)?)null : (row.TemplateKey, row.CallbackData);
        });

    private async Task<Setup> SetUpAsync(string prefix)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var curator = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-kurator");
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-mustaqil");

        var (courseId, lessonId) = await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"{prefix} kurs {Guid.NewGuid():N}"[..40] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var module = new CourseModule { CourseId = course.Id, Name = "1-modul", Position = 1 };
            db.Modules.Add(module);
            await db.SaveChangesAsync();

            var lesson = new ModuleLesson { ModuleId = module.Id, Name = "Harflar", Position = 1 };
            db.ModuleLessons.Add(lesson);
            await db.SaveChangesAsync();

            // Vazifasi bor dars: o'quvchida «qarz» bor (bo'sh dars «ish bajarilgan,
            // kurator kutilmoqda» deb hisoblanib, eslatma umuman ketmasdi).
            db.Assignments.Add(new Assignment
            {
                ModuleLessonId = lesson.Id,
                Title = "Vazifa " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text,
            });
            await db.SaveChangesAsync();
            return (course.Id, lesson.Id);
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

        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new { studentId = student.Id, planId, method = "Cash" });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));

        var assign = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{student.Id}", new { curatorId = curator.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(assign));

        var telegramId = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
        var strangerTelegramId = telegramId + 1;

        // Obuna davri 20 kun oldindan boshlangan qilinadi: test soati shu
        // nuqtadan sanaladi va oyna «bugun» bilan to'qnashmaydi.
        var anchor = await factory.WithDbAsync(async db =>
        {
            var start = DateTimeOffset.UtcNow.AddDays(-20);
            var period = await db.SubscriptionPeriods.AsTracking().SingleAsync(p => p.StudentId == student.Id);
            period.StartsAt = start;

            var user = await db.Users.AsTracking().SingleAsync(u => u.Id == student.Id);
            user.LinkTelegram(telegramId, null, DateTimeOffset.UtcNow);

            var other = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-begona");
            var otherUser = await db.Users.AsTracking().SingleAsync(u => u.Id == other.Id);
            otherUser.LinkTelegram(strangerTelegramId, null, DateTimeOffset.UtcNow);

            await db.SaveChangesAsync();
            return start;
        });

        return new Setup(student, curator, lessonId, telegramId, strangerTelegramId, anchor);
    }

    private async Task SetSettingAsync(string key, string value)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var response = await admin.PutAsJsonAsync(new Uri("/api/v1/settings/" + key, UriKind.Relative), new { value });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task ResetSettingAsync(string key)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var response = await admin.PostAsync(new Uri($"/api/v1/settings/{key}/reset", UriKind.Relative), content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private sealed record IdRow(long Id);
}
