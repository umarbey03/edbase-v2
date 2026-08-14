using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// O'quvchi ilovasi testlari uchun UMUMIY dunyo quruvchi.
///
/// NIMA UCHUN ALOHIDA FAYL: reyting, davomat, kalendar va yozishma
/// testlari AYNI shakldagi ma'lumotni talab qiladi — ustoz, kurator,
/// o'quvchi va ular bog'langan guruh. Har test sinfida qayta yozilsa,
/// bittasida kurator biriktirilishi unutilib, test "sababsiz" yiqilardi.
/// </summary>
internal sealed record StudentWorld(
    long GroupId,
    string GroupName,
    TestUser Student,
    TestUser Teacher,
    TestUser Curator);

internal sealed record TestUser(long Id, string Email, string Password);

internal static class WorldBuilder
{
    private const string Password = "Talaba!2345";

    /// <summary>
    /// Ustoz + kurator + o'quvchi va ularni bog'lovchi guruh yaratadi.
    /// Kurator guruhga TO'G'RIDAN-TO'G'RI (<c>AssistantId</c>) biriktiriladi —
    /// eski tizimdagi ikki bog'lanish yo'lidan birinchisi.
    ///
    /// ★ <c>courseMonths = 1</c> ATAYLAB: guruh yaratilishi bilan jadval
    /// AVTOMATIK generatsiya qilinadi. 8 oylik guruh 2026-yanvardan
    /// sentabrgacha ~70 dars yaratadi va testlar tekshiradigan oyga
    /// (2026-may) o'nlab "begona" dars tushib qolardi — natijada
    /// "bitta dars bo'lishi kerak" turidagi tasdiqlar sababsiz yiqilardi.
    /// Bir oylik guruh esa faqat yanvar-fevralni to'ldiradi va testlar
    /// o'z ma'lumotini o'zi to'liq boshqaradi.
    /// </summary>
    public static async Task<StudentWorld> CreateAsync(
        ZinnurApiFactory factory, string prefix)
    {
        using var admin = await AdminClientAsync(factory);

        var teacher = await CreateUserAsync(admin, UserRole.Teacher, prefix);
        var curator = await CreateUserAsync(admin, UserRole.Assistant, prefix);
        var student = await CreateUserAsync(admin, UserRole.Student, prefix);

        var groupName = $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}";

        var response = await admin.PostAsJsonAsync("/api/v1/groups", new
        {
            name = groupName,
            startDate = "2026-01-05",
            weekdays = new[] { "Monday", "Wednesday" },
            startTime = "19:00:00",
            teacherId = teacher.Id,
            assistantId = curator.Id,
            courseMonths = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await Body(response));

        var group = (await response.Content.ReadFromJsonAsync<CreatedGroup>())!;

        var member = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{group.Group.Id}/members", new { studentId = student.Id });

        member.StatusCode.Should().Be(HttpStatusCode.Created, await Body(member));

        return new StudentWorld(group.Group.Id, groupName, student, teacher, curator);
    }

    /// <summary>Guruhga qo'shimcha o'quvchi qo'shadi (reyting jadvali uchun).</summary>
    public static async Task<TestUser> AddStudentAsync(
        ZinnurApiFactory factory, long groupId, string prefix)
    {
        using var admin = await AdminClientAsync(factory);

        var student = await CreateUserAsync(admin, UserRole.Student, prefix);

        var member = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{groupId}/members", new { studentId = student.Id });

        member.StatusCode.Should().Be(HttpStatusCode.Created, await Body(member));

        return student;
    }

    /// <summary>
    /// YAKUNLANGAN dars yaratadi va berilgan o'quvchilarga davomat yozadi.
    ///
    /// Bazaga TO'G'RIDAN-TO'G'RI yoziladi: darsni "haqiqiy" yo'l bilan
    /// o'tkazish LiveKit webhook'lari va real vaqtni talab qilardi —
    /// bu yerdagi testlar esa HISOBNI tekshiradi, dars oqimini emas
    /// (uni `LiveSessionEndpointsTests` qoplaydi).
    /// </summary>
    public static Task<long> AddEndedSessionAsync(
        ZinnurApiFactory factory,
        long groupId,
        DateTimeOffset scheduledStartUtc,
        SessionType type,
        IReadOnlyDictionary<long, AttendanceStatus> attendance) =>
        factory.WithDbAsync(async db =>
        {
            var session = new LiveSession
            {
                GroupId = groupId,
                Type = type,
                Status = SessionStatus.Ended,
                ScheduledStart = scheduledStartUtc,
                ScheduledEnd = scheduledStartUtc.AddMinutes(80),
                ActualStart = scheduledStartUtc,
                ActualEnd = scheduledStartUtc.AddMinutes(80),
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);
            await db.SaveChangesAsync();

            foreach (var (studentId, status) in attendance)
            {
                db.Attendances.Add(new Attendance
                {
                    SessionId = session.Id,
                    StudentId = studentId,
                    Status = status,
                    DurationSeconds = status == AttendanceStatus.Absent ? 0 : 3600,
                });
            }

            await db.SaveChangesAsync();
            return session.Id;
        });

