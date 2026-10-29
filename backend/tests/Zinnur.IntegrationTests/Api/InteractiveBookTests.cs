using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Interactive.Content;
using Zinnur.Application.Interactive.Import;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// INTERAKTIV DARSLIK (2026-10-29) — import, kirish, kalit oqishi, baholash
/// ════════════════════════════════════════════════════════════════════
///
/// Haqiqiy ATF-1 1-dars `dars.json` vaqtinchalik papkaga yoziladi, audio
/// fayllar soxta (lekin haqiqiy mp3 sarlavhali) — import ularni MinIO ga yuklaydi.
///
/// Kirish matritsasi (egasi qarorlari): mehmon 401; Mustaqil — 404 (+ "upsell"
/// kartasi); Mustaqil+ — 200; guruh (Milliy) — 200; muddati o'tgan — 404.
/// </summary>
public sealed class InteractiveBookTests(StorageBackedApiFactory factory)
    : IClassFixture<StorageBackedApiFactory>
{
    private const string BookCode = "ATF-T";

    [Fact]
    public async Task Import_is_idempotent_and_keeps_bytes()
    {
        var setup = await SetUpAsync("ib-import");

        var again = await ImportAsync(setup);

        again.Ok.Should().BeTrue(string.Join("; ", again.Errors));
        again.Units.Should().OnlyContain(u => u.Status == "unchanged");
        again.AudioUploaded.Should().Be(0);

        var stored = await factory.WithDbAsync(db => db.InteractiveUnitVersions
            .Where(v => v.UnitId == setup.UnitId)
            .Select(v => v.ContentRaw)
            .ToListAsync());
        stored.Should().ContainSingle().Which.Should().Be(File.ReadAllText(FixturePath));
    }

    [Fact]
    public async Task Guest_is_unauthorized()
    {
        var setup = await SetUpAsync("ib-guest");
        using var guest = factory.CreateClient();

        (await guest.GetAsync($"/api/v1/interactive/units/{setup.UnitId}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await guest.GetAsync($"/api/v1/interactive/units/{setup.UnitId}/audio/{setup.AnyHandle}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Plain_self_study_student_is_denied_and_sees_upsell()
    {
        var setup = await SetUpAsync("ib-plain");
        var student = await SubscribedStudentAsync(setup, "ib-plain", includesBook: false);
        using var client = await WorldBuilder.ClientAsync(factory, student);

        (await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}/audio-ticket")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var card = await client.GetFromJsonAsync<JsonObject>($"/api/v1/interactive/lessons/{setup.LessonId}");
        card!["access"]!.GetValue<string>().Should().Be("upsell");
        card["unit"].Should().BeNull();
    }

    [Fact]
    public async Task Self_study_plus_student_gets_the_unit_without_any_key()
    {
        var setup = await SetUpAsync("ib-plus");
        var student = await SubscribedStudentAsync(setup, "ib-plus", includesBook: true);
        using var client = await WorldBuilder.ClientAsync(factory, student);

        var response = await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var body = await response.Content.ReadAsStringAsync();
        AssertNoKeys(body);

        var card = await client.GetFromJsonAsync<JsonObject>($"/api/v1/interactive/lessons/{setup.LessonId}");
        card!["access"]!.GetValue<string>().Should().Be("granted");
        card["unit"]!["id"]!.GetValue<long>().Should().Be(setup.UnitId);
    }

    [Fact]
    public async Task Group_student_of_the_course_is_allowed()
    {
        var setup = await SetUpAsync("ib-group");
        var world = await WorldBuilder.CreateAsync(factory, "ib-group");

        await factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FindAsync(world.GroupId);
            group!.CourseId = setup.CourseId;
            return await db.SaveChangesAsync();
        });

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);
        var response = await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        AssertNoKeys(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expired_subscription_is_denied()
    {
        var setup = await SetUpAsync("ib-expired");
        var student = await SubscribedStudentAsync(setup, "ib-expired", includesBook: true);

        await factory.WithDbAsync(async db =>
        {
            var period = await db.SubscriptionPeriods.SingleAsync(p => p.StudentId == student.Id);
            period.StartsAt = DateTimeOffset.UtcNow.AddMonths(-2);
            period.EndsAt = DateTimeOffset.UtcNow.AddHours(-1);
            return await db.SaveChangesAsync();
        });

        using var client = await WorldBuilder.ClientAsync(factory, student);
        (await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unpublished_book_is_hidden_from_students_but_not_staff()
    {
        var setup = await SetUpAsync("ib-draft", publish: false);
        var student = await SubscribedStudentAsync(setup, "ib-draft", includesBook: true);

        using (var client = await WorldBuilder.ClientAsync(factory, student))
        {
            (await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}")).StatusCode
                .Should().Be(HttpStatusCode.NotFound);
        }

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var staff = await admin.GetAsync($"/api/v1/interactive/units/{setup.UnitId}/staff");
        staff.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(staff));

        // Xodim ko'rinishida ustoz izohi bor — o'quvchiga esa hech qachon.
        (await staff.Content.ReadAsStringAsync()).Should().Contain("ustozga");
    }

    [Fact]
    public async Task Student_cannot_open_the_staff_view()
    {
        var setup = await SetUpAsync("ib-staffonly");
        var student = await SubscribedStudentAsync(setup, "ib-staffonly", includesBook: true);
        using var client = await WorldBuilder.ClientAsync(factory, student);

        (await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}/staff")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task First_attempt_counts_once_and_progress_accumulates()
    {
        var setup = await SetUpAsync("ib-grade");
        var student = await SubscribedStudentAsync(setup, "ib-grade", includesBook: true);
        using var client = await WorldBuilder.ClientAsync(factory, student);
        var url = $"/api/v1/interactive/units/{setup.UnitId}/items/A2m1/check";

        // A2m1: 4 savol, kalitlar [2, 0, 2, 1]. Birinchi savolga avval xato.
        var wrong = await PostAsync(client, url, new { index = 0, choice = 0 });
        wrong["correct"]!.GetValue<bool>().Should().BeFalse();
        wrong["reveal"]!["correctChoice"]!.GetValue<int>().Should().Be(2);
        wrong["itemDone"]!.GetValue<bool>().Should().BeFalse();

        (await PostAsync(client, url, new { index = 0, choice = 2 }))["correct"]!.GetValue<bool>().Should().BeTrue();
        await PostAsync(client, url, new { index = 1, choice = 0 });
        await PostAsync(client, url, new { index = 2, choice = 2 });
        var done = await PostAsync(client, url, new { index = 3, choice = 1 });

        done["itemDone"]!.GetValue<bool>().Should().BeTrue();
        done["first"]!["right"]!.GetValue<int>().Should().Be(3);
        done["first"]!["total"]!.GetValue<int>().Should().Be(4);

        // Qayta mashq: natija o'zgarmaydi.
        var again = await PostAsync(client, url, new { index = 0, choice = 2 });
        again["first"]!["right"]!.GetValue<int>().Should().Be(3);

        var bad = await client.PostAsJsonAsync(url, new { index = 9, choice = 0 });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unit = await client.GetFromJsonAsync<JsonObject>($"/api/v1/interactive/units/{setup.UnitId}");
        unit!["progress"]!["items"]!["A2m1"]!["right"]!.GetValue<int>().Should().Be(3);
        unit["progress"]!["exerciseTotal"]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public async Task Parallel_first_answers_are_recorded_once()
    {
        var setup = await SetUpAsync("ib-race");
        var student = await SubscribedStudentAsync(setup, "ib-race", includesBook: true);
        using var client = await WorldBuilder.ClientAsync(factory, student);
        var url = $"/api/v1/interactive/units/{setup.UnitId}/items/A3m1/check";

        // A3m1: 3 savol. Hammasi bir vaqtda, birinchi savolga ikki xil javob.
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(url, new { index = 0, choice = 1 }),
            client.PostAsJsonAsync(url, new { index = 0, choice = 0 }),
            client.PostAsJsonAsync(url, new { index = 1, choice = 1 }),
            client.PostAsJsonAsync(url, new { index = 2, choice = 2 }));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        var row = await factory.WithDbAsync(db => db.InteractiveItemResults
            .SingleAsync(r => r.StudentId == student.Id && r.ItemKey == "A3m1"));
        row.FirstTotal.Should().Be(3);
        row.FirstRight.Should().BeOneOf(2, 3);

        var progress = await factory.WithDbAsync(db => db.InteractiveUnitProgress
            .SingleAsync(p => p.StudentId == student.Id && p.UnitId == setup.UnitId));
        progress.ExerciseTotal.Should().Be(3);
    }

    [Fact]
    public async Task Blocks_done_complete_the_unit()
    {
        var setup = await SetUpAsync("ib-blocks");
        var student = await SubscribedStudentAsync(setup, "ib-blocks", includesBook: true);
        using var client = await WorldBuilder.ClientAsync(factory, student);

        JsonObject? last = null;
        foreach (var id in new[] { "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A6" })
        {
            var response = await client.PostAsync($"/api/v1/interactive/units/{setup.UnitId}/blocks/{id}/done", null);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
            last = await response.Content.ReadFromJsonAsync<JsonObject>();
        }

        last!["blocksDone"]!.AsArray().Should().HaveCount(7);
        last["completedAt"].Should().NotBeNull();

        (await client.PostAsync($"/api/v1/interactive/units/{setup.UnitId}/blocks/ZZ/done", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Audio_streams_with_a_unit_ticket_and_quiz_handles_differ()
    {
        var setup = await SetUpAsync("ib-audio");
        var student = await SubscribedStudentAsync(setup, "ib-audio", includesBook: true);
        using var client = await WorldBuilder.ClientAsync(factory, student);

        var unit = (await client.GetFromJsonAsync<JsonObject>($"/api/v1/interactive/units/{setup.UnitId}"))!;
        var tovush = unit["darslar"]![0]!["bloklar"]!.AsArray().OfType<JsonObject>()
            .First(b => b["tur"]!.GetValue<string>() == "tovush");
        var shown = tovush["juftlar"]![0]!["audio"]![1]!.GetValue<string>();   // s02
        var quiz = tovush["test"]![0]!["audio"]!.GetValue<string>();            // s02 — boshqa belgi
        quiz.Should().NotBe(shown);

        var ticket = (await client.GetFromJsonAsync<JsonObject>($"/api/v1/interactive/units/{setup.UnitId}/audio-ticket"))!;
        var token = ticket["token"]!.GetValue<string>();

        using var anonymous = factory.CreateClient();
        var a = await anonymous.GetAsync($"/api/v1/interactive/units/{setup.UnitId}/audio/{shown}?t={token}");
        var b = await anonymous.GetAsync($"/api/v1/interactive/units/{setup.UnitId}/audio/{quiz}?t={token}");

        a.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(a));
        a.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
        a.Headers.CacheControl!.ToString().Should().Contain("immutable");
        (await a.Content.ReadAsByteArrayAsync()).Should().Equal(await b.Content.ReadAsByteArrayAsync());

        // Boshqa birlik uchun chipta ishlamaydi; o'ylab topilgan belgi — 404.
        (await anonymous.GetAsync($"/api/v1/interactive/units/{setup.UnitId + 1}/audio/{shown}?t={token}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/interactive/units/{setup.UnitId}/audio/AAAAAAAAAAAAAAAA")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Mini_test_has_no_keys_and_first_submission_counts()
    {
        var setup = await SetUpAsync("ib-test");
        var student = await SubscribedStudentAsync(setup, "ib-test", includesBook: true);
        using var client = await WorldBuilder.ClientAsync(factory, student);

        var start = await client.PostAsync($"/api/v1/interactive/units/{setup.UnitId}/test/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(start));
        var startBody = await start.Content.ReadAsStringAsync();
        startBody.Should().NotContain("\"javob").And.NotContain("\"matn\"");

        var source = JsonNode.Parse(File.ReadAllText(FixturePath))!["test"]!["savollar"]!.AsArray().OfType<JsonObject>().ToList();
        var answers = source.Select((q, i) => new
        {
            index = i,
            choice = q["javob"]?.GetValue<int>(),
            text = q["javob_matn"]?.GetValue<string>(),
        }).ToList();

        var first = await PostAsync(client, $"/api/v1/interactive/units/{setup.UnitId}/test/submit", new { answers });
        first["counted"]!.GetValue<bool>().Should().BeTrue();
        first["score"]!.GetValue<int>().Should().Be(first["max"]!.GetValue<int>());

        var second = await PostAsync(client, $"/api/v1/interactive/units/{setup.UnitId}/test/submit", new { answers = Array.Empty<object>() });
        second["counted"]!.GetValue<bool>().Should().BeFalse();

        var unit = await client.GetFromJsonAsync<JsonObject>($"/api/v1/interactive/units/{setup.UnitId}");
        unit!["progress"]!["test"]!["score"]!.GetValue<int>().Should().Be(first["score"]!.GetValue<int>());
    }

    // ================================================================ qurilma

    private sealed record Setup(long CourseId, long LessonId, long UnitId, string AnyHandle, string Dir);

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Interactive", "atf1_birlik_01.json");

    private async Task<Setup> SetUpAsync(string prefix, bool publish = true)
    {
        var (courseId, lessonId) = await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"{prefix} {Guid.NewGuid():N}"[..30] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var module = new CourseModule { CourseId = course.Id, Name = "Harf moduli", Position = 1 };
            db.Modules.Add(module);
            await db.SaveChangesAsync();

            var lesson = new ModuleLesson { ModuleId = module.Id, Name = "Alif", Position = 1 };
            db.ModuleLessons.Add(lesson);
            await db.SaveChangesAsync();
            return (course.Id, lesson.Id);
        });

        // Har test o'z kitobi bilan: kitob kodi prefiksdan.
        var dir = Path.Combine(Path.GetTempPath(), "zinnur-itest-" + Guid.NewGuid().ToString("N")[..8], "birlik_01");
        Directory.CreateDirectory(Path.Combine(dir, "audio"));
        File.Copy(FixturePath, Path.Combine(dir, "dars.json"));
        File.WriteAllText(Path.Combine(dir, "ustoz_qollanmasi.md"), "# Ustoz uchun\nMaxfiy qo'llanma.");

        var unitJson = UnitJson.Parse(File.ReadAllText(FixturePath));
        foreach (var id in ContentVerifier.AudioIds(unitJson))
            File.WriteAllBytes(Path.Combine(dir, "audio", id + ".mp3"), FakeMp3(id));

        var setup = new Setup(courseId, lessonId, 0, string.Empty, Path.GetDirectoryName(dir)!);
        var report = await ImportAsync(setup, prefix, lessonId);
        report.Ok.Should().BeTrue(string.Join("; ", report.Errors.Concat(report.Units.SelectMany(u => u.Errors))));

        var (unitId, versionId) = await factory.WithDbAsync(async db =>
        {
            var book = await db.InteractiveBooks.SingleAsync(b => b.Code == Code(prefix));
            book.IsPublished = publish;
            await db.SaveChangesAsync();

            var unit = await db.InteractiveUnits.SingleAsync(u => u.BookId == book.Id);
            return (unit.Id, unit.CurrentVersionId!.Value);
        });

        var handle = factory.Services.GetRequiredService<AudioHandleCodec>().Handle(versionId, "s01", AudioHandleCodec.Display);
        return setup with { UnitId = unitId, AnyHandle = handle };
    }

    private Task<ImportReport> ImportAsync(Setup setup) =>
        ImportAsync(setup, PrefixOf(setup), setup.LessonId);

    private readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal);

    private string PrefixOf(Setup setup) => _prefixes[setup.Dir];

    private async Task<ImportReport> ImportAsync(Setup setup, string prefix, long lessonId)
    {
        _prefixes[setup.Dir] = prefix;

        using var scope = factory.Services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<InteractiveImporter>();

        var map = new UnitLessonMap
        {
            Units = new Dictionary<string, UnitLessonRef>(StringComparer.Ordinal)
            {
                ["birlik_01"] = new UnitLessonRef { LessonId = lessonId },
            },
        };

        return await importer.ImportAsync(new ImportOptions(setup.Dir, Code(prefix), setup.CourseId, map, false, []));
    }

    private static string Code(string prefix) => (BookCode + "-" + prefix)[..Math.Min(32, BookCode.Length + 1 + prefix.Length)];

    private async Task<TestUser> SubscribedStudentAsync(Setup setup, string prefix, bool includesBook)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var plan = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
        {
            name = prefix + (includesBook ? " plus" : " oddiy"),
            courseId = setup.CourseId,
            months = 1,
            price = 200_000m,
            includesInteractiveBook = includesBook,
        });
        plan.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(plan));
        var planId = (await plan.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<long>();

        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-s");
        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            studentId = student.Id,
            planId,
            method = "Cash",
        });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));

        return student;
    }

    private static async Task<JsonObject> PostAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    /// <summary>🔴 O'quvchi javobida kalit, ustoz izohi, qo'llanma va audio id YO'Q.</summary>
    private static void AssertNoKeys(string body)
    {
        foreach (var key in new[] { "\"ustozga\"", "\"javob\"", "\"javob_matn\"", "\"tts\"", "\"namuna\"", "\"javoblar\"", "\"tuzatish\"", "\"bosh\"" })
            body.Should().NotContain(key);

        body.Should().NotContain("Maxfiy").And.NotContain("\"s02\"").And.NotContain("\"x01\"");

        var source = JsonNode.Parse(File.ReadAllText(FixturePath))!.AsObject();
        foreach (var note in UnitJson.Blocks(source).Select(b => UnitJson.Str(b.Block, "ustozga")).OfType<string>())
            body.Should().NotContain(JsonEncodedText.Encode(note).ToString());
    }

    /// <summary>Haqiqiy mp3 sarlavhasi (ID3) + id bo'yicha farqli to'ldirma (> 5 KB).</summary>
    private static byte[] FakeMp3(string id)
    {
        var bytes = new byte[6 * 1024];
        Encoding.ASCII.GetBytes("ID3").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(id).CopyTo(bytes, 10);
        return bytes;
    }
}
