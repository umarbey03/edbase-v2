using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// DAVOMATNI QO'LDA TUZATISH — ustoz panelidagi "Davomat" tabi
/// ========================================================================
///
/// Ikki narsa isbotlanadi:
///
///  1) RUXSAT. O'quvchi o'z bahosini O'ZGARTIRA OLMAYDI va begona ustoz
///     boshqa guruhning davomatiga tegа olmaydi. Bu shunchaki "ma'lumot
///     ko'rinmasin" emas: davomat foizi reytingga va ogohlantirishlarga
///     kiradi, ya'ni uni o'quvchi o'zgartira olsa butun hisob ma'nosini
///     yo'qotadi.
///
///  2) IZ. Har tuzatish auditga tushadi (kim, qachon, nimadan-nimaga) va
///     asosiy o'zgarish bilan BIR tranzaksiyada saqlanadi.
/// </summary>
public sealed class SessionAttendanceEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>2026-05-14 14:00 UTC = 19:00 Toshkent.</summary>
    private static readonly DateTimeOffset MayEvening =
        new(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

    // ================================================================= varaqni o'qish

    /// <summary>
    /// ★ YOZUVI YO'Q o'quvchi ham varaqda KO'RINADI (<c>status: null</c>).
    /// Aks holda ustoz uni belgilay olmasdi — qatorni umuman ko'rmaydi,
    /// va aynan xonaga kira olmagan o'quvchini tuzatish kerak bo'ladi.
    /// </summary>
    [Fact]
    public async Task Sheet_IncludesMemberWithoutAnyAttendanceRecord()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shnorec");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>());

        var sheet = await SheetAsync(world.Teacher, sessionId);

        sheet.SessionId.Should().Be(sessionId);
        sheet.GroupId.Should().Be(world.GroupId);
        sheet.CanEdit.Should().BeTrue();

        var row = sheet.Rows.Should().ContainSingle().Subject;

        row.StudentId.Should().Be(world.Student.Id);
        row.Status.Should().BeNull("o'quvchi xonaga umuman kirmagan");
        row.IsManual.Should().BeFalse();
        row.EditedByName.Should().BeNull();
    }

    [Fact]
    public async Task Sheet_ReturnsAutomaticMeasurementForStudentWhoJoined()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shauto");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Present,
            });

        var sheet = await SheetAsync(world.Teacher, sessionId);

        var row = sheet.Rows.Should().ContainSingle().Subject;

        row.Status.Should().Be(nameof(AttendanceStatus.Present));
        row.IsManual.Should().BeFalse("o'lchov platformadan keldi");
        row.DurationSeconds.Should().Be(3600);
    }

    /// <summary>Kurator ham o'z guruhining davomat varag'ini ko'radi.</summary>
    [Fact]
    public async Task Sheet_ForCurator_ReturnsOk()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shcur");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        var sheet = await SheetAsync(world.Curator, sessionId);

        sheet.Rows.Should().ContainSingle();
    }

    // ================================================================= ruxsat

    /// <summary>
    /// ★★ O'QUVCHI VARAQNI UMUMAN KO'RMAYDI.
    ///
    /// Bu nafaqat "boshqalarning bahosi ko'rinmasin" — varaqqa kirish
    /// tuzatish endpointining ham darvozasi. O'quvchi o'z bahosini
    /// "kelgan" qilib qo'ysa, davomat foizi, reyting va qarzdorlik
    /// ogohlantirishlari yolg'on bo'lardi.
    /// </summary>
    [Fact]
    public async Task Sheet_ForStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shstud");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/attendance", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>★★ O'quvchi O'Z bahosini ham TUZATA OLMAYDI.</summary>
    [Fact]
    public async Task Update_ByStudentOnOwnRow_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shstudput");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Absent,
            });

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{world.Student.Id}",
            new { status = nameof(AttendanceStatus.Present) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // ★ Baza HAM o'zgarmagan bo'lishi kerak — 403 qaytib, yozuv
        //   baribir tuzatilgan bo'lsa, tekshiruv hech nima isbotlamasdi.
        var status = await factory.WithDbAsync(db => db.Attendances
            .Where(a => a.SessionId == sessionId && a.StudentId == world.Student.Id)
            .Select(a => a.Status)
            .FirstAsync());

        status.Should().Be(AttendanceStatus.Absent);
    }

    /// <summary>★ Begona guruhning ustozi — 403 (bo'sh varaq emas).</summary>
    [Fact]
    public async Task Sheet_ForTeacherOfAnotherGroup_ReturnsForbidden()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "shmine");
        var other = await WorldBuilder.CreateAsync(factory, "shother");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, mine.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, other.Teacher);

        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/attendance", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_ByTeacherOfAnotherGroup_ReturnsForbidden()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "shxmine");
        var other = await WorldBuilder.CreateAsync(factory, "shxother");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, mine.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, other.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{mine.Student.Id}",
            new { status = nameof(AttendanceStatus.Present) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>O'quv bo'limi HAR QANDAY guruhning davomatini tuzata oladi.</summary>
    [Fact]
    public async Task Update_ByAcademic_Succeeds()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shacad");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, "shacad");

        var row = await UpdateAsync(
            academic, sessionId, world.Student.Id, AttendanceStatus.Late, "kechikib keldi");

        row.Status.Should().Be(nameof(AttendanceStatus.Late));
        row.IsManual.Should().BeTrue();
    }

    [Fact]
    public async Task Sheet_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/v1/live-sessions/1/attendance", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= tuzatish

    /// <summary>
    /// ★ Yozuvi UMUMAN yo'q o'quvchini "kelgan" deb belgilash — asosiy
    /// stsenariy (interneti uzilib qolgan o'quvchi).
    /// </summary>
    [Fact]
    public async Task Update_ForStudentWithNoRecord_CreatesManualRow()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shnew");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>());

        var row = await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id,
            AttendanceStatus.Present, "interneti uzildi, telefonda tingladi");

        row.StudentId.Should().Be(world.Student.Id);
        row.Status.Should().Be(nameof(AttendanceStatus.Present));
        row.IsManual.Should().BeTrue();
        row.Reason.Should().Be("interneti uzildi, telefonda tingladi");
        row.EditedById.Should().Be(world.Teacher.Id);

        // ★ O'LCHOV TO'QILMAYDI: qator qo'lda yaratilgan, ya'ni o'quvchi
        //   xonada 0 soniya bo'lgan. "Present, 0 daqiqa" — ziddiyat emas,
        //   bu ustozning QARORI va platformaning O'LCHOVI yonma-yon.
        row.DurationSeconds.Should().Be(0);
        row.FirstJoinAt.Should().BeNull();
    }

    /// <summary>★★ Tuzatish avtomatik O'LCHOVNI buzmaydi.</summary>
    [Fact]
    public async Task Update_OverAutomaticRecord_KeepsMeasuredDuration()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shkeep");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Present,
            });

        var row = await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id,
            AttendanceStatus.Absent, "boshqa o'quvchining kompyuteridan kirgan");

        row.Status.Should().Be(nameof(AttendanceStatus.Absent));
        row.DurationSeconds.Should().Be(3600, "o'lchov faktik ma'lumot — baho uni o'zgartirmaydi");
    }

    /// <summary>Tuzatilgan qator VARAQDA ham tuzatilgan holida ko'rinadi.</summary>
    [Fact]
    public async Task Sheet_AfterUpdate_ShowsManualFlagReasonAndEditor()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shafter");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>());

        await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id, AttendanceStatus.Late, "kechikdi");

        var sheet = await SheetAsync(world.Teacher, sessionId);
        var row = sheet.Rows.Should().ContainSingle().Subject;

        row.Status.Should().Be(nameof(AttendanceStatus.Late));
        row.IsManual.Should().BeTrue();
        row.Reason.Should().Be("kechikdi");
        row.EditedById.Should().Be(world.Teacher.Id);
        row.EditedByName.Should().NotBeNullOrEmpty();
        row.EditedAt.Should().NotBeNull();
    }

    /// <summary>
    /// ★ PUT — TO'LIQ ALMASHTIRISH: <c>reason</c> yuborilmasa avvalgi
    /// sabab O'CHADI. Aks holda noto'g'ri sabab qatorga yopishib qolardi.
    /// </summary>
    [Fact]
    public async Task Update_WithoutReason_ClearsThePreviousReason()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shclear");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id, AttendanceStatus.Absent, "kasal");

        var row = await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id, AttendanceStatus.Present, reason: null);

        row.Reason.Should().BeNull();
    }

    // ================================================================= ★ AUDIT IZI

    /// <summary>
    /// ★★ HAR TUZATISH IZ QOLDIRADI: kim, qachon, nimadan-nimaga.
    ///
    /// Eski tizimda qo'lda o'zgartirish qatorning ustiga yozilar va
    /// avvalgi qiymat izsiz yo'qolardi — "nega bu o'quvchida shu dars
    /// 'kelgan' bo'lib turibdi?" degan savolga javob yo'q edi.
    /// </summary>
    [Fact]
    public async Task Update_WritesAuditTrailWithOldAndNewValues()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shaudit");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Absent,
            });

        await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id, AttendanceStatus.Present, "interneti uzildi");

        var audits = await factory.WithDbAsync(db => db.AttendanceAudits
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.Id)
            .ToListAsync());

        var audit = audits.Should().ContainSingle().Subject;

        audit.StudentId.Should().Be(world.Student.Id);
        audit.ActorId.Should().Be(world.Teacher.Id, "KIM");
        audit.OldStatus.Should().Be(AttendanceStatus.Absent, "NIMADAN");
        audit.NewStatus.Should().Be(AttendanceStatus.Present, "NIMAGA");
        audit.OldIsManual.Should().BeFalse("avvalgi qiymat AVTOMATIK o'lchovdan edi");
        audit.NewReason.Should().Be("interneti uzildi");
        audit.CreatedAt.Should().NotBe(default, "QACHON");
        audit.AttendanceId.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// ★ Qator SHU tuzatishda yaratilgan bo'lsa <c>oldStatus = null</c>:
    /// "avval 'kelmagan' edi" bilan "avval YOZUV YO'Q edi" — boshqa-boshqa
    /// hodisa, va nizoda aynan farqi muhim.
    /// </summary>
    [Fact]
    public async Task Update_ForBrandNewRow_RecordsNullOldStatus()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shaudnew");

        var sessionId = await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>());

        await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id, AttendanceStatus.Present, reason: null);

        var audit = await factory.WithDbAsync(db => db.AttendanceAudits
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.Id)
            .FirstAsync());

        audit.OldStatus.Should().BeNull();
        audit.NewStatus.Should().Be(AttendanceStatus.Present);
    }

    /// <summary>Ketma-ket tuzatishlar HAR BIRI alohida iz qoldiradi.</summary>
    [Fact]
    public async Task Update_TwiceInARow_KeepsBothAuditEntries()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shaud2");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id, AttendanceStatus.Absent, "kelmadi");

        await UpdateAsync(
            world.Teacher, sessionId, world.Student.Id, AttendanceStatus.Present, "ma'lumotnoma keltirdi");

        var audits = await factory.WithDbAsync(db => db.AttendanceAudits
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.Id)
            .ToListAsync());

        audits.Should().HaveCount(2);

        audits[1].OldStatus.Should().Be(AttendanceStatus.Absent);
        audits[1].OldIsManual.Should().BeTrue("avvalgi qiymat ham QO'LDA qo'yilgan edi");
        audits[1].OldReason.Should().Be("kelmadi");
        audits[1].NewReason.Should().Be("ma'lumotnoma keltirdi");
    }

    // ================================================================= kiruvchi ma'lumot

    [Fact]
    public async Task Update_WithoutStatus_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shnost");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{world.Student.Id}",
            new { reason = "sababi bor, holati yo'q" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_WithUnknownStatus_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shbadst");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{world.Student.Id}",
            new { status = "Kelgandir" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Juda uzun sabab — 400 (500 EMAS). Baza chegarasiga urilib
    /// `DbUpdateException` bo'lsa, foydalanuvchi "kutilmagan xato"
    /// ko'rardi va nimani tuzatishni bilmasdi.
    /// </summary>
    [Fact]
    public async Task Update_WithTooLongReason_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shlong");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{world.Student.Id}",
            new { status = nameof(AttendanceStatus.Present), reason = new string('x', 301) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>★ Begona o'quvchi Id'si — 404 (jimgina yozib qo'yish YO'Q).</summary>
    [Fact]
    public async Task Update_ForStudentOutsideTheGroup_ReturnsNotFound()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "shoutm");
        var other = await WorldBuilder.CreateAsync(factory, "shouto");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, mine.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, mine.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{other.Student.Id}",
            new { status = nameof(AttendanceStatus.Present) });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Bekor qilingan dars uchun davomat belgilanmaydi: u hisobotlarga
    /// umuman kirmaydi, ya'ni yozuv ko'rinmas bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Update_ForCancelledSession_ReturnsConflict()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shcanc");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening, SessionStatus.Cancelled);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{world.Student.Id}",
            new { status = nameof(AttendanceStatus.Present) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sheet_ForMissingSession_ReturnsNotFound()
    {
        var world = await WorldBuilder.CreateAsync(factory, "shmiss");

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.GetAsync(
            new Uri("/api/v1/live-sessions/999999999/attendance", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================= yordamchi

    private async Task<SheetResponse> SheetAsync(TestUser actor, long sessionId)
    {
        using var client = await WorldBuilder.ClientAsync(factory, actor);

        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/attendance", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<SheetResponse>())!;
    }

    private async Task<RowResponse> UpdateAsync(
        TestUser actor,
        long sessionId,
        long studentId,
        AttendanceStatus status,
        string? reason)
    {
        using var client = await WorldBuilder.ClientAsync(factory, actor);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/attendance/{studentId}",
            new { status = status.ToString(), reason });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<RowResponse>())!;
    }

    /// <summary>
    /// Javob shakli ATAYLAB qo'lda yozilgan (servis DTO'siga havola
    /// qilinmagan): shunda DTO o'zgarsa test yiqiladi va frontend bilan
    /// kelishilgan shartnoma buzilgani darhol ma'lum bo'ladi.
    /// </summary>
    private sealed record SheetResponse(
        long SessionId,
        long GroupId,
        string GroupName,
        string? Title,
        string Type,
        string Status,
        DateTimeOffset ScheduledStart,
        DateTimeOffset ScheduledEnd,
        bool CanEdit,
        List<RowResponse> Rows);

    /// <summary>
    /// <c>Status</c> ATAYLAB <c>string?</c>: JSON'da u enum EMAS, SATR
    /// bo'lib chiqishi shart (<c>"Present"</c>), aks holda frontend
    /// raqam olib qolardi.
    /// </summary>
    private sealed record RowResponse(
        long StudentId,
        string StudentName,
        string? Status,
        bool IsManual,
        string? Reason,
        DateTimeOffset? FirstJoinAt,
        DateTimeOffset? LeftAt,
        int DurationSeconds,
        long? EditedById,
        string? EditedByName,
        DateTimeOffset? EditedAt);
}
