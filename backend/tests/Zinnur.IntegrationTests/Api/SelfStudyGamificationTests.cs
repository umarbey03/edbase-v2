using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Progress.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL GAMIFIKATSIYA (2026-11-21)
/// ════════════════════════════════════════════════════════════════════
///
/// Tekshiriladi:
///   - Kurs quruvchi darsga arab harflarini yozadi (alifbodan tashqari — 400);
///   - o'quvchi profilida 28 harf: o'zlashtirilgan dars harfi "Mastered",
///     belgilanmagan harf "Locked";
///   - bugun video ko'rgan Mustaqil o'quvchining seriyasi 1, bugun bajarilgan;
///   - ball daftari: kurator "o'zlashtirdi" desa +20.
/// </summary>
public sealed class SelfStudyGamificationTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Letters_streak_and_mastery_bonus()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, "gm-oquv");
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "gm-mustaqil");

        var (courseId, lesson1, lesson2) = await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"gm kurs {Guid.NewGuid():N}"[..30] };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var module = new CourseModule { CourseId = course.Id, Name = "Harflar", Position = 1 };
            db.Modules.Add(module);
            await db.SaveChangesAsync();

            var first = new ModuleLesson { ModuleId = module.Id, Name = "Alif harfi", Position = 1 };
            var second = new ModuleLesson { ModuleId = module.Id, Name = "Roo va Zaa harflari", Position = 2 };
            db.ModuleLessons.AddRange(first, second);
            await db.SaveChangesAsync();
            return (course.Id, first.Id, second.Id);
        });

        // 1) Kurs quruvchi: harflar (bo'shliq va takror tozalanadi).
        var set1 = await admin.PutAsJsonAsync($"/api/v1/courses/{courseId}/lessons/{lesson1}/letters", new { letters = "ا" });
        set1.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(set1));
        var set2 = await admin.PutAsJsonAsync($"/api/v1/courses/{courseId}/lessons/{lesson2}/letters", new { letters = "ر ز ر" });
        (await set2.Content.ReadFromJsonAsync<LessonLettersDto>(Json))!.Letters.Should().Be("رز");

        var bad = await admin.PutAsJsonAsync($"/api/v1/courses/{courseId}/lessons/{lesson2}/letters", new { letters = "abc" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var listed = await admin.GetFromJsonAsync<List<LessonLettersDto>>($"/api/v1/courses/{courseId}/letters", Json);
        listed!.Select(l => l.Number).Should().Equal(1, 2);

        // 2) Obuna (Mustaqil), 1-dars o'zlashtirilgan, bugun video ko'rilgan.
        var plan = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
        {
            name = "gm oylik",
            courseId,
            months = 1,
            price = 299_000m,
        });
        plan.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(plan));
        var planId = (await plan.Content.ReadFromJsonAsync<IdRow>(Json))!.Id;
        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new { studentId = student.Id, planId, method = "Cash" });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));

        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            db.LessonMasteries.Add(LessonMastery.Create(student.Id, lesson1, MasteryStatus.Mastered, null, academic.Id, now));
            db.LessonProgress.Add(new LessonProgress
            {
                StudentId = student.Id,
                ModuleLessonId = lesson1,
                VideoWatchedAt = now,
                CreatedAt = now,
            });
            return await db.SaveChangesAsync();
        });

        using var client = await WorldBuilder.ClientAsync(factory, student);

        // 3) Profil harflar jadvali.
        var letters = await client.GetFromJsonAsync<MyLettersDto>("/api/v1/progress/letters", Json);
        letters!.Total.Should().Be(28);
        letters.Letters.Single(l => l.Letter == "ا").State.Should().Be("Mastered");
        letters.Letters.Single(l => l.Letter == "ا").LessonNumber.Should().Be(1);
        letters.Letters.Single(l => l.Letter == "ب").LessonId.Should().BeNull();
        letters.Letters.Single(l => l.Letter == "ر").LessonId.Should().Be(lesson2);
        letters.Mastered.Should().Be(1);

        // 4) Kunlik seriya.
        var streak = await client.GetFromJsonAsync<MyStreakDto>("/api/v1/progress/streak", Json);
        streak!.Enabled.Should().BeTrue();
        streak.Current.Should().Be(1);
        streak.DoneToday.Should().BeTrue();
        streak.Days.Should().HaveCount(14);
        streak.Days[^1].State.Should().Be("Active");
        streak.NextBonusDay.Should().Be(3);

        // 5) Ball daftari: o'zlashtirish +20.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPointsSyncService>().SyncAsync();

        var mastery = await factory.WithDbAsync(db => db.StudentPoints.AsNoTracking()
            .Where(p => p.StudentId == student.Id && p.Kind == PointKind.LessonMastered)
            .Select(p => p.Delta)
            .ToListAsync());
        mastery.Should().Equal(20);
    }

    [Fact]
    public async Task Group_student_has_no_daily_streak()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "gm-guruh");
        using var client = await WorldBuilder.ClientAsync(factory, student);

        var streak = await client.GetFromJsonAsync<MyStreakDto>("/api/v1/progress/streak", Json);
        streak!.Enabled.Should().BeFalse();
    }

    private sealed record IdRow(long Id);
}
