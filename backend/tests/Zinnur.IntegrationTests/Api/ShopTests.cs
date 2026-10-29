using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Shop;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ONLAYN SOTUV (2026-10-29): tarif → buyurtma → xodim tasdiqlashi.
///
/// ★ ASOSIY TEKSHIRUVLAR: pul ikki marta yozilmaydi; mustaqil tarif guruh
///   o'quvchisiga sotilmaydi; tasdiqlashni faqat o'quv bo'limi/admin qiladi
///   (panel ham, Telegram tugmasi ham); guruhsiz "mehmon" o'quvchi guruh
///   dunyosidan hech narsa ko'rmaydi.
/// </summary>
public sealed class ShopTests(ZinnurApiFactory factory) : IClassFixture<ZinnurApiFactory>
{
    [Fact]
    public async Task Offers_are_public_and_show_only_public_active_plans()
    {
        var setup = await SetUpAsync("shop-offers");

        using var anonymous = factory.CreateClient();
        var body = await anonymous.GetStringAsync("/api/v1/shop/offers");

        body.Should().Contain(setup.SelfPlanName);
        body.Should().Contain(setup.GroupPlanName);
        body.Should().NotContain(setup.HiddenPlanName);
    }

    [Fact]
    public async Task Offers_mark_the_plan_with_the_interactive_book()
    {
        var setup = await SetUpAsync("shop-plus");
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var plusName = $"shop-plus mustaqil+ {Guid.NewGuid():N}"[..40];
        var create = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
        {
            name = plusName,
            courseId = setup.CourseId,
            months = 1,
            price = 249_000m,
            kind = PlanKind.SelfStudy.ToString(),
            isPublic = true,
            includesInteractiveBook = true,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(create));

        using var anonymous = factory.CreateClient();
        var offers = await anonymous.GetFromJsonAsync<OffersRow>("/api/v1/shop/offers");

        offers!.Plans.Should().ContainSingle(p => p.Name == plusName)
            .Which.IncludesInteractiveBook.Should().BeTrue();
        offers.Plans.Where(p => p.Name == setup.SelfPlanName).Should().NotBeEmpty()
            .And.OnlyContain(p => !p.IncludesInteractiveBook);
    }

    [Fact]
    public async Task Confirmed_self_study_order_opens_the_course_once()
    {
        var setup = await SetUpAsync("shop-self");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);

        var before = await guest.GetAsync($"/api/v1/courses/{setup.CourseId}");
        before.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var order = await CreateOrderAsync(guest, setup.SelfPlanId);
        order.Status.Should().Be(nameof(PurchaseOrderStatus.Pending));
        order.Amount.Should().Be(200_000m);

        // Ikki marta bosish — yangi buyurtma ochilmaydi.
        (await CreateOrderAsync(guest, setup.SelfPlanId)).Id.Should().Be(order.Id);

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var confirm = await admin.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/confirm", new { note = (string?)null });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(confirm));

