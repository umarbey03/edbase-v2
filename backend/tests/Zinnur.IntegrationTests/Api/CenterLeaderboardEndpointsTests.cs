using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// BUTUN O'QUV MARKAZ REYTINGI — HAQIQIY baza bilan
/// ========================================================================
///
/// Guruh jadvali <c>LeaderboardEndpointsTests</c> da; bu yerda faqat
/// MARKAZ qamroviga xos xatti-harakat tekshiriladi:
///
///   1) turli guruhlardagi o'quvchilar BITTA jadvalga qo'shiladi va
///      davomat maxraji HAR BIRIDA O'Z GURUHINIKI qoladi;
///   2) kurs darsi vazifalari (GroupId — NULL) markaz qamrovida ham
///      hisobga kiradi;
///   3) ruxsat: markazning har qanday faol foydalanuvchisi ko'radi;
///   4) <c>/me?scope=</c> diskriminatori.
///
/// ★ HAR TEST BOSHQA OYNI ISHLATADI — ATAYLAB. Markaz kesh kaliti
///   (<c>leaderboard:center:solo:{oy}</c>) da guruh Id'si YO'Q, ya'ni bir
///   sinfdagi ikki test bir xil oyni so'rasa ikkinchisi BIRINCHISINING
///   keshlangan jadvalini olardi (sinf ichida Redis prefiksi ham, baza ham
///   umumiy) va test o'zi yaratmagan ma'lumot ustida yashil bo'lib
///   qolardi. Oy — kalitning bir qismi, shuning uchun u ajratadi.
///
/// ★ TASDIQLAR "BAG'RIKENG": markaz qamroviga bazadagi HAMMA faol
///   o'quvchi kiradi (seed qilingan "Demo O'quvchi" ham, boshqa
///   testlarniki ham). Shuning uchun qatorlar Id bo'yicha topiladi,
///   jadval uzunligi bo'yicha emas.
/// </summary>
public sealed class CenterLeaderboardEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string Url = "/api/v1/leaderboard/center";

    /// <summary>
    /// ★ MARKAZ QAMROVIDAGI ENG MUHIM QOIDA: DAVOMAT MAXRAJI HAR
    /// O'QUVCHIDA O'ZINIKI.
    ///
    /// A guruhida 2 dars o'tilgan, B guruhida 1 dars. Ikkala o'quvchi ham
    /// o'z guruhidagi darslarning bittasiga kelgan.
    ///
    /// To'g'ri javob: A -> 50% (2 darsdan 1), B -> 100% (1 darsdan 1).
    ///
    /// Agar maxraj UMUMIY olinganda (2 + 1 = 3), ikkalasi ham 33% chiqardi
    /// va B o'zi bormagan — hatto o'z guruhida BO'LMAGAN — dars uchun
    /// jazolangan bo'lardi.
    /// </summary>
    [Fact]
    public async Task CenterBoard_UsesEachStudentsOwnGroupAsAttendanceDenominator()
    {
        const string period = "2026-05";
        var inside = new DateTimeOffset(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

        var alpha = await WorldBuilder.CreateAsync(factory, "cdena");
        var beta = await WorldBuilder.CreateAsync(factory, "cdenb");

        // A guruhi: IKKI dars, o'quvchi BIRIGA kelgan.
        await WorldBuilder.AddEndedSessionAsync(
            factory, alpha.GroupId, inside, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [alpha.Student.Id] = AttendanceStatus.Present });

        await WorldBuilder.AddEndedSessionAsync(
            factory, alpha.GroupId, inside.AddDays(2), SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [alpha.Student.Id] = AttendanceStatus.Absent });

        // B guruhi: BITTA dars, o'quvchi kelgan.
        await WorldBuilder.AddEndedSessionAsync(
            factory, beta.GroupId, inside.AddDays(1), SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [beta.Student.Id] = AttendanceStatus.Present });

        var board = await BoardAsync(alpha.Student, period);

        var rowA = board.Rows.Single(r => r.StudentId == alpha.Student.Id);
        var rowB = board.Rows.Single(r => r.StudentId == beta.Student.Id);

        rowA.AttendancePercent.Should().Be(50m, "A guruhida 2 dars o'tilgan");
        rowB.AttendancePercent.Should().Be(100m, "B guruhida 1 dars o'tilgan");

        rowB.Rank.Should().BeLessThan(rowA.Rank, "100% > 50%");

        // ★ "Men" bayrog'i faqat SO'ROVCHIDA.
        rowA.IsMe.Should().BeTrue();
        rowB.IsMe.Should().BeFalse();
        board.Me!.StudentId.Should().Be(alpha.Student.Id);

        board.TopCount.Should().Be(100, "javobdagi chegara frontendga shu maydonda aytiladi");
    }

    /// <summary>
    /// ★ KURS DARSI VAZIFALARI MARKAZ QAMROVIDA HAM HISOBGA KIRADI
    /// (<c>GroupId</c> — NULL, <c>ModuleLessonId</c> — bor).
    ///
    /// QAROR QULFI: v2 da vazifalarning asosiy qismi kurs darsiga
    /// biriktirilgan. Ular chiqarib tashlansa markaz jadvalida vazifa
    /// mezoni deyarli har doim bo'sh chiqardi va bitta o'quvchi bitta oyda
    /// guruh jadvalida bir ball, markaz jadvalida BOSHQA ball ko'rardi.
    /// </summary>
    [Fact]
    public async Task CenterBoard_CountsCourseLessonAssignments()
    {
        const string period = "2026-06";
        var inside = new DateTimeOffset(2026, 6, 10, 14, 0, 0, TimeSpan.Zero);

        var world = await WorldBuilder.CreateAsync(factory, "ccrs");

        await AddCourseLessonSubmissionAsync(
            world.Student.Id, score: 4m, maxScore: 5m, inside);

        var board = await BoardAsync(world.Student, period);

        var row = board.Rows.Single(r => r.StudentId == world.Student.Id);

        row.AssignmentPercent.Should().Be(80m, "kurs vazifasi markazda ham sanaladi");
        row.AttendancePercent.Should().BeNull("bu oyda dars o'tilmagan");
        row.Total.Should().Be(80m, "yagona mavjud mezon — vazifa");
    }

    /// <summary>
    /// ★ RUXSAT: markazning HAR QANDAY faol foydalanuvchisi jadvalni
    /// ko'radi — ustoz ham. Bu YANGI qoida: bugungacha reytingda
    /// guruhdan tashqari qamrov umuman yo'q edi.
    ///
    /// Xodim jadvalning ICHIDA emas, shuning uchun <c>me</c> — <c>null</c>.
    /// </summary>
    [Fact]
    public async Task CenterBoard_AsTeacher_IsAllowed_ButHasNoOwnRow()
    {
        const string period = "2026-07";

        var world = await WorldBuilder.CreateAsync(factory, "cstaff");

        var board = await BoardAsync(world.Teacher, period);

        board.Me.Should().BeNull("ustoz o'quvchi emas — jadvalning ichida turmaydi");
        board.Rows.Should().Contain(r => r.StudentId == world.Student.Id);
        board.StudentCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// ★ BEGONA GURUH ENDI TO'SIQ EMAS — VA BU ATAYLAB.
    ///
    /// Guruh jadvalida boshqa guruh o'quvchisi 403 oladi
    /// (<c>LeaderboardEndpointsTests</c>). Markaz jadvalida esa aynan
    /// maqsad — butun markazni bitta ro'yxatda ko'rsatish, ya'ni begona
    /// guruhdagi o'quvchi ham shu jadvalda turadi.
    ///
    /// 🔴 CHEGARA YO'QOLMADI, U KO'CHDI: chegara endi GURUH emas, MARKAZ.
    ///    Bugun bitta deployment bitta markazga xizmat qiladi, shuning
    ///    uchun bu testda ikkinchi markaz yo'q — ko'p-markazli o'zgarish
    ///    kelganda shu yerga "begona markaz ko'rinmaydi" testi qo'shiladi.
    /// </summary>
    [Fact]
    public async Task CenterBoard_ShowsStudentsFromOtherGroups()
    {
        const string period = "2026-09";

        var mine = await WorldBuilder.CreateAsync(factory, "cmine");
        var other = await WorldBuilder.CreateAsync(factory, "cother");

        var board = await BoardAsync(mine.Student, period);

        board.Rows.Should().Contain(r => r.StudentId == other.Student.Id,
            "markaz jadvali guruh chegarasini kesib o'tadi");
    }

    /// <summary>Noto'g'ri <c>period</c> — 400 (guruh yo'lidagi qoidaning aynan o'zi).</summary>
    [Theory]
    [InlineData("2026-13")]
    [InlineData("iyun")]
    public async Task CenterBoard_WithMalformedPeriod_ReturnsBadRequest(string period)
    {
        var world = await WorldBuilder.CreateAsync(factory, "cbad");

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(
            new Uri($"{Url}?period={period}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CenterBoard_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(Url, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= "mening o'rnim"

    /// <summary>
    /// <c>/me?scope=Center</c> — markazdagi o'rin, GURUHSIZ.
    ///
    /// ★ <c>groupId</c> HAR DOIM <c>null</c> va buni "guruh topilmadi"
    /// bilan aralashtirmaslik uchun javobda <c>scope</c> diskriminatori
    /// turadi.
    /// </summary>
    [Fact]
    public async Task MyRank_WithCenterScope_ReportsCenterRankWithoutGroup()
    {
        const string period = "2026-10";
        var inside = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);

        var world = await WorldBuilder.CreateAsync(factory, "cmyrank");

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, inside, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [world.Student.Id] = AttendanceStatus.Present });

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var rank = await client.GetFromJsonAsync<MyRankBody>(
            $"/api/v1/leaderboard/me?scope=Center&period={period}");

        rank!.Scope.Should().Be("Center");
        rank.GroupId.Should().BeNull("markaz jadvalining guruhi yo'q");
        rank.GroupName.Should().BeNull();
        rank.Me!.IsMe.Should().BeTrue();
        rank.Me.AttendancePercent.Should().Be(100m);
        rank.StudentCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// ★ ESKI MIJOZLAR BUZILMAYDI: <c>scope</c> berilmasa javob AVVALGIDEK
    /// guruh bo'yicha keladi. Bosh sahifa kartochkasi hech narsa
    /// yubormaydi — va u qimmat markaz hisobini bexosdan chaqirmasligi
    /// kerak.
    /// </summary>
    [Fact]
    public async Task MyRank_WithoutScopeParameter_StaysGroupScoped()
    {
        var world = await WorldBuilder.CreateAsync(factory, "cdefsc");

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var rank = await client.GetFromJsonAsync<MyRankBody>("/api/v1/leaderboard/me");

        rank!.Scope.Should().Be("Group");
        rank.GroupId.Should().Be(world.GroupId);
        rank.GroupName.Should().Be(world.GroupName);
    }

    // ================================================================= yordamchi

    private async Task<CenterBoardBody> BoardAsync(TestUser user, string period)
    {
        using var client = await WorldBuilder.ClientAsync(factory, user);

        var response = await client.GetAsync(
            new Uri($"{Url}?period={period}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<CenterBoardBody>())!;
    }

    /// <summary>
    /// KURS DARSIGA biriktirilgan vazifa + baholangan javob.
    /// <c>Assignment.GroupId</c> ATAYLAB <c>null</c> — aynan shu shakl
    /// tekshiriladi.
    /// </summary>
    private async Task AddCourseLessonSubmissionAsync(
        long studentId, decimal score, decimal maxScore, DateTimeOffset gradedAtUtc) =>
        await factory.WithDbAsync(async db =>
        {
            // Kurs -> modul -> dars daraxti navigatsiya orqali BIR YO'LA
            // yoziladi (`DbInitializer` dagi naqsh).
            var course = new Course
            {
                Name = "Kurs " + Guid.NewGuid().ToString("N")[..6],
                Modules =
                {
                    new CourseModule
                    {
                        Name = "Modul",
                        Position = 1,
                        Lessons = { new ModuleLesson { Name = "Dars", Position = 1 } },
                    },
                },
            };

            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var lesson = course.Modules.Single().Lessons.Single();

            var assignment = new Assignment
            {
                GroupId = null,                 // ★ kurs vazifasi — guruhsiz
                ModuleLessonId = lesson.Id,
                Title = "Kurs vazifasi",
                MaxScore = maxScore,
            };

            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            db.Submissions.Add(new Submission
            {
                AssignmentId = assignment.Id,
                StudentId = studentId,
                Status = SubmissionStatus.Graded,
                Score = score,
                SubmittedAt = gradedAtUtc.AddHours(-1),
                GradedAt = gradedAtUtc,
            });

            await db.SaveChangesAsync();
            return 0;
        });

    private sealed record CenterBoardBody(
        string Period,
        int StudentCount,
        int TopCount,
        CenterRowBody? Me,
        List<CenterRowBody> Rows);

    private sealed record MyRankBody(
        string Scope,
        long? GroupId,
        string? GroupName,
        string Period,
        int StudentCount,
        CenterRowBody? Me);

    private sealed record CenterRowBody(
        long StudentId,
        string StudentName,
        int Rank,
        decimal Total,
        decimal? AttendancePercent,
        decimal? AssignmentPercent,
        decimal? TestPercent,
        bool IsMe);
}
