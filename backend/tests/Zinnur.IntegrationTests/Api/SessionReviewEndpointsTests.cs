using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// DARS SIFATI TAHLILI (R29 / R30) — <c>/api/v1/live-sessions/{id}/review</c>
/// ========================================================================
///
/// R29: o'quv bo'limi yozuvlar ro'yxatidagi darsga xulosa qo'yadi.
/// R30: ustoz AYNI xulosani "Darslarim" bo'limidan o'qiydi.
///
/// Testlar UCH chegarani qo'riqlaydi:
///
///   1) 🔴 O'QUVCHI HECH QACHON KO'RMAYDI. Matn ustoz haqidagi ichki baho
///      ("tushuntirish sust") va u o'quvchiga yetsa qaytarib bo'lmaydi.
///      Bu — funksional talab emas, VOSITANING ISHLASH SHARTI: ko'rinadigan
///      baho yozilmaydigan bahoga aylanadi.
///
///   2) 🔴 USTOZ FAQAT O'QIYDI. U sifat nazoratining OBYEKTI: tahrirlay
///      olsa "Muammo bor" ni "Tasdiqlandi" ga aylantirib qo'yardi.
///
///   3) 🔴 BEGONA GURUHNING USTOZI KO'RMAYDI. R30 "o'zining dars tahlili"
///      deydi — hamkasbining bahosi emas.
///
/// ★ TAHLIL DARSGA BOG'LANGAN, YOZUVGA EMAS — pastdagi
///   <see cref="Review_Survives_WhenTheRecordingIsReplaced"/> aynan shu
///   qarorni qulflaydi.
/// </summary>
public sealed class SessionReviewEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= 1) CRUD

    [Fact]
    public async Task Review_WrittenByAcademic_IsReadableAndCarriesTheAuthor()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-crud");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var academic = await AcademicClientAsync("tahlil-crud");

        var saved = await SaveAsync(academic, sessionId, "HasIssue", "Vaqt sust taqsimlangan.");

        saved.Verdict.Should().Be("HasIssue");
        saved.Conclusion.Should().Be("Vaqt sust taqsimlangan.");
        saved.AuthorName.Should().NotBeNullOrEmpty();
        saved.CanEdit.Should().BeTrue();
    }

    /// <summary>
    /// UPSERT: ikkinchi <c>PUT</c> yangi qator YARATMAYDI, mavjudini
    /// yangilaydi (bitta darsda bitta tahlil — unikal indeks).
    /// </summary>
    [Fact]
    public async Task Review_SavedTwice_IsUpdatedNotDuplicated()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-upsert");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var academic = await AcademicClientAsync("tahlil-upsert");

        var first = await SaveAsync(academic, sessionId, "NotReviewed", "Qoralama.");
        var second = await SaveAsync(academic, sessionId, "Approved", "Yakuniy xulosa.");

        second.Id.Should().Be(first.Id, "bitta darsda bitta tahlil bo'ladi");
        second.Verdict.Should().Be("Approved");
        second.UpdatedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Tahlil yo'q bo'lsa <c>200</c> va JSON <c>null</c> — 404 EMAS.
    /// "Hali yozilmagan" normal holat, xato emas.
    /// </summary>
    [Fact]
    public async Task MissingReview_Returns200WithNull_NotAnError()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-yoq");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.GetAsync(Uri(sessionId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    [Fact]
    public async Task Review_Deleted_DisappearsAndDeleteIsIdempotent()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-ochir");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var academic = await AcademicClientAsync("tahlil-ochir");
        await SaveAsync(academic, sessionId, "Approved", "O'chiriladi.");

        var first = await academic.DeleteAsync(Uri(sessionId));
        first.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(first));

        // IDEMPOTENT: ikkinchi urinish ham 204 — holat allaqachon
        // so'ralganidek.
        var second = await academic.DeleteAsync(Uri(sessionId));
        second.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(second));
    }

    [Fact]
    public async Task Review_WithEmptyConclusion_IsRejected()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-bosh");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var academic = await AcademicClientAsync("tahlil-bosh");

        var response = await academic.PutAsJsonAsync(
            Uri(sessionId), new { verdict = "Approved", conclusion = "   " });

        // Bo'sh xulosa — Domain qoidasi (`DomainException` -> 409).
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(response));
    }

    // ================================================================= 2) RUXSAT

    /// <summary>
    /// 🔴 BU TESTNI HECH QACHON YUMSHATMANG.
    ///
    /// O'quvchi guruh a'zosi, ya'ni darsning O'ZINI ko'ra oladi
    /// (<c>ILiveSessionService.GetAsync</c> undan o'tkazadi). Aynan shuning
    /// uchun tahlil o'sha darvozaga ULANMAGAN — u alohida, qat'iyroq
    /// tekshiruvga ega.
    /// </summary>
    [Fact]
    public async Task Student_IsAlwaysForbidden_EvenForTheirOwnLesson()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-talaba");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var academic = await AcademicClientAsync("tahlil-talaba");
        await SaveAsync(academic, sessionId, "HasIssue", "Ichki baho.");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var read = await student.GetAsync(Uri(sessionId));
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(read));

        // Javob tanasida tahlil matnining ZARRASI ham bo'lmasligi kerak.
        (await read.Content.ReadAsStringAsync()).Should().NotContain("Ichki baho");

        var write = await student.PutAsJsonAsync(
            Uri(sessionId), new { verdict = "Approved", conclusion = "Men yozdim" });

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(write));
    }

    /// <summary>R30: ustoz O'Z darsining tahlilini KO'RADI.</summary>
    [Fact]
    public async Task Teacher_CanReadTheReviewOfTheirOwnLesson()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-ustoz");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var academic = await AcademicClientAsync("tahlil-ustoz");
        await SaveAsync(academic, sessionId, "Approved", "Yaxshi olib borildi.");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var dto = await teacher.GetFromJsonAsync<ReviewResponse>(Uri(sessionId));

        dto!.Conclusion.Should().Be("Yaxshi olib borildi.");

        // ⚠️ LEKIN TAHRIRLAY OLMAYDI: u sifat nazoratining obyekti.
        dto.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task Teacher_CannotWriteAReview()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-yozolmas");
        var sessionId = await AddSessionAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PutAsJsonAsync(
            Uri(sessionId), new { verdict = "Approved", conclusion = "O'zimni tasdiqlayman" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));
    }

    /// <summary>R30 "O'ZINING dars tahlili" deydi — hamkasbining emas.</summary>
    [Fact]
    public async Task Teacher_CannotReadAnotherTeachersLesson()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "tahlil-meniki");
        var foreign = await WorldBuilder.CreateAsync(factory, "tahlil-begona");

        var foreignSessionId = await AddSessionAsync(foreign.GroupId);

        using var academic = await AcademicClientAsync("tahlil-begona");
        await SaveAsync(academic, foreignSessionId, "HasIssue", "Begona baho.");

        using var teacher = await WorldBuilder.ClientAsync(factory, mine.Teacher);

        var response = await teacher.GetAsync(Uri(foreignSessionId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));
    }

    // ================================================================= 3) DARSGA BOG'LANISH

    /// <summary>
    /// 🔴 ENTITY QARORINI QULFLAYDIGAN TEST.
    ///
    /// Bir dars ikki marta yozib olinsa (birinchi urinish yiqilib,
    /// ikkinchisi ishlaganda) tahlil O'RNIDA QOLADI va IKKALA yozuvda
    /// ham ko'rinadi. Tahlil YOZUVGA bog'langan bo'lsa, yiqilgan yozuv
    /// uni o'zi bilan olib ketardi va ro'yxatda hech qayerda
    /// ko'rinmasdi.
    /// </summary>
    [Fact]
    public async Task Review_Survives_WhenTheRecordingIsReplaced()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-qayta");
        var sessionId = await AddSessionAsync(world.GroupId);

        // Birinchi urinish — yiqilgan.
        await AddRecordingAsync(sessionId, RecordingStatus.Failed);

        using var academic = await AcademicClientAsync("tahlil-qayta");
        await SaveAsync(academic, sessionId, "HasIssue", "Sekin tushuntirildi.");

        // Ikkinchi urinish — tayyor.
        await AddRecordingAsync(sessionId, RecordingStatus.Completed);

        var response = await academic.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/recordings", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var rows = (await response.Content.ReadFromJsonAsync<List<RecordingBrief>>())!;

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.HasReview && r.ReviewStatus == "HasIssue",
            "tahlil DARSGA bog'langan, ya'ni har ikkala yozuvda ham ko'rinadi");
    }

    /// <summary>
    /// R29 nishoni N+1 so'rovsiz chizilishi uchun xulosa yozuv DTO'sida
    /// keladi; O'QUVCHIGA esa hech qanday ishora bermaydi.
    /// </summary>
    [Fact]
    public async Task ReviewFlags_AreNeverExposedToStudents()
    {
        var world = await WorldBuilder.CreateAsync(factory, "tahlil-nishon");
        var sessionId = await AddSessionAsync(world.GroupId);

        await AddRecordingAsync(sessionId, RecordingStatus.Completed);

        using var academic = await AcademicClientAsync("tahlil-nishon");
        await SaveAsync(academic, sessionId, "HasIssue", "Ichki baho.");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var rows = await student.GetFromJsonAsync<List<RecordingBrief>>(
            new Uri($"/api/v1/live-sessions/{sessionId}/recordings", UriKind.Relative));

        rows.Should().ContainSingle();
        rows![0].HasReview.Should().BeFalse();
        rows[0].ReviewStatus.Should().BeNull();
    }

    // ================================================================= yordamchilar

    private static Uri Uri(long sessionId) =>
        new($"/api/v1/live-sessions/{sessionId}/review", UriKind.Relative);

    private async Task<HttpClient> AcademicClientAsync(string prefix)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, prefix);
        return await WorldBuilder.ClientAsync(factory, academic);
    }

    private static async Task<ReviewResponse> SaveAsync(
        HttpClient client, long sessionId, string verdict, string conclusion)
    {
        var response = await client.PutAsJsonAsync(Uri(sessionId), new { verdict, conclusion });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
    }

    private Task<long> AddSessionAsync(long groupId) =>
        factory.WithDbAsync(async db =>
        {
            var session = new LiveSession
            {
                GroupId = groupId,
                Type = SessionType.Teacher,
                Status = SessionStatus.Ended,
                ScheduledStart = DateTimeOffset.UtcNow.AddHours(-2),
                ScheduledEnd = DateTimeOffset.UtcNow.AddHours(-1),
                ActualStart = DateTimeOffset.UtcNow.AddHours(-2),
                ActualEnd = DateTimeOffset.UtcNow.AddHours(-1),
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        });

    private Task<long> AddRecordingAsync(long sessionId, RecordingStatus status) =>
        factory.WithDbAsync(async db =>
        {
            var recording = new SessionRecording
            {
                SessionId = sessionId,
                ObjectKey = $"itest/{Guid.NewGuid():N}.mp4",
                Status = status,
                DurationSeconds = status == RecordingStatus.Completed ? 3600 : null,
                // ★ 2026-08-15 dan standart `false` (`RecordingVisibilityModelTests`
                // izohi) — bu yerda ATAYLAB `true`: sinf o'quvchi ko'rinishini
                // tekshiradi, yozuv yashirin bo'lsa ro'yxat bo'sh chiqib, sinov
                // maqsadini yo'qotardi.
                IsVisibleToStudents = true,
            };

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();
            return recording.Id;
        });

    private sealed record ReviewResponse(
        long Id, long SessionId, string Verdict, string? Plus, string? Minus, string Conclusion,
        long AuthorId, string AuthorName, bool CanEdit,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record RecordingBrief(long Id, bool HasReview, string? ReviewStatus);
}
