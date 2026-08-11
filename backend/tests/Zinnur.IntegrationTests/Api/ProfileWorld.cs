using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// O'quvchi PROFILI, IZOHLARI va FILTRLARI testlari uchun umumiy dunyo.
///
/// NIMA UCHUN ALOHIDA FAYL: profil, izohlar, Telegram uzish va ro'yxat
/// filtrlari AYNI shakldagi ma'lumotni talab qiladi (ustoz + kurator +
/// o'quvchi + guruh, ustiga moliya va o'quv natijalari). Har test sinfida
/// qayta yozilsa, bittasida tarif yoki a'zolik unutilib, test "sababsiz"
/// yiqilardi — `StudentWorld` bilan AYNI sabab.
///
/// `WorldBuilder` (mavjud) qayta ishlatiladi: u guruh, ustoz, kurator va
/// o'quvchini API orqali yaratadi, ya'ni ma'lumot HAQIQIY oqim bilan
/// tug'iladi (bazaga qo'lda yozib qo'yilgan holat emas).
/// </summary>
internal static class ProfileWorldBuilder
{
    internal const decimal MonthlyPrice = 540_000m;

    /// <summary>
    /// Hisob oyi ATAYLAB guruh jadvalining ichida (`WorldBuilder` guruhi
    /// 2026-01-05 dan boshlanadi va bir oy davom etadi) — shunda "o'sha
    /// oydagi darslar soni" ni haqiqiy darslar bilan tekshirish mumkin.
    /// </summary>
    internal const string Period = "2026-01";

    /// <summary>Tarif + oy ochish + qisman to'lov bo'lgan to'liq dunyo.</summary>
    internal static async Task<StudentWorld> CreateWithFinanceAsync(
        ZinnurApiFactory factory, string prefix, decimal paid = 200_000m)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var tariff = await admin.PostAsJsonAsync("/api/v1/payments/tariffs", new
        {
            name = prefix + " tarifi",
            amount = MonthlyPrice,
            activeFrom = "2026-01-01",
            groupId = world.GroupId,
        });

        tariff.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(tariff));

        var opened = await admin.PostAsJsonAsync(
            "/api/v1/payments/periods/open", new { period = Period, groupId = world.GroupId });

        opened.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(opened));

        if (paid > 0)
        {
            var receipt = await admin.PostAsJsonAsync("/api/v1/payments", new
            {
                studentId = world.Student.Id,
                amount = paid,
                method = "Cash",
            });

            receipt.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(receipt));
        }

        return world;
    }

    /// <summary>
    /// Vazifa javobi + BIRIKTIRILGAN FAYL.
    ///
    /// 🔴 Fayl ATAYLAB: profil javobida <c>objectKey</c> chiqmasligini
    /// tekshirish uchun ombor kaliti aniq va izlanadigan bo'lishi kerak.
    /// </summary>
    internal static Task<string> AddSubmissionWithFileAsync(
        ZinnurApiFactory factory, long groupId, long studentId) =>
        factory.WithDbAsync(async db =>
        {
            var objectKey = "submissions/SIR-" + Guid.NewGuid().ToString("N") + ".png";

            var assignment = new Assignment
            {
                GroupId = groupId,
                Title = "Profil sinov vazifasi",
                MaxScore = 10m,
            };

            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var submission = new Submission
            {
                AssignmentId = assignment.Id,
                StudentId = studentId,
                Status = SubmissionStatus.Graded,
                Score = 8m,
                SubmittedAt = new DateTimeOffset(2026, 1, 20, 10, 0, 0, TimeSpan.Zero),
                GradedAt = new DateTimeOffset(2026, 1, 21, 10, 0, 0, TimeSpan.Zero),
                IsLate = true,
            };

            db.Submissions.Add(submission);
            await db.SaveChangesAsync();

            db.SubmissionFiles.Add(new SubmissionFile
            {
                SubmissionId = submission.Id,
                ObjectKey = objectKey,
                Kind = AttachmentKind.Image,
                SizeBytes = 1024,
                ContentType = "image/png",
            });

            await db.SaveChangesAsync();

            return objectKey;
        });

    /// <summary>
    /// Ikkita YAKUNLANGAN dars: bittasiga kelgan, bittasiga kelmagan.
    /// Shu tufayli davomat 50% va oydagi darslar soni 2 bo'ladi.
    /// </summary>
    internal static async Task AddTwoEndedSessionsAsync(
        ZinnurApiFactory factory, long groupId, long studentId)
    {
        // 14:00 UTC = 19:00 Toshkent — ikkalasi ham YANVARGA tushadi
        // (oy chegarasiga tegmaydi, shuning uchun test zonaga bog'liq emas).
        await WorldBuilder.AddEndedSessionAsync(
            factory,
            groupId,
            new DateTimeOffset(2026, 1, 12, 14, 0, 0, TimeSpan.Zero),
            SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [studentId] = AttendanceStatus.Present });

        await WorldBuilder.AddEndedSessionAsync(
            factory,
            groupId,
            new DateTimeOffset(2026, 1, 14, 14, 0, 0, TimeSpan.Zero),
            SessionType.Teacher,
            new Dictionary<long, AttendanceStatus> { [studentId] = AttendanceStatus.Absent });
    }

    /// <summary>Profilni berilgan klient bilan o'qiydi (xom JSON ham qaytadi).</summary>
    internal static async Task<(HttpStatusCode Status, string Json)> GetProfileRawAsync(
        HttpClient client, long userId)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(
            new Uri($"/api/v1/users/{userId.ToString(CultureInfo.InvariantCulture)}/profile",
                UriKind.Relative));

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static async Task<ProfileResponse> GetProfileAsync(HttpClient client, long userId)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(
            new Uri($"/api/v1/users/{userId.ToString(CultureInfo.InvariantCulture)}/profile",
                UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
    }

    /// <summary>Bazadagi joriy sessiya versiyasi (uzishdan keyin oshgani tekshiriladi).</summary>
    internal static Task<int> TokenVersionOfAsync(ZinnurApiFactory factory, long userId)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.WithDbAsync(db => db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TokenVersion)
            .FirstAsync());
    }

    /// <summary>Telegram'ni BAZADAN bog'laydi (API orqali bog'lash ataylab yo'q).</summary>
    internal static Task LinkTelegramAsync(
        ZinnurApiFactory factory, long userId, long telegramId, string? username = "test_user")
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.LinkTelegram(telegramId, username, DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });
    }

    /// <summary>Har test uchun betakror Telegram ID.</summary>
    internal static long NextTelegramId() => Interlocked.Increment(ref _telegramId);

    private static long _telegramId = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 100;
}