    /// <summary>Rejalashtirilgan (hali o'tmagan) dars — kalendar testlari uchun.</summary>
    public static Task<long> AddScheduledSessionAsync(
        ZinnurApiFactory factory,
        long groupId,
        DateTimeOffset scheduledStartUtc,
        SessionStatus status = SessionStatus.Scheduled) =>
        factory.WithDbAsync(async db =>
        {
            var session = new LiveSession
            {
                GroupId = groupId,
                Type = SessionType.Teacher,
                Status = status,
                ScheduledStart = scheduledStartUtc,
                ScheduledEnd = scheduledStartUtc.AddMinutes(80),
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        });

    /// <summary>Baholangan vazifa javobi (reyting vazifa mezoni uchun).</summary>
    public static Task AddGradedSubmissionAsync(
        ZinnurApiFactory factory,
        long groupId,
        long studentId,
        decimal score,
        decimal maxScore,
        DateTimeOffset gradedAtUtc) =>
        factory.WithDbAsync(async db =>
        {
            var assignment = new Assignment
            {
                GroupId = groupId,
                Title = "Vazifa " + Guid.NewGuid().ToString("N")[..6],
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

    /// <summary>Topshirilgan test urinishi (reyting test mezoni uchun).</summary>
    public static Task AddTestAttemptAsync(
        ZinnurApiFactory factory,
        long studentId,
        decimal score,
        decimal maxScore,
        DateTimeOffset submittedAtUtc) =>
        factory.WithDbAsync(async db =>
        {
            var test = new Test
            {
                Title = "Test " + Guid.NewGuid().ToString("N")[..6],
                Kind = TestKind.Competition,
                IsPublished = true,
            };

            db.Tests.Add(test);
            await db.SaveChangesAsync();

            db.TestAttempts.Add(new TestAttempt
            {
                TestId = test.Id,
                StudentId = studentId,
                Status = AttemptStatus.Submitted,
                Score = score,
                MaxScore = maxScore,
                StartedAt = submittedAtUtc.AddMinutes(-30),
                SubmittedAt = submittedAtUtc,
            });

            await db.SaveChangesAsync();
            return 0;
        });

    public static async Task<HttpClient> ClientAsync(ZinnurApiFactory factory, TestUser user)
    {
        var tokens = await factory.LoginAsync(user.Email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    public static async Task<HttpClient> AdminClientAsync(ZinnurApiFactory factory)
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    public static async Task<TestUser> CreateUserAsync(
        HttpClient admin, UserRole role, string prefix)
    {
        /*
          🔴 KESISH PREFIKSGA QO'LLANADI, TASODIFIY QISMGA EMAS.

          Ilgari bu qator `$"{prefix}-{Guid...}"[..20]` edi — ya'ni 20 belgi
          prefiks BILAN BIRGA sanalardi. Uzun prefiksli testda ("izoh-…",
          "staff-responsibility-…") tasodifiy qismdan atigi 2–3 belgi qolardi
          va umumiy dev bazasida yozuvlar to'plangach email TO'QNASHARDI:
          `POST /users` → 409 "Bu email allaqachon ro'yxatda", test esa 201
          kutardi.

          ★ Bu "flaky test" bo'lib ko'rinardi — ba'zi yurishlarda o'tib,
          ba'zisida yiqilardi — chunki natija baza tarixiga bog'liq edi.
          Sabab kodda emas, AYNAN shu kesishda.

          Endi prefiks 8 belgigacha qisqaradi va GUID'dan doim 11 belgi
          qoladi (16^11 ≈ 1.7·10^13 variant), ya'ni to'qnashuv amalda
          bo'lmaydi. Email uzunligi o'zgarmadi — 20 + domen.
        */
        var slug = prefix.Length <= 8 ? prefix : prefix[..8];
        var email = $"{slug}-{Guid.NewGuid():N}"[..20] + "@zinnur.uz";

        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = $"{prefix} {role}",
            email,
            role = role.ToString(),

            // 🔴 TELEFON MAJBURIY (2026-08-13): xodim rollari uchun server
            //    uni talab qiladi, chunki kirish faqat telefon orqali.
            //    O'quvchiga shart emas, lekin BERILADI — shu tufayli
            //    o'quvchi ham telefon oqimini haydaydigan testlarda
            //    ishlatila oladi.
            phone = TestPhones.Next(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await Body(response));

        var created = (await response.Content.ReadFromJsonAsync<CreatedUser>())!;
        return new TestUser(created.User.Id, email, Password);
    }

    /// <summary>Xato javobini o'qib beradi — test yiqilganda sabab ko'rinsin.</summary>
    public static async Task<string> Body(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var content = await response.Content.ReadAsStringAsync();

        return string.Create(CultureInfo.InvariantCulture, $"javob: {content}");
    }

    private sealed record CreatedGroup(GroupBrief Group);

    private sealed record GroupBrief(long Id, string Name);

    private sealed record CreatedUser(UserBrief User);

    private sealed record UserBrief(long Id);
}
