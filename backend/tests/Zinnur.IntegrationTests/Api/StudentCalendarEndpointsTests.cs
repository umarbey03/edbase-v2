using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// KALENDAR VA DAVOMAT XULOSASI
/// ========================================================================
///
/// ★ ENG MUHIM TEKSHIRUV: <see cref="ExistingUpcomingListContract_IsUnchanged"/>.
/// Kalendar YANGI endpoint bo'lib qo'shildi, mavjud
/// <c>GET /api/v1/live-sessions</c> esa o'zgarmadi — frontend uni
/// allaqachon ishlatadi va shartnomasi buzilishi ilovani yiqitardi.
/// </summary>
public sealed class StudentCalendarEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>2026-05-14 14:00 UTC = 19:00 Toshkent (odatiy dars vaqti).</summary>
    private static readonly DateTimeOffset MayEvening =
        new(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

    // ================================================================= kalendar

    [Fact]
    public async Task Calendar_ReturnsSessionsInRange_WithLocalDateAndAttendance()
    {
        var world = await WorldBuilder.CreateAsync(factory, "cal");

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Late,
            });

        var days = await CalendarAsync(world, "2026-05-01", "2026-05-31");

        var session = days.Should().ContainSingle().Subject;

        session.LocalDate.Should().Be(new DateOnly(2026, 5, 14));
        session.Status.Should().Be(nameof(SessionStatus.Ended));
        session.Type.Should().Be(nameof(SessionType.Teacher));
        session.MyAttendance.Should().Be(nameof(AttendanceStatus.Late));
        session.GroupName.Should().Be(world.GroupName);
        session.IsHost.Should().BeFalse();
    }

    /// <summary>Oraliqdan tashqaridagi dars QAYTMAYDI.</summary>
    [Fact]
    public async Task Calendar_ExcludesSessionsOutsideRange()
    {
        var world = await WorldBuilder.CreateAsync(factory, "calrange");

        await WorldBuilder.AddScheduledSessionAsync(factory, world.GroupId, MayEvening);

        var june = await CalendarAsync(world, "2026-06-01", "2026-06-30");

        june.Should().BeEmpty();
    }

    /// <summary>
    /// ★ BEKOR QILINGAN dars kalendarda KO'RINADI (yaqin darslar
    /// ro'yxatidan farqli). Aks holda o'quvchi jadvalda bo'shliqni ko'rib
    /// "tizim adashdimi?" deb o'ylardi.
    /// </summary>
    [Fact]
    public async Task Calendar_IncludesCancelledSessions()
    {
        var world = await WorldBuilder.CreateAsync(factory, "calcancel");

        await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening, SessionStatus.Cancelled);

        var days = await CalendarAsync(world, "2026-05-01", "2026-05-31");

        days.Should().ContainSingle()
            .Which.Status.Should().Be(nameof(SessionStatus.Cancelled));
    }

    /// <summary>★ Begona guruh darslari kalendarda KO'RINMAYDI.</summary>
    [Fact]
    public async Task Calendar_DoesNotLeakOtherGroupsSessions()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "calmine");
        var other = await WorldBuilder.CreateAsync(factory, "calother");

        await WorldBuilder.AddScheduledSessionAsync(factory, mine.GroupId, MayEvening);
        await WorldBuilder.AddScheduledSessionAsync(factory, other.GroupId, MayEvening);

        var days = await CalendarAsync(mine, "2026-05-01", "2026-05-31");

        days.Should().ContainSingle("faqat o'z guruhining darsi ko'rinadi")
            .Which.GroupId.Should().Be(mine.GroupId);
    }

    /// <summary>Juda uzun oraliq rad etiladi — butun baza bitta javobga sig'masin.</summary>
    [Fact]
    public async Task Calendar_WithOversizedRange_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "calbig");

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(new Uri(
            "/api/v1/live-sessions/calendar?from=2020-01-01&to=2030-01-01", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Calendar_WithReversedRange_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "calrev");

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(new Uri(
            "/api/v1/live-sessions/calendar?from=2026-05-31&to=2026-05-01", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// ★ MAVJUD SHARTNOMA BUZILMAGAN: <c>GET /api/v1/live-sessions</c>
    /// hamon AYNAN o'z maydonlari bilan ishlaydi va yangi marshrut
    /// (<c>/calendar</c>) unga xalaqit bermaydi.
    ///
    /// Tekshiruv MAYDON NOMLARI darajasida: javob JSON'i qo'lda yozilgan
    /// shaklga to'liq mos kelishi shart, aks holda test yiqiladi va
    /// shartnoma buzilgani darhol ma'lum bo'ladi. Kalendarga kerak
    /// bo'lgan yangi maydonlar (`localDate`, `myAttendance`) bu javobda
    /// BO'LMASLIGI kerak — ular alohida DTO'da.
    /// </summary>
    [Fact]
    public async Task ExistingUpcomingListContract_IsUnchanged()
    {
        var world = await WorldBuilder.CreateAsync(factory, "calkeep");

        // Kelajakdagi dars — "yaqin darslar" ro'yxati aynan shunisini beradi.
        var future = DateTimeOffset.UtcNow.AddDays(3);
        await WorldBuilder.AddScheduledSessionAsync(factory, world.GroupId, future);

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(
            new Uri("/api/v1/live-sessions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var payload = await response.Content.ReadAsStringAsync();

        payload.Should().NotContain("localDate", "kalendar maydoni bu shartnomada yo'q");
        payload.Should().NotContain("myAttendance", "kalendar maydoni bu shartnomada yo'q");

        var sessions = await client.GetFromJsonAsync<List<UpcomingResponse>>("/api/v1/live-sessions");

        var session = sessions.Should().ContainSingle().Subject;

        session.GroupId.Should().Be(world.GroupId);
        session.GroupName.Should().Be(world.GroupName);
        session.Status.Should().Be(nameof(SessionStatus.Scheduled));
        session.IsHost.Should().BeFalse();
        session.ActualStart.Should().BeNull();
        session.EndsAt.Should().BeNull();
    }

    // ================================================================= davomat xulosasi

    [Fact]
    public async Task AttendanceSummary_SplitsTeacherAndAssistantSessions()
    {
        var world = await WorldBuilder.CreateAsync(factory, "att");
        var me = world.Student.Id;

        // Ustoz darslari: 2 tadan 1 ta qatnashgan
        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Present });

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening.AddDays(2), SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Absent });

        // Kurator darsi: 1 tadan 1 ta (kechikkan ham qatnashgan)
        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening.AddDays(1), SessionType.Assistant,
            new Dictionary<long, AttendanceStatus> { [me] = AttendanceStatus.Late });

        var summary = await SummaryAsync(world);

        summary.Overall.Total.Should().Be(3);
        summary.Overall.Attended.Should().Be(2);
        summary.Overall.Missed.Should().Be(1);
        summary.Overall.Percent.Should().Be(66.7m);

        summary.Teacher.Total.Should().Be(2);
        summary.Teacher.Attended.Should().Be(1);
        summary.Teacher.Percent.Should().Be(50m);

        summary.Assistant.Total.Should().Be(1);
        summary.Assistant.Attended.Should().Be(1);
        summary.Assistant.Percent.Should().Be(100m);
    }

    /// <summary>
    /// ★ "DAVOMAT YOZUVI YO'Q" = "KELMAGAN": xonaga kirmagan o'quvchi
    /// uchun qator umuman yaratilmaydi, lekin dars maxrajga kiradi.
    /// </summary>
    [Fact]
    public async Task AttendanceSummary_CountsSessionWithNoRecordAsMissed()
    {
        var world = await WorldBuilder.CreateAsync(factory, "attnone");

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>());

        var summary = await SummaryAsync(world);

        summary.Overall.Total.Should().Be(1);
        summary.Overall.Attended.Should().Be(0);
        summary.Overall.Percent.Should().Be(0m);
        summary.Streak.Should().Be(0);
    }

    /// <summary>Seriya eng oxirgi darsdan orqaga sanaladi va qoldirilganda uziladi.</summary>
    [Fact]
    public async Task AttendanceSummary_StreakBreaksOnMissedLesson()
    {
        var world = await WorldBuilder.CreateAsync(factory, "attstreak");
        var me = world.Student.Id;

        AttendanceStatus[] chronological =
        [
            AttendanceStatus.Present,       // eng eski
            AttendanceStatus.Absent,
            AttendanceStatus.Present,
            AttendanceStatus.Late,          // eng yangi
        ];

        for (var i = 0; i < chronological.Length; i++)
        {
            await WorldBuilder.AddEndedSessionAsync(
                factory, world.GroupId, MayEvening.AddDays(i), SessionType.Teacher,
                new Dictionary<long, AttendanceStatus> { [me] = chronological[i] });
        }

        var summary = await SummaryAsync(world);

        summary.Streak.Should().Be(2, "oxirgi ikkitasi qatnashgan, uchinchisi — qoldirilgan");
    }

    /// <summary>★ Begona guruh Id'si so'ralsa 403 (bo'sh natija emas).</summary>
    [Fact]
    public async Task AttendanceSummary_ForForeignGroup_ReturnsForbidden()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "attmine");
        var other = await WorldBuilder.CreateAsync(factory, "attother");

        using var client = await WorldBuilder.ClientAsync(factory, mine.Student);

        var response = await client.GetAsync(new Uri(
            $"/api/v1/progress/attendance?groupId={other.GroupId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AttendanceSummary_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/v1/progress/attendance", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= yordamchi

    private async Task<List<CalendarResponse>> CalendarAsync(
        StudentWorld world, string from, string to)
    {
        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(new Uri(
            $"/api/v1/live-sessions/calendar?from={from}&to={to}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<List<CalendarResponse>>())!;
    }

    private async Task<SummaryResponse> SummaryAsync(StudentWorld world)
    {
        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(new Uri(
            $"/api/v1/progress/attendance?groupId={world.GroupId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<SummaryResponse>())!;
    }

    private sealed record CalendarResponse(
        long Id,
        long GroupId,
        string GroupName,
        string? Title,
        string Type,
        string Status,
        DateOnly LocalDate,
        DateTimeOffset ScheduledStart,
        DateTimeOffset ScheduledEnd,
        bool IsHost,
        string? MyAttendance);

    /// <summary>
    /// Mavjud "yaqin darslar" javobining shakli. Maydon nomlari ATAYLAB
    /// qo'lda yozilgan (servis DTO'siga havola qilinmagan): shunda DTO
    /// o'zgarsa test yiqiladi va shartnoma buzilgani darhol ma'lum bo'ladi.
    /// </summary>
    private sealed record UpcomingResponse(
        long Id,
        long GroupId,
        string GroupName,
        string? Title,
        string Type,
        string Status,
        DateTimeOffset ScheduledStart,
        DateTimeOffset ScheduledEnd,
        DateTimeOffset? ActualStart,
        DateTimeOffset? EndsAt,
        bool IsHost);

    private sealed record SummaryResponse(
        List<long> GroupIds,
        DateOnly? From,
        DateOnly? To,
        BucketResponse Overall,
        BucketResponse Teacher,
        BucketResponse Assistant,
        int Streak);

    private sealed record BucketResponse(int Total, int Attended, int Missed, decimal Percent);
}
