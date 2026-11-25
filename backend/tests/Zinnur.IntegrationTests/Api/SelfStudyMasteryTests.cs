using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// «YAXSHI O'ZLASHTIRDI» — QAT'IY QOIDA (Mustaqil, 2026-11-18)
/// ════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi: kurator 1-darsni «o'zlashtirdi» desa 2-dars ochiladi va
/// hokazo; bu qoida chetlab o'tilmasligi kerak. Tekshiriladi: ochilish
/// tartibi, ish bajarilmasa rad etish, kim qaror chiqara olishi,
/// administrator istisnosi (sabab bilan) va jurnal.
/// </summary>
public sealed class SelfStudyMasteryTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    [Fact]
    public async Task Curator_mastery_opens_the_next_lesson_and_notifies_the_student()
    {
        var s = await SetUpAsync("ssm-flow");
        using var student = await WorldBuilder.ClientAsync(factory, s.Student);
        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);

        // 1-dars ochiq, 2-dars — ish bajarilmagan.
        (await LessonsAsync(student, s.CourseId))[1].LockReason.Should().Be("PreviousIncomplete");

        await SubmitAsync(s);

        // Ish bajarildi — lekin kurator hali qaror chiqarmagan.
        var lessons = await LessonsAsync(student, s.CourseId);
        lessons[1].Unlocked.Should().BeFalse();
        lessons[1].LockReason.Should().Be("AwaitingReview");

        var board = await BoardAsync(curator, s.Student.Id);
        board.GetProperty("canDecide").GetBoolean().Should().BeTrue();
        var first = board.GetProperty("lessons")[0];
        first.GetProperty("workDone").GetBoolean().Should().BeTrue();
        first.GetProperty("assignment").GetProperty("submissionId").ValueKind.Should().Be(JsonValueKind.Number);

        var decide = await curator.PutAsJsonAsync(MasteryUri(s, s.LessonIds[0]), new { status = "Mastered", note = "Zo'r" });
        decide.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(decide));

        lessons = await LessonsAsync(student, s.CourseId);
        lessons[1].Unlocked.Should().BeTrue();
        lessons[2].Unlocked.Should().BeFalse("3-dars 2-dars o'zlashtirilgach ochiladi");

        var notified = await factory.WithDbAsync(db => db.Notifications
            .AnyAsync(n => n.UserId == s.Student.Id && n.Kind == NotificationKind.LessonMasteryDecided));
        notified.Should().BeTrue();

        var events = await curator.GetFromJsonAsync<JsonElement>(
            $"/api/v1/self-study/students/{s.Student.Id}/mastery-events");
        events.GetArrayLength().Should().Be(1);
        events[0].GetProperty("kind").GetString().Should().Be("Mastered");
    }

    [Fact]
    public async Task Mastered_is_rejected_while_the_work_is_not_done()
    {
        var s = await SetUpAsync("ssm-early");
        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);

        var early = await curator.PutAsJsonAsync(MasteryUri(s, s.LessonIds[0]), new { status = "Mastered" });
        early.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(early));
        (await ProblemText.ReadAsync(early)).Should().Contain("vazifani topshirmagan");

        // Hali ochilmagan darsga ham qaror chiqarib bo'lmaydi.
        var locked = await curator.PutAsJsonAsync(MasteryUri(s, s.LessonIds[2]), new { status = "Mastered" });
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Needs_work_requires_a_note_and_keeps_the_next_lesson_closed()
    {
        var s = await SetUpAsync("ssm-redo");
        using var student = await WorldBuilder.ClientAsync(factory, s.Student);
        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);
        await SubmitAsync(s);

        var noNote = await curator.PutAsJsonAsync(MasteryUri(s, s.LessonIds[0]), new { status = "NeedsWork" });
        noNote.StatusCode.Should().Be(HttpStatusCode.BadRequest, await WorldBuilder.Body(noNote));

        var redo = await curator.PutAsJsonAsync(
            MasteryUri(s, s.LessonIds[0]), new { status = "NeedsWork", note = "Harflarni qayta yozing" });
        redo.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(redo));

        var lessons = await LessonsAsync(student, s.CourseId);
        lessons[1].Unlocked.Should().BeFalse();
        lessons[1].LockReason.Should().Be("NeedsWork");
    }

    [Fact]
    public async Task Perfect_test_opens_one_lesson_ahead_until_the_curator_decides()
    {
        // ★ «Bitta dars oldinga» (2026-11-25): kurator kechqurun ishlamaydi.
        var s = await SetUpAsync("ssm-ahead");
        using var student = await WorldBuilder.ClientAsync(factory, s.Student);
        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);

        await AddLessonTestAsync(s, s.LessonIds[0], score: 10m, maxScore: 10m);
        await SubmitAsync(s); // keshni ham tozalaydi

        var lessons = await LessonsAsync(student, s.CourseId);
        lessons[1].Unlocked.Should().BeTrue("1-dars to'liq bajarildi va testi 100%");
        lessons[2].Unlocked.Should().BeFalse("faqat bitta dars qarzga ochiladi");

        // Vaqtincha ochilgan darsga qaror tartib bilan: avval 1-dars.
        var outOfOrder = await curator.PutAsJsonAsync(MasteryUri(s, s.LessonIds[1]), new { status = "NeedsWork", note = "x" });
        outOfOrder.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(outOfOrder));

        var board = await BoardAsync(curator, s.Student.Id);
        board.GetProperty("lessons")[1].GetProperty("provisional").GetBoolean().Should().BeTrue();

        // «Qayta ishlasin» — vaqtinchalik ochish bekor.
        var redo = await curator.PutAsJsonAsync(
            MasteryUri(s, s.LessonIds[0]), new { status = "NeedsWork", note = "Harflarni qayta yozing" });
        redo.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(redo));

        lessons = await LessonsAsync(student, s.CourseId);
        lessons[1].Unlocked.Should().BeFalse();
        lessons[1].LockReason.Should().Be("NeedsWork");
    }

    [Fact]
    public async Task Imperfect_test_still_waits_for_the_curator()
    {
        var s = await SetUpAsync("ssm-ahead90");
        using var student = await WorldBuilder.ClientAsync(factory, s.Student);

        await AddLessonTestAsync(s, s.LessonIds[0], score: 9m, maxScore: 10m);
        await SubmitAsync(s);

        var lessons = await LessonsAsync(student, s.CourseId);
        lessons[1].Unlocked.Should().BeFalse();
        lessons[1].LockReason.Should().Be("AwaitingReview");
    }

    [Fact]
    public async Task Only_the_curator_and_admin_decide()
    {
        var s = await SetUpAsync("ssm-roles");
        await SubmitAsync(s);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var stranger = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, "ssm-roles-begona");
        using (var strangerClient = await WorldBuilder.ClientAsync(factory, stranger))
        {
            (await strangerClient.GetAsync($"/api/v1/self-study/students/{s.Student.Id}/lessons"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await strangerClient.PutAsJsonAsync(MasteryUri(s, s.LessonIds[0]), new { status = "Mastered" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, "ssm-roles-oquv");
        using (var academicClient = await WorldBuilder.ClientAsync(factory, academic))
        {
            var board = await BoardAsync(academicClient, s.Student.Id);
            board.GetProperty("canDecide").GetBoolean().Should().BeFalse();
            (await academicClient.PutAsJsonAsync(MasteryUri(s, s.LessonIds[0]), new { status = "Mastered" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var studentClient = await WorldBuilder.ClientAsync(factory, s.Student))
        {
            (await studentClient.PutAsJsonAsync(MasteryUri(s, s.LessonIds[0]), new { status = "Mastered" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var byAdmin = await admin.PutAsJsonAsync(MasteryUri(s, s.LessonIds[0]), new { status = "Mastered" });
        byAdmin.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(byAdmin));
    }

    [Fact]
    public async Task Admin_exception_needs_a_reason_and_is_logged()
    {
        var s = await SetUpAsync("ssm-admin");
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        using var curator = await WorldBuilder.ClientAsync(factory, s.Curator);
        using var student = await WorldBuilder.ClientAsync(factory, s.Student);

        (await curator.PutAsJsonAsync(UnlockUri(s, s.LessonIds[1]), new { unlocked = true, reason = "Kasal edi" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var noReason = await admin.PutAsJsonAsync(UnlockUri(s, s.LessonIds[1]), new { unlocked = true, reason = "ok" });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest, await WorldBuilder.Body(noReason));

        var unlock = await admin.PutAsJsonAsync(
            UnlockUri(s, s.LessonIds[1]), new { unlocked = true, reason = "Kasallik — shifokor ma'lumotnomasi bor" });
        unlock.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(unlock));

        (await LessonsAsync(student, s.CourseId))[1].Unlocked.Should().BeTrue();

        var logged = await factory.WithDbAsync(db => db.LessonMasteryEvents
            .SingleAsync(e => e.StudentId == s.Student.Id && e.Kind == MasteryEventKind.AdminUnlocked));
        logged.Note.Should().Contain("Kasallik");
    }

    [Fact]
    public async Task Group_students_keep_their_rule()
    {
        var s = await SetUpAsync("ssm-group");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.GetAsync($"/api/v1/self-study/students/{s.World.Student.Id}/lessons");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ================================================================ qurilma

    private sealed record Setup(
        StudentWorld World,
        TestUser Student,
        TestUser Curator,
        long CourseId,
        IReadOnlyList<long> LessonIds,
        long AssignmentId);

    private sealed record LessonRow(bool Unlocked, string? LockReason);

    private static string MasteryUri(Setup s, long lessonId) =>
        $"/api/v1/self-study/students/{s.Student.Id}/lessons/{lessonId}/mastery";

    private static string UnlockUri(Setup s, long lessonId) =>
        $"/api/v1/self-study/students/{s.Student.Id}/lessons/{lessonId}/unlock";

    /// <summary>
    /// Haqiqiy topshirish endpointi — u gating keshini ham tozalaydi
    /// (bazaga to'g'ridan yozilsa o'quvchining eski holati keshda qolardi).
    /// </summary>
    private async Task SubmitAsync(Setup s)
    {
        using var student = await WorldBuilder.ClientAsync(factory, s.Student);
        using var content = new MultipartFormDataContent { { new StringContent("Javob"), "text" } };
        var response = await student.PostAsync($"/api/v1/assignments/{s.AssignmentId}/submit", content);
        response.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(response));
    }

    /// <summary>Darsga e'lon qilingan test va o'quvchining topshirilgan urinishi.</summary>
    private Task<int> AddLessonTestAsync(Setup s, long lessonId, decimal score, decimal maxScore) =>
        factory.WithDbAsync(async db =>
        {
            var test = new Test
            {
                Title = "Dars testi " + Guid.NewGuid().ToString("N")[..6],
                Kind = TestKind.Lesson,
                ModuleLessonId = lessonId,
                IsPublished = true,
            };
            db.Tests.Add(test);
            await db.SaveChangesAsync();

            var now = DateTimeOffset.UtcNow;
            db.TestAttempts.Add(new TestAttempt
            {
                TestId = test.Id,
                StudentId = s.Student.Id,
                Status = AttemptStatus.Submitted,
                Score = score,
                MaxScore = maxScore,
                StartedAt = now.AddMinutes(-10),
                SubmittedAt = now,
            });
            await db.SaveChangesAsync();
            return 0;
        });

    private static async Task<JsonElement> BoardAsync(HttpClient client, long studentId)
    {
        var response = await client.GetAsync($"/api/v1/self-study/students/{studentId}/lessons");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<LessonRow>> LessonsAsync(HttpClient student, long courseId)
    {
        var response = await student.GetAsync($"/api/v1/courses/{courseId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("modules").EnumerateArray()
            .SelectMany(m => m.GetProperty("lessons").EnumerateArray())
            .Select(l => new LessonRow(
                l.GetProperty("unlocked").GetBoolean(),
                l.GetProperty("lockReason").ValueKind == JsonValueKind.Null ? null : l.GetProperty("lockReason").GetString()))
            .ToList();
    }

    private async Task<Setup> SetUpAsync(string prefix)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var curator = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, prefix + "-kurator");
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-mustaqil");

        var (courseId, lessonIds, assignmentId) = await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"{prefix} kurs {Guid.NewGuid():N}"[..40] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var module = new CourseModule { CourseId = course.Id, Name = "1-modul", Position = 1 };
            db.Modules.Add(module);
            await db.SaveChangesAsync();

            var lessons = Enumerable.Range(1, 3)
                .Select(i => new ModuleLesson { ModuleId = module.Id, Name = $"{i}-dars", Position = i })
                .ToList();
            db.ModuleLessons.AddRange(lessons);
            await db.SaveChangesAsync();

            // Har darsda kurs vazifasi — "ish bajarildi" = javob topshirildi.
            var assignments = lessons.Select(l => new Assignment
            {
                ModuleLessonId = l.Id,
                Title = $"{l.Name} vazifasi",
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text,
            }).ToList();
            db.Assignments.AddRange(assignments);
            await db.SaveChangesAsync();

            return (course.Id, (IReadOnlyList<long>)lessons.Select(l => l.Id).ToList(), assignments[0].Id);
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

        return new Setup(world, student, curator, courseId, lessonIds, assignmentId);
    }

    private sealed record IdRow(long Id);
}
