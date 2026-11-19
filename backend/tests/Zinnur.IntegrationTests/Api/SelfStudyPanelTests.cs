using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Messaging.Dtos;
using Zinnur.Application.SelfStudy.Panel;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL PANELI (2026-11-19)
/// ════════════════════════════════════════════════════════════════════
///
/// Tekshiriladi:
///   - panel FAQAT o'quv bo'limi / administrator uchun;
///   - o'quvchilar ro'yxati: obuna holati, kurator, progress, filtrlar;
///   - ommaviy biriktirish har o'quvchini alohida hal qiladi;
///   - kuratorlar yuki va vazifalar filtri;
///   - yozishmalar FAQAT O'QISH: audit yoziladi, "o'qildi" o'zgarmaydi,
///     guruh o'quvchisining yozishmasi panelda ko'rinmaydi.
///
/// Baza test sinfi bo'yicha umumiy — har test o'z prefiksi bilan qidiradi.
/// </summary>
public sealed class SelfStudyPanelTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string Base = "/api/v1/self-study/panel";

    /// <summary>API enum'larni NOM bilan yozadi (`JsonStringEnumConverter`) — test ham shunday o'qiydi.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Panel_is_closed_to_curators_and_students()
    {
        var s = await SetUpAsync("ssp-auth");

        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);
        (await curator.GetAsync($"{Base}/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await curator.GetAsync($"{Base}/chats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var student = await WorldBuilder.ClientAsync(factory, s.Student);
        (await student.GetAsync($"{Base}/students")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Students_list_shows_subscription_curator_progress_and_filters()
    {
        var s = await SetUpAsync("ssp-list");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var all = await StudentsAsync(admin, "search=ssp-list");
        var row = all.Items.Should().ContainSingle().Subject;
        row.StudentId.Should().Be(s.Student.Id);
        row.State.Should().Be(SelfStudySubscriptionState.Active);
        row.CuratorId.Should().BeNull();
        row.PendingSubmissions.Should().Be(1);
        row.TotalLessons.Should().Be(1);
        row.PlanName.Should().NotBeNullOrEmpty();

        (await StudentsAsync(admin, "search=ssp-list&unassignedOnly=true")).Items.Should().ContainSingle();

        var assign = await admin.PostAsJsonAsync($"{Base}/students/assign", new
        {
            studentIds = new[] { s.Student.Id },
            curatorId = s.Curator.Id,
            backupCuratorId = s.Backup.Id,
        });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(assign));
        var result = (await assign.Content.ReadFromJsonAsync<BulkAssignResultDto>(Json))!;
        result.Assigned.Should().Be(1);
        result.Failed.Should().BeEmpty();

        var afterAssign = (await StudentsAsync(admin, "search=ssp-list")).Items.Single();
        afterAssign.CuratorId.Should().Be(s.Curator.Id);
        afterAssign.BackupCuratorId.Should().Be(s.Backup.Id);

        (await StudentsAsync(admin, "search=ssp-list&unassignedOnly=true")).Items.Should().BeEmpty();
        (await StudentsAsync(admin, $"search=ssp-list&curatorId={s.Backup.Id}")).Items.Should().ContainSingle(
            "zaxira kurator filtri ham o'quvchini topadi");
        (await StudentsAsync(admin, "search=ssp-list&state=Expired")).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Bulk_assign_reports_each_failure_and_keeps_going()
    {
        var s = await SetUpAsync("ssp-bulk");
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var noSubscription = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "ssp-bulk-obunasiz");

        var response = await admin.PostAsJsonAsync($"{Base}/students/assign", new
        {
            studentIds = new[] { noSubscription.Id, s.Student.Id },
            curatorId = s.Curator.Id,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var result = (await response.Content.ReadFromJsonAsync<BulkAssignResultDto>(Json))!;
        result.Assigned.Should().Be(1, "obunali o'quvchi biriktirildi — obunasizning xatosi uni to'xtatmadi");
        var failure = result.Failed.Should().ContainSingle().Subject;
        failure.StudentId.Should().Be(noSubscription.Id);
        failure.Error.Should().Contain("obuna");
    }

    [Fact]
    public async Task Curators_tab_shows_primary_backup_and_pending_load()
    {
        var s = await SetUpAsync("ssp-cur");
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        await AssignAsync(admin, s.Student.Id, s.Curator.Id, s.Backup.Id);

        var rows = await admin.GetFromJsonAsync<List<SelfStudyCuratorRowDto>>($"{Base}/curators", Json);
        var curator = rows!.Single(r => r.Id == s.Curator.Id);
        curator.PrimaryStudents.Should().Be(1);
        curator.PendingSubmissions.Should().Be(1);

        var backup = rows!.Single(r => r.Id == s.Backup.Id);
        backup.BackupStudents.Should().Be(1);
        backup.PrimaryStudents.Should().Be(0);
    }

    [Fact]
    public async Task Submissions_tab_lists_self_study_work_and_filters_by_curator()
    {
        var s = await SetUpAsync("ssp-sub");
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        await AssignAsync(admin, s.Student.Id, s.Curator.Id, null);

        var page = await admin.GetFromJsonAsync<Page<SelfStudySubmissionRowDto>>(
            $"{Base}/submissions?search=ssp-sub&status=Submitted", Json);
        var row = page!.Items.Should().ContainSingle().Subject;
        row.SubmissionId.Should().Be(s.SubmissionId);
        row.CuratorName.Should().NotBeNullOrEmpty();
        row.LessonName.Should().Be("1-dars");

        var otherCurator = await admin.GetFromJsonAsync<Page<SelfStudySubmissionRowDto>>(
            $"{Base}/submissions?search=ssp-sub&curatorId={s.Stranger.Id}", Json);
        otherCurator!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Chats_are_read_only_audited_and_do_not_mark_messages_read()
    {
        var s = await SetUpAsync("ssp-chat");
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        await AssignAsync(admin, s.Student.Id, s.Curator.Id, null);

        var messageId = await factory.WithDbAsync(async db =>
        {
            var message = DirectMessage.Create(
                s.Student.Id, s.Curator.Id, s.Student.Id, null, "Ustoz, 2-mashqni tushunmadim", DateTimeOffset.UtcNow);
            db.DirectMessages.Add(message);
            await db.SaveChangesAsync();
            return message.Id;
        });

        var chats = await admin.GetFromJsonAsync<Page<SelfStudyChatRowDto>>($"{Base}/chats?search=ssp-chat", Json);
        var chat = chats!.Items.Should().ContainSingle().Subject;
        chat.StaffId.Should().Be(s.Curator.Id);
        chat.IsCurrentCurator.Should().BeTrue();
        chat.LastFromStudent.Should().BeTrue();
        chat.WaitingSince.Should().NotBeNull("kurator hali javob bermagan");
        chat.UnreadByStaff.Should().Be(1);

        var waiting = await admin.GetFromJsonAsync<Page<SelfStudyChatRowDto>>(
            $"{Base}/chats?search=ssp-chat&waitingOnly=true", Json);
        waiting!.Items.Should().ContainSingle();

        var thread = await admin.GetAsync($"{Base}/chats/{s.Student.Id}/{s.Curator.Id}/messages");
        thread.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(thread));
        var page = (await thread.Content.ReadFromJsonAsync<MessagePageDto>(Json))!;
        page.Items.Should().ContainSingle(m => m.Id == messageId && !m.Mine);

        var (audits, stillUnread) = await factory.WithDbAsync(async db =>
        (
            await db.DirectMessageAccessAudits.AsNoTracking()
                .Where(a => a.StudentId == s.Student.Id && a.StaffId == s.Curator.Id)
                .ToListAsync(),
            await db.DirectMessages.AsNoTracking().Where(m => m.Id == messageId).Select(m => !m.ReadByStaff).SingleAsync()
        ));
        audits.Should().ContainSingle(a => a.Kind == DirectMessageAccessKind.Thread);
        stillUnread.Should().BeTrue("nazoratchi ochgani kurator uchun xabarni 'o'qilgan' qilmaydi");

        var log = await admin.GetFromJsonAsync<List<ChatAccessLogEntryDto>>(
            $"{Base}/chats/{s.Student.Id}/{s.Curator.Id}/access-log", Json);
        log.Should().ContainSingle(e => e.Kind == DirectMessageAccessKind.Thread);

        // Eskiroq sahifani varaqlash — o'sha ochilishning davomi, yangi audit emas.
        (await admin.GetAsync($"{Base}/chats/{s.Student.Id}/{s.Curator.Id}/messages?beforeId={messageId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var auditCount = await factory.WithDbAsync(db => db.DirectMessageAccessAudits
            .CountAsync(a => a.StudentId == s.Student.Id && a.StaffId == s.Curator.Id));
        auditCount.Should().Be(1);
    }

    [Fact]
    public async Task Group_student_chat_is_not_reachable_through_the_panel()
    {
        var world = await WorldBuilder.CreateAsync(factory, "ssp-grp");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await factory.WithDbAsync(async db =>
        {
            db.DirectMessages.Add(DirectMessage.Create(
                world.Student.Id, world.Curator.Id, world.Student.Id, null, "Guruh savoli", DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync();
        });

        var response = await admin.GetAsync($"{Base}/chats/{world.Student.Id}/{world.Curator.Id}/messages");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "qaror faqat Mustaqil yozishmalari uchun — guruh o'quvchisining yozishmasi panel orqali ochilmaydi");

        var audits = await factory.WithDbAsync(db =>
            db.DirectMessageAccessAudits.CountAsync(a => a.StudentId == world.Student.Id));
        audits.Should().Be(0);
    }

    // ================================================================ qurilma

    private sealed record Page<T>(List<T> Items, int Total);

    private sealed record Setup(TestUser Student, TestUser Curator, TestUser Backup, TestUser Stranger, long SubmissionId);

    private static async Task<Page<SelfStudyStudentRowDto>> StudentsAsync(HttpClient admin, string query)
    {
        var response = await admin.GetAsync($"{Base}/students?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        return (await response.Content.ReadFromJsonAsync<Page<SelfStudyStudentRowDto>>(Json))!;
    }

    private static async Task AssignAsync(HttpClient admin, long studentId, long curatorId, long? backupId)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{studentId}",
            new { curatorId, backupCuratorId = backupId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
    }

    private async Task<Setup> SetUpAsync(string prefix)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var curator = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-kurator");
        var backup = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-zaxira");
        var stranger = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-begona");
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-mustaqil");

        var (courseId, lessonId) = await factory.WithDbAsync(async db =>
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
        var planId = (await plan.Content.ReadFromJsonAsync<IdRow>(Json))!.Id;

        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            studentId = student.Id,
            planId,
            method = "Cash",
        });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));

        var submissionId = await factory.WithDbAsync(async db =>
        {
            var assignment = new Assignment
            {
                ModuleLessonId = lessonId,
                Title = "Kurs vazifasi " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text,
            };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var submission = Submission.Create(assignment.Id, student.Id, "Javob", false, DateTimeOffset.UtcNow);
            db.Submissions.Add(submission);
            await db.SaveChangesAsync();
            return submission.Id;
        });

        return new Setup(student, curator, backup, stranger, submissionId);
    }

    private sealed record IdRow(long Id);
}
