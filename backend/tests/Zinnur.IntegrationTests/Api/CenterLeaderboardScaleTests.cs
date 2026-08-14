using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// 500 DAN KO'P O'QUVCHILI MARKAZ — QAROR QULFI
/// ========================================================================
///
/// 🔴 BU TEST NIMANI QO'RIQLAYDI. <c>LeaderboardRanking.MaxRows</c> = 500
///    va u <c>DomainException</c> ko'taradi, global xaritada esa u
///    <c>409 Conflict</c> ga tushadi. Guruh jadvalida bu to'g'ri
///    (500 kishilik "guruh" — ma'lumotdagi xato), MARKAZ jadvalida esa
///    500+ o'quvchi MUTLAQO NORMAL holat: mahsulot bir necha o'quv
///    markazga sotiladi va ularning kattasi minglab o'quvchiga ega.
///
///    Chegara "ko'tarilmadi", u markazga UMUMAN QO'LLANMAYDI: markaz
///    yo'li <c>RankAll</c> dan o'tadi va javob TOP-N gacha qisqartiriladi.
///
/// ── TANLANGAN XULQ (aynan shu test qulflaydi) ──────────────────────────
///
///   • 500+ o'quvchida javob — <c>200</c>, <c>409</c> EMAS;
///   • <c>rows</c> uzunligi — AYNAN <c>topCount</c> (100);
///   • <c>studentCount</c> — TO'LIQ son (qisqartirilmagan);
///   • so'rovchining qatori TOP-100 dan tashqarida bo'lsa ham
///     <c>me</c> da keladi va O'RNI HAQIQIY (kesilgan ro'yxatdagi
///     pozitsiya emas).
///
/// ★ ALOHIDA SINF, ALOHIDA FIXTURE — ATAYLAB. Sinf o'z Postgres bazasini
///   oladi, ya'ni o'quvchilar soni AYNIQ ma'lum bo'ladi va yuqoridagi
///   "aynan 522" kabi qat'iy tasdiqlar mumkin. Umumiy bazada bu sonni
///   boshqa testlar o'zgartirib turardi.
///
/// ★ O'QUVCHILAR BAZAGA TO'G'RIDAN-TO'G'RI YOZILADI. 520 ta foydalanuvchini
///   <c>POST /users</c> orqali yaratish 520 ta HTTP so'rovi bo'lardi va
///   test o'nlab sekund yurardi — bu yerda tekshirilayotgan narsa esa
///   ro'yxatdan o'tkazish oqimi emas, REYTING HAJMI.
/// </summary>
public sealed class CenterLeaderboardScaleTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>Guruh chegarasidan (500) ATAYLAB oshadigan son.</summary>
    private const int BulkStudents = 520;

    /// <summary>Javobdagi chegara — <c>LeaderboardService.CenterTopRows</c>.</summary>
    private const int ExpectedTopRows = 100;

    private const string Period = "2026-05";

    [Fact]
    public async Task CenterBoard_WithMoreThanGroupLimitStudents_ReturnsTopRowsAndOwnRow()
    {
        var world = await WorldBuilder.CreateAsync(factory, "cscale");

        await AddScoredStudentsAsync();

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(
            new Uri($"/api/v1/leaderboard/center?period={Period}", UriKind.Relative));

        // ★ ENG MUHIM TASDIQ: 409 EMAS.
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var board = (await response.Content.ReadFromJsonAsync<CenterBoardBody>())!;

        // 520 ta yaratilgan + seed qilingan "Demo O'quvchi" + dunyo o'quvchisi.
        board.StudentCount.Should().Be(BulkStudents + 2);

        board.TopCount.Should().Be(ExpectedTopRows);
        board.Rows.Should().HaveCount(ExpectedTopRows, "javob TOP-N gacha qisqartiriladi");

        board.Rows[0].Rank.Should().Be(1);
        board.Rows[^1].Rank.Should().Be(ExpectedTopRows);

        // ── so'rovchining qatori ────────────────────────────────────────
        //
        // Uning hech qanday natijasi yo'q (ball 0), ya'ni u ro'yxatning
        // OXIRIDA — seed qilingan "Demo O'quvchi" bilan TENG ballda,
        // shuning uchun ikkalasi ham AYNI o'rinni oladi (musobaqa tartibi).
        board.Rows.Should().NotContain(r => r.StudentId == world.Student.Id,
            "u yuqori yuzlikka kirmaydi");

        board.Me.Should().NotBeNull("o'z qatoring jadvaldan tashqarida bo'lsa ham yuboriladi");
        board.Me!.StudentId.Should().Be(world.Student.Id);
        board.Me.IsMe.Should().BeTrue();
        board.Me.Total.Should().Be(0m);

        // 520 ta ballli o'quvchi 1..520 o'rinlarni oladi -> keyingisi 521.
        board.Me.Rank.Should().Be(BulkStudents + 1,
            "o'rin TO'LIQ ro'yxatdan olinadi, kesilganidan emas");

        // ── `/me?scope=Center` AYNI JAVOBNI BERADI ─────────────────────
        //
        // ★ AYNI TESTNING ICHIDA — ATAYLAB ALOHIDA `[Fact]` EMAS. Sinf
        //   ichidagi ikki test bitta bazani va bitta Redis prefiksini
        //   bo'lishadi: ikkinchi test 520 ta yangi o'quvchi qo'shsa,
        //   birinchisining "aynan 522" tasdig'i tasodifiy tartibga
        //   bog'lanib qolardi. Bitta test — bitta aniq holat.
        var rank = await client.GetFromJsonAsync<MyRankBody>(
            $"/api/v1/leaderboard/me?scope=Center&period={Period}");

        rank!.Scope.Should().Be("Center");
        rank.GroupId.Should().BeNull();
        rank.StudentCount.Should().Be(board.StudentCount);
        rank.Me!.Rank.Should().Be(board.Me.Rank,
            "ikki endpoint bitta hisobdan (va bitta keshdan) oziqlanadi");
    }

    /// <summary>
    /// Ballari TURLICHA bo'lgan <see cref="BulkStudents"/> ta o'quvchi.
    ///
    /// ★ BALLAR ATAYLAB TAKRORLANMAYDI (test bali <c>i</c> / 1000, ya'ni
    ///   0.1% qadam bilan): teng ballda o'rinlar birlashib ketardi va
    ///   "521-o'rin" tasdig'i ma'nosiz bo'lardi.
    ///
    /// ★ BU O'QUVCHILAR HECH QAYSI GURUHDA EMAS — davomat mezoni ularda
    ///   <c>null</c>, ya'ni yakuniy ball faqat test foizidan iborat.
    ///   Bu markaz jadvalining yana bir qoidasini isbotlaydi: guruhsiz
    ///   o'quvchi reytingdan CHIQARILMAYDI.
    ///
    /// ★ EMAIL PREFIKSI TASODIFIY — sinf o'z bazasini olsa ham, unikal
    ///   indeks bilan to'qnashish ehtimoli qolmasin.
    /// </summary>
    private async Task AddScoredStudentsAsync() =>
        await factory.WithDbAsync(async db =>
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];

            var test = new Test
            {
                Title = "Markaz testi " + suffix,
                Kind = TestKind.Competition,
                IsPublished = true,
            };

            db.Tests.Add(test);
            await db.SaveChangesAsync();

            var students = new List<User>(BulkStudents);

            for (var i = 1; i <= BulkStudents; i++)
            {
                var index = i.ToString("D4", CultureInfo.InvariantCulture);

                students.Add(new User
                {
                    FullName = $"Ommaviy {index}",
                    Email = $"bulk-{suffix}-{index}@zinnur.uz",
                    // Bu hisoblar hech qachon KIRMAYDI — hash faqat
                    // ustunni to'ldirish uchun.
                    PasswordHash = "x",
                    Role = UserRole.Student,
                    IsActive = true,
                });
            }

            db.Users.AddRange(students);
            await db.SaveChangesAsync();

            var submittedAt = new DateTimeOffset(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

            for (var i = 0; i < students.Count; i++)
            {
                db.TestAttempts.Add(new TestAttempt
                {
                    TestId = test.Id,
                    StudentId = students[i].Id,
                    Status = AttemptStatus.Submitted,
                    Score = i + 1,
                    MaxScore = 1000m,
                    StartedAt = submittedAt.AddMinutes(-30),
                    SubmittedAt = submittedAt,
                });
            }

            await db.SaveChangesAsync();
            return 0;
        });

    private sealed record CenterBoardBody(
        string Period,
        int StudentCount,
        int TopCount,
        RowBody? Me,
        List<RowBody> Rows);

    private sealed record MyRankBody(
        string Scope,
        long? GroupId,
        string? GroupName,
        string Period,
        int StudentCount,
        RowBody? Me);

    private sealed record RowBody(
        long StudentId,
        string StudentName,
        int Rank,
        decimal Total,
        decimal? AttendancePercent,
        decimal? AssignmentPercent,
        decimal? TestPercent,
        bool IsMe);
}