        var again = await admin.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/confirm", new { note = (string?)null });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var periods = await factory.WithDbAsync(db =>
            db.SubscriptionPeriods.CountAsync(p => p.StudentId == setup.Guest.Id));
        periods.Should().Be(1);

        var after = await guest.GetAsync($"/api/v1/courses/{setup.CourseId}");
        after.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(after));
    }

    [Fact]
    public async Task Confirmed_group_order_goes_to_the_balance()
    {
        var setup = await SetUpAsync("shop-group");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);

        var order = await CreateOrderAsync(guest, setup.GroupPlanId);

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var confirm = await admin.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/confirm", new { note = (string?)null });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(confirm));

        var (transactionId, balance, periods) = await factory.WithDbAsync(async db =>
        {
            var saved = await db.PurchaseOrders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            var account = await db.StudentAccounts.AsNoTracking().FirstAsync(a => a.StudentId == setup.Guest.Id);
            var count = await db.SubscriptionPeriods.CountAsync(p => p.StudentId == setup.Guest.Id);
            return (saved.PaymentTransactionId, account.Balance, count);
        });

        transactionId.Should().NotBeNull();
        balance.Should().Be(540_000m);
        periods.Should().Be(0, "guruh tarifi obuna ochmaydi");
    }

    [Fact]
    public async Task Group_student_cannot_buy_self_study()
    {
        var setup = await SetUpAsync("shop-grouped");
        using var grouped = await WorldBuilder.ClientAsync(factory, setup.World.Student);

        var response = await grouped.PostAsJsonAsync("/api/v1/shop/orders", new { planId = setup.SelfPlanId });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(response));

        // Guruh tarifi esa — oylik to'lov sifatida — ochiq.
        var monthly = await grouped.PostAsJsonAsync("/api/v1/shop/orders", new { planId = setup.GroupPlanId });
        monthly.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(monthly));
    }

    [Fact]
    public async Task Only_managers_decide_orders_and_students_see_only_their_own()
    {
        var setup = await SetUpAsync("shop-roles");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        var order = await CreateOrderAsync(guest, setup.SelfPlanId);

        using var teacher = await WorldBuilder.ClientAsync(factory, setup.World.Teacher);
        (await teacher.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/confirm", new { note = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await teacher.GetAsync("/api/v1/shop/orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await guest.GetAsync("/api/v1/shop/orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var other = await WorldBuilder.ClientAsync(factory, setup.World.Student);
        (await other.PostAsync($"/api/v1/shop/orders/{order.Id}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var mine = await other.GetStringAsync("/api/v1/shop/me");
        mine.Should().NotContain(order.Code);
    }

    [Fact]
    public async Task Telegram_button_works_only_for_linked_managers()
    {
        var setup = await SetUpAsync("shop-tg");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        var order = await CreateOrderAsync(guest, setup.SelfPlanId);

        var strangerTelegram = 9_000_000_000L + Random.Shared.Next(1, 999_999);
        var managerTelegram = strangerTelegram + 1_000_000;

        await factory.WithDbAsync(async db =>
        {
            var academic = new User
            {
                FullName = "shop-tg Kassir",
                PasswordHash = "x",
                Role = UserRole.Academic,
                IsActive = true,
            };
            academic.LinkTelegram(managerTelegram, null, DateTimeOffset.UtcNow);
            db.Users.Add(academic);
            return await db.SaveChangesAsync();
        });

        using var scope = factory.Services.CreateScope();
        var shop = scope.ServiceProvider.GetRequiredService<IShopService>();

        (await shop.HandleCallbackAsync(strangerTelegram, $"so:ok:{order.Id}"))
            .Should().Contain("faqat administrator");
        (await shop.HandleCallbackAsync(managerTelegram, "av:yes:1")).Should().BeNull();

        (await shop.HandleCallbackAsync(managerTelegram, $"so:ok:{order.Id}"))
            .Should().Contain("tasdiqlandi");
        (await shop.HandleCallbackAsync(managerTelegram, $"so:no:{order.Id}"))
            .Should().Contain("allaqachon tasdiqlangan");
    }

    [Fact]
    public async Task Paid_claim_marks_the_order_once_and_only_while_pending()
    {
        var setup = await SetUpAsync("shop-paid");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        var order = await CreateOrderAsync(guest, setup.SelfPlanId);

        var first = await guest.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/paid", new { });
        first.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(first));
        var claimedAt = (await first.Content.ReadFromJsonAsync<PaidRow>())!.PaidClaimedAt;
        claimedAt.Should().NotBeNull();

        var again = await guest.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/paid", new { });
        (await again.Content.ReadFromJsonAsync<PaidRow>())!.PaidClaimedAt.Should().Be(claimedAt);

        var cancel = await guest.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var late = await guest.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/paid", new { });
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Former_group_student_sees_no_center_leaderboard()
    {
        var former = await WorldBuilder.CreateAsync(factory, "shop-former");
        var active = await WorldBuilder.CreateAsync(factory, "shop-active");

        await factory.WithDbAsync(db => db.GroupMembers
            .Where(m => m.StudentId == former.Student.Id && m.GroupId == former.GroupId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MemberStatus.Stopped)));

        using var client = await WorldBuilder.ClientAsync(factory, former.Student);
        var board = await client.GetStringAsync("/api/v1/leaderboard/center?metric=Points");

        board.Should().NotContain("shop-active Student");
    }

    [Fact]
    public async Task Guest_student_sees_no_group_world()
    {
        var setup = await SetUpAsync("shop-guest");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);

        var board = await guest.GetStringAsync("/api/v1/leaderboard/center?metric=Points");
        board.Should().NotContain(setup.GroupStudentName);

        var w = setup.World;
        foreach (var url in new[]
        {
            "/api/v1/live-sessions",
            "/api/v1/group-chat/threads",
            "/api/v1/messages/conversations",
            "/api/v1/students/me/classroom",
            $"/api/v1/leaderboard/groups/{w.GroupId}",
            $"/api/v1/users/{w.Student.Id}/profile",
            $"/api/v1/profile/avatar/{w.Teacher.Id}",
            "/api/v1/books",
        })
        {
            var response = await guest.GetAsync(url);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) continue;

            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(w.GroupName, url);
            body.Should().NotContain(setup.GroupStudentName, url);
        }
    }

    // ================================================================ qurilma

    private sealed record Setup(
        StudentWorld World,
        TestUser Guest,
        string GuestName,
        string GroupStudentName,
        long CourseId,
        long SelfPlanId,
        string SelfPlanName,
        long GroupPlanId,
        string GroupPlanName,
        string HiddenPlanName);

    private sealed record OrderRow(long Id, string Code, string Status, decimal Amount);

    private sealed record IdRow(long Id);

    private sealed record PaidRow(DateTimeOffset? PaidClaimedAt);

    private sealed record OfferPlanRow(string Name, bool IncludesInteractiveBook);

    private sealed record OffersRow(IReadOnlyList<OfferPlanRow> Plans);

    private static async Task<OrderRow> CreateOrderAsync(HttpClient client, long planId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/shop/orders", new { planId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        return (await response.Content.ReadFromJsonAsync<OrderRow>())!;
    }

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

            db.ModuleLessons.Add(new ModuleLesson { ModuleId = module.Id, Name = "1-dars", Position = 1 });
            await db.SaveChangesAsync();
            return course.Id;
        });

        async Task<long> PlanAsync(string name, PlanKind kind, decimal price, bool isPublic)
        {
            var response = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
            {
                name,
                courseId,
                months = 1,
                price,
                kind = kind.ToString(),
                description = "Video darslar\nVazifalar",
                isPublic,
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
            return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
        }

        var selfName = $"{prefix} mustaqil";
        var groupName = $"{prefix} guruh";
        var hiddenName = $"{prefix} yashirin";

        var selfPlan = await PlanAsync(selfName, PlanKind.SelfStudy, 200_000m, isPublic: true);
        var groupPlan = await PlanAsync(groupName, PlanKind.Group, 540_000m, isPublic: true);
        await PlanAsync(hiddenName, PlanKind.SelfStudy, 100_000m, isPublic: false);

        var guestPrefix = prefix + "-mehmon";
        var guest = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, guestPrefix);

        return new Setup(
            world, guest, $"{guestPrefix} Student", $"{prefix} Student",
            courseId, selfPlan, selfName, groupPlan, groupName, hiddenName);
    }
}
