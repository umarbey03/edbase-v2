using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.SelfStudy.Pool;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Xodimlar Telegram guruhi sozlangan API — "kuratorlar to'ldi" eslatmasi
/// navbatga tushishini tekshirish uchun. Xabar yuborilmaydi (worker o'chiq).
/// </summary>
public sealed class CuratorPoolApiFactory : ZinnurApiFactory
{
    public const long StaffChatId = -1001234567890;

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Sales:StaffChatId", StaffChatId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("Notifications:Enabled", "false"),
    ];
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// MUSTAQIL KURATORLARI — AVTOMATIK BIRIKTIRISH (2026-11-20)
/// ════════════════════════════════════════════════════════════════════
///
/// Loyiha egasining talabi: kurator qo'shilsa kuratorsiz o'quvchilar unga
/// o'zi biriktiriladi (sig'im to'lguncha); to'lsa — eslatma; yangi kurator
/// qo'shilsa navbat unga o'tadi.
///
/// ⚠️ Baza test sinfi bo'yicha umumiy va navbat GLOBAL (barcha kuratorsiz
/// o'quvchilar), shuning uchun Mustaqil o'quvchi yaratadigan test BITTA —
/// ssenariy bosqichma-bosqich, bir metodda.
/// </summary>
public sealed class CuratorPoolTests(CuratorPoolApiFactory factory)
    : IClassFixture<CuratorPoolApiFactory>
{
    private const string Pool = "/api/v1/self-study/panel/pool";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Pool_fills_curators_in_order_reminds_when_full_and_drains_to_a_new_curator()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var curatorA = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, "cp-a");
        var curatorB = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, "cp-b");
        var planId = await CreatePlanAsync(admin, "cp");

        var s1 = await MustaqilStudentAsync(admin, planId, "cp-1");
        var s2 = await MustaqilStudentAsync(admin, planId, "cp-2");
        var s3 = await MustaqilStudentAsync(admin, planId, "cp-3");

        // 1) A — sig'imi 2: birinchi kelgan IKKI o'quvchi unga o'zi biriktiriladi.
        var afterA = await AddAsync(admin, curatorA.Id, capacity: 2);
        afterA.Members.Should().ContainSingle(m => m.CuratorId == curatorA.Id && m.ActiveStudents == 2);
        afterA.WaitingStudents.Should().Be(1);
        afterA.NeedsNewCurator.Should().BeTrue("A to'ldi, uchinchi o'quvchi navbatda");

        (await CuratorOfAsync(s1.Id)).Should().Be(curatorA.Id);
        (await CuratorOfAsync(s2.Id)).Should().Be(curatorA.Id);
        (await CuratorOfAsync(s3.Id)).Should().BeNull();

        // 2) Eslatma xodimlar guruhiga — kuniga BIR marta (qayta yurishda takror yo'q).
        await RunJobAsync();
        var reminders = await RemindersAsync();
        reminders.Should().ContainSingle(m => m.RecipientAddress == CuratorPoolApiFactory.StaffChatId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

        // 3) B qo'shiladi — navbatdagi o'quvchi unga o'tadi.
        var afterB = await AddAsync(admin, curatorB.Id, capacity: 5);
        afterB.WaitingStudents.Should().Be(0);
        afterB.NeedsNewCurator.Should().BeFalse();
        (await CuratorOfAsync(s3.Id)).Should().Be(curatorB.Id);

        // 4) Yangi xaridor — fon vazifasi uni A ni (to'la) o'tkazib, B ga beradi.
        var s4 = await MustaqilStudentAsync(admin, planId, "cp-4");
        (await CuratorOfAsync(s4.Id)).Should().BeNull("hali vazifa yurmagan");
        await RunJobAsync();
        (await CuratorOfAsync(s4.Id)).Should().Be(curatorB.Id);

        // 5) A to'xtatildi va sig'imi oshirildi — yangi o'quvchi baribir unga tushmaydi.
        var paused = await admin.PutAsJsonAsync($"{Pool}/{curatorA.Id}", new { capacity = 50, isAccepting = false });
        paused.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(paused));
        var s5 = await MustaqilStudentAsync(admin, planId, "cp-5");
        await RunJobAsync();
        (await CuratorOfAsync(s5.Id)).Should().Be(curatorB.Id, "to'xtatilgan kurator yangi o'quvchi qabul qilmaydi");

        // 6) Ro'yxatdan chiqarish mavjud biriktirishlarga tegmaydi.
        (await admin.DeleteAsync($"{Pool}/{curatorA.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CuratorOfAsync(s1.Id)).Should().Be(curatorA.Id);
    }

    [Fact]
    public async Task Pool_is_managed_only_by_staff_and_validates_capacity()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var curator = await WorldBuilder.CreateUserAsync(admin, UserRole.Assistant, "cp-auth");
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "cp-auth-st");

        using var asCurator = await WorldBuilder.ClientAsync(factory, curator);
        (await asCurator.GetAsync(Pool)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var tooBig = await admin.PostAsJsonAsync(Pool, new { curatorId = curator.Id, capacity = 5000 });
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var notCurator = await admin.PostAsJsonAsync(Pool, new { curatorId = student.Id, capacity = 10 });
        notCurator.StatusCode.Should().Be(HttpStatusCode.BadRequest, "o'quvchi kurator bo'la olmaydi");
    }

    // ================================================================ qurilma

    private static async Task<CuratorPoolDto> AddAsync(HttpClient admin, long curatorId, int capacity)
    {
        var response = await admin.PostAsJsonAsync(Pool, new { curatorId, capacity });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        return (await response.Content.ReadFromJsonAsync<CuratorPoolDto>(Json))!;
    }

    private async Task RunJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICuratorPoolService>().AutoAssignAsync();
    }

    private Task<long?> CuratorOfAsync(long studentId) => factory.WithDbAsync(db =>
        db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.EndedAt == null)
            .Select(a => (long?)a.CuratorId)
            .FirstOrDefaultAsync());

    private Task<List<MessageOutbox>> RemindersAsync() => factory.WithDbAsync(db =>
        db.MessageOutbox.AsNoTracking()
            .Where(m => m.TemplateKey == TelegramTemplates.SelfStudyCuratorsFull)
            .ToListAsync());

    private async Task<long> CreatePlanAsync(HttpClient admin, string prefix)
    {
        var courseId = await factory.WithDbAsync(async db =>
        {
            var course = new Course { Name = $"{prefix} kurs {Guid.NewGuid():N}"[..40] };
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
        return (await plan.Content.ReadFromJsonAsync<IdRow>(Json))!.Id;
    }

    private static async Task<TestUser> MustaqilStudentAsync(HttpClient admin, long planId, string name)
    {
        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, name);
        var grant = await admin.PostAsJsonAsync("/api/v1/subscriptions", new
        {
            studentId = student.Id,
            planId,
            method = "Cash",
        });
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(grant));
        return student;
    }

    private sealed record IdRow(long Id);
}
