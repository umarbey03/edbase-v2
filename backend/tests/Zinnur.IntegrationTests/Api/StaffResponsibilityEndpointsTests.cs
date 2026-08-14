using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Staffing;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// R33 + R40 — «KIM MAS'UL» TANLOVI (bitta dizayn, ikki talab)
/// ========================================================================
///
/// Bu testlar ikki narsani qo'riqlaydi va ikkalasi ham JIMGINA buziladigan
/// turdagi:
///
///   1) 🔴 STANDART SOZLAMADA HECH NARSA O'ZGARMASLIGI. Migratsiya kuni
///      baholash navbati ham, savollar ro'yxati ham bugungidek qolishi
///      kerak. Buzilsa xato deploydan keyin, foydalanuvchilar orqali
///      bilinardi.
///
///   2) 🔴 RUXSAT CHEGARASI. R40 <c>ResolvePairAsync</c> ni "tenglik" dan
///      "to'plamga tegishlilik" ga o'tkazdi — bu QO'SHIMCHA emas,
///      O'ZGARISH. <c>DirectMessage</c> izohida eski tizimda shaxsiy
///      savol butun sinfga sizib chiqqani yozilgan, shuning uchun
///      chegara alohida va qattiq tekshiriladi.
/// </summary>
public sealed class StaffResponsibilityEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= R33 · baholash

    /// <summary>
    /// 🔴 STANDART (`Both`) — BUGUNGI XATTI-HARAKAT: ustoz ham, kurator ham
    /// javobni ko'radi va baholaydi.
    /// </summary>
    [Fact]
    public async Task Grading_DefaultBoth_BothStaffCanGrade()
    {
        var world = await CreateWorldAsync("r33def");

        var teacherQueue = await SubmissionsAsync(world.TeacherClient, world.AssignmentId);
        var curatorQueue = await SubmissionsAsync(world.CuratorClient, world.AssignmentId);

        teacherQueue.Should().ContainSingle("standart sozlamada ustoz ham navbatni ko'radi");
        curatorQueue.Should().ContainSingle();

        (await GradeAsync(world.CuratorClient, world.SubmissionId, 5))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Guruh «kurator tekshiradi» ga o'tkazilsa ustoz navbatni UMUMAN
    /// ko'rmaydi va baholay olmaydi.
    ///
    /// ★ NAVBAT VA BAHOLASH BIRGA TEKSHIRILADI: ular bir xil qoidadan
    /// o'tishi shart. Faqat baholash yopilib, ro'yxat ochiq qolsa, ustoz
    /// BAJARIB BO'LMAYDIGAN navbat ko'rardi — ekran ish bor deydi, server
    /// esa har bosganda 403 beradi.
    /// </summary>
    [Fact]
    public async Task Grading_AssistantOnly_ExcludesTeacherFromQueueAndGrading()
    {
        var world = await CreateWorldAsync("r33asst");

        await SetGroupRolesAsync(world.GroupId, grading: GroupStaffRole.Assistant);

        (await SubmissionsAsync(world.TeacherClient, world.AssignmentId))
            .Should().BeEmpty("ustoz endi tekshiruvchi emas");

        (await SubmissionsAsync(world.CuratorClient, world.AssignmentId))
            .Should().ContainSingle();

        var refused = await GradeAsync(world.TeacherClient, world.SubmissionId, 5);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // ★ XATO MATNI SABABNI AYTADI: "guruhingizda emas" chalg'itardi —
        //   o'quvchi guruhda BOR, faqat tekshiruvchi boshqa.
        (await WorldBuilder.Body(refused)).Should().Contain("boshqa xodim tekshiradi");

        (await GradeAsync(world.CuratorClient, world.SubmissionId, 4))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// 🔴 O'QUV BO'LIMI VA ADMIN TANLOVGA BO'YSUNMAYDI. Sabab
    /// <c>AssignmentService</c> ruxsat jadvalidagi izohda: ular ustozning
    /// xatosini tuzatishi kerak, aks holda noto'g'ri baho butun tizimda
    /// tuzatilmas bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Grading_AcademicOverride_SurvivesTheSetting()
    {
        var world = await CreateWorldAsync("r33acad");

        await SetGroupRolesAsync(world.GroupId, grading: GroupStaffRole.Assistant);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        (await SubmissionsAsync(admin, world.AssignmentId)).Should().ContainSingle();

        (await GradeAsync(admin, world.SubmissionId, 3))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// 🔴 EGASIZ QOLGAN ISH BO'LMASIN: «kurator tekshirsin» deb qo'yilgan
    /// guruhdan kurator OLIB TASHLANSA, topshirilgan ishni ustoz baholay
    /// olishi kerak. Aks holda o'quvchining javobi hech kimga yetmasdi va
    /// buni faqat u shikoyat qilganda bilinardi.
    /// </summary>
    [Fact]
    public async Task Grading_FallsBackToTeacher_WhenAssistantSeatBecomesEmpty()
    {
        var world = await CreateWorldAsync("r33fall");

        await SetGroupRolesAsync(world.GroupId, grading: GroupStaffRole.Assistant);
        await ClearAssistantAsync(world.GroupId);

        (await SubmissionsAsync(world.TeacherClient, world.AssignmentId))
            .Should().ContainSingle("bo'sh o'rindiq ikkinchisiga o'tadi");

        (await GradeAsync(world.TeacherClient, world.SubmissionId, 5))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Vazifa darajasidagi istisno GURUH sozlamasini yengadi (R33 ning
    /// so'zma-so'z o'qilishi: "vazifalarni tekshirishni").
    /// </summary>
    [Fact]
    public async Task Grading_AssignmentOverride_BeatsGroupSetting()
    {
        var world = await CreateWorldAsync("r33ovr");

        // Guruh: ustoz tekshiradi. Vazifa: kurator.
        await SetGroupRolesAsync(world.GroupId, grading: GroupStaffRole.Teacher);
        await SetAssignmentGraderAsync(world.AssignmentId, GroupStaffRole.Assistant);

        (await SubmissionsAsync(world.TeacherClient, world.AssignmentId)).Should().BeEmpty();
        (await SubmissionsAsync(world.CuratorClient, world.AssignmentId)).Should().ContainSingle();
    }

    /// <summary>
    /// 🔴 KURS VAZIFASIGA ISTISNO QO'YIB BO'LMAYDI: u o'nlab guruhga
    /// taalluqli va ularning har birida boshqa xodim ishlaydi. Bitta
    /// bayroq guruhlarga qo'yilgan tanlovni bexosdan bekor qilardi.
    /// </summary>
    [Fact]
    public async Task CourseAssignment_WithGraderRole_IsRejected()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var lessonId = await CreateLessonAsync(admin, "r33crs");

        var response = await admin.PostAsJsonAsync("/api/v1/assignments", new
        {
            title = "Kurs vazifasi",
            moduleLessonId = lessonId,
            graderRole = nameof(GroupStaffRole.Assistant),
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict, await WorldBuilder.Body(response));
    }

    /// <summary>
    /// Kuratori yo'q guruhga «kurator tekshirsin» deb qo'yib bo'lmaydi —
    /// 400, tushunarli matn bilan. Bu ZAXIRA YO'LNI almashtirmaydi: u
    /// KEYIN buzilgan sozlama uchun, bu esa xatoni oldindan to'sadi.
    /// </summary>
    [Fact]
    public async Task Assignment_GraderRole_OnGroupWithoutSeat_IsRejected()
    {
        var world = await CreateWorldAsync("r33seat");

        await ClearAssistantAsync(world.GroupId);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync("/api/v1/assignments", new
        {
            title = "Kurator tekshirsin",
            groupId = world.GroupId,
            graderRole = nameof(GroupStaffRole.Assistant),
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest, await WorldBuilder.Body(response));
    }

    // ================================================================= R40 · savollar

    /// <summary>
    /// 🔴 STANDART SOZLAMA — BUGUNGI XATTI-HARAKAT: o'quvchida BITTA
    /// suhbatdosh (kurator), ustozda esa BO'SH ro'yxat.
    /// </summary>
    [Fact]
    public async Task Questions_DefaultAssistant_KeepsSinglePeerAndEmptyTeacherInbox()
    {
        var world = await CreateWorldAsync("r40def");

        var studentPeers = await ConversationsAsync(world.StudentClient);
        studentPeers.Should().ContainSingle();
        studentPeers[0].PeerId.Should().Be(world.Curator.Id);

        (await ConversationsAsync(world.TeacherClient))
            .Should().BeEmpty("ustoz `/ustoz/savollar` da bugun bo'sh ro'yxat ko'radi");
    }

    /// <summary>
    /// «Ikkalasi ham» tanlanganda o'quvchida IKKI suhbat bo'ladi va
    /// KURATOR BIRINCHI qoladi — mavjud o'quvchilarning odatlangan
    /// birinchi qatori bir kechada almashib ketmasin.
    /// </summary>
    [Fact]
    public async Task Questions_Both_GivesStudentTwoConversations_CuratorFirst()
    {
        var world = await CreateWorldAsync("r40both");

        await SetGroupRolesAsync(world.GroupId, questions: GroupStaffRole.Both);

        var peers = await ConversationsAsync(world.StudentClient);

        peers.Should().HaveCount(2);
        peers[0].PeerId.Should().Be(world.Curator.Id, "kurator mas'uliyat tartibida birinchi");
        peers[1].PeerId.Should().Be(world.Teacher.Id);

        (await ConversationsAsync(world.TeacherClient)).Should().ContainSingle();
    }

    /// <summary>
    /// 🔴 XAVFSIZLIK CHEGARASI. Ikki suhbat OCHILGANDA ham ular
    /// BIR-BIRIDAN YOPIQ: suhbat kaliti <c>(StudentId, StaffId)</c>
    /// bo'lgani uchun ustoz kuratorga yozilgan savolni KO'RA OLMAYDI.
    ///
    /// Aynan shu <c>DirectMessage</c> izohidagi eski tizim sizib
    /// chiqishining takrorlanmasligini kafolatlaydi.
    /// </summary>
    [Fact]
    public async Task Questions_TwoPeers_ThreadsStayPrivateFromEachOther()
    {
        var world = await CreateWorldAsync("r40priv");

        await SetGroupRolesAsync(world.GroupId, questions: GroupStaffRole.Both);

        await SendAsync(world.StudentClient, world.Curator.Id, "Faqat kuratorga", null);
        await SendAsync(world.StudentClient, world.Teacher.Id, "Faqat ustozga", null);

        var teacherThread = await ThreadAsync(world.TeacherClient, world.Student.Id);
        var curatorThread = await ThreadAsync(world.CuratorClient, world.Student.Id);

        teacherThread.Items.Should().ContainSingle();
        teacherThread.Items[0].Body.Should().Be("Faqat ustozga");

        curatorThread.Items.Should().ContainSingle();
        curatorThread.Items[0].Body.Should().Be("Faqat kuratorga");
    }

    /// <summary>
    /// Mas'ul BO'LMAGAN xodim bilan yozishmaga urinish — 403. Standart
    /// sozlamada ustoz aynan shunday xodim.
    /// </summary>
    [Fact]
    public async Task Questions_NonResponder_IsForbidden()
    {
        var world = await CreateWorldAsync("r40deny");

        var response = await world.StudentClient.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{world.Teacher.Id}/messages",
            new { body = "Ustozga yozmoqchiman" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// 🔴 SOZLAMA QAYTARILGANDA ESKI SUHBAT YOPILADI. Bu bugungi
    /// xatti-harakat bilan bir xil (kurator guruhdan olib tashlansa ham
    /// shunday) va ATAYLAB o'zgartirilmadi: mas'uliyat — ruxsatning
    /// yagona manbai.
    /// </summary>
    [Fact]
    public async Task Questions_RevokedResponsibility_ClosesTheThread()
    {
        var world = await CreateWorldAsync("r40rev");

        await SetGroupRolesAsync(world.GroupId, questions: GroupStaffRole.Both);
        await SendAsync(world.StudentClient, world.Teacher.Id, "Ustozga savol", null);

        await SetGroupRolesAsync(world.GroupId, questions: GroupStaffRole.Assistant);

        var response = await world.TeacherClient.GetAsync(new Uri(
            $"/api/v1/messages/conversations/{world.Student.Id}/messages", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= R40 · dars savollari

    /// <summary>
    /// DARS SAVOLLARI NAVBATI: faqat darsga bog'langan savollar, javobsizlar
    /// tepada, xodimning O'Z javobi savol sifatida ko'rinmaydi.
    /// </summary>
    [Fact]
    public async Task LessonQuestions_ListsOnlyLessonBoundQuestions_UnansweredFirst()
    {
        var world = await CreateWorldAsync("r40q");

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        // Dars o'quvchining O'Z kursida bo'lishi shart — aks holda savol
        // yuborishda server 403 qaytaradi.
        var lessonId = await CreateLessonAsync(admin, "r40q", world.GroupId);

        // Umumiy savol — navbatga TUSHMAYDI.
        await SendAsync(world.StudentClient, world.Curator.Id, "Umumiy savol", null);

        // Dars savoli.
        await SendAsync(world.StudentClient, world.Curator.Id, "Videoni tushunmadim", lessonId);

        var queue = await LessonQuestionsAsync(world.CuratorClient);

        queue.Should().ContainSingle("faqat darsga bog'langan savollar navbatga tushadi");
        queue[0].ModuleLessonId.Should().Be(lessonId);
        queue[0].PeerId.Should().Be(world.Student.Id);
        queue[0].Answered.Should().BeFalse();
        queue[0].GroupName.Should().Be(world.GroupName);

        // Javobdan keyin — "javob berilgan". Kurator o'z javobi navbatda
        // ALOHIDA qator bo'lib CHIQMAYDI (u savol emas).
        await SendAsync(world.CuratorClient, world.Student.Id, "Mana tushuntirish", lessonId);

        var after = await LessonQuestionsAsync(world.CuratorClient);

        after.Should().ContainSingle();
        after[0].Answered.Should().BeTrue();
    }

    /// <summary>O'quvchi bu navbatni umuman ko'rmaydi (u xodim ekrani).</summary>
    [Fact]
    public async Task LessonQuestions_ForStudent_IsForbidden()
    {
        var world = await CreateWorldAsync("r40qstud");

        var response = await world.StudentClient.GetAsync(
            new Uri("/api/v1/messages/lesson-questions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- yordamchi

    private sealed record Fixture(
        long GroupId,
        string GroupName,
        long AssignmentId,
        long SubmissionId,
        TestUser Student,
        TestUser Teacher,
        TestUser Curator,
        HttpClient StudentClient,
        HttpClient TeacherClient,
        HttpClient CuratorClient);

    /// <summary>
    /// Ustoz + kurator + o'quvchi + guruh vazifasi + TOPSHIRILGAN javob.
    ///
    /// ★ Javob HAQIQIY endpoint orqali topshiriladi: baholash darvozasi
    /// javob holatiga bog'liq va uni bazaga qo'lda yozish testni haqiqiy
    /// oqimdan uzib qo'yardi.
    /// </summary>
    private async Task<Fixture> CreateWorldAsync(string prefix)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var created = await admin.PostAsJsonAsync("/api/v1/assignments", new
        {
            title = $"{prefix} vazifasi",
            groupId = world.GroupId,
            allowedFormats = "Text",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(created));

        var assignmentId = (await created.Content.ReadFromJsonAsync<IdOnly>())!.Id;

        var studentClient = await WorldBuilder.ClientAsync(factory, world.Student);

        /*
          🔴 TOPSHIRISH — `multipart/form-data`, `/submit`, javob `200`.

          Bu yordamchi dastlab `POST /assignments/{id}/submissions` ga JSON
          yuborib `201` kutardi va 13 ta test shu sababdan yiqilgan edi.
          Uchta xato bir joyda:
            • yo'l — `submissions` FAQAT `[HttpGet]` (ro'yxat), shuning uchun
              POST `405 MethodNotAllowed` qaytardi; topshirish yo'li `submit`;
            • tur — endpoint `[Consumes("multipart/form-data")]`, chunki
              javobga fayl biriktirilishi mumkin (JSON qabul qilinmaydi);
            • holat — `200 OK`, `201 Created` emas: takroriy topshirish
              mavjud qatorni YANGILAYDI (`AttemptNumber` oshadi), ya'ni har
              safar yangi resurs yaratilmaydi.

          Naqsh `AssignmentEndpointsTests.Multipart(...)` dan olindi — u
          AYNI endpointni to'g'ri chaqiradigan mavjud test.
        */
        using var submission = new MultipartFormDataContent
        {
            { new StringContent("Javobim"), "text" },
        };

        var submitted = await studentClient.PostAsync(
            new Uri($"/api/v1/assignments/{assignmentId}/submit", UriKind.Relative),
            submission);

        submitted.StatusCode.Should().Be(
            HttpStatusCode.OK, await WorldBuilder.Body(submitted));

        var submissionId = (await submitted.Content.ReadFromJsonAsync<IdOnly>())!.Id;

        return new Fixture(
            world.GroupId,
            world.GroupName,
            assignmentId,
            submissionId,
            world.Student,
            world.Teacher,
            world.Curator,
            studentClient,
            await WorldBuilder.ClientAsync(factory, world.Teacher),
            await WorldBuilder.ClientAsync(factory, world.Curator));
    }

    /// <summary>
    /// Guruh sozlamasini TO'G'RIDAN-TO'G'RI bazada o'zgartiradi.
    ///
    /// ★ `PUT /groups/{id}` ATAYLAB ISHLATILMADI: u TO'LIQ ALMASHTIRISH
    ///   (jadval, kurs, shtat) va test niyatidan ancha ko'p narsaga
    ///   tegardi — bitta ustunni o'zgartirish uchun butun jadvalni
    ///   qaytadan yuborish kerak bo'lardi. Endpointning O'ZI esa
    ///   `GroupEndpointsTests` da qoplangan.
    /// </summary>
    private Task<bool> SetGroupRolesAsync(
        long groupId,
        GroupStaffRole? grading = null,
        GroupStaffRole? questions = null) =>
        factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FirstAsync(g => g.Id == groupId);

            if (grading is { } gradingRole) group.AssignmentGraderRole = gradingRole;
            if (questions is { } questionRole) group.QuestionResponderRole = questionRole;

            await db.SaveChangesAsync();
            return true;
        });

    private Task<bool> ClearAssistantAsync(long groupId) =>
        factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FirstAsync(g => g.Id == groupId);
            group.AssistantId = null;
            await db.SaveChangesAsync();
            return true;
        });

    private Task<bool> SetAssignmentGraderAsync(long assignmentId, GroupStaffRole role) =>
        factory.WithDbAsync(async db =>
        {
            var assignment = await db.Assignments.FirstAsync(a => a.Id == assignmentId);
            assignment.GraderRole = role;
            await db.SaveChangesAsync();
            return true;
        });

    /// <summary>Kurs + modul + dars (savol konteksti va kurs vazifasi uchun).</summary>
    /// <summary>
    /// Modul + dars yaratadi va dars id'sini qaytaradi.
    ///
    /// 🔴 <paramref name="inGroupId"/> BERILSA dars O'SHA GURUHNING KURSIDA
    /// yaratiladi. Bu ixtiyoriy qulaylik emas — darsga bog'langan savol
    /// yuborishda server darsning o'quvchi kursiga tegishliligini tekshiradi
    /// va begona kursdagi dars uchun <c>403 "Bu dars sizning kursingizga
    /// tegishli emas"</c> qaytaradi. Yordamchi dastlab HAR DOIM yangi kurs
    /// yaratardi, shuning uchun savol navbati testi yiqilardi.
    ///
    /// <c>null</c> — yangi kurs yaratiladi (kurs vazifasini rad etish testi
    /// uchun yetarli: u guruhga umuman bog'lanmaydi).
    /// </summary>
    private static async Task<long> CreateLessonAsync(
        HttpClient admin, string prefix, long? inGroupId = null)
    {
        long courseId;

        if (inGroupId is { } groupId)
        {
            /*
              ★ `WorldBuilder` guruhni KURSSIZ yaratadi (`courseId: null`) —
              guruh uchun kurs ixtiyoriy. Lekin darsga bog'langan savol
              yuborishda server darsning o'quvchi kursiga tegishliligini
              tekshiradi, ya'ni kurssiz guruhda BIRON dars haqida savol
              berib bo'lmaydi.

              Shuning uchun bu yerda kurs yaratiladi va guruhga BIRIKTIRILADI.

              🔴 `PUT /groups/{id}` — TO'LIQ ALMASHTIRISH (`GroupDtos.cs:108`).
              Shu sababli guruh AVVAL `GET` bilan olinadi va o'sha javobning
              O'ZIGA `courseId` qo'shib qaytariladi: qo'lda yig'ilgan payload
              berilmagan maydonlarni (jadval, xodimlar, tarif) jimgina
              tozalab yuborardi.
            */
            var current = await admin.GetAsync(
                new Uri($"/api/v1/groups/{groupId}", UriKind.Relative));

            current.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(current));

            var payload = JsonNode.Parse(await current.Content.ReadAsStringAsync())!.AsObject();

            var course = await admin.PostAsJsonAsync("/api/v1/courses", new
            {
                name = $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}",
            });

            course.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(course));
            courseId = (await course.Content.ReadFromJsonAsync<IdOnly>())!.Id;

            payload["courseId"] = courseId;

            var attached = await admin.PutAsJsonAsync(
                new Uri($"/api/v1/groups/{groupId}", UriKind.Relative), payload);

            attached.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(attached));
        }
        else
        {
            var course = await admin.PostAsJsonAsync("/api/v1/courses", new
            {
                name = $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}",
            });

            course.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(course));
            courseId = (await course.Content.ReadFromJsonAsync<IdOnly>())!.Id;
        }

        var module = await admin.PostAsJsonAsync(
            $"/api/v1/courses/{courseId}/modules", new { name = "1-modul" });

        module.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(module));
        var moduleId = (await module.Content.ReadFromJsonAsync<IdOnly>())!.Id;

        // ★ Yo'lda `courseId` HAM bo'lishi shart — marshrut
        //   `{courseId}/modules/{moduleId}/lessons` (`CoursesController:167`).
        //   `courseId` siz yozilgani 404 berardi.
        var lesson = await admin.PostAsJsonAsync(
            $"/api/v1/courses/{courseId}/modules/{moduleId}/lessons", new { name = "1-dars" });

        lesson.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(lesson));

        return (await lesson.Content.ReadFromJsonAsync<IdOnly>())!.Id;
    }

    private static async Task<List<SubmissionRow>> SubmissionsAsync(
        HttpClient client, long assignmentId)
    {
        var response = await client.GetAsync(new Uri(
            $"/api/v1/assignments/{assignmentId}/submissions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<List<SubmissionRow>>())!;
    }

    private static Task<HttpResponseMessage> GradeAsync(
        HttpClient client, long submissionId, decimal score) =>
        client.PostAsJsonAsync($"/api/v1/submissions/{submissionId}/grade", new { score });

    private static async Task<List<ConversationRow>> ConversationsAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            new Uri("/api/v1/messages/conversations", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<List<ConversationRow>>())!;
    }

    private static async Task<List<LessonQuestionRow>> LessonQuestionsAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            new Uri("/api/v1/messages/lesson-questions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<List<LessonQuestionRow>>())!;
    }

    private static async Task SendAsync(
        HttpClient client, long peerId, string body, long? moduleLessonId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/messages/conversations/{peerId}/messages",
            new { body, moduleLessonId });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await WorldBuilder.Body(response));
    }

    private static async Task<ThreadRow> ThreadAsync(HttpClient client, long peerId)
    {
        var response = await client.GetAsync(new Uri(
            $"/api/v1/messages/conversations/{peerId}/messages", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<ThreadRow>())!;
    }

    private sealed record IdOnly(long Id);


    /*
      ★ `Status` — `string`, `SubmissionStatus` EMAS.

      API enum'larni NOM bilan seriyalaydi (`JsonStringEnumConverter`), test
      esa `ReadFromJsonAsync` ni sukut sozlamalari bilan chaqiradi — u
      konverterni bilmaydi va `"Submitted"` ni enum'ga o'gira olmay
      `JsonException` tashlaydi. Shu fayldagi `ConversationRow.PeerRole` ham
      aynan shu sababdan `string`.

      Bu maydon hech qayerda O'QILMAYDI — testlar faqat navbat UZUNLIGINI
      tekshiradi ("ustoz ko'radimi / kurator ko'radimi"). Shuning uchun
      konverter qo'shish emas, turni to'g'rilash yetarli.
    */
    private sealed record SubmissionRow(long Id, long StudentId, string Status);

    private sealed record ConversationRow(long PeerId, string? PeerName, string PeerRole);

    private sealed record ThreadRow(long PeerId, List<MessageRow> Items);

    private sealed record MessageRow(long Id, string Body, long? ModuleLessonId);

    private sealed record LessonQuestionRow(
        long MessageId,
        long PeerId,
        string? PeerName,
        string? GroupName,
        long ModuleLessonId,
        string? ModuleLessonName,
        string Body,
        bool Answered,
        bool Read);
}
