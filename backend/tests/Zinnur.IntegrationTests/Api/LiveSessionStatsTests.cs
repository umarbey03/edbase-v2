using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// R31 — "DARSLARIM" JADVALINING AGREGATI
/// ========================================================================
///
/// Talab (2026-08-13): *"darslarim bo'limida jadval ma'lumoti sifatida
/// nechta student borligi, nechta qatnashganligi, davomiyligi"*.
///
/// Nima isbotlanadi:
///   1) uchala son ham SERVERDA hisoblanadi va bitta so'rovda keladi
///      (mijoz har dars uchun davomat varag'iga bormaydi — 69 darsli
///      guruhda bu 69 ta so'rov bo'lardi, <c>attendance-matrix.ts</c>);
///   2) 🔴 <c>Late</c> va <c>Partial</c> QATNASHGAN hisoblanadi, davomat
///      yozuvi YO'Q o'quvchi esa yo'q hisoblanadi;
///   3) davomiylik HAQIQIY (<c>ActualEnd − ActualStart</c>), reja esa
///      yonida alohida maydon bo'lib qaytadi;
///   4) ruxsat <c>ScopeByRole</c> dan keladi: begona ustoz bu darsni
///      ko'rmaydi, o'quvchi esa endpointga umuman kira olmaydi.
///
/// ★ MA'LUMOT API ORQALI EMAS, BAZAGA TO'G'RIDAN-TO'G'RI yoziladi: guruhni
/// API bilan yaratish 8 oylik jadvalni (~70 dars) generatsiya qilardi va
/// test tekshirayotgan yagona qatorni o'nlab begona qator ichida
/// qidirishga majbur bo'lardi.
/// </summary>
public sealed class LiveSessionStatsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>Reja: 80 daqiqa. Haqiqiy: 45 — ikkisi FARQ qilishi shart.</summary>
    private const int PlannedMinutes = 80;
    private const int ActualMinutes = 45;

    [Fact]
    public async Task Stats_CountsMembersAttendanceAndActualDuration()
    {
        var world = await CreateWorldAsync();

        using var teacher = await ClientAsync(world.TeacherEmail);

        var page = await teacher.GetFromJsonAsync<PagedSessions>(
            "/api/v1/live-sessions/stats?status=Ended&pageSize=100");

        page.Should().NotBeNull();

        var row = page!.Items.Single(s => s.Id == world.SessionId);

        row.StudentCount.Should().Be(4,
            "guruhda TO'RTTA faol a'zo bor (chiqarilgan a'zo sanalmaydi)");

        row.AttendedCount.Should().Be(3,
            "Present + Late + Partial — uchalasi ham 'keldi' degani; "
            + "Absent va davomat yozuvi umuman yo'q o'quvchi sanalmaydi");

        row.ActualMinutes.Should().Be(ActualMinutes,
            "davomiylik HAQIQIY: ActualEnd − ActualStart");

        row.PlannedMinutes.Should().Be(PlannedMinutes,
            "reja ham qaytadi — 'reja 80, haqiqiy 45' taqqoslash uchun");

        row.Status.Should().Be(nameof(SessionStatus.Ended));
        row.IsHost.Should().BeTrue("dars ustozning o'z guruhida");
        row.GroupName.Should().Be(world.GroupName);
    }

    /// <summary>
    /// ★ Boshlanmagan darsda davomiylik <c>null</c> — 0 EMAS.
    ///
    /// Nol yozilsa jadvalda "0 daqiqa o'tdi" ko'rinardi va rejalashtirilgan
    /// dars o'tkazilmagan darsdan farq qilmasdi.
    /// </summary>
    [Fact]
    public async Task Stats_ForScheduledSession_LeavesActualDurationNull()
    {
        var world = await CreateWorldAsync();

        using var teacher = await ClientAsync(world.TeacherEmail);

        var page = await teacher.GetFromJsonAsync<PagedSessions>(
            "/api/v1/live-sessions/stats?status=Scheduled&pageSize=100");

        var row = page!.Items.Single(s => s.Id == world.FutureSessionId);

        row.ActualMinutes.Should().BeNull("dars hali boshlanmagan");
        row.PlannedMinutes.Should().Be(PlannedMinutes);
        row.AttendedCount.Should().Be(0);
        row.StudentCount.Should().Be(4, "a'zolar soni kelajakdagi darsda ham ma'lum");
    }

    /// <summary>
    /// ★ Ruxsat <c>ScopeByRole</c> dan — yangi qoida YOZILMADI. Begona
    /// ustozning ro'yxatida bu dars UMUMAN bo'lmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Stats_AsForeignTeacher_DoesNotLeakOtherGroupsSessions()
    {
        var world = await CreateWorldAsync();
        var strangerEmail = await CreateStaffAsync(UserRole.Teacher, "Begona Ustoz");

        using var stranger = await ClientAsync(strangerEmail);

        var page = await stranger.GetFromJsonAsync<PagedSessions>(
            "/api/v1/live-sessions/stats?pageSize=100");

        page!.Items.Select(s => s.Id).Should().NotContain(world.SessionId);
    }

    /// <summary>
    /// 🔴 O'QUVCHI — 403. Sanoqlar guruhdagi BOSHQA o'quvchilar haqidagi
    /// ma'lumot; o'quvchi o'z davomatini kalendardan ko'radi.
    /// </summary>
    [Fact]
    public async Task Stats_AsStudent_ReturnsForbidden()
    {
        var world = await CreateWorldAsync();

        using var student = await ClientAsync(world.PresentStudentEmail);

        var response = await student.GetAsync(
            new Uri("/api/v1/live-sessions/stats", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> ClientAsync(string email)
    {
        var tokens = await factory.LoginAsync(email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<string> CreateStaffAsync(UserRole role, string fullName)
    {
        var email = Unique("st") + "@zinnur.uz";

        await factory.WithDbAsync(async db =>
        {
            db.Users.Add(NewUser(fullName, email, role));
            return await db.SaveChangesAsync();
        });

        return email;
    }

    /// <summary>
    /// Bitta ustoz, to'rtta FAOL o'quvchi, bitta CHIQARILGAN o'quvchi,
    /// yakunlangan dars va kelajakdagi dars.
    ///
    /// Davomat taqsimoti ATAYLAB shunday:
    ///   • 1-o'quvchi — <c>Present</c>   -> qatnashgan
    ///   • 2-o'quvchi — <c>Late</c>      -> qatnashgan (kechikkan, lekin KELGAN)
    ///   • 3-o'quvchi — <c>Partial</c>   -> qatnashgan (xonada vaqt o'tkazgan)
    ///   • 4-o'quvchi — YOZUVI YO'Q      -> qatnashmagan
    ///   • 5-o'quvchi — guruhni TARK ETGAN (<c>Stopped</c>), yozuvi
    ///     <c>Absent</c> -> hech qayerga qo'shilmaydi
    ///
    /// ★ SONLAR ATAYLAB HAR XIL (4 va 3): teng bo'lsa, ikkala sanoqni bir
    /// xil ifodadan hisoblaydigan xato test yashil turgan holda o'tib
    /// ketardi.
    ///
    /// ⚠️ Beshinchi o'quvchi <c>StudentCount</c> "hozirgi FAOL a'zolar"
    /// ekanini qotirib qo'yadi: barcha a'zolar sanalganda son 5 chiqardi.
    /// </summary>
    private async Task<World> CreateWorldAsync()
    {
        var groupName = "R31-" + Guid.NewGuid().ToString("N")[..8];

        var teacherEmail = Unique("t31") + "@zinnur.uz";
        var emails = new[]
        {
            Unique("s1") + "@zinnur.uz",
            Unique("s2") + "@zinnur.uz",
            Unique("s3") + "@zinnur.uz",
            Unique("s4") + "@zinnur.uz",
            Unique("s5") + "@zinnur.uz",
        };

        return await factory.WithDbAsync(async db =>
        {
            var teacher = NewUser("R31 Ustoz", teacherEmail, UserRole.Teacher);

            var students = emails
                .Select((email, i) => NewUser(
                    "R31 O'quvchi " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    email,
                    UserRole.Student))
                .ToList();

            db.Users.Add(teacher);
            db.Users.AddRange(students);

            var group = new Group
            {
                Name = groupName,
                TeacherId = null,   // quyida Id ma'lum bo'lgach qo'yiladi
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
                Weekdays = [DayOfWeek.Monday, DayOfWeek.Wednesday],
                DurationMinutes = PlannedMinutes,
            };

            db.Groups.Add(group);
            await db.SaveChangesAsync();

            group.TeacherId = teacher.Id;

            // Beshinchi o'quvchi guruhni TARK ETGAN (`Stopped`) — faol
            // a'zolar soniga kirmaydi, lekin darsdagi davomat yozuvi qoladi.
            for (var i = 0; i < students.Count; i++)
            {
                db.GroupMembers.Add(new GroupMember
                {
                    GroupId = group.Id,
                    StudentId = students[i].Id,
                    Status = i == 4 ? MemberStatus.Stopped : MemberStatus.Active,
                });
            }

            var startedAt = DateTimeOffset.UtcNow.AddHours(-3);

            var ended = new LiveSession
            {
                GroupId = group.Id,
                HostId = teacher.Id,
                RoomName = LiveSession.GenerateRoomName(),
                Type = SessionType.Teacher,
                Status = SessionStatus.Ended,
                ScheduledStart = startedAt,
                ScheduledEnd = startedAt.AddMinutes(PlannedMinutes),
                ActualStart = startedAt,
                ActualEnd = startedAt.AddMinutes(ActualMinutes),
            };

            var future = new LiveSession
            {
                GroupId = group.Id,
                HostId = teacher.Id,
                RoomName = LiveSession.GenerateRoomName(),
                Type = SessionType.Teacher,
                Status = SessionStatus.Scheduled,
                ScheduledStart = DateTimeOffset.UtcNow.AddDays(2),
                ScheduledEnd = DateTimeOffset.UtcNow.AddDays(2).AddMinutes(PlannedMinutes),
            };

            db.LiveSessions.AddRange(ended, future);
            await db.SaveChangesAsync();

            AttendanceStatus?[] statuses =
            [
                AttendanceStatus.Present,
                AttendanceStatus.Late,
                AttendanceStatus.Partial,
                null,                          // yozuv UMUMAN yo'q
                AttendanceStatus.Absent,       // guruhni tark etgan a'zo
            ];

            for (var i = 0; i < students.Count; i++)
            {
                if (statuses[i] is not { } status) continue;

                db.Attendances.Add(new Attendance
                {
                    SessionId = ended.Id,
                    StudentId = students[i].Id,
                    Status = status,
                });
            }

            await db.SaveChangesAsync();

            return new World(
                group.Id, groupName, ended.Id, future.Id, teacherEmail, emails[0]);
        });
    }

    /// <summary>
    /// Parol bilan kirish 2026-08-13 da olib tashlandi, lekin ustun hamon
    /// MAJBURIY — shuning uchun testda o'rin egallovchi qiymat yoziladi
    /// (`LoginAsync` tokenni to'g'ridan-to'g'ri yasaydi, parolga tegmaydi).
    /// </summary>
    private static User NewUser(string fullName, string email, UserRole role) => new()
    {
        FullName = fullName,
        Email = email,
        PasswordHash = "test-only-placeholder",
        Role = role,
        IsActive = true,
    };

    private static string Unique(string prefix) =>
        prefix + Guid.NewGuid().ToString("N")[..10];

    private sealed record World(
        long GroupId,
        string GroupName,
        long SessionId,
        long FutureSessionId,
        string TeacherEmail,
        string PresentStudentEmail);

    private sealed record PagedSessions(List<StatsRow> Items, int Page, int Total);

    private sealed record StatsRow(
        long Id,
        long GroupId,
        string GroupName,
        string? Title,
        string Type,
        string Status,
        int PlannedMinutes,
        int? ActualMinutes,
        int StudentCount,
        int AttendedCount,
        bool IsHost);
}
