using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// MOLIYA YIG'MA HISOBOTI — `GET /payments/summary` va eksport
/// ========================================================================
///
/// HAQIQIY Postgres bilan, mock'siz: hisobotning butun ma'nosi SQL tomonda
/// bajariladigan agregatsiyada. Mock bilan sinalganda aynan tekshirilishi
/// kerak bo'lgan narsa — `GROUP BY`, `SUM(CASE WHEN ...)` va davr
/// chegarasi — umuman ishtirok etmasdi.
///
/// ★ TASDIQLASH USULI — FARQ (delta). Bitta test sinfi bitta bazani
/// bo'lishadi va seed ma'lumotlari ham bor, hisobot esa BUTUN markaz
/// bo'yicha. Shuning uchun testlar "summa aynan X" demaydi, balki
/// "amaldan KEYIN raqam qanchaga o'zgardi" ni tekshiradi. Aks holda
/// testlar bir-birining ma'lumotidan qizarardi va yashil natija hech
/// nima isbotlamasdi.
/// </summary>
public sealed class PaymentSummaryTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const decimal MonthlyPrice = 540_000m;

    // ================================================================= ★ 1) KECHIRIM

    /// <summary>
    /// ★★ ENG MUHIM TEST — O'TGAN SESSIYADA TOPILGAN XATONING QO'RIQCHISI.
    ///
    /// Kechirilgan oy hisobotda "qarz 540 000" bo'lib turardi, o'quvchining
    /// shaxsiy hisobida esa "qarz 0" — natijada kassir markaz ALLAQACHON
    /// kechirgan oy uchun ota-onadan yana pul so'ragan.
    ///
    /// Endi: kechirilgandan keyin QARZ O'SMAYDI (delta 0), summa esa
    /// "kechirilgan" ko'rsatkichida ko'rinadi — pul yo'qolmaydi, faqat
    /// to'g'ri ustunga tushadi.
    /// </summary>
    [Fact]
    public async Task Summary_WaivedMonth_IsNotCountedAsDebt()
    {
        var world = await NewWorldAsync();

        var before = await SummaryAsync();

        var opened = await OpenPeriodAsync(world, CurrentPeriod());
        var paymentId = opened.Payments[0].Id;

        // Oy ochilgach qarz O'SDI — bu kutilgan holat.
        var withDebt = await SummaryAsync();
        (withDebt.Kpi.Outstanding - before.Kpi.Outstanding)
            .Should().Be(MonthlyPrice, "yangi ochilgan oy qarz bo'lib turadi");

        await WaiveAsync(paymentId);

        var after = await SummaryAsync();

        // ★ ASOSIY TASDIQ: kechirilgandan keyin qarz BOSHLANG'ICH holatga
        // qaytadi — kechirilgan oy hisobotda qarz sifatida QAYTA ko'rinmaydi.
        (after.Kpi.Outstanding - before.Kpi.Outstanding)
            .Should().Be(0m, "kechirilgan oy QARZ EMAS");

        (after.Kpi.Waived - before.Kpi.Waived)
            .Should().Be(MonthlyPrice, "kechirilgan summa alohida ko'rsatkichda ko'rinishi kerak");

        // Qarz yoshi jadvali ham AYNI ta'rifga bo'ysunadi: kechirilgan oy
        // hech qaysi guruhga tushmaydi.
        (Total(after.Aging) - Total(before.Aging))
            .Should().Be(0m, "kechirilgan oy qarz yoshi jadvaliga ham tushmasligi kerak");

        // O'quvchining shaxsiy hisobi bilan MOSLIK: ikki ekran bir xil
        // javob berishi kerak — eski tizimda ular bir-biriga zid edi.
        var account = await AccountAsync(world.StudentId);
        account.Debt.Should().Be(0m);
    }

    // ================================================================= 2) DAVR CHEGARASI

    /// <summary>
    /// ★ DAVR CHEGARASI: <c>from</c> va <c>to</c> — IKKALASI HAM KIRADI.
    ///
    /// <c>to = bugun</c> deganda bugun soat 18:00 da kelgan pul ham
    /// kirishi kerak. Ichkarida oraliq <c>[from 00:00, to+1 kun 00:00)</c>
    /// ga aylanadi: <c>23:59:59</c> yozilganda o'sha oxirgi soniyada kelgan
    /// to'lov IKKI kunning HECH BIRIGA tushmay yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Summary_PeriodFilter_IncludesBothEdgeDaysAndExcludesOutside()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, CurrentPeriod());

        var today = LocalToday();

        var before = await SummaryAsync(today, today);
        await PayAsync(world.StudentId, 100_000m);
        var after = await SummaryAsync(today, today);

        (after.Kpi.Collected - before.Kpi.Collected)
            .Should().Be(100_000m, "bugungi to'lov `to = bugun` oralig'iga KIRISHI kerak");

        // Kechagi kun bilan tugaydigan oraliqqa bugungi to'lov TUSHMAYDI.
        var yesterday = today.AddDays(-1);
        var earlier = await SummaryAsync(yesterday.AddDays(-1), yesterday);

        earlier.Kpi.Collected.Should().Be(0m, "kechagi oraliqda bugungi to'lov bo'lmasligi kerak");

        // Ertangi kundan boshlanadigan oraliq ham bo'sh.
        var tomorrow = today.AddDays(1);
        var later = await SummaryAsync(tomorrow, tomorrow.AddDays(1));

        later.Kpi.Collected.Should().Be(0m, "ertangi oraliqda bugungi to'lov bo'lmasligi kerak");

        // Javobda qo'llangan oraliq OSHKOR qaytadi — UI "qaysi davr
        // ko'rsatilyapti" ni taxmin qilmasin.
        after.From.Should().Be(today);
        after.To.Should().Be(today);
    }

    /// <summary>Teskari oraliq — 400 va sabab <c>errors</c> ichida.</summary>
    [Fact]
    public async Task Summary_WithReversedRange_ReturnsBadRequest()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(new Uri(
            "/api/v1/payments/summary?from=2026-07-31&to=2026-07-01", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("from", "sabab `errors` ichida maydon nomi bilan qaytishi kerak");
    }

    // ================================================================= 3) RUXSAT

    /// <summary>
    /// ★ ROL MATRITSASI. Ustoz va kurator moliyaga UMUMAN kirmaydi —
    /// dars beruvchi odam o'z o'quvchisining qarzini ko'rib, unga
    /// munosabatini o'zgartirmasligi kerak (manfaatlar to'qnashuvi).
    /// </summary>
    [Fact]
    public async Task Summary_RoleMatrix_OnlyAcademicAndAdminAreAllowed()
    {
        var world = await NewWorldAsync();

        using var admin = await AdminClientAsync();
        (await admin.GetAsync(SummaryUri())).StatusCode
            .Should().Be(HttpStatusCode.OK, "admin hisobotni ko'radi");

        var academic = await CreateUserAsync(admin, UserRole.Academic);
        using var academicClient = await ClientAsync(academic.Email, academic.Password);
        (await academicClient.GetAsync(SummaryUri())).StatusCode
            .Should().Be(HttpStatusCode.OK, "o'quv bo'limi hisobotni ko'radi");

        using var teacher = await ClientAsync(world.TeacherEmail, world.TeacherPassword);
        (await teacher.GetAsync(SummaryUri())).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "ustoz moliyaga kira olmaydi");

        using var student = await ClientAsync(world.StudentEmail, world.StudentPassword);
        (await student.GetAsync(SummaryUri())).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "o'quvchi butun markaz moliyasini ko'ra olmaydi");

        // Eksport ham AYNI qoidaga bo'ysunadi — ruxsat ikki joyda ikki xil
        // bo'lib qolmasin.
        (await teacher.GetAsync(ExportUri())).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "eksport ham ustozga yopiq");
    }

    // ================================================================= 4) BO'SH MA'LUMOT

    /// <summary>
    /// ★ BO'SH DAVRDA <c>0</c> QAYTADI, <c>null</c> EMAS.
    ///
    /// UI'da `null` arifmetikaga tushib "NaN" va "undefined" ko'rsatardi.
    /// Shuning uchun bu yerda JSON MATNI ham tekshiriladi: maydon
    /// umuman `null` bo'lib kelmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Summary_WithEmptyRange_ReturnsZerosAndFullShapeNotNulls()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(new Uri(
            "/api/v1/payments/summary?from=2001-01-01&to=2001-01-31", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContain("\"collected\":null");
        raw.Should().NotContain("\"collectionRate\":null");
        raw.Should().NotContain("\"outstanding\":null");

        var summary = await response.Content.ReadFromJsonAsync<SummaryResponse>();

        summary!.Kpi.Collected.Should().Be(0m);
        summary.Kpi.Billed.Should().Be(0m);
        summary.Kpi.Discounts.Should().Be(0m);
        summary.Kpi.PaymentCount.Should().Be(0);

        // ★ Bo'linish maxraji nol bo'lsa foiz 0 — `null` ham, "NaN" ham emas.
        summary.Kpi.CollectionRate.Should().Be(0m);

        // Shakl DOIM to'liq: grafik va jadval bo'sh massivdan yiqilmasin.
        summary.Aging.Should().HaveCount(4, "qarz yoshi jadvali doim to'rt qatordan iborat");
        summary.Months.Should().HaveCount(12, "dinamika grafigi doim 12 nuqta");
        summary.Months.Should().OnlyContain(m => m.Billed == 0m && m.Collected == 0m);
        summary.Groups.Should().BeEmpty();
        summary.Methods.Should().BeEmpty();
    }

    // ================================================================= 5) QARZ YOSHI

    /// <summary>
    /// Qarz yoshi guruhlari: yangi oy <c>0-30</c> ga, ikki yil oldingi oy
    /// <c>90+</c> ga tushadi.
    ///
    /// ★ Guruhlar yig'indisi umumiy qarzga AYNAN teng bo'lishi shart:
    /// aks holda ikki raqam bir-biriga to'g'ri kelmay, qaysi biri
    /// haqiqiyligini hech kim bilmasdi. (Aniq 30/31, 60/61, 90/91 kun
    /// chegaralari `DebtAgingTests` da — u yerda sana boshqarib bo'ladi.)
    /// </summary>
    [Fact]
    public async Task Summary_Aging_SplitsDebtByAgeAndSumsToOutstanding()
    {
        var world = await NewWorldAsync();

        var before = await SummaryAsync();

        await OpenPeriodAsync(world, CurrentPeriod());
        await OpenPeriodAsync(world, OldPeriod);

        var after = await SummaryAsync();

        var fresh = Bucket(after, "0-30").Amount - Bucket(before, "0-30").Amount;
        var stale = Bucket(after, "90+").Amount - Bucket(before, "90+").Amount;

        fresh.Should().Be(MonthlyPrice, "joriy oy qarzi eng yangi guruhga tushadi");
        stale.Should().Be(MonthlyPrice, "ikki yil oldingi qarz `90+` guruhiga tushadi");

        Bucket(after, "90+").MaxDays.Should().BeNull("oxirgi guruh cheksiz");
        Bucket(after, "0-30").MinDays.Should().Be(0);

        // ★ YIG'INDI MOSLIGI — hisobotning ichki izchilligi.
        Total(after.Aging).Should().Be(after.Kpi.Outstanding);

        after.Kpi.DebtorStudents.Should().BeGreaterThan(0);
    }

    // ================================================================= 6) KESIMLAR

    /// <summary>
    /// Guruh va to'lov usuli kesimlari haqiqiy raqam beradi.
    ///
    /// Usul kesimi JURNALDAN olinadi (`Payments.Method` dan emas): oylik
    /// yozuvdagi usul "oxirgi to'lovniki" bo'lib, bir oy ikki xil usulda
    /// yopilsa birinchisi yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Summary_Slices_ReportGroupAndMethodBreakdown()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, CurrentPeriod());

        await PayAsync(world.StudentId, 200_000m, "Cash");
        await PayAsync(world.StudentId, 40_000m, "Card");

        var summary = await SummaryAsync();

        var group = summary.Groups.Find(g => g.GroupId == world.GroupId);
        group.Should().NotBeNull("guruh kesimida yangi guruh ko'rinishi kerak");
        group!.Billed.Should().Be(MonthlyPrice);
        group.Collected.Should().Be(240_000m);
        group.Outstanding.Should().Be(MonthlyPrice - 240_000m);
        group.Students.Should().Be(1);

        var cash = summary.Methods.Find(m => m.Method == "Cash");
        var card = summary.Methods.Find(m => m.Method == "Card");

        cash.Should().NotBeNull();
        card.Should().NotBeNull();
        card!.Amount.Should().BeGreaterThanOrEqualTo(40_000m);
        cash!.MethodName.Should().Be("Naqd");
        card.MethodName.Should().Be("Karta");

        // Kesimlar tushum bo'yicha kamayish tartibida keladi.
        summary.Methods.Should().BeInDescendingOrder(m => m.Amount);

        // Oxirgi 12 oy — joriy oy oxirgi nuqta bo'lishi kerak.
        summary.Months[^1].Period.Should().Be(summary.ToPeriod);
        summary.Months[^1].Collected.Should().BeGreaterThanOrEqualTo(240_000m);
    }

    // ================================================================= 7) EKSPORT

    /// <summary>
    /// ★ EKSPORT EXCEL UCHUN: BOM + <c>sep=,</c> direktivasi.
    ///
    /// BOM'siz Excel UTF-8 ni ANSI deb o'qiydi va o'zbek harflari buziladi.
    /// <c>sep=,</c> qatorisiz esa uz-UZ/ru-RU lokalidagi Excel faylni
    /// nuqtali vergul bo'yicha bo'lishga urinib, BUTUN hisobotni bitta
    /// ustunga tiqib qo'yardi.
    /// </summary>
    [Fact]
    public async Task ExportSummary_ReturnsExcelReadyCsvWithAllSections()
    {
        var world = await NewWorldAsync();
        await OpenPeriodAsync(world, CurrentPeriod());
        await PayAsync(world.StudentId, 150_000m);

        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(ExportUri());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentType.CharSet.Should().Be("utf-8");

        response.Content.Headers.ContentDisposition!.FileName
            .Should().Contain("zinnur-moliya", "fayl nomi tushunarli bo'lishi kerak");

        var bytes = await response.Content.ReadAsByteArrayAsync();

        // ★ KODLASH: dastlabki uch bayt — UTF-8 BOM.
        bytes.Take(3).Should().Equal([(byte)0xEF, (byte)0xBB, (byte)0xBF], "Excel BOM kutadi");

        var csv = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // ★ AJRATGICH: birinchi qator — Excel uchun direktiva.
        lines[0].Should().Be("sep=,");

        // Barcha bo'limlar joyida (eski .xlsx dagi beshta varaqning muqobili).
        csv.Should().Contain("ZIN-NUR — MOLIYA HISOBOTI");
        csv.Should().Contain("UMUMIY,Qiymat");
        csv.Should().Contain("QARZ YOSHI (kun),Summa,O'quvchi,Oylar");
        csv.Should().Contain("OY,Reja,Yig'ilgan,Qarz,Kechirilgan,Chegirma,Foiz");
        csv.Should().Contain("GURUH,Reja,Yig'ilgan,Qarz,Kechirilgan,Foiz,O'quvchi");
        // "Ulush, %" ichida VERGUL bor — yozuvchi uni qo'shtirnoqqa oladi,
        // aks holda ustunlar surilib ketardi.
        csv.Should().Contain("TO'LOV USULI,Summa,Soni,\"Ulush, %\"");

        // ★ QATORLAR SONI: 4 ta qarz yoshi + 12 ta oy qatori DOIM bo'ladi.
        lines.Count(l => l.StartsWith("0-30,", StringComparison.Ordinal)).Should().Be(1);
        lines.Count(l => l.StartsWith("90+,", StringComparison.Ordinal)).Should().Be(1);

        var summary = await SummaryAsync();
        lines.Count(l => l.StartsWith(summary.ToPeriod + ",", StringComparison.Ordinal))
            .Should().Be(1, "joriy oy dinamikada bitta qator");

        // ★ SONLAR: pul AJRATGICHSIZ butun son — vergulli lokalda ham
        // Excel uni SON deb o'qiydi.
        csv.Should().NotContain("540 000");
        csv.Should().MatchRegex(@"Kassaga tushgan \(davrda\),\d+");

        // ★ EKRANDAGI RAQAM = FAYLDAGI RAQAM (ikki hisoblash yo'li yo'q).
        csv.Should().Contain("Joriy umumiy qarz," +
            summary.Kpi.Outstanding.ToString("0.##", CultureInfo.InvariantCulture));
    }

    // ================================================================= yordamchi

    private async Task<SummaryResponse> SummaryAsync(DateOnly? from = null, DateOnly? to = null)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(SummaryUri(from, to));
        await EnsureStatusAsync(response, HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<SummaryResponse>())!;
    }

    private static Uri SummaryUri(DateOnly? from = null, DateOnly? to = null) =>
        new(Query("/api/v1/payments/summary", from, to), UriKind.Relative);

    private static Uri ExportUri(DateOnly? from = null, DateOnly? to = null) =>
        new(Query("/api/v1/payments/summary/export", from, to), UriKind.Relative);

    private static string Query(string path, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return path;

        var parts = new List<string>(2);

        if (from is { } f) parts.Add("from=" + f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (to is { } t) parts.Add("to=" + t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return path + "?" + string.Join('&', parts);
    }

    private static decimal Total(List<AgingResponse> buckets) =>
        buckets.Sum(b => b.Amount);

    private static AgingResponse Bucket(SummaryResponse summary, string key) =>
        summary.Aging.Find(b => b.Bucket == key)
        ?? throw new InvalidOperationException("Qarz yoshi guruhi topilmadi: " + key);

    /// <summary>
    /// Markaz vaqtidagi BUGUN. Server UTC'da ishlaydi, hisobot esa mahalliy
    /// kalendar bo'yicha — test ham AYNI zonaga qarashi kerak, aks holda
    /// u kuniga besh soat davomida sababsiz qizarardi.
    /// </summary>
    private static DateOnly LocalToday() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Tashkent).DateTime);

    private static string CurrentPeriod() =>
        LocalToday().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static readonly TimeZoneInfo Tashkent =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    /// <summary>90 kundan ANIQ eski davr — `90+` guruhiga tushishi kafolatlangan.</summary>
    private const string OldPeriod = "2024-01";

    private async Task<World> NewWorldAsync()
    {
        using var admin = await AdminClientAsync();

        var teacher = await CreateUserAsync(admin, UserRole.Teacher);
        var student = await CreateUserAsync(admin, UserRole.Student);

        var courseId = await FirstCourseIdAsync();

        var groupResponse = await admin.PostAsJsonAsync("/api/v1/groups", new
        {
            name = "Hisobot-" + Guid.NewGuid().ToString("N")[..6],
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

        // Tarif ESKI sanadan kuchda: test eski davrlarni ham ochadi
        // (qarz yoshi guruhlari uchun).
        var tariff = await admin.PostAsJsonAsync("/api/v1/payments/tariffs", new
        {
            name = "Hisobot tarifi",
            amount = MonthlyPrice,
            activeFrom = "2020-01-01",
            groupId = group.Group.Id,
        });

        await EnsureStatusAsync(tariff, HttpStatusCode.Created);

        return new World(
            student.Id, student.Email, student.Password,
            teacher.Email, teacher.Password,
            group.Group.Id);
    }

    private async Task<OpenPeriodResponse> OpenPeriodAsync(World world, string period)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/payments/periods/open", new { period, groupId = world.GroupId });

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<OpenPeriodResponse>())!;
    }

    private async Task PayAsync(long studentId, decimal amount, string method = "Cash")
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/payments", new { studentId, amount, method });

        await EnsureStatusAsync(response, HttpStatusCode.Created);
    }

    private async Task WaiveAsync(long paymentId)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/waive", new { reason = "Ijtimoiy holat" });

        await EnsureStatusAsync(response, HttpStatusCode.OK);
    }

    private async Task<AccountResponse> AccountAsync(long studentId)
    {
        using var admin = await AdminClientAsync();

        return (await admin.GetFromJsonAsync<AccountResponse>(
            $"/api/v1/payments/students/{studentId}"))!;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var tokens = await factory.LoginAsync(email, password);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private Task<long> FirstCourseIdAsync() =>
        factory.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(db.Courses.OrderBy(c => c.Id).Select(c => c.Id)));

    private static async Task<(long Id, string Email, string Password)> CreateUserAsync(
        HttpClient client, UserRole role)
    {
        var email = $"sum-{Guid.NewGuid():N}"[..16] + "@zinnur.uz";
        const string password = "Hisobot!2345";

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = "Hisobot " + role.ToString(),
            email,
            role = role.ToString(),
            password,
        });

        await EnsureStatusAsync(response, HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        return (created!.User.Id, email, password);
    }

    /// <summary>Holatni tekshiradi va xato bo'lsa JAVOB TANASINI ko'rsatadi.</summary>
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

    private sealed record OpenPeriodResponse(string Period, int Created, List<PaymentRef> Payments);

    private sealed record PaymentRef(long Id, string Period, decimal Amount, decimal Outstanding);

    private sealed record AccountResponse(long StudentId, decimal Debt, decimal Balance);

    private sealed record SummaryResponse(
        DateOnly From,
        DateOnly To,
        string FromPeriod,
        string ToPeriod,
        DateOnly AsOf,
        KpiResponse Kpi,
        List<AgingResponse> Aging,
        List<MonthResponse> Months,
        List<GroupSliceResponse> Groups,
        List<MethodSliceResponse> Methods);

    private sealed record KpiResponse(
        decimal Collected,
        decimal Refunded,
        decimal NetCollected,
        decimal BalanceUsed,
        decimal Waived,
        decimal Billed,
        decimal Discounts,
        decimal PeriodCollected,
        decimal CollectionRate,
        decimal Outstanding,
        decimal StudentBalance,
        int PayingStudents,
        int DebtorStudents,
        int PaymentCount);

    private sealed record AgingResponse(
        string Bucket, int MinDays, int? MaxDays, decimal Amount, int Students, int Months);

    private sealed record MonthResponse(
        string Period,
        decimal Billed,
        decimal Collected,
        decimal Outstanding,
        decimal Waived,
        decimal Discounts,
        decimal CollectionRate,
        int Records);

    private sealed record GroupSliceResponse(
        long GroupId,
        string GroupName,
        decimal Billed,
        decimal Collected,
        decimal Outstanding,
        decimal Waived,
        decimal CollectionRate,
        int Students);

    private sealed record MethodSliceResponse(
        string? Method, string MethodName, decimal Amount, int Count, decimal Share);
}