// ============================================================================
// JAVOB SHAKLLARI
//
// Enumlar SATR sifatida o'qiladi (API `JsonStringEnumConverter` bilan
// yozadi) — mavjud moliya testlaridagi bilan AYNI naqsh.
// ============================================================================

internal sealed record ProfileResponse(
    ProfileUser User,
    ProfileTelegram Telegram,
    List<ProfileGroup> Groups,
    ProfileFinance? Finance,
    ProfileStudy Study,
    List<NoteResponse>? Notes);

internal sealed record ProfileUser(
    long Id,
    string FullName,
    string Email,
    string? Phone,
    long? TelegramId,
    string? TelegramUsername,
    string Role,
    bool IsActive);

internal sealed record ProfileTelegram(
    bool Linked,
    long? TelegramId,
    string? Username,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? UnlinkedAt,
    string? UnlinkedByName,
    string? UnlinkReason);

internal sealed record ProfileGroup(
    long GroupId,
    string GroupName,
    string? TeacherName,
    string Status,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt,
    long? MovedToGroupId,
    string? MovedToGroupName,
    DateOnly? PausedUntil);

internal sealed record ProfileFinance(
    decimal Balance,
    decimal TotalPaid,
    decimal TotalDue,
    string BlockScope,
    List<ProfilePeriod> Periods,
    List<ProfileTransaction>? Transactions,
    bool HasMoreTransactions);

internal sealed record ProfilePeriod(
    string Month,
    long GroupId,
    string GroupName,
    decimal Amount,
    decimal PaidAmount,
    decimal Outstanding,
    string Status,
    int SessionCount);

internal sealed record ProfileTransaction(
    long Id,
    string Kind,
    decimal Amount,
    string? Method,
    string? GroupName,
    string? ActorName,
    DateTimeOffset CreatedAt);

internal sealed record ProfileStudy(
    List<ProfileAssignment> Assignments,
    bool HasMoreAssignments,
    List<ProfileTest> Tests,
    bool HasMoreTests,
    ProfileAttendance Attendance);

internal sealed record ProfileAssignment(
    long SubmissionId,
    long AssignmentId,
    string Title,
    string? GroupName,
    string? LessonName,
    decimal? Score,
    decimal MaxScore,
    string Status,
    DateTimeOffset SubmittedAt,
    bool IsLate,
    int FileCount);

internal sealed record ProfileTest(
    long AttemptId,
    long TestId,
    string Title,
    string Kind,
    decimal? Score,
    decimal? MaxScore,
    decimal? ScorePercent,
    bool ClosedByTimeout,
    DateTimeOffset? FinishedAt);

internal sealed record ProfileAttendance(int Total, int Present, int Missed, decimal Percent);

internal sealed record NoteResponse(
    long Id,
    long StudentId,
    string Body,
    long AuthorId,
    string AuthorName,
    long? GroupId,
    string? GroupName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit);

internal sealed record UnlinkResponse(long? TelegramId, string? TelegramUsername);

internal sealed record UserListResponse(List<ProfileUser> Items, int Page, int PageSize, int Total);
