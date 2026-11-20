using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.CuratorWorkspace;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// KURATOR ISH STOLI (2026-11-20)
/// ════════════════════════════════════════════════════════════════════
///
/// Tekshiriladi:
///   - o'quvchi vazifa topshirganda kuratorga (va zaxirasiga) bildirishnoma;
///   - ro'yxat: tekshirilmagan javob va dars qarori kutayotgani ko'rinadi,
///     o'quvchi "ish kutmoqda" deb tepada;
///   - o'quvchining javoblari to'liq shaklda, baholash huquqi bilan;
///   - zaxira kurator yozishmani faqat o'qiydi (yoza olmaydi);
///   - begona xodim o'quvchini ocholmaydi.
/// </summary>
public sealed class CuratorWorkspaceTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string Base = "/api/v1/curator/workspace";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Submission_reaches_the_curator_and_the_workspace_shows_what_to_do()
    {
        var s = await SetUpAsync("cw-main");

        // 1) O'quvchi API orqali topshiradi — kurator va zaxirasiga bildirishnoma.
        using (var student = await WorldBuilder.ClientAsync(factory, s.Student))
        {
            using var form = new MultipartFormDataContent { { new StringContent("Mening javobim"), "text" } };
            var submit = await student.PostAsync($"/api/v1/assignments/{s.AssignmentId}/submit", form);
            submit.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(submit));
        }

        var notified = await factory.WithDbAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.Kind == NotificationKind.SubmissionReceived)
            .Select(n => n.UserId)
            .ToListAsync());
        notified.Should().BeEquivalentTo([s.Curator.Id, s.Backup.Id]);

        // 2) Ro'yxat: tekshiruv va dars qarori kutilmoqda.
        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);
        var rows = await curator.GetFromJsonAsync<List<WorkspaceStudentDto>>($"{Base}/students", Json);
        var row = rows!.Should().ContainSingle(r => r.StudentId == s.Student.Id).Subject;
        row.IsSelfStudy.Should().BeTrue();
        row.CanChat.Should().BeTrue();
        row.IsBackup.Should().BeFalse();
        row.PendingSubmissions.Should().Be(1);
        row.AwaitingDecision.Should().NotBeNull();
        row.AwaitingDecision!.LessonId.Should().Be(s.LessonId);
        row.NeedsAction.Should().BeTrue();
        rows![0].StudentId.Should().Be(s.Student.Id, "ish kutayotgan o'quvchi ro'yxat tepasida");

        // 3) Javoblar — baholash oynasi uchun to'liq shakl, baholash huquqi bilan.
        var submissions = await curator.GetFromJsonAsync<List<StaffStudentSubmissionDto>>(
            $"{Base}/students/{s.Student.Id}/submissions", Json);
        var item = submissions!.Should().ContainSingle().Subject;
        item.CanGrade.Should().BeTrue();
        item.LessonId.Should().Be(s.LessonId);
        item.Submission.Text.Should().Be("Mening javobim");

        // 4) Sarlavha: progress va kutilayotgan qaror.
        var header = await curator.GetFromJsonAsync<WorkspaceStudentHeaderDto>($"{Base}/students/{s.Student.Id}", Json);
        header!.TotalLessons.Should().Be(1);
        header.AwaitingDecision.Should().NotBeNull();
    }

    [Fact]
    public async Task Backup_curator_reads_the_chat_but_cannot_write_and_strangers_are_kept_out()
    {
        var s = await SetUpAsync("cw-backup");

        await factory.WithDbAsync(async db =>
        {
            db.DirectMessages.Add(DirectMessage.Create(
                s.Student.Id, s.Curator.Id, s.Student.Id, s.LessonId, "Alif qanday yoziladi?", DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync();
        });

        // Asosiy kurator: javobsiz dars savoli ro'yxatda ko'rinadi.
        using (var curator = await WorldBuilder.ClientAsync(factory, s.Curator))
        {
            var rows = await curator.GetFromJsonAsync<List<WorkspaceStudentDto>>($"{Base}/students", Json);
            var row = rows!.Single(r => r.StudentId == s.Student.Id);
            row.UnreadCount.Should().Be(1);
            row.UnansweredQuestions.Should().Be(1);
            row.WaitingSince.Should().NotBeNull();

            (await curator.GetAsync($"{Base}/students/{s.Student.Id}/backup-thread"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, "asosiy kurator oddiy yozishmadan foydalanadi");
        }

        using (var backup = await WorldBuilder.ClientAsync(factory, s.Backup))
        {
            var rows = await backup.GetFromJsonAsync<List<WorkspaceStudentDto>>($"{Base}/students", Json);
            var row = rows!.Single(r => r.StudentId == s.Student.Id);
            row.IsBackup.Should().BeTrue();
            row.CanChat.Should().BeFalse();

            var thread = await backup.GetAsync($"{Base}/students/{s.Student.Id}/backup-thread");
            thread.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(thread));

            var write = await backup.PostAsJsonAsync(
                $"/api/v1/messages/conversations/{s.Student.Id}/messages", new { body = "Salom" });
            write.IsSuccessStatusCode.Should().BeFalse("zaxira kurator o'quvchiga yozmaydi");
        }

        using var stranger = await WorldBuilder.ClientAsync(factory, s.Stranger);
        (await stranger.GetAsync($"{Base}/students/{s.Student.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"{Base}/students/{s.Student.Id}/submissions"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var strangerRows = await stranger.GetFromJsonAsync<List<WorkspaceStudentDto>>($"{Base}/students", Json);
        strangerRows.Should().NotContain(r => r.StudentId == s.Student.Id);
    }

    // ================================================================ qurilma

    private sealed record Setup(
        TestUser Student, TestUser Curator, TestUser Backup, TestUser Stranger, long LessonId, long AssignmentId);

    private async Task<Setup> SetUpAsync(string prefix)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var curator = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-kurator");
        var backup = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-zaxira");
        var stranger = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-begona");
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-mustaqil");

        var (courseId, lessonId, assignmentId) = await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"{prefix} kurs {Guid.NewGuid():N}"[..40] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var module = new CourseModule { CourseId = course.Id, Name = "1-modul", Position = 1 };
            db.Modules.Add(module);
            await db.SaveChangesAsync();

            var lesson = new ModuleLesson { ModuleId = module.Id, Name = "1-dars", Position = 1 };
            db.ModuleLessons.Add(lesson);
            await db.SaveChangesAsync();

            var assignment = new Assignment
            {
                ModuleLessonId = lesson.Id,
                Title = "Kurs vazifasi " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text,
            };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            return (course.Id, lesson.Id, assignment.Id);
        });

        var plan = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
        {
            name = prefix + " oylik",
            courseId,
            months = 1,
            price = 299_000m,
        });
        plan.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(plan));
        var planId = (await plan.Content.ReadFromJsonAsync<IdRow>(Json))!.Id;

        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new { studentId = student.Id, planId, method = "Cash" });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));

        var assign = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{student.Id}",
            new { curatorId = curator.Id, backupCuratorId = backup.Id });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(assign));

        return new Setup(student, curator, backup, stranger, lessonId, assignmentId);
    }

    private sealed record IdRow(long Id);
}
