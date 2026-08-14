using System.Net;
using System.Net.Http.Json;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// 🔴 O'QUVCHINING O'Z DARS BAHOLARI — <c>GET /api/v1/progress/lesson-grades</c>
/// ========================================================================
///
/// R24 bilan ustoz har DARSGA baho qo'yadigan bo'ldi, lekin o'quvchi uchun
/// o'z bahosini o'qiydigan yo'l YO'Q edi: u faqat reyting ekranidagi
/// yig'ma <c>lessonPercent</c> ni ko'rardi. Bu fayl o'sha yangi yo'lni va
/// undan ham muhimi — UNING CHEGARASINI qo'riqlaydi.
///
/// Isbotlanadigan qoidalar:
///   • 🔴 o'quvchi FAQAT O'ZINING bahosini oladi — bir darsda ikki o'quvchi
///     baholangan bo'lsa ham javobda BITTA qator bo'ladi;
///   • begona guruh Id'si — 403 (bo'sh ro'yxat EMAS: farqni yashirish
///     "ruxsat bor, ma'lumot yo'q" degan yolg'on xulosaga olib kelardi);
///   • sana oralig'i tashqarisidagi dars javobga TUSHMAYDI (reyting
///     ekrani varaqani AYNI oy bo'yicha so'raydi);
///   • tokensiz — 401.
///
/// ★ BAHOLAR HAQIQIY YO'L BILAN (ustoz endpointi orqali) QO'YILADI —
///   bazaga to'g'ridan-to'g'ri yozilmaydi. Shu tufayli test ikki tomon
///   AYNI ma'lumotni ko'rishini ham isbotlaydi.
/// </summary>
public sealed class MyLessonGradesEndpointTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>2026-05-14 14:00 UTC = 19:00 Toshkent.</summary>
    private static readonly DateTimeOffset MayEvening =
        new(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

    /// <summary>Boshqa OYdagi dars — sana filtri uchun.</summary>
    private static readonly DateTimeOffset JuneEvening =
        new(2026, 6, 10, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 BEGONA BAHO JAVOBGA TUSHMAYDI
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Ikkala o'quvchi AYNI darsda baholangan. O'quvchi so'rov yuborganda
    /// javobda faqat O'ZINIKI bo'lishi kerak — aks holda reyting jadvali
    /// ko'rsatmaydigan ma'lumot (boshqa bolaning bahosi va ustoz izohi)
    /// shu endpoint orqali oshkor bo'lardi.
    /// </summary>
    [Fact]
    public async Task Mine_WhenAnotherStudentIsGradedToo_ReturnsOnlyOwnRow()
    {
        var world = await WorldBuilder.CreateAsync(factory, "mygrade");
        var other = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "mygrade2");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await GradeAsync(world, sessionId, world.Student.Id, score: 4, comment: "meniki");
        await GradeAsync(world, sessionId, other.Id, score: 2, comment: "begona");

        var mine = await MineAsync(world.Student);

        mine.Items.Should().ContainSingle().Which.Should().Match<GradeItem>(
            item => item.SessionId == sessionId
                 && item.Score == 4m
                 && item.Comment == "meniki");

        mine.GradedCount.Should().Be(1);

        // 5 ballik standart shkala: 4 / 5 = 80%.
        mine.AveragePercent.Should().Be(80m);
        mine.Items[0].MaxScore.Should().Be(mine.DefaultMaxScore);
        mine.Items[0].Percent.Should().Be(80m);

        // Ustozning ismi ko'rinadi — "bahoni kim qo'ydi" savoliga javob.
        mine.Items[0].GradedByName.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Sana oralig'i: mayda baholangan dars KIRADI, iyundagi CHIQADI.
    ///
    /// ★ NIMA UCHUN MUHIM: reyting varaqasi ro'yxatni AYNI oy bo'yicha
    ///   so'raydi va uning yonida o'sha oyning yig'ma foizi turadi. Filtr
    ///   ishlamasa ro'yxat va foiz bir-biriga ZID ko'rinardi.
    /// </summary>
    [Fact]
    public async Task Mine_WithDateRange_ExcludesLessonsOutsideIt()
    {
        var world = await WorldBuilder.CreateAsync(factory, "mygrng");

        var may = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        var june = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, JuneEvening);

        await GradeAsync(world, may, world.Student.Id, score: 5, comment: null);
        await GradeAsync(world, june, world.Student.Id, score: 3, comment: null);

        var all = await MineAsync(world.Student);
        all.Items.Should().HaveCount(2, "oraliqsiz so'rov butun tarixni beradi");

        var mayOnly = await MineAsync(world.Student, "?from=2026-05-01&to=2026-05-31");

        mayOnly.Items.Should().ContainSingle().Which.SessionId.Should().Be(may);
        mayOnly.AveragePercent.Should().Be(100m);
    }

    /// <summary>
    /// Begona guruh Id'si — 403.
    ///
    /// ★ BO'SH RO'YXAT EMAS: "ruxsat bor, lekin ma'lumot yo'q" degan
    ///   yolg'on xulosa berardi. Qoida davomat xulosasidagi bilan AYNI.
    /// </summary>
    [Fact]
    public async Task Mine_WithForeignGroupId_ReturnsForbidden()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "mygrmy");
        var stranger = await WorldBuilder.CreateAsync(factory, "mygrfr");

        using var client = await WorldBuilder.ClientAsync(factory, mine.Student);

        var response = await client.GetAsync(MineUri($"?groupId={stranger.GroupId}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Buzuq oraliq (`from > to`) — 400.</summary>
    [Fact]
    public async Task Mine_WithReversedRange_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "mygrbad");

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(MineUri("?from=2026-06-01&to=2026-05-01"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Tokensiz — 401 ("havolani bilish" hech nima bermaydi).</summary>
    [Fact]
    public async Task Mine_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(MineUri(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= yordamchi

    private async Task<MyGrades> MineAsync(TestUser student, string query = "")
    {
        using var client = await WorldBuilder.ClientAsync(factory, student);

        var response = await client.GetAsync(MineUri(query));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<MyGrades>())!;
    }

    /// <summary>Ustoz nomidan baho qo'yadi (haqiqiy yo'l).</summary>
    private async Task GradeAsync(
        StudentWorld world, long sessionId, long studentId, decimal score, string? comment)
    {
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{studentId}",
            new { score, maxScore = (decimal?)null, comment });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
    }

    private static Uri MineUri(string query) =>
        new("/api/v1/progress/lesson-grades" + query, UriKind.Relative);

    private sealed record MyGrades(
        decimal DefaultMaxScore,
        int GradedCount,
        decimal? AveragePercent,
        List<GradeItem> Items);

    private sealed record GradeItem(
        long SessionId,
        string? Title,
        string Type,
        decimal Score,
        decimal MaxScore,
        decimal Percent,
        string? Comment,
        string? GradedByName);
}
