using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.SelfStudy;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL O'QUVCHINING KURATORI (2026-11-18)
/// ════════════════════════════════════════════════════════════════════
///
/// Guruhsiz o'quvchiga kurator bevosita biriktiriladi. Tekshiriladi:
///   - biriktirilgan kurator (va zaxirasi) javobni ko'radi va baholaydi,
///     begona xodim — yo'q;
///   - yozishmada suhbatdosh faqat ASOSIY kurator;
///   - almashtirishda tarix qoladi, faol qator esa bitta;
///   - guruh o'quvchisiga biriktirib bo'lmaydi, guruhga o'tganda yopiladi.
/// </summary>
public sealed class SelfStudyCuratorTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    [Fact]
    public async Task Assigned_curator_and_backup_grade_the_student_and_strangers_cannot()
    {
        var s = await SetUpAsync("ssc-grade");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        // Biriktirishdan OLDIN: kurator javobni umuman ko'rmaydi.
        using (var curatorBefore = await WorldBuilder.ClientAsync(factory, s.Curator))
        {
            var before = await curatorBefore.PostAsJsonAsync(GradeUri(s.SubmissionId), new { score = 4m });
            before.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(before));
        }

        var assign = await AssignAsync(admin, s.Student.Id, s.Curator.Id, s.Backup.Id);
        assign.Current.Should().NotBeNull();
        assign.Current!.CuratorId.Should().Be(s.Curator.Id);
        assign.Current.BackupCuratorId.Should().Be(s.Backup.Id);

        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);
        var mine = await curator.GetFromJsonAsync<List<MySelfStudyStudentDto>>("/api/v1/self-study/curators/my-students");
        mine.Should().ContainSingle(r => r.StudentId == s.Student.Id && !r.IsBackup && r.PendingSubmissions == 1);

        var list = await curator.GetStringAsync($"/api/v1/assignments/{s.AssignmentId}/submissions");
        list.Should().Contain(s.SubmissionId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var graded = await curator.PostAsJsonAsync(GradeUri(s.SubmissionId), new { score = 4m, feedback = "Yaxshi" });
        graded.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(graded));

        using var backup = await WorldBuilder.ClientAsync(factory, s.Backup);
        var backupList = await backup.GetFromJsonAsync<List<MySelfStudyStudentDto>>("/api/v1/self-study/curators/my-students");
        backupList.Should().ContainSingle(r => r.StudentId == s.Student.Id && r.IsBackup);

        using var stranger = await WorldBuilder.ClientAsync(factory, s.Stranger);
        var strangerGrade = await stranger.PostAsJsonAsync(GradeUri(s.SubmissionId), new { score = 2m });
        strangerGrade.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetFromJsonAsync<List<MySelfStudyStudentDto>>("/api/v1/self-study/curators/my-students"))
            .Should().NotContain(r => r.StudentId == s.Student.Id);
    }

    [Fact]
    public async Task Only_the_primary_curator_is_the_conversation_partner()
    {
        var s = await SetUpAsync("ssc-dm");
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        await AssignAsync(admin, s.Student.Id, s.Curator.Id, s.Backup.Id);

        using var student = await WorldBuilder.ClientAsync(factory, s.Student);
        var mine = await student.GetFromJsonAsync<MyCuratorDto>("/api/v1/self-study/curators/mine");
        mine!.CuratorId.Should().Be(s.Curator.Id);

        var toCurator = await student.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{s.Curator.Id}/messages", new { body = "Salom" });
        toCurator.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(toCurator));

        var toBackup = await student.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{s.Backup.Id}/messages", new { body = "Salom" });
        toBackup.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reassigning_keeps_history_with_a_single_active_row()
    {
        var s = await SetUpAsync("ssc-history");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await AssignAsync(admin, s.Student.Id, s.Curator.Id, null);
        await AssignAsync(admin, s.Student.Id, s.Curator.Id, null); // qayta saqlash — tarixga yozilmaydi
        var info = await AssignAsync(admin, s.Student.Id, s.Stranger.Id, s.Curator.Id);

        info.Current!.CuratorId.Should().Be(s.Stranger.Id);
        info.History.Should().HaveCount(2);
        info.History.Should().ContainSingle(h => h.EndedAt == null);

        // Eski kurator endi ASOSIY emas, faqat zaxira: baholay oladi, suhbatdosh emas.
        using var oldCurator = await WorldBuilder.ClientAsync(factory, s.Curator);
        var rows = await oldCurator.GetFromJsonAsync<List<MySelfStudyStudentDto>>("/api/v1/self-study/curators/my-students");
        rows.Should().ContainSingle(r => r.StudentId == s.Student.Id && r.IsBackup);

        var removed = await admin.DeleteAsync($"/api/v1/self-study/curators/students/{s.Student.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await removed.Content.ReadFromJsonAsync<StudentCuratorInfoDto>();
        after!.Current.Should().BeNull();
        after.History.Should().OnlyContain(h => h.EndedAt != null);
    }

    [Fact]
    public async Task Group_students_and_non_staff_are_rejected()
    {
        var s = await SetUpAsync("ssc-reject");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var groupStudent = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{s.World.Student.Id}", new { curatorId = s.Curator.Id });
        groupStudent.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(groupStudent));

        var otherStudent = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "ssc-reject-o'quvchi");
        var studentAsCurator = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{s.Student.Id}", new { curatorId = otherStudent.Id });
        studentAsCurator.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var sameBackup = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{s.Student.Id}",
            new { curatorId = s.Curator.Id, backupCuratorId = s.Curator.Id });
        sameBackup.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var teacher = await WorldBuilder.ClientAsync(factory, s.World.Teacher);
        var byTeacher = await teacher.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{s.Student.Id}", new { curatorId = s.Curator.Id });
        byTeacher.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unassigned_list_shows_students_without_a_curator()
    {
        var s = await SetUpAsync("ssc-unassigned");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var before = await admin.GetFromJsonAsync<List<UnassignedSelfStudyStudentDto>>("/api/v1/self-study/curators/unassigned");
        before.Should().Contain(r => r.StudentId == s.Student.Id);

        await AssignAsync(admin, s.Student.Id, s.Curator.Id, null);

        var after = await admin.GetFromJsonAsync<List<UnassignedSelfStudyStudentDto>>("/api/v1/self-study/curators/unassigned");
        after.Should().NotContain(r => r.StudentId == s.Student.Id);
    }

    [Fact]
    public async Task Moving_to_a_group_ends_the_assignment()
    {
        var s = await SetUpAsync("ssc-convert");
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        await AssignAsync(admin, s.Student.Id, s.Curator.Id, null);

        // Guruhga qo'shish obunani balansga aylantiradi (`ConvertSubscription`).
        var add = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{s.World.GroupId}/members",
            new { studentId = s.Student.Id, convertSubscription = true });
        add.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(add));

        var active = await factory.WithDbAsync(db => db.SelfStudyCuratorAssignments
            .CountAsync(a => a.StudentId == s.Student.Id && a.EndedAt == null));
        active.Should().Be(0);
    }

    // ================================================================ qurilma

    private sealed record Setup(
        StudentWorld World,
        TestUser Student,
        TestUser Curator,
        TestUser Backup,
        TestUser Stranger,
        long AssignmentId,
        long SubmissionId);

    private static string GradeUri(long submissionId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/api/v1/submissions/{submissionId}/grade");

    private static async Task<StudentCuratorInfoDto> AssignAsync(
        HttpClient admin, long studentId, long curatorId, long? backupId)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/self-study/curators/students/{studentId}",
            new { curatorId, backupCuratorId = backupId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        return (await response.Content.ReadFromJsonAsync<StudentCuratorInfoDto>())!;
    }

    private async Task<Setup> SetUpAsync(string prefix)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);
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
        var planId = (await plan.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            studentId = student.Id,
            planId,
            method = "Cash",
        });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));

        var (assignmentId, submissionId) = await factory.WithDbAsync(async db =>
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
            return (assignment.Id, submission.Id);
        });

        return new Setup(world, student, curator, backup, stranger, assignmentId, submissionId);
    }

    private sealed record IdRow(long Id);
}
