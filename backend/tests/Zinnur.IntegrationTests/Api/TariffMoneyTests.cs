using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TARIF PULI QOIDALARI (2026-10-29, loyiha egasi qarorlari Q-1, Q-2, Q-4, Q-5)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ ASOSIY TEKSHIRUV — PUL SAQLANADI: har testda balans o'zgarishi jurnaldagi
///   yozuvlar yig'indisiga AYNAN teng, hech bir so'm ikki marta yozilmaydi.
/// ★ Vaqt — haqiqiy soat: pro-rata summasi so'rov vaqtiga bog'liq, shuning
///   uchun aniq qiymat emas, tor oraliq tekshiriladi (aniq formula —
///   `SubscriptionPeriodTests` unit testlarida).
/// </summary>
public sealed class TariffMoneyTests(ZinnurApiFactory factory) : IClassFixture<ZinnurApiFactory>
{
    // ================================================================ Q-1

    [Fact]
    public async Task Upgrade_mid_period_starts_now_and_credits_the_unused_part()
    {
        var setup = await SetUpAsync("tm-upgrade");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await BuyAsync(guest, admin, setup.BasicPlanId);
        await ShiftLivePeriodAsync(setup.Guest.Id, daysAgo: 10, daysLeft: 20);

        var upgrade = await BuyAsync(guest, admin, setup.PlusPlanId);
        var now = DateTimeOffset.UtcNow;

        var periods = await PeriodsAsync(setup.Guest.Id);
        periods.Should().HaveCount(2);

        var old = periods.Single(p => p.PlanId == setup.BasicPlanId);
        old.CancelledAt.Should().BeNull("eski davr bekor qilinmaydi — ishlatilgan qismi tarixda qoladi");
        old.EndsAt.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));

        var plus = periods.Single(p => p.PlanId == setup.PlusPlanId);
        plus.StartsAt.Should().BeCloseTo(now, TimeSpan.FromMinutes(1), "tarif almashtirish darhol boshlanadi");
        plus.Amount.Should().Be(450_000m, "buyurtma summasi o'zgarmaydi");
        plus.IncludesInteractiveBook.Should().BeTrue("darslik belgisi davrga muzlatiladi");

        // 300 000 × 20/30 = 200 000 (so'rov vaqti tufayli bir necha so'm kam bo'lishi mumkin).
        var ledger = await LedgerAsync(setup.Guest.Id);
        var credit = ledger.Single(t => t.Kind == PaymentTransactionKind.SubscriptionCredit).Amount;
        credit.Should().BeInRange(199_900m, 200_000m);
        (credit % 1).Should().Be(0, "pastga butun so'mgacha yaxlitlanadi");
        (await BalanceAsync(setup.Guest.Id)).Should().Be(credit);

        ledger.Where(t => t.Kind == PaymentTransactionKind.Subscription)
            .Select(t => t.Amount).Should().BeEquivalentTo(new[] { 300_000m, 450_000m });

        var link = await factory.WithDbAsync(db => db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Id == upgrade.Id)
            .Select(o => new { o.SubscriptionPeriodId, o.PaymentTransactionId })
            .SingleAsync());
        link.SubscriptionPeriodId.Should().Be(plus.Id);
        link.PaymentTransactionId.Should().NotBeNull("obuna puli ham jurnalga yoziladi (Q-5)");

        var me = await guest.GetFromJsonAsync<ShopMeRow>("/api/v1/shop/me");
        me!.CurrentPlanId.Should().Be(setup.PlusPlanId);
        me.Balance.Should().Be(credit);
    }

    [Fact]
    public async Task Same_plan_renewal_stacks_after_the_current_end_without_credit()
    {
        var setup = await SetUpAsync("tm-renew");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await BuyAsync(guest, admin, setup.BasicPlanId);
        await BuyAsync(guest, admin, setup.BasicPlanId);

        var periods = (await PeriodsAsync(setup.Guest.Id)).OrderBy(p => p.StartsAt).ToList();
        periods.Should().HaveCount(2);
        periods[1].StartsAt.Should().Be(periods[0].EndsAt);
        periods.Should().OnlyContain(p => p.CancelledAt == null);

        (await LedgerAsync(setup.Guest.Id)).Should()
            .NotContain(t => t.Kind == PaymentTransactionKind.SubscriptionCredit);
        (await BalanceAsync(setup.Guest.Id)).Should().Be(0m);
    }

    // ================================================================ Q-2

    [Fact]
    public async Task Moving_to_a_group_turns_unused_days_into_balance()
    {
        var setup = await SetUpAsync("tm-togroup");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await BuyAsync(guest, admin, setup.BasicPlanId);
        await ShiftLivePeriodAsync(setup.Guest.Id, daysAgo: 10, daysLeft: 20);
        await BuyAsync(guest, admin, setup.BasicPlanId); // oldindan to'langan keyingi oy

        var uri = $"/api/v1/groups/{setup.World.GroupId}/members";

        // Tasdiqsiz — 409, xabarda kun va summa.
        var refused = await admin.PostAsJsonAsync(uri, new { studentId = setup.Guest.Id });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("kun");

        var preview = await admin.GetFromJsonAsync<ConversionRow>(
            $"/api/v1/subscriptions/students/{setup.Guest.Id}/group-conversion");
        preview!.HasLiveSubscription.Should().BeTrue();
        preview.Amount.Should().BeInRange(499_900m, 500_000m, "20/30 × 300 000 + to'liq 300 000");
        preview.DaysLeft.Should().BeInRange(48, 51);

        var added = await admin.PostAsJsonAsync(uri, new { studentId = setup.Guest.Id, convertSubscription = true });
        added.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(added));

        var now = DateTimeOffset.UtcNow;
        var periods = (await PeriodsAsync(setup.Guest.Id)).OrderBy(p => p.StartsAt).ToList();
        periods[0].CancelledAt.Should().BeNull("joriy davr yopiladi, bekor qilinmaydi");
        periods[0].EndsAt.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));
        periods[1].CancelledAt.Should().NotBeNull("kelgusi davr bekor qilinadi");

        var credits = (await LedgerAsync(setup.Guest.Id))
            .Where(t => t.Kind == PaymentTransactionKind.SubscriptionCredit).ToList();
        credits.Should().HaveCount(2, "har davr uchun alohida jurnal yozuvi");
        credits.Should().Contain(t => t.Amount == 300_000m);

        var balance = await BalanceAsync(setup.Guest.Id);
        balance.Should().Be(credits.Sum(t => t.Amount));
        balance.Should().BeInRange(499_900m, 500_000m);

        var member = await factory.WithDbAsync(db => db.GroupMembers.AsNoTracking()
            .AnyAsync(m => m.StudentId == setup.Guest.Id && m.GroupId == setup.World.GroupId
                        && m.Status == MemberStatus.Active));
        member.Should().BeTrue();

        // Ikkinchi marta o'tkazib bo'lmaydi — tirik davr qolmagan.
        var again = await admin.GetFromJsonAsync<ConversionRow>(
            $"/api/v1/subscriptions/students/{setup.Guest.Id}/group-conversion");
        again!.HasLiveSubscription.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_group_add_keeps_the_subscription_untouched()
    {
        var setup = await SetUpAsync("tm-togroup-fail");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        await BuyAsync(guest, admin, setup.BasicPlanId);
        var before = (await PeriodsAsync(setup.Guest.Id)).Single();

        // A'zolik yozuvi bazada yiqiladi (obuna allaqachon pulga aylangandan KEYIN) —
        // hammasi qaytishi kerak: davr ham, balans ham, jurnal ham.
        var trigger = $"tm_member_{setup.Guest.Id}";
        await ExecAsync(string.Create(CultureInfo.InvariantCulture, $$"""
            CREATE FUNCTION {{trigger}}() RETURNS trigger AS $$
            BEGIN
                IF NEW."StudentId" = {{setup.Guest.Id}} THEN
                    RAISE EXCEPTION 'test: member insert failed';
                END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER {{trigger}} BEFORE INSERT ON "GroupMembers"
                FOR EACH ROW EXECUTE FUNCTION {{trigger}}();
            """));

        try
        {
            var response = await admin.PostAsJsonAsync(
                $"/api/v1/groups/{setup.World.GroupId}/members",
                new { studentId = setup.Guest.Id, convertSubscription = true });
            response.IsSuccessStatusCode.Should().BeFalse();
        }
        finally
        {
            await ExecAsync($"""DROP TRIGGER {trigger} ON "GroupMembers"; DROP FUNCTION {trigger}();""");
        }

        var after = (await PeriodsAsync(setup.Guest.Id)).Single();
        after.CancelledAt.Should().BeNull();
        after.EndsAt.Should().Be(before.EndsAt, "davr yopilmagan bo'lib qoladi");
        (await BalanceAsync(setup.Guest.Id)).Should().Be(0m);
        (await LedgerAsync(setup.Guest.Id)).Should().OnlyContain(t => t.Kind == PaymentTransactionKind.Subscription);
    }

    // ================================================================ Q-4

    [Fact]
    public async Task Balance_pays_for_self_study_without_staff()
    {
        var setup = await SetUpAsync("tm-balance");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        // Balansga 540 000 — Milliy buyurtmasi tasdiqlangan.
        await BuyAsync(guest, admin, setup.GroupPlanId);
        (await BalanceAsync(setup.Guest.Id)).Should().Be(540_000m);

        var response = await guest.PostAsJsonAsync("/api/v1/shop/orders/from-balance", new { planId = setup.BasicPlanId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        var order = (await response.Content.ReadFromJsonAsync<OrderRow>())!;
        order.Status.Should().Be(nameof(PurchaseOrderStatus.Confirmed));

        (await BalanceAsync(setup.Guest.Id)).Should().Be(240_000m);

        var period = (await PeriodsAsync(setup.Guest.Id)).Should().ContainSingle().Subject;
        period.Method.Should().BeNull("balansdan to'langan — kassaga pul tushmagan");
        period.Amount.Should().Be(300_000m);

        var ledger = await LedgerAsync(setup.Guest.Id);
        ledger.Should().ContainSingle(t => t.Kind == PaymentTransactionKind.SubscriptionBalanceUse)
            .Which.Amount.Should().Be(300_000m);
        ledger.Should().NotContain(t => t.Kind == PaymentTransactionKind.Subscription,
            "balansdan to'lov kassa tushumi emas");

        var saved = await factory.WithDbAsync(db => db.PurchaseOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id));
        saved.SubscriptionPeriodId.Should().Be(period.Id);
        saved.DecisionNote.Should().Contain("Balansdan");

        // Mablag' yetmaydi (240 000 < 450 000) — hech narsa o'zgarmaydi.
        var poor = await guest.PostAsJsonAsync("/api/v1/shop/orders/from-balance", new { planId = setup.PlusPlanId });
        poor.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BalanceAsync(setup.Guest.Id)).Should().Be(240_000m);
        (await PeriodsAsync(setup.Guest.Id)).Should().HaveCount(1);
        (await factory.WithDbAsync(db => db.PurchaseOrders.CountAsync(o => o.StudentId == setup.Guest.Id)))
            .Should().Be(2, "yiqilgan amal buyurtma ham qoldirmaydi");
    }

    [Fact]
    public async Task Balance_purchase_respects_the_group_and_pending_order_rules()
    {
        var setup = await SetUpAsync("tm-balance-rules");
        using var grouped = await WorldBuilder.ClientAsync(factory, setup.World.Student);

        var inGroup = await grouped.PostAsJsonAsync("/api/v1/shop/orders/from-balance", new { planId = setup.BasicPlanId });
        inGroup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        await GiveBalanceAsync(setup.Guest.Id, 1_000_000m);
        (await guest.PostAsJsonAsync("/api/v1/shop/orders", new { planId = setup.BasicPlanId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // To'lanmagan buyurtma bor — u Payme'da to'langan bo'lishi mumkin, ikki marta olinmasin.
        var pending = await guest.PostAsJsonAsync("/api/v1/shop/orders/from-balance", new { planId = setup.BasicPlanId });
        pending.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BalanceAsync(setup.Guest.Id)).Should().Be(1_000_000m);
    }

    // ================================================================ Q-5

    [Fact]
    public async Task Confirmed_self_study_order_is_in_the_ledger_but_not_in_group_income()
    {
        var setup = await SetUpAsync("tm-ledger");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await BuyAsync(guest, admin, setup.BasicPlanId);

        var entry = (await LedgerAsync(setup.Guest.Id)).Should().ContainSingle().Subject;
        entry.Kind.Should().Be(PaymentTransactionKind.Subscription);
        entry.Amount.Should().Be(300_000m);
        entry.ReceiptNo.Should().StartWith("ZN-");
        entry.Method.Should().Be(PaymentMethod.Card);

        var summary = await admin.GetFromJsonAsync<SummaryRow>("/api/v1/payments/summary");
        summary!.Kpi.SubscriptionCollected.Should().BeGreaterThanOrEqualTo(300_000m);

        // Guruh hisobi (o'quvchi kartochkasi) — qarz yoki to'langan oy paydo bo'lmaydi.
        (await factory.WithDbAsync(db => db.Payments.CountAsync(p => p.StudentId == setup.Guest.Id)))
            .Should().Be(0);
    }

    [Fact]
    public async Task Cancelling_a_period_writes_the_refund_the_staff_chose()
    {
        var setup = await SetUpAsync("tm-refund");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await BuyAsync(guest, admin, setup.BasicPlanId);
        await BuyAsync(guest, admin, setup.BasicPlanId);
        var periods = (await PeriodsAsync(setup.Guest.Id)).OrderBy(p => p.StartsAt).ToList();

        // Summadan ko'p — 400.
        var tooMuch = await CancelAsync(admin, periods[1].Id, "ToBalance", 300_001m);
        tooMuch.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Balansga.
        var toBalance = await CancelAsync(admin, periods[1].Id, "ToBalance", 300_000m);
        toBalance.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(toBalance));
        (await BalanceAsync(setup.Guest.Id)).Should().Be(300_000m);

        // Naqd — balans o'zgarmaydi, kassadan chiqim yoziladi.
        var cash = await CancelAsync(admin, periods[0].Id, "Cash", 120_000m);
        cash.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(cash));
        (await BalanceAsync(setup.Guest.Id)).Should().Be(300_000m);

        var ledger = await LedgerAsync(setup.Guest.Id);
        ledger.Should().ContainSingle(t => t.Kind == PaymentTransactionKind.SubscriptionCredit)
            .Which.Amount.Should().Be(300_000m);
        var refund = ledger.Should().ContainSingle(t => t.Kind == PaymentTransactionKind.SubscriptionRefund).Subject;
        refund.Amount.Should().Be(120_000m);
        refund.Method.Should().Be(PaymentMethod.Cash);
        ledger.Should().NotContain(t => t.Kind == PaymentTransactionKind.Refund,
            "obuna qaytarish guruh to'lovlari qaytarishiga aralashmaydi");

        // Ikkinchi marta — 409, pul ikki marta chiqmaydi.
        (await CancelAsync(admin, periods[0].Id, "Cash", 1m)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Ended_period_cannot_be_refunded_and_none_mode_writes_nothing()
    {
        var setup = await SetUpAsync("tm-refund-ended");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        await BuyAsync(guest, admin, setup.BasicPlanId);
        await ShiftLivePeriodAsync(setup.Guest.Id, daysAgo: 40, daysLeft: -10);
        var period = (await PeriodsAsync(setup.Guest.Id)).Single();

        (await CancelAsync(admin, period.Id, "ToBalance", 1_000m)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var none = await CancelAsync(admin, period.Id, "None", null);
        none.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(none));
        (await LedgerAsync(setup.Guest.Id)).Should().OnlyContain(t => t.Kind == PaymentTransactionKind.Subscription);
        (await BalanceAsync(setup.Guest.Id)).Should().Be(0m);
    }

    // ================================================================ P0-1

    [Fact]
    public async Task Sold_plan_keeps_its_kind_and_course()
    {
        var setup = await SetUpAsync("tm-kind");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        // Buyurtmasi yo'q tarif — turi o'zgaradi.
        (await UpdatePlanAsync(admin, setup, setup.PlusPlanId, PlanKind.Group, 450_000m))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await guest.PostAsJsonAsync("/api/v1/shop/orders", new { planId = setup.BasicPlanId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var kind = await UpdatePlanAsync(admin, setup, setup.BasicPlanId, PlanKind.Group, 300_000m);
        kind.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await kind.Content.ReadAsStringAsync()).Should().Contain("turi va kursi");

        // Narx o'zgarishi — mumkin (buyurtmadagi narx muzlatilgan).
        (await UpdatePlanAsync(admin, setup, setup.BasicPlanId, PlanKind.SelfStudy, 310_000m))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ================================================================ P1-7

    [Fact]
    public async Task Confirm_is_atomic_when_the_last_step_fails()
    {
        var setup = await SetUpAsync("tm-atomic");
        using var guest = await WorldBuilder.ClientAsync(factory, setup.Guest);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var order = await CreateOrderAsync(guest, setup.BasicPlanId);

        // Oxirgi qadam (buyurtmaga havola yozish) bazada yiqiladi — faqat SHU buyurtma uchun.
        var trigger = $"tm_atomic_{order.Id}";
        await ExecAsync(string.Create(CultureInfo.InvariantCulture, $$"""
            CREATE FUNCTION {{trigger}}() RETURNS trigger AS $$
            BEGIN
                IF NEW."Id" = {{order.Id}} AND NEW."SubscriptionPeriodId" IS NOT NULL THEN
                    RAISE EXCEPTION 'test: link failed';
                END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER {{trigger}} BEFORE UPDATE ON "PurchaseOrders"
                FOR EACH ROW EXECUTE FUNCTION {{trigger}}();
            """));

        try
        {
            var failed = await admin.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/confirm", new { note = (string?)null });
            failed.IsSuccessStatusCode.Should().BeFalse();

            var state = await factory.WithDbAsync(db => db.PurchaseOrders.AsNoTracking()
                .Where(o => o.Id == order.Id)
                .Select(o => new { o.Status, o.SubscriptionPeriodId, o.PaymentTransactionId, o.DecidedById })
                .SingleAsync());
            state.Status.Should().Be(PurchaseOrderStatus.Pending, "band qilish ham qaytadi");
            state.DecidedById.Should().BeNull();
            (await PeriodsAsync(setup.Guest.Id)).Should().BeEmpty("davr qolmaydi");
            (await LedgerAsync(setup.Guest.Id)).Should().BeEmpty("pul jurnalga yozilmaydi");
        }
        finally
        {
            await ExecAsync($"""DROP TRIGGER {trigger} ON "PurchaseOrders"; DROP FUNCTION {trigger}();""");
        }

        // Nosozlik tuzalgach — bir marta, to'g'ri tasdiqlanadi.
        var ok = await admin.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/confirm", new { note = (string?)null });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(ok));
        (await PeriodsAsync(setup.Guest.Id)).Should().ContainSingle();
        (await LedgerAsync(setup.Guest.Id)).Should().ContainSingle(t => t.Kind == PaymentTransactionKind.Subscription);
    }

    // ================================================================ qurilma

    private sealed record Setup(
        StudentWorld World,
        TestUser Guest,
        long CourseId,
        long BasicPlanId,
        long PlusPlanId,
        long GroupPlanId);

    private sealed record OrderRow(long Id, string Code, string Status, decimal Amount);

    private sealed record IdRow(long Id);

    private sealed record ShopMeRow(decimal Balance, long? CurrentPlanId);

    private sealed record ConversionRow(bool HasLiveSubscription, int DaysLeft, decimal Amount);

    private sealed record SummaryKpiRow(decimal Collected, decimal SubscriptionCollected);

    private sealed record SummaryRow(SummaryKpiRow Kpi);

    private static async Task<OrderRow> CreateOrderAsync(HttpClient client, long planId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/shop/orders", new { planId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        return (await response.Content.ReadFromJsonAsync<OrderRow>())!;
    }

    private static async Task<OrderRow> BuyAsync(HttpClient student, HttpClient admin, long planId)
    {
        var order = await CreateOrderAsync(student, planId);
        var confirm = await admin.PostAsJsonAsync($"/api/v1/shop/orders/{order.Id}/confirm", new { note = (string?)null });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(confirm));
        return order;
    }

    private static Task<HttpResponseMessage> CancelAsync(
        HttpClient admin, long periodId, string mode, decimal? amount) =>
        admin.PostAsJsonAsync($"/api/v1/subscriptions/{periodId}/cancel", new
        {
            reason = "sinov",
            refundMode = mode,
            refundAmount = amount,
        });

    private static Task<HttpResponseMessage> UpdatePlanAsync(
        HttpClient admin, Setup setup, long planId, PlanKind kind, decimal price) =>
        admin.PutAsJsonAsync($"/api/v1/subscriptions/plans/{planId}", new
        {
            name = $"tm plan {planId}",
            courseId = setup.CourseId,
            months = 1,
            price,
            kind = kind.ToString(),
            isPublic = true,
        });

    /// <summary>Hozirgi (eng birinchi tirik) davrni vaqtda suradi — "10 kun o'tdi, 20 kun qoldi".</summary>
    private Task<int> ShiftLivePeriodAsync(long studentId, int daysAgo, int daysLeft) =>
        factory.WithDbAsync(async db =>
        {
            var first = await db.SubscriptionPeriods
                .Where(p => p.StudentId == studentId && p.CancelledAt == null)
                .OrderBy(p => p.StartsAt)
                .FirstAsync();

            var now = DateTimeOffset.UtcNow;
            first.StartsAt = now.AddDays(-daysAgo);
            first.EndsAt = now.AddDays(daysLeft);
            return await db.SaveChangesAsync();
        });

    private Task<int> GiveBalanceAsync(long studentId, decimal amount) =>
        factory.WithDbAsync(async db =>
        {
            db.StudentAccounts.Add(new StudentAccount { StudentId = studentId, Balance = amount });
            return await db.SaveChangesAsync();
        });

    private Task<int> ExecAsync(string sql) =>
        factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(sql));

    private Task<List<SubscriptionPeriod>> PeriodsAsync(long studentId) =>
        factory.WithDbAsync(db => db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.StudentId == studentId)
            .ToListAsync());

    private Task<List<PaymentTransaction>> LedgerAsync(long studentId) =>
        factory.WithDbAsync(db => db.PaymentTransactions.AsNoTracking()
            .Where(t => t.StudentId == studentId)
            .ToListAsync());

    private Task<decimal> BalanceAsync(long studentId) =>
        factory.WithDbAsync(async db => await db.StudentAccounts.AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .Select(a => (decimal?)a.Balance)
            .FirstOrDefaultAsync() ?? 0m);

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

        async Task<long> PlanAsync(string name, PlanKind kind, decimal price, bool book)
        {
            var response = await admin.PostAsJsonAsync("/api/v1/subscriptions/plans", new
            {
                name,
                courseId,
                months = 1,
                price,
                kind = kind.ToString(),
                isPublic = true,
                includesInteractiveBook = book,
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
            return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
        }

        var basic = await PlanAsync($"{prefix} mustaqil", PlanKind.SelfStudy, 300_000m, book: false);
        var plus = await PlanAsync($"{prefix} mustaqil+", PlanKind.SelfStudy, 450_000m, book: true);
        var group = await PlanAsync($"{prefix} milliy", PlanKind.Group, 540_000m, book: false);

        var guest = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, prefix + "-mehmon");

        return new Setup(world, guest, courseId, basic, plus, group);
    }
}
