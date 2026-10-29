using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL O'QISH OBUNASI — IZOLYATSIYA MATRITSASI (2026-10-23)
/// ════════════════════════════════════════════════════════════════════
///
/// Mustaqil o'quvchi FAQAT o'z obunasidagi kursni ko'radi. Guruh dunyosi
/// (jonli dars, chat, yozuvlar, sinfdoshlar, ustozlar, boshqalarning
/// profili/to'lovi) unga BUTUNLAY yopiq — va aksincha, guruh o'quvchilari
/// uni markaz reytingida ko'rmaydi.
///
/// ★ TEKSHIRUV SHAKLGA BOG'LIQ EMAS: har endpoint uchun javob yo 403/404,
///   yo 200 bo'lib ichida guruh dunyosidan BIRORTA ism ham yo'q. Shu
///   tufayli endpoint javobi o'zgarsa ham test yolg'on "yashil" bo'lmaydi.
///   Yangi endpoint qo'shilsa — shu ro'yxatga qo'shing.
/// </summary>
public sealed class SelfStudyIsolationTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    [Fact]
    public async Task Self_study_student_sees_own_course_with_self_paced_unlocking()
    {
        var setup = await SetUpAsync("ss-course");
        using var client = await WorldBuilder.ClientAsync(factory, setup.SelfStudent);

        var tree = await client.GetAsync($"/api/v1/courses/{setup.CourseId}");
        tree.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(tree));

        var lessons = await LessonsAsync(tree);
        lessons.Should().HaveCount(3);

        // ★ USTOZ SUR'ATI YO'Q: guruhda bu holda (o'tilgan dars 0) 2- va
        //   3-dars TeacherPace bilan yopiq bo'lardi. Sinov darslarida
        //   video/vazifa/test yo'q — ya'ni har biri "tugallangan" va faqat
        //   "oldingi darsni tugat" qoidasi qolgani uchun hammasi ochiq.
        lessons.Should().OnlyContain(l => l.Unlocked);
    }

    [Fact]
    public async Task Self_study_student_cannot_reach_any_group_data()
    {
        var setup = await SetUpAsync("ss-leak");
        using var client = await WorldBuilder.ClientAsync(factory, setup.SelfStudent);

        var w = setup.World;
        var gets = new[]
        {
            "/api/v1/live-sessions",
            "/api/v1/live-sessions/calendar?from=2026-10-01&to=2026-12-01",
            $"/api/v1/live-sessions/{setup.SessionId}",
            $"/api/v1/live-sessions/{setup.SessionId}/messages",
            $"/api/v1/live-sessions/{setup.SessionId}/breakouts",
            $"/api/v1/live-sessions/{setup.SessionId}/recordings",
            "/api/v1/recordings?from=2026-10-01&to=2026-12-01",
            "/api/v1/group-chat/threads",
            $"/api/v1/group-chat/groups/{w.GroupId}/messages",
            "/api/v1/messages/conversations",
            $"/api/v1/messages/conversations/{w.Curator.Id}/messages",
            "/api/v1/students/me/classroom",
            $"/api/v1/leaderboard/groups/{w.GroupId}",
            $"/api/v1/progress/attendance?groupId={w.GroupId}",
            $"/api/v1/users/{w.Student.Id}/profile",
            $"/api/v1/payments/students/{w.Student.Id}",
            $"/api/v1/profile/avatar/{w.Teacher.Id}",
            "/api/v1/books",
            "/api/v1/assignments/mine",
            "/api/v1/tests/available",
        };

        foreach (var url in gets)
            await ExpectNoGroupDataAsync(await client.GetAsync(url), url, setup);

        var token = await client.PostAsync($"/api/v1/live-sessions/{setup.SessionId}/token", null);
        token.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Center_boards_keep_the_two_populations_apart()
    {
        var setup = await SetUpAsync("ss-board");

        using var self = await WorldBuilder.ClientAsync(factory, setup.SelfStudent);
        var selfBoard = await self.GetStringAsync("/api/v1/leaderboard/center?metric=Points");
        selfBoard.Should().Contain(setup.SelfStudentName);
        selfBoard.Should().NotContain(setup.GroupStudentName);

        using var grouped = await WorldBuilder.ClientAsync(factory, setup.World.Student);
        var groupBoard = await grouped.GetStringAsync("/api/v1/leaderboard/center?metric=Points");
        groupBoard.Should().NotContain(setup.SelfStudentName);
    }

    [Fact]
    public async Task Group_membership_and_subscription_exclude_each_other()
    {
        var setup = await SetUpAsync("ss-exclusive");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var addToGroup = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{setup.World.GroupId}/members", new { studentId = setup.SelfStudent.Id });
        addToGroup.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(addToGroup));

        var grantToGrouped = await admin.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            studentId = setup.World.Student.Id,
            planId = setup.PlanId,
            method = "Cash",
        });
        grantToGrouped.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(grantToGrouped));
    }

    [Fact]
    public async Task Expired_subscription_shows_the_course_but_locks_every_lesson()
    {
        var setup = await SetUpAsync("ss-expired");

        await factory.WithDbAsync(async db =>
        {
            var period = await db.SubscriptionPeriods.FindAsync(setup.PeriodId);
            period!.StartsAt = DateTimeOffset.UtcNow.AddMonths(-2);
            period.EndsAt = DateTimeOffset.UtcNow.AddHours(-1);
            return await db.SaveChangesAsync();
        });

        using var client = await WorldBuilder.ClientAsync(factory, setup.SelfStudent);
        var lessons = await LessonsAsync(await client.GetAsync($"/api/v1/courses/{setup.CourseId}"));

        lessons.Should().NotBeEmpty();
        lessons.Should().OnlyContain(l => !l.Unlocked && l.LockReason == nameof(LessonLockReason.SubscriptionExpired));
    }

    [Fact]
    public async Task Expired_subscription_closes_the_course_assignments()
    {
        var setup = await SetUpAsync("ss-expired-task");

        var assignmentId = await factory.WithDbAsync(async db =>
        {
            var lessonId = await db.ModuleLessons
                .Where(l => l.Module!.CourseId == setup.CourseId && l.Position == 1)
                .Select(l => l.Id)
                .SingleAsync();

            var assignment = new Assignment
            {
                ModuleLessonId = lessonId,
                Title = "Kurs vazifasi " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text,
            };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            return assignment.Id;
        });

        using var client = await WorldBuilder.ClientAsync(factory, setup.SelfStudent);
        (await client.GetAsync($"/api/v1/assignments/{assignmentId}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await factory.WithDbAsync(async db =>
        {
            var period = await db.SubscriptionPeriods.FindAsync(setup.PeriodId);
            period!.StartsAt = DateTimeOffset.UtcNow.AddMonths(-2);
            period.EndsAt = DateTimeOffset.UtcNow.AddHours(-1);
            return await db.SaveChangesAsync();
        });

        (await client.GetAsync($"/api/v1/assignments/{assignmentId}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cancelled_subscription_revokes_the_course()
    {
        var setup = await SetUpAsync("ss-cancel");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var cancel = await admin.PostAsJsonAsync(
            $"/api/v1/subscriptions/{setup.PeriodId}/cancel", new { reason = "sinov" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(cancel));

        using var client = await WorldBuilder.ClientAsync(factory, setup.SelfStudent);
        var tree = await client.GetAsync($"/api/v1/courses/{setup.CourseId}");
        tree.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_staff_managers_can_grant_subscriptions()
    {
        var setup = await SetUpAsync("ss-roles");
        using var teacher = await WorldBuilder.ClientAsync(factory, setup.World.Teacher);

        var response = await teacher.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            studentId = setup.SelfStudent.Id,
            planId = setup.PlanId,
            method = "Cash",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================ qurilma

    private sealed record Setup(
        StudentWorld World,
        TestUser SelfStudent,
        string SelfStudentName,
        string GroupStudentName,
        long CourseId,
        long PlanId,
        long PeriodId,
        long SessionId);

    private sealed record LessonRow(bool Unlocked, string? LockReason);

    private async Task<Setup> SetUpAsync(string prefix)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var courseId = await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"{prefix} kurs {Guid.NewGuid():N}"[..40] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var module = new CourseModule { CourseId = course.Id, Name = "1-modul", Position = 1 };
            db.Modules.Add(module);
            await db.SaveChangesAsync();

            for (var i = 1; i <= 3; i++)
                db.ModuleLessons.Add(new ModuleLesson { ModuleId = module.Id, Name = $"{i}-dars", Position = i });

            await db.SaveChangesAsync();
            return course.Id;
        });

        var plan = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
        {
            name = prefix + " oylik",
            courseId,
            months = 1,
            price = 200_000m,
        });
        plan.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(plan));
        var planId = (await plan.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var selfPrefix = prefix + "-mustaqil";
        var selfStudent = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, selfPrefix);

        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            studentId = selfStudent.Id,
            planId,
            method = "Cash",
        });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));
        var periodId = (await grant.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory,
            world.GroupId,
            DateTimeOffset.UtcNow.AddDays(-1),
            SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [world.Student.Id] = AttendanceStatus.Present });

        return new Setup(
            world, selfStudent, $"{selfPrefix} Student", $"{prefix} Student",
            courseId, planId, periodId, sessionId);
    }

    private static async Task ExpectNoGroupDataAsync(HttpResponseMessage response, string url, Setup setup)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return;

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} — {await WorldBuilder.Body(response)}");

        var body = await response.Content.ReadAsStringAsync();
        var w = setup.World;
        foreach (var secret in new[] { w.GroupName, setup.GroupStudentName, $"{GroupPrefix(setup)} Teacher", $"{GroupPrefix(setup)} Assistant" })
            body.Should().NotContain(secret, $"{url} guruh ma'lumotini oshkor qilmasligi kerak");
    }

    private static string GroupPrefix(Setup setup) => setup.GroupStudentName[..^" Student".Length];

    private static async Task<List<LessonRow>> LessonsAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("modules").EnumerateArray()
            .SelectMany(m => m.GetProperty("lessons").EnumerateArray())
            .Select(l => new LessonRow(
                l.GetProperty("unlocked").GetBoolean(),
                l.GetProperty("lockReason").ValueKind == JsonValueKind.Null ? null : l.GetProperty("lockReason").GetString()))
            .ToList();
    }

    private sealed record IdRow(long Id);
}
