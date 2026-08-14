using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// MOLIYA ENDPOINTLARI (FAZA 4.3) — HAQIQIY baza bilan
/// ========================================================================
///
/// NIMA UCHUN AYNAN BU TESTLAR: bu modul PUL bilan ishlaydi va eski
/// tizimning eng qimmat xatolari aynan shu yerda edi:
///
///   • qisman to'lov "to'langan" bo'lib qolardi (100 000 so'm 540 000 lik
///     oyni yopardi) — markaz jimgina pul yo'qotardi;
///   • ortiqcha to'lov hech qayerga yozilmasdi — oldindan to'lagan o'quvchi
///     keyingi oy qarzdor bo'lib chiqardi;
///   • qaytarish faqat jurnalga tushardi, oy esa "to'langan" turardi;
///   • oylik yozuvlarni ikki marta ochish dublikat qatorlar yaratardi.
///
/// Domain unit testlari MATEMATIKANI qo'riqlaydi; bu yerdagi testlar esa
/// BAZA bilan chegarani: unikal indeks, `numeric(18,2)` aniqligi,
/// tranzaksiya butunligi va ruxsat matritsasi.
/// </summary>
public sealed class PaymentEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const decimal MonthlyPrice = 540_000m;
    private const string Period = "2026-05";
    private const string OtherPeriod = "2026-06";

    // ================================================================= 1) OY OCHISH

    /// <summary>
    /// ★ IDEMPOTENTLIK: ikkinchi chaqiruv yangi qator YARATMAYDI va XATO ham
    /// bermaydi — u shunchaki "allaqachon ochilgan" deb hisobot qiladi.
    ///
    /// Eski tizimda konteyner qayta ko'tarilsa yoki xodim tugmani ikki marta
    /// bossa, bitta oy uchun ikkita qator paydo bo'lardi va o'quvchining
    /// qarzi IKKI BARAVAR ko'rinardi.
    /// </summary>
    [Fact]
    public async Task OpenPeriod_CalledTwice_CreatesRowsOnlyOnce()
    {
        var world = await NewWorldAsync();

        var first = await OpenPeriodAsync(world, Period);
        first.Created.Should().Be(1);
        first.AlreadyOpen.Should().Be(0);

        var second = await OpenPeriodAsync(world, Period);
        second.Created.Should().Be(0, "takror chaqiruv yangi qator yaratmasligi kerak");
        second.AlreadyOpen.Should().Be(1);

        var rows = await factory.WithDbAsync(db => db.Payments
            .CountAsync(p => p.StudentId == world.StudentId && p.Period == Period));

        rows.Should().Be(1);
    }

    /// <summary>
    /// ★ BAZA — OXIRGI HIMOYA: kod darajasidagi tekshiruvni chetlab o'tib
    /// qo'lda ikkinchi qator yozishga urinsak, `(StudentId, GroupId, Period)`
    /// unikal indeksi uni rad etadi.
    /// </summary>
    [Fact]
    public async Task Payments_DuplicateStudentGroupPeriod_IsRejectedByDatabase()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, Period);

        var act = async () => await factory.WithDbAsync(async db =>
        {
            db.Payments.Add(new Payment
            {
                StudentId = world.StudentId,
                GroupId = world.GroupId,
                Period = Period,
                BaseAmount = MonthlyPrice,
                DiscountAmount = 0m,
                Amount = MonthlyPrice,
            });

            return await db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateException>(
            "unikal indeks dublikat oylik yozuvni to'xtatishi kerak");
    }

    /// <summary>
    /// ★★ IKKI KASSIR BIR VAQTDA BIR OYNI YOPSA — PUL YO'QOLMAYDI.
    ///
    /// `Payment` da Postgres'ning `xmin` tizim ustuni optimistik qulf
    /// sifatida sozlangan: ikkinchi <c>UPDATE</c> 0 qator o'zgartiradi va
    /// <c>DbUpdateConcurrencyException</c> ko'tariladi (servis uni 409 ga
    /// aylantiradi). "Oxirgi yozgan yutadi" bo'lsa bitta to'lov jimgina
    /// ustidan yozilardi va kassa hisobi bir necha yuz ming so'mga
    /// kamayardi — buni faqat oy oxirida sezish mumkin bo'lardi.
    ///
    /// Test ATAYLAB ikkita ALOHIDA `DbContext` bilan: bitta kontekstda
    /// ikki marta o'zgartirish qulfni umuman ishga tushirmasdi.
    /// </summary>
    [Fact]
    public async Task ConcurrentUpdatesOnTheSameMonth_AreRejectedByOptimisticLock()
    {
        var world = await NewWorldAsync();
        var opened = await OpenPeriodAsync(world, Period);
        var paymentId = opened.Payments[0].Id;

        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();

        var firstDb = firstScope.ServiceProvider
            .GetRequiredService<Zinnur.Infrastructure.Persistence.ApplicationDbContext>();
        var secondDb = secondScope.ServiceProvider
            .GetRequiredService<Zinnur.Infrastructure.Persistence.ApplicationDbContext>();

        var first = await firstDb.Payments.FirstAsync(p => p.Id == paymentId);
        var second = await secondDb.Payments.FirstAsync(p => p.Id == paymentId);

        first.PaidAmount = 100_000m;
        await firstDb.SaveChangesAsync();

        second.PaidAmount = 200_000m;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondDb.SaveChangesAsync());

        var stored = await factory.WithDbAsync(db => db.Payments
            .Where(p => p.Id == paymentId)
            .Select(p => p.PaidAmount)
            .FirstAsync());

        stored.Should().Be(100_000m, "yutqazgan yozuv HECH NARSANI o'zgartirmasligi kerak");
    }

    /// <summary>
    /// Tarif ANIQLIKDAN UMUMIYGA tanlanadi (guruh &gt; kurs &gt; umumiy) va
    /// chegirma qo'llanadi. Uch summa ham yozuvda saqlanadi — hisobot
    /// "tarif bo'yicha kutilgan tushum" va "berilgan chegirma" ni ko'rsatishi
    /// kerak.
    /// </summary>
    [Fact]
    public async Task OpenPeriod_PicksMostSpecificTariffAndAppliesDiscount()
    {
        var world = await NewWorldAsync(createTariff: false);

        await CreateTariffAsync(new { name = "Umumiy", amount = 400_000m, activeFrom = "2026-01-01" });
        await CreateTariffAsync(new
        {
            name = "Guruhga",
            amount = MonthlyPrice,
            activeFrom = "2026-01-01",
            groupId = world.GroupId,
        });

        using var admin = await AdminClientAsync();

        var discount = await admin.PostAsJsonAsync(
            $"/api/v1/payments/students/{world.StudentId}/discounts",
            new { kind = "Percent", value = 10m, validFrom = "2026-01-01" });

        await EnsureStatusAsync(discount, HttpStatusCode.Created);

        var opened = await OpenPeriodAsync(world, Period);

        opened.Created.Should().Be(1);

        var month = opened.Payments[0];

        month.BaseAmount.Should().Be(MonthlyPrice, "guruhga atalgan tarif umumiysidan ustun");
        month.DiscountAmount.Should().Be(54_000m);
        month.Amount.Should().Be(486_000m);
        month.Status.Should().Be("Due");
    }

    // ================================================================= 2) TO'LOV

    /// <summary>
    /// ★★ ENG QIMMAT ESKI XATO: qisman to'lov to'liq to'lov EMAS.
    /// 100 000 so'm 540 000 lik oyni yopmaydi — oy `Partial` bo'lib qoladi,
    /// qolgan 440 000 hamon QARZ va `paidAt` QO'YILMAYDI.
    /// </summary>
    [Fact]
    public async Task RecordPayment_Partial_LeavesRemainderAsDebt()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, Period);

        var receipt = await PayAsync(world.StudentId, 100_000m);

        receipt.Applied.Should().Be(100_000m);
        receipt.ToBalance.Should().Be(0m);
        receipt.MonthsClosed.Should().Be(0);
        receipt.MonthsPartial.Should().Be(1);
        receipt.DebtAfter.Should().Be(440_000m);

        var account = await AccountAsync(world.StudentId);
        var month = account.Months[0];

        month.Status.Should().Be("Partial");
        month.PaidAmount.Should().Be(100_000m);
        month.Outstanding.Should().Be(440_000m);
        month.PaidAt.Should().BeNull("oy hali to'liq to'lanmagan");
    }

    /// <summary>
    /// ★ ORTIQCHA PUL BALANSGA TUSHADI, yo'qolmaydi. Eski tizimda ota-ona
    /// "3 oyga oldindan to'ladim" desa, ortig'i hech qayerga yozilmasdi.
    /// </summary>
    [Fact]
    public async Task RecordPayment_MoreThanDebt_PutsRemainderOnBalance()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, Period);

        var receipt = await PayAsync(world.StudentId, 800_000m);

        receipt.Applied.Should().Be(MonthlyPrice);
        receipt.ToBalance.Should().Be(260_000m);
        receipt.MonthsClosed.Should().Be(1);
        receipt.Balance.Should().Be(260_000m);
        receipt.DebtAfter.Should().Be(0m);

        var account = await AccountAsync(world.StudentId);

        account.Balance.Should().Be(260_000m);
        account.Debt.Should().Be(0m);
        account.Months[0].Status.Should().Be("Paid");
        account.Months[0].PaidAt.Should().NotBeNull();
    }

    /// <summary>Pul ENG ESKI qarzdan boshlab yopiladi (Domain qoidasi).</summary>
    [Fact]
    public async Task RecordPayment_ClosesOldestDebtFirst()
    {
        var world = await NewWorldAsync();

        await OpenPeriodAsync(world, Period);
        await OpenPeriodAsync(world, OtherPeriod);

        await PayAsync(world.StudentId, MonthlyPrice);

        var account = await AccountAsync(world.StudentId);

        Month(account, Period).Status.Should().Be("Paid", "eng eski oy birinchi yopiladi");
        Month(account, OtherPeriod).Status.Should().Be("Due");
        account.Debt.Should().Be(MonthlyPrice);
    }

    /// <summary>
    /// ★ OLDINDAN TO'LAGAN O'QUVCHI QARZDOR BO'LIB CHIQMAYDI: oy ochilgandan
    /// KEYIN balans avtomatik sarflanadi.
    /// </summary>
    [Fact]
    public async Task OpenPeriod_AfterPrepayment_ClosesMonthFromBalance()
    {
        var world = await NewWorldAsync();

        // Hali bironta oy ochilmagan — pul TO'LIQ balansga tushadi.
        var prepaid = await PayAsync(world.StudentId, MonthlyPrice);
        prepaid.ToBalance.Should().Be(MonthlyPrice);

        var opened = await OpenPeriodAsync(world, Period);

        opened.Created.Should().Be(1);
        opened.BalanceApplied.Should().Be(MonthlyPrice);
        opened.MonthsClosedFromBalance.Should().Be(1);

        var account = await AccountAsync(world.StudentId);

        account.Balance.Should().Be(0m);
        account.Debt.Should().Be(0m, "ochilgan oy balansdan darhol yopilishi kerak");
        Month(account, Period).Status.Should().Be("Paid");

        account.RecentTransactions.Should()
            .Contain(t => t.Kind == "BalanceUse" && t.Amount == MonthlyPrice);
    }

    /// <summary>
    /// Kvitansiya raqami <c>ZN-YYYY-MM-NNNNNN</c> formatida va oy ichida
    /// ketma-ket. Raqam qog'ozda beriladi va nizoda shu bo'yicha qidiriladi.
    /// </summary>
    [Fact]
    public async Task RecordPayment_IssuesSequentialReceiptNumbers()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, Period);

        var first = await PayAsync(world.StudentId, 50_000m);
        var second = await PayAsync(world.StudentId, 50_000m);

        first.ReceiptNo.Should().MatchRegex(@"^ZN-\d{4}-\d{2}-\d{6}$");

        var firstSeq = int.Parse(first.ReceiptNo[^6..], CultureInfo.InvariantCulture);
        var secondSeq = int.Parse(second.ReceiptNo[^6..], CultureInfo.InvariantCulture);

        secondSeq.Should().Be(firstSeq + 1);
    }

    /// <summary>Nol yoki manfiy summa — 400, `errors` ichida maydon nomi bilan.</summary>
    [Fact]
    public async Task RecordPayment_WithNonPositiveAmount_ReturnsBadRequest()
    {
        var world = await NewWorldAsync();

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/payments", new
        {
            studentId = world.StudentId,
            amount = 0m,
            method = "Cash",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("errors");
    }

    // ================================================================= 3) QAYTARISH / KECHIRIM

    /// <summary>
    /// ★ QAYTARISH HISOBNI ORQAGA QAYTARADI: avval balansdan, so'ng eng
    /// YANGI to'langan oydan. Oy `payments` jadvalida ham ochiladi — eski
    /// tizimda u "to'langan" bo'lib qolardi va tizim o'quvchini qarzsiz deb
    /// bilardi.
    /// </summary>
    [Fact]
    public async Task Reverse_TakesFromBalanceThenNewestMonth()
    {
        var world = await NewWorldAsync();

        await OpenPeriodAsync(world, Period);
        await OpenPeriodAsync(world, OtherPeriod);

        // Ikkala oy yopiladi + 100 000 balansga.
        await PayAsync(world.StudentId, (MonthlyPrice * 2) + 100_000m);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/payments/reverse", new
        {
            studentId = world.StudentId,
            amount = 300_000m,
            reason = "Ota-ona so'rovi",
        });

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        var reversal = (await response.Content.ReadFromJsonAsync<ReversalResponse>())!;

        reversal.FromBalance.Should().Be(100_000m, "avval balansdagi pul ishlatiladi");
        reversal.FromPayments.Should().Be(200_000m);
        reversal.Unreturned.Should().Be(0m);

        var account = await AccountAsync(world.StudentId);

        Month(account, Period).Status.Should().Be("Paid", "eski oy yopiq qolishi kerak");
        Month(account, OtherPeriod).Status.Should().Be("Partial");
        Month(account, OtherPeriod).Outstanding.Should().Be(200_000m);
        account.Balance.Should().Be(0m);

        account.RecentTransactions.Should().Contain(t => t.Kind == "Refund" && t.Amount == 300_000m);
    }

    /// <summary>Qaytariladigan pul umuman bo'lmasa — 409 (jimgina 200 EMAS).</summary>
    [Fact]
    public async Task Reverse_WithNothingToReturn_ReturnsConflict()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, Period);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/payments/reverse", new
        {
            studentId = world.StudentId,
            amount = 10_000m,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Kechirim: qarz yopiladi, lekin `paidAt` QO'YILMAYDI (kassaga pul
    /// tushmagan) va jurnalda alohida `Waiver` yozuvi qoladi.
    /// </summary>
    [Fact]
    public async Task Waive_ClosesMonthWithoutCashAndLeavesJournalEntry()
    {
        var world = await NewWorldAsync();
        var opened = await OpenPeriodAsync(world, Period);
        var paymentId = opened.Payments[0].Id;

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/waive", new { reason = "Ijtimoiy holat" });

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        var month = (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;

        month.Status.Should().Be("Waived");
        month.PaidAt.Should().BeNull("kechirimda kassaga pul tushmagan");
        month.PaidAmount.Should().Be(0m);

        var account = await AccountAsync(world.StudentId);

        account.Debt.Should().Be(0m, "kechirilgan oy qarz emas");
        account.RecentTransactions.Should()
            .Contain(t => t.Kind == "Waiver" && t.Amount == MonthlyPrice);

        var audits = await factory.WithDbAsync(db => db.PaymentAudits
            .CountAsync(a => a.EntityId == paymentId && a.Action == "waive"));

        audits.Should().Be(1, "kechirim audit izi qoldirishi shart");
    }

    /// <summary>To'liq to'langan oyni kechirib bo'lmaydi — 409.</summary>
    [Fact]
    public async Task Waive_OnFullyPaidMonth_ReturnsConflict()
    {
        var world = await NewWorldAsync();
        var opened = await OpenPeriodAsync(world, Period);

        await PayAsync(world.StudentId, MonthlyPrice);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/payments/{opened.Payments[0].Id}/waive", new { reason = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ================================================================= 4) RUXSAT MATRITSASI

    /// <summary>O'quvchi O'Z hisobini ko'radi.</summary>
    [Fact]
    public async Task StudentAccount_OwnAccount_IsVisibleToStudent()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, Period);

        using var student = await ClientAsync(world.StudentEmail, world.StudentPassword);

        var response = await student.GetAsync(
            new Uri($"/api/v1/payments/students/{world.StudentId}", UriKind.Relative));

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
        account.Debt.Should().Be(MonthlyPrice);
    }

    /// <summary>★ Begona hisob — 403 (404 EMAS: resurs bor, ruxsat yo'q).</summary>
    [Fact]
    public async Task StudentAccount_ForeignAccount_IsForbiddenForStudent()
    {
        var world = await NewWorldAsync();
        var other = await NewWorldAsync();

        using var student = await ClientAsync(world.StudentEmail, world.StudentPassword);

        var response = await student.GetAsync(
            new Uri($"/api/v1/payments/students/{other.StudentId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ★ USTOZ MOLIYAGA UMUMAN KIRMAYDI — o'z o'quvchisining hisobiga ham.
    /// Bu ataylab: dars beruvchi odam qarzni "kechirib" yubora olmasligi
    /// kerak (manfaatlar to'qnashuvi).
    /// </summary>
    [Fact]
    public async Task Finance_IsCompletelyClosedForTeachers()
    {
        var world = await NewWorldAsync();

        using var teacher = await ClientAsync(world.TeacherEmail, world.TeacherPassword);

        var read = await teacher.GetAsync(
            new Uri($"/api/v1/payments/students/{world.StudentId}", UriKind.Relative));

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var write = await teacher.PostAsJsonAsync("/api/v1/payments", new
        {
            studentId = world.StudentId,
            amount = 10_000m,
            method = "Cash",
        });

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await teacher.GetAsync(new Uri("/api/v1/payments", UriKind.Relative));
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>O'quvchi to'lov kirita olmaydi (faqat o'qiy oladi).</summary>
    [Fact]
    public async Task RecordPayment_ByStudent_IsForbidden()
    {
        var world = await NewWorldAsync();

        using var student = await ClientAsync(world.StudentEmail, world.StudentPassword);

        var response = await student.PostAsJsonAsync("/api/v1/payments", new
        {
            studentId = world.StudentId,
            amount = 10_000m,
            method = "Cash",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= 5) TARIF (PUT)

    /// <summary>
    /// ★ <c>PUT</c> — TO'LIQ ALMASHTIRISH: yuborilmagan <c>groupId</c>
    /// <c>null</c> bo'lib yoziladi (tarif "barcha guruhlar" ga aylanadi).
    /// Shartnoma shunday va test buni QOTIRADI — aks holda klient qisman
    /// yangilash kutib, ma'lumot jimgina yo'qolardi.
    /// </summary>
    [Fact]
    public async Task UpdateTariff_ReplacesEveryFieldIncludingOmittedOnes()
    {
        var world = await NewWorldAsync(createTariff: false);

        var tariff = await CreateTariffAsync(new
        {
            name = "Guruhga",
            amount = MonthlyPrice,
            activeFrom = "2026-01-01",
            groupId = world.GroupId,
        });

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/payments/tariffs/{tariff.Id}", new
        {
            name = "Umumiy",
            amount = 600_000m,
            activeFrom = "2026-02-01",
            lessonsCount = 12,
            isActive = true,
            // groupId ATAYLAB yuborilmadi
        });

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        var updated = (await response.Content.ReadFromJsonAsync<TariffResponse>())!;

        updated.GroupId.Should().BeNull("PUT to'liq almashtirish — yuborilmagan maydon null bo'ladi");
        updated.Amount.Should().Be(600_000m);
        updated.LessonsCount.Should().Be(12);
        updated.Specificity.Should().Be(0);
    }

    /// <summary>
    /// ★ Sana YUBORILMASA 400. Busiz `activeFrom` <c>0001-01-01</c> bo'lib
    /// tushardi va tarif "har doim amalda" bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task UpdateTariff_WithoutActiveFrom_ReturnsBadRequest()
    {
        var world = await NewWorldAsync(createTariff: false);

        var tariff = await CreateTariffAsync(new
        {
            name = "Tarif",
            amount = MonthlyPrice,
            activeFrom = "2026-01-01",
            groupId = world.GroupId,
        });

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/payments/tariffs/{tariff.Id}", new
        {
            name = "Tarif",
            amount = MonthlyPrice,
            lessonsCount = 8,
            isActive = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Guruh uchun qaysi tarif tushishini oldindan ko'rsatadi.</summary>
    [Fact]
    public async Task ResolveTariff_ReturnsMostSpecificCandidate()
    {
        var world = await NewWorldAsync(createTariff: false);

        await CreateTariffAsync(new { name = "Umumiy", amount = 400_000m, activeFrom = "2026-01-01" });

        var specific = await CreateTariffAsync(new
        {
            name = "Guruhga",
            amount = MonthlyPrice,
            activeFrom = "2026-01-01",
            groupId = world.GroupId,
        });

        using var admin = await AdminClientAsync();

        var resolved = await admin.GetFromJsonAsync<TariffResponse>(
            $"/api/v1/payments/tariffs/resolve?groupId={world.GroupId}&onDate=2026-05-01");

        resolved!.Id.Should().Be(specific.Id);
    }

    // ================================================================= yordamchi

    private async Task<World> NewWorldAsync(bool createTariff = true)
    {
        using var admin = await AdminClientAsync();

        var teacher = await CreateUserAsync(admin, UserRole.Teacher);
        var student = await CreateUserAsync(admin, UserRole.Student);

        var courseId = await FirstCourseIdAsync();

        var groupResponse = await admin.PostAsJsonAsync("/api/v1/groups", new
        {
            name = "Moliya-" + Guid.NewGuid().ToString("N")[..6],
            startDate = "2026-01-05",
            weekdays = new[] { "Monday", "Wednesday" },
            startTime = "19:00:00",
            courseId,
            teacherId = teacher.Id,
            courseMonths = 8,
        });

        await EnsureStatusAsync(groupResponse, HttpStatusCode.Created);

        var group = (await groupResponse.Content.ReadFromJsonAsync<CreateGroupResponse>())!;

        var member = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{group.Group.Id}/members", new { studentId = student.Id });

        await EnsureStatusAsync(member, HttpStatusCode.Created);

        var world = new World(
            student.Id, student.Email, student.Password,
            teacher.Email, teacher.Password,
            group.Group.Id);

        if (createTariff)
        {
            await CreateTariffAsync(new
            {
                name = "Guruh tarifi",
                amount = MonthlyPrice,
                activeFrom = "2026-01-01",
                groupId = world.GroupId,
            });
        }

        return world;
    }

    private async Task<TariffResponse> CreateTariffAsync(object payload)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/payments/tariffs", payload);
        await EnsureStatusAsync(response, HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<TariffResponse>())!;
    }

    private async Task<OpenPeriodResponse> OpenPeriodAsync(World world, string period)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/payments/periods/open", new { period, groupId = world.GroupId });

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<OpenPeriodResponse>())!;
    }

    private async Task<ReceiptResponse> PayAsync(long studentId, decimal amount)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/payments", new
        {
            studentId,
            amount,
            method = "Cash",
        });

        await EnsureStatusAsync(response, HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<ReceiptResponse>())!;
    }

    private async Task<AccountResponse> AccountAsync(long studentId)
    {
        using var admin = await AdminClientAsync();

        var account = await admin.GetFromJsonAsync<AccountResponse>(
            $"/api/v1/payments/students/{studentId}");

        return account!;
    }

    private static PaymentResponse Month(AccountResponse account, string period) =>
        account.Months.Find(m => m.Period == period)
        ?? throw new InvalidOperationException("Oy topilmadi: " + period);

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var tokens = await factory.LoginAsync(email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private Task<long> FirstCourseIdAsync() =>
        factory.WithDbAsync(db => db.Courses.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync());

    private static async Task<(long Id, string Email, string Password)> CreateUserAsync(
        HttpClient client, UserRole role)
    {
        var email = $"fin-{Guid.NewGuid():N}"[..16] + "@zinnur.uz";
        const string password = "Moliya!2345";

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = "Moliya " + role.ToString(),
            email,
            role = role.ToString(),
            phone = TestPhones.Next(),
        });

        await EnsureStatusAsync(response, HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        return (created!.User.Id, email, password);
    }

    /// <summary>
    /// Holatni tekshiradi va xato bo'lsa JAVOB TANASINI ko'rsatadi
    /// (`because` argumentiga berib bo'lmaydi — u JSON'dagi `{` ni format
    /// belgisi deb o'qib, testni boshqa sababdan yiqitadi).
    /// </summary>
    private static async Task EnsureStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected) return;

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail(
            "Kutilgan holat " + expected.ToString()
            + ", olingan " + response.StatusCode.ToString()
            + ". Javob tanasi: " + body);
    }

    // ---------------------------------------------------------------- javob shakllari

    private sealed record World(
        long StudentId,
        string StudentEmail,
        string StudentPassword,
        string TeacherEmail,
        string TeacherPassword,
        long GroupId);

    private sealed record CreateGroupResponse(GroupRef Group);

    private sealed record GroupRef(long Id);

    private sealed record CreatedUserResponse(UserRef User);

    private sealed record UserRef(long Id);

    private sealed record OpenPeriodResponse(
        string Period,
        int Created,
        int AlreadyOpen,
        int SkippedNoTariff,
        decimal BalanceApplied,
        int MonthsClosedFromBalance,
        List<PaymentResponse> Payments,
        List<string> Warnings);

    private sealed record PaymentResponse(
        long Id,
        long StudentId,
        long GroupId,
        string Period,
        decimal BaseAmount,
        decimal DiscountAmount,
        decimal Amount,
        decimal PaidAmount,
        decimal Outstanding,
        string Status,
        DateTimeOffset? PaidAt,
        string? Method);

    private sealed record ReceiptResponse(
        long TransactionId,
        string ReceiptNo,
        long StudentId,
        decimal Amount,
        decimal Applied,
        decimal ToBalance,
        int MonthsClosed,
        int MonthsPartial,
        decimal Balance,
        decimal DebtAfter);

    private sealed record ReversalResponse(
        long StudentId,
        decimal Requested,
        decimal Returned,
        decimal FromBalance,
        decimal FromPayments,
        decimal Unreturned,
        decimal Balance,
        decimal DebtAfter);

    private sealed record AccountResponse(
        long StudentId,
        string FullName,
        decimal Debt,
        decimal Balance,
        bool Exempt,
        int OpenMonths,
        decimal Paid,
        List<PaymentResponse> Months,
        List<TransactionResponse> RecentTransactions);

    private sealed record TransactionResponse(
        long Id,
        string Kind,
        decimal Amount,
        string? ReceiptNo,
        string? Method,
        string? Note);

    private sealed record TariffResponse(
        long Id,
        string Name,
        decimal Amount,
        int LessonsCount,
        long? CourseId,
        long? GroupId,
        DateOnly ActiveFrom,
        bool IsActive,
        int Specificity);
}
