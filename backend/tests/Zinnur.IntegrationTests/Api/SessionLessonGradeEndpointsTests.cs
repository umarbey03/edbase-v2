using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// R24 · DARS BAHOSI — ustoz panelidagi "Baholar" tabi
/// ========================================================================
///
/// Loyiha egasining talabi: "baholar har bitta darsga qo'yiladi" va
/// "guruh studentlari baholari jadval ko'rinishida joylashsin".
///
/// Uch narsa isbotlanadi:
///
///  1) RUXSAT — davomat varag'iniki BILAN AYNI. O'quvchi O'ZIGA baho
///     qo'ya OLMAYDI va begona ustoz boshqa guruhning bahosiga tega
///     olmaydi. Bu shunchaki "ko'rinmasin" emas: dars bahosi oylik
///     reytingga kiradi.
///
///  2) IZ — har o'zgarish auditga tushadi (kim, qachon, nimadan-nimaga)
///     va asosiy o'zgarish bilan BIR tranzaksiyada saqlanadi. Baho
///     O'CHIRILGANDA ham iz QOLADI.
///
///  3) SHARTNOMA — <c>Submission</c> QO'ZG'ATILMAYDI. Dars bahosi
///     topshiriq soni/baholanganlar sonini o'zgartirmasligi kerak, aks
///     holda baholash navbatining sanog'i yolg'on ko'rsatardi.
///
/// 🔴 BU FAYL SXEMA TAYYOR BO'LGUNCHA YIQILADI: `LessonGrades` jadvali
///    umumiy migratsiyada yaratiladi (R24 migratsiyani O'ZI qo'shmaydi —
///    parallel tarmoqlar bitta snapshot faylini qayta yozardi).
/// </summary>
public sealed class SessionLessonGradeEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>2026-05-14 14:00 UTC = 19:00 Toshkent.</summary>
    private static readonly DateTimeOffset MayEvening =
        new(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);

    // ================================================================= varaqni o'qish

    /// <summary>
    /// ★ BAHOSI YO'Q o'quvchi ham varaqda KO'RINADI (<c>score: null</c>).
    /// Aks holda ustoz unga baho qo'ya olmasdi — qatorni umuman ko'rmaydi.
    /// </summary>
    [Fact]
    public async Task Sheet_IncludesMemberWithoutAnyGrade()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgnone");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        var sheet = await SheetAsync(world.Teacher, sessionId);

        sheet.SessionId.Should().Be(sessionId);
        sheet.GroupId.Should().Be(world.GroupId);
        sheet.CanEdit.Should().BeTrue();
        sheet.DefaultMaxScore.Should().Be(LessonGrade.DefaultMaxScore);

        var row = sheet.Rows.Should().ContainSingle().Subject;

        row.StudentId.Should().Be(world.Student.Id);
        row.Score.Should().BeNull("hech kim baholamagan");
        row.Percent.Should().BeNull();
        row.GradedByName.Should().BeNull();
    }

    /// <summary>Kurator ham o'z guruhining baho varag'ini ko'radi.</summary>
    [Fact]
    public async Task Sheet_ForCurator_ReturnsOk()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgcur");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        var sheet = await SheetAsync(world.Curator, sessionId);

        sheet.Rows.Should().ContainSingle();
    }

    // ================================================================= ruxsat

    /// <summary>
    /// ★★ O'QUVCHI VARAQNI UMUMAN KO'RMAYDI — davomat varag'idagi bilan
    /// AYNI qoida. Varaqqa kirish yozish endpointining ham darvozasi.
    /// </summary>
    [Fact]
    public async Task Sheet_ForStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgstud");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/grades", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>★★ O'quvchi O'ZIGA baho qo'ya OLMAYDI.</summary>
    [Fact]
    public async Task Upsert_ByStudentOnOwnRow_ReturnsForbidden_AndWritesNothing()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgstudput");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}",
            new { score = 5m });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // ★ Baza HAM o'zgarmagan bo'lishi kerak — 403 qaytib, yozuv
        //   baribir yaratilgan bo'lsa tekshiruv hech nima isbotlamasdi.
        var exists = await factory.WithDbAsync(db => db.LessonGrades
            .AnyAsync(g => g.SessionId == sessionId));

        exists.Should().BeFalse();
    }

    /// <summary>★ Begona guruhning ustozi — 403 (bo'sh varaq emas).</summary>
    [Fact]
    public async Task Sheet_ForTeacherOfAnotherGroup_ReturnsForbidden()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "lgmine");
        var other = await WorldBuilder.CreateAsync(factory, "lgother");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, mine.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, other.Teacher);

        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/grades", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>O'quv bo'limi HAR QANDAY guruhga baho qo'ya oladi.</summary>
    [Fact]
    public async Task Upsert_ByAcademic_Succeeds()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgacad");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, "lgacad");

        var row = await UpsertAsync(academic, sessionId, world.Student.Id, 4m);

        row.Score.Should().Be(4m);
        row.GradedById.Should().Be(academic.Id);
    }

    [Fact]
    public async Task Sheet_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/v1/live-sessions/1/grades", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= baho qo'yish

    /// <summary>
    /// ★ ASOSIY STSENARIY: "bugungi darsga 5". Maxraj berilmadi —
    /// standart shkala (5) ishlatiladi va foiz 100 chiqadi.
    /// </summary>
    [Fact]
    public async Task Upsert_WithoutMaxScore_UsesDefaultScale()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgdef");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        var row = await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m);

        row.Score.Should().Be(5m);
        row.MaxScore.Should().BeNull("tanlanmagan shkala qatorda saqlanmaydi");
        row.Percent.Should().Be(100m);
        row.GradedById.Should().Be(world.Teacher.Id);
        row.GradedByName.Should().NotBeNullOrEmpty();
        row.GradedAt.Should().NotBeNull();
    }

    /// <summary>Imtihon darsi — 100 ballik shkala.</summary>
    [Fact]
    public async Task Upsert_WithExplicitMaxScore_ComputesPercentFromIt()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgmax");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        var row = await UpsertAsync(
            world.Teacher, sessionId, world.Student.Id, 87m, maxScore: 100m);

        row.MaxScore.Should().Be(100m);
        row.Percent.Should().Be(87m);
    }

    /// <summary>Qayta baholash qatorni ALMASHTIRADI, ikkinchisini yaratmaydi.</summary>
    [Fact]
    public async Task Upsert_Twice_KeepsSingleRow()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgtwice");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 3m);
        var row = await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m);

        row.Score.Should().Be(5m);

        var count = await factory.WithDbAsync(db => db.LessonGrades
            .CountAsync(g => g.SessionId == sessionId && g.StudentId == world.Student.Id));

        count.Should().Be(1, "(SessionId, StudentId) — UNIKAL");
    }

    /// <summary>
    /// ★ PUT — TO'LIQ ALMASHTIRISH: <c>comment</c> yuborilmasa avvalgi
    /// izoh O'CHADI (davomatdagi <c>reason</c> bilan AYNI shartnoma).
    /// </summary>
    [Fact]
    public async Task Upsert_WithoutComment_ClearsThePreviousComment()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgclear");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(
            world.Teacher, sessionId, world.Student.Id, 3m, comment: "uy ishini qilmagan");

        var row = await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m);

        row.Comment.Should().BeNull();
    }

    /// <summary>Qo'yilgan baho VARAQDA ham o'sha holida ko'rinadi.</summary>
    [Fact]
    public async Task Sheet_AfterUpsert_ShowsScoreCommentAndGrader()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgafter");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 4m, comment: "faol");

        var sheet = await SheetAsync(world.Teacher, sessionId);
        var row = sheet.Rows.Should().ContainSingle().Subject;

        row.Score.Should().Be(4m);
        row.Percent.Should().Be(80m);
        row.Comment.Should().Be("faol");
        row.GradedById.Should().Be(world.Teacher.Id);
        row.GradedByName.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// 🔴 ENG MUHIM SHARTNOMA TESTI: dars bahosi `Submission` YARATMAYDI.
    ///
    /// Soxta topshiriq yasalsa `AssignmentDto.submissionCount` va
    /// `gradedCount`, baholash navbatining "kutayotganlar" sanog'i va
    /// reytingdagi vazifa mezoni jimgina buzilardi — aynan shu sabab
    /// `Submission` qayta ishlatilmadi.
    /// </summary>
    [Fact]
    public async Task Upsert_DoesNotCreateAnySubmissionOrAssignment()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgnosub");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m);

        var submissions = await factory.WithDbAsync(db => db.Submissions
            .CountAsync(s => s.StudentId == world.Student.Id));

        var assignments = await factory.WithDbAsync(db => db.Assignments
            .CountAsync(a => a.GroupId == world.GroupId));

        submissions.Should().Be(0);
        assignments.Should().Be(0);
    }

    // ================================================================= ★ AUDIT IZI

    /// <summary>
    /// ★★ HAR O'ZGARISH IZ QOLDIRADI: kim, qachon, nimadan-nimaga.
    /// "Nega bolamda 5 turgan edi, endi 3?" — bu savolga javob shu jadvalda.
    /// </summary>
    [Fact]
    public async Task Upsert_WritesAuditTrailWithOldAndNewValues()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgaudit");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m, comment: "faol");
        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 3m, comment: "adashdim");

        var audits = await AuditsAsync(sessionId);

        audits.Should().HaveCount(2);

        // ★ BIRINCHI yozuvda `OldScore = null`: "bahosi 0 edi" bilan
        //   "bahosi YO'Q edi" — boshqa-boshqa hodisa.
        audits[0].OldScore.Should().BeNull("qator SHU amalda yaratilgan");
        audits[0].NewScore.Should().Be(5m);
        audits[0].ActorId.Should().Be(world.Teacher.Id, "KIM");
        audits[0].CreatedAt.Should().NotBe(default, "QACHON");

        audits[1].OldScore.Should().Be(5m, "NIMADAN");
        audits[1].NewScore.Should().Be(3m, "NIMAGA");
        audits[1].OldComment.Should().Be("faol");
        audits[1].NewComment.Should().Be("adashdim");
    }

    /// <summary>
    /// ★ MAXRAJ HAM IZDA: "3" ning ma'nosi shkalasiz o'qilmaydi
    /// (3/5 va 3/100 — boshqa-boshqa natija).
    /// </summary>
    [Fact]
    public async Task Upsert_RecordsMaxScoreChangeInAudit()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgaudmax");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 3m, maxScore: 5m);
        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 3m, maxScore: 100m);

        var audits = await AuditsAsync(sessionId);

        audits[1].OldMaxScore.Should().Be(5m);
        audits[1].NewMaxScore.Should().Be(100m);
    }

    // ================================================================= o'chirish

    /// <summary>
    /// ★★ O'CHIRISH "0 QO'YISH" EMAS: qator YO'QOLADI, ya'ni reyting uni
    /// umuman hisobga olmaydi. Bu amalsiz adashib qo'yilgan bahoni
    /// tuzatishning yagona yo'li o'quvchiga 0 yozib qo'yish bo'lardi.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheRow_AndSheetShowsNullAgain()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgdel");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.DeleteAsync(new Uri(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));

        var sheet = await SheetAsync(world.Teacher, sessionId);
        var row = sheet.Rows.Should().ContainSingle().Subject;

        row.Score.Should().BeNull("baho o'chirildi — 0 EMAS, YO'Q");
    }

    /// <summary>
    /// ★★ 🔴 O'CHIRISHDAN KEYIN HAM IZ QOLADI.
    ///
    /// Aynan shuning uchun audit jadvali baho qatoriga FK bilan
    /// bog'lanMAGAN: `Cascade` bo'lsa bahoni o'chirish uning butun
    /// tarixini ham o'chirib yuborardi, ya'ni "izsiz yo'qotish" audit
    /// to'sishi kerak bo'lgan holat sukut bo'yicha ishlab turardi.
    /// </summary>
    [Fact]
    public async Task Delete_KeepsAuditTrail_AndRecordsNullNewScore()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgdelaud");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);
        await client.DeleteAsync(new Uri(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}", UriKind.Relative));

        var audits = await AuditsAsync(sessionId);

        audits.Should().HaveCount(2, "qo'yish va o'chirish — ikkala iz ham qoladi");

        audits[1].OldScore.Should().Be(5m);
        audits[1].NewScore.Should().BeNull("`null` = OLIB TASHLANDI, `0` = nol qo'yildi");
        audits[1].ActorId.Should().Be(world.Teacher.Id);
    }

    /// <summary>IDEMPOTENT: bahosi yo'q katakni o'chirish ham 204.</summary>
    [Fact]
    public async Task Delete_WhenNoGradeExists_ReturnsNoContent_AndWritesNoAudit()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgdelnone");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.DeleteAsync(new Uri(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var audits = await AuditsAsync(sessionId);
        audits.Should().BeEmpty("bo'lmagan narsaning o'chirilishi iz emas, SHOVQIN");
    }

    /// <summary>★ O'quvchi bahoni o'chira OLMAYDI.</summary>
    [Fact]
    public async Task Delete_ByStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgdelstud");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 2m);

        using var client = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await client.DeleteAsync(new Uri(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= kiruvchi ma'lumot

    [Fact]
    public async Task Upsert_WithoutScore_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgnosc");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}",
            new { comment = "izoh bor, baho yo'q" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// 🔴 ENG MUHIM TEKSHIRUV: ball maxrajdan katta bo'lolmaydi.
    ///
    /// Himoyasiz 6/5 = 120% bo'lardi va oylik reytingdagi "har mezon
    /// 0..100" invarianti buzilib, yakuniy ball 100 dan oshib ketardi.
    /// </summary>
    [Fact]
    public async Task Upsert_WithScoreAboveMaxScore_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgover");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}",
            new { score = 6m, maxScore = 5m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Maxraj berilmaganda ham chegara STANDART shkala bo'yicha.</summary>
    [Fact]
    public async Task Upsert_WithScoreAboveDefaultScale_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgoverdef");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}",
            new { score = 10m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upsert_WithNegativeScore_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgneg");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}",
            new { score = -1m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Juda uzun izoh — 400 (500 EMAS). Baza chegarasiga urilib
    /// `DbUpdateException` bo'lsa foydalanuvchi "kutilmagan xato"
    /// ko'rardi va nimani tuzatishni bilmasdi.
    /// </summary>
    [Fact]
    public async Task Upsert_WithTooLongComment_ReturnsBadRequest()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lglong");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}",
            new
            {
                score = 5m,
                comment = new string('x', LessonGrade.MaxCommentLength + 1),
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>★ Begona o'quvchi Id'si — 404 (jimgina yozib qo'yish YO'Q).</summary>
    [Fact]
    public async Task Upsert_ForStudentOutsideTheGroup_ReturnsNotFound()
    {
        var mine = await WorldBuilder.CreateAsync(factory, "lgoutm");
        var other = await WorldBuilder.CreateAsync(factory, "lgouto");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, mine.GroupId, MayEvening);

        using var client = await WorldBuilder.ClientAsync(factory, mine.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{other.Student.Id}",
            new { score = 5m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Bekor qilingan dars uchun baho qo'yilmaydi: u o'tilmagan, ya'ni
    /// baholanadigan ish ham yo'q.
    /// </summary>
    [Fact]
    public async Task Upsert_ForCancelledSession_ReturnsConflict()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgcanc");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening, SessionStatus.Cancelled);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}",
            new { score = 5m });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// ★ BEKOR QILINGAN DARSDAN BAHONI O'CHIRISH MUMKIN (qo'yish esa
    /// mumkin emas): dars baholangandan KEYIN bekor qilinishi mumkin va
    /// endi ma'nosiz bo'lib qolgan bahoni olib tashlash kerak bo'ladi.
    /// </summary>
    [Fact]
    public async Task Delete_ForCancelledSession_StillWorks()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgcancdel");

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 5m);

        await factory.WithDbAsync(async db =>
        {
            var session = await db.LiveSessions.FirstAsync(s => s.Id == sessionId);
            session.Status = SessionStatus.Cancelled;
            await db.SaveChangesAsync();
            return 0;
        });

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.DeleteAsync(new Uri(
            $"/api/v1/live-sessions/{sessionId}/grades/{world.Student.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));
    }

    [Fact]
    public async Task Sheet_ForMissingSession_ReturnsNotFound()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgmiss");

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.GetAsync(
            new Uri("/api/v1/live-sessions/999999999/grades", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================= reyting

    /// <summary>
    /// ★★ QAROR TESTI (R24): DARS BAHOSI OYLIK REYTINGGA KIRADI.
    ///
    /// Bu jimgina yo'qolishi mumkin bo'lgan bog'lanish: ustoz har kuni
    /// baho qo'yadi, reyting esa ularni umuman ko'rmasa jadval yolg'on
    /// gapirardi. Test aynan shu bog'lanishni qotiradi.
    ///
    /// Stsenariy: mayda BITTA dars, unga 5/5 baho. Boshqa mezon yo'q,
    /// ya'ni yakuniy ball MEZONLAR O'RTACHASI qoidasi bo'yicha aynan
    /// o'sha foizga teng bo'lishi kerak.
    /// </summary>
    [Fact]
    public async Task Leaderboard_CountsLessonGrade_AsItsOwnCriterion()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgboard");

        // Dars `Scheduled` — davomat mezoni MAXRAJI 0 bo'lib qoladi
        // (u faqat `Ended` darslarni sanaydi), ya'ni jadvalda YAGONA
        // mezon dars bahosi bo'ladi va tekshiruv aniq chiqadi.
        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, MayEvening);

        await UpsertAsync(world.Teacher, sessionId, world.Student.Id, 4m);

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.GetAsync(new Uri(
            $"/api/v1/leaderboard/groups/{world.GroupId}?period=2026-05", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var board = (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
        var row = board.Rows.Should().ContainSingle().Subject;

        row.LessonPercent.Should().Be(80m, "4 / 5 = 80%");
        row.Total.Should().Be(80m, "boshqa mezon yo'q — o'rtacha aynan shu");
    }

    /// <summary>
    /// ★ ORQAGA MOSLIK: dars bahosi YO'Q guruhda mezon <c>null</c> va
    /// yakuniy ball AVVALGIDEK hisoblanadi (mezon o'rtachaga kirmaydi).
    /// </summary>
    [Fact]
    public async Task Leaderboard_WithoutLessonGrades_LeavesCriterionNull()
    {
        var world = await WorldBuilder.CreateAsync(factory, "lgboardno");

        await WorldBuilder.AddEndedSessionAsync(
            factory, world.GroupId, MayEvening, SessionType.Teacher,
            new Dictionary<long, AttendanceStatus>
            {
                [world.Student.Id] = AttendanceStatus.Present,
            });

        using var client = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await client.GetAsync(new Uri(
            $"/api/v1/leaderboard/groups/{world.GroupId}?period=2026-05", UriKind.Relative));

        var board = (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
        var row = board.Rows.Should().ContainSingle().Subject;

        row.LessonPercent.Should().BeNull();
        row.Total.Should().Be(100m, "faqat davomat mezoni bor va u 100%");
    }

    // ================================================================= yordamchi

    private Task<List<LessonGradeAudit>> AuditsAsync(long sessionId) =>
        factory.WithDbAsync(db => db.LessonGradeAudits
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.Id)
            .ToListAsync());

    private async Task<SheetResponse> SheetAsync(TestUser actor, long sessionId)
    {
        using var client = await WorldBuilder.ClientAsync(factory, actor);

        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/grades", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<SheetResponse>())!;
    }

    private async Task<RowResponse> UpsertAsync(
        TestUser actor,
        long sessionId,
        long studentId,
        decimal score,
        decimal? maxScore = null,
        string? comment = null)
    {
        using var client = await WorldBuilder.ClientAsync(factory, actor);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/live-sessions/{sessionId}/grades/{studentId}",
            new { score, maxScore, comment });

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
        decimal DefaultMaxScore,
        bool CanEdit,
        List<RowResponse> Rows);

    private sealed record RowResponse(
        long StudentId,
        string StudentName,
        decimal? Score,
        decimal? MaxScore,
        decimal? Percent,
        string? Comment,
        long? GradedById,
        string? GradedByName,
        DateTimeOffset? GradedAt);

    private sealed record BoardResponse(List<BoardRow> Rows);

    private sealed record BoardRow(long StudentId, decimal Total, decimal? LessonPercent);
}
