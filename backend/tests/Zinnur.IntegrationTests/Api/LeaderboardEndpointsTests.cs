using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// REYTING ENDPOINTLARI — HAQIQIY baza bilan
/// ========================================================================
///
/// Ball MATEMATIKASI unit testlarda (<c>LeaderboardScoreTests</c>).
/// Bu yerda ikki boshqa narsa tekshiriladi:
///
///   1) SO'ROVLAR to'g'ri ma'lumot yig'adimi — oy chegarasi, faqat
///      yakunlangan darslar, faqat baholangan vazifalar;
///   2) RUXSAT MATRITSASI — begona guruh reytingi 403, arxivlangan
///      guruh 403, guruhdan chiqarilgan o'quvchi 403.
/// </summary>
public sealed class LeaderboardEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>Reyting hisoblanadigan oy va uning ICHIDAGI bir on (Toshkent kunduzi).</summary>
    private const string Period = "2026-05";

    private static readonly DateTimeOffset InsidePeriod =
        new(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

    /// <summary>Oldingi oy — chegara tekshiruvi uchun.</summary>
    private static readonly DateTimeOffset OutsidePeriod =
        new(2026, 4, 14, 14, 0, 0, TimeSpan.Zero);

    // ================================================================= hisob

    /// <summary>
    /// ★ UCH MEZON UCHALASI HAM BOR: davomat 50% (2 darsdan 1),
    /// vazifa 80% (4/5), test 60% (6/10) -> yakuniy (50+80+60)/3 = 63.3.
    /// </summary>
    [Fact]
    public async Task GroupBoard_CombinesAllThreeCriteria()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lb");
        var me = world.Student.Id;

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, InsidePeriod, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Present });

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, InsidePeriod.AddDays(2), SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Absent });

        await WorldBuilder.AddGradedSubmissionAsync(
            factory, world.GroupId, me, score: 4m, maxScore: 5m, InsidePeriod);

        await WorldBuilder.AddTestAttemptAsync(
            factory, me, score: 6m, maxScore: 10m, InsidePeriod);

        var board = await BoardAsync(world, Period);

        var row = board.Rows.Single(r => r.StudentId == me);

        row.AttendancePercent.Should().Be(50m);
        row.AssignmentPercent.Should().Be(80m);
        row.TestPercent.Should().Be(60m);
        row.Total.Should().Be(63.3m);
        row.Rank.Should().Be(1);
        row.IsMe.Should().BeTrue();

        board.Me!.StudentId.Should().Be(me);
        board.StudentCount.Should().Be(1);
        board.Period.Should().Be(Period);
    }

    /// <summary>
    /// ★ MEZON BO'SH BO'LSA `null` — 0 EMAS. Shu oyda dars o'tilmagan,
    /// vazifa baholanmagan va test topshirilmagan bo'lsa yakuniy ball 0,
    /// lekin uch mezon ham "ma'lumot yo'q" deb belgilanadi. Frontend
    /// shunga qarab "hali ma'lumot yo'q" deb ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task GroupBoard_WithNoActivityInPeriod_ReturnsNullCriteria()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbempty");

        var board = await BoardAsync(world, Period);

        var row = board.Rows.Single();

        row.AttendancePercent.Should().BeNull();
        row.AssignmentPercent.Should().BeNull();
        row.TestPercent.Should().BeNull();
        row.Total.Should().Be(0m);
    }

    /// <summary>
    /// ★ OY CHEGARASI: aprelda o'tilgan dars may oyining reytingiga
    /// TUSHMAYDI. Eski tizimning "har oy toza start" qoidasi shunda.
    /// </summary>
    [Fact]
    public async Task GroupBoard_IgnoresActivityFromOtherMonths()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbmonth");
        var me = world.Student.Id;

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, OutsidePeriod, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Present });

        var may = await BoardAsync(world, Period);
        may.Rows.Single().AttendancePercent.Should().BeNull("may oyida dars o'tilmagan");

        var april = await BoardAsync(world, "2026-04");
        april.Rows.Single().AttendancePercent.Should().Be(100m);
    }

    /// <summary>
    /// ★ FAQAT USTOZ DARSLARI davomat mezoniga kiradi (eski qoidaning
    /// aynan o'zi). Kurator darsi hisoblansa, kurator darsiga bormagan
    /// o'quvchi ustoz darsidagi to'liq davomatini yo'qotardi.
    /// </summary>
    [Fact]
    public async Task GroupBoard_AttendanceCountsTeacherSessionsOnly()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbtype");
        var me = world.Student.Id;

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, InsidePeriod, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Present });

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, InsidePeriod.AddDays(1), SessionType.Assistant,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Absent });

        var board = await BoardAsync(world, Period);

        board.Rows.Single().AttendancePercent.Should().Be(100m,
            "kurator darsi davomat mezoniga kirmaydi");
    }

    /// <summary>Teng ball — teng o'rin; jadval guruhdagi hamma faol a'zoni qamraydi.</summary>
    [Fact]
    public async Task GroupBoard_RanksEveryActiveMember()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbmany");
        var second = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "lbmany2");

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, InsidePeriod, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Present,
                [second.Id] = AttendanceStatus.Late,
            });

        var board = await BoardAsync(world, Period);

        board.StudentCount.Should().Be(2);
        board.Rows.Should().HaveCount(2);

        // `Late` ham qatnashgan hisoblanadi -> ikkalasi 100% -> teng o'rin.
        board.Rows.Select(r => r.Rank).Should().AllBeEquivalentTo(1);
    }

    // ================================================================= "mening o'rnim"

    [Fact]
    public async Task MyRank_ReturnsOwnRowWithoutTable()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbme");

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, InsidePeriod, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Present,
            });

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var rank = await client.GetFromJsonAsync<MyRankResponse>(
            $"/api/v1/leaderboard/me?period={Period}");

        rank!.GroupId.Should().Be(world.GroupId);
        rank.GroupName.Should().Be(world.GroupName);
        rank.Me!.Rank.Should().Be(1);
        rank.Me.IsMe.Should().BeTrue();
        rank.Me.AttendancePercent.Should().Be(100m);
    }

    /// <summary>Guruhsiz o'quvchi uchun 404 emas, BO'SH javob — ekran baribir ochiladi.</summary>
    [Fact]
    public async Task MyRank_ForStudentWithoutGroup_ReturnsEmptyPayload()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var loner = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "lbsolo");

        using var client = await WorldBuilder.ClientAsync(factory, loner);

        var rank = await client.GetFromJsonAsync<MyRankResponse>("/api/v1/leaderboard/me");

        rank!.GroupId.Should().BeNull();
        rank.Me.Should().BeNull();
    }

    // ================================================================= ruxsat

    /// <summary>★ Begona guruh reytingi KO'RINMAYDI.</summary>
    [Fact]
    public async Task GroupBoard_AsOutsiderStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbown");
        var other = await WorldBuilder.CreateAsync(factory, "lbother");

        using var client = await WorldBuilder.ClientAsync(factory, other.Student);

        var response = await client.GetAsync(
            new Uri($"/api/v1/leaderboard/groups/{world.GroupId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Guruh ustozi va kuratori o'z guruhini ko'radi.</summary>
    [Fact]
    public async Task GroupBoard_AsGroupStaff_IsAllowed()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbstaff");

        foreach (var staff in new[] { world.Teacher, world.Curator })
        {
            using var client = await WorldBuilder.ClientAsync(factory, staff);

            var response = await client.GetAsync(
                new Uri($"/api/v1/leaderboard/groups/{world.GroupId}", UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

            var board = await response.Content.ReadFromJsonAsync<BoardResponse>();
            board!.Me.Should().BeNull("xodim jadvalning ichida emas");
        }
    }

    /// <summary>
    /// ★ ARXIVLANGAN guruh reytingi o'quvchiga KO'RINMAYDI (eski tizimning
    /// QA-04 tuzatishi bilan bir xil), lekin admin uni hisobot uchun
    /// ko'ra oladi.
    /// </summary>
    [Fact]
    public async Task GroupBoard_ForArchivedGroup_HiddenFromStudent_VisibleToAdmin()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbarch");

        using (var admin = await WorldBuilder.AdminClientAsync(factory))
        {
            var archive = await admin.PostAsync(
                new Uri($"/api/v1/groups/{world.GroupId}/archive", UriKind.Relative), content: null);

            archive.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(archive));
        }

        using (var student = await WorldBuilder.ClientAsync(factory, world.Student))
        {
            var response = await student.GetAsync(
                new Uri($"/api/v1/leaderboard/groups/{world.GroupId}", UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var admin = await WorldBuilder.AdminClientAsync(factory))
        {
            var response = await admin.GetAsync(
                new Uri($"/api/v1/leaderboard/groups/{world.GroupId}", UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task GroupBoard_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/v1/leaderboard/groups/1", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// ★ NOTO'G'RI `period` — 400, 409 EMAS. Domain istisnosi global
    /// xaritada 409 ga tushadi; so'rov qatoridagi xato uchun bu noto'g'ri
    /// signal va frontend uni "qayta urinib ko'ring" deb tushunardi.
    /// </summary>
    [Theory]
    [InlineData("2026-13")]
    [InlineData("may")]
    [InlineData("2026")]
    public async Task GroupBoard_WithMalformedPeriod_ReturnsBadRequest(string period)
    {
        var world = await WorldBuilder.CreateAsync(factory, "lbbad");

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(
            new Uri($"/api/v1/leaderboard/groups/{world.GroupId}?period={period}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================= yordamchi

    private async Task<BoardResponse> BoardAsync(StudentWorld world, string period)
    {
        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(
            new Uri($"/api/v1/leaderboard/groups/{world.GroupId}?period={period}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private sealed record BoardResponse(
        long GroupId,
        string GroupName,
        string Period,
        int StudentCount,
        RowResponse? Me,
        List<RowResponse> Rows);

    private sealed record MyRankResponse(
        long? GroupId, string? GroupName, string Period, int StudentCount, RowResponse? Me);

    private sealed record RowResponse(
        long StudentId,
        string StudentName,
        int Rank,
        decimal Total,
        decimal? AttendancePercent,
        decimal? AssignmentPercent,
        decimal? TestPercent,
        bool IsMe);
}
