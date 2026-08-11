using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// GURUH: "VIDEO DARSLAR QAYSI QISMDAN BOSHLANADI"
/// ========================================================================
///
/// MUAMMO (hayotiy): bitta kursga ko'p guruh biriktiriladi va yarim yildan
/// keyin ochilgan guruh kursning 1-modulidan emas, O'RTASIDAN boshlaydi.
/// Bunday sozlama bo'lmaganda o'quvchi hech qachon o'tmagan 20 ta darsni
/// "tugatmagan" bo'lib turadi va sur'at nazorati (gating) BUTUN kursni
/// qulflab qo'yadi.
///
/// Bu fayl ATAYLAB alohida: `GroupEndpointsTests` jadval qoidasini,
/// `CourseEndpointsTests` kurs kontentini qo'riqlaydi — bu yerda esa
/// GURUH sozlamasi bilan GATING orasidagi chegara tekshiriladi va
/// ikkalasi bitta stsenariyda uchrashadi.
///
/// ★ Eng muhim tekshiruvlar:
///   • begona kursning darsi -> 400 (`problem.errors.videoStartLessonId`);
///   • `PUT` = TO'LIQ ALMASHTIRISH -> kurs almashganda maydon TOZALANADI;
///   • gating: boshlanish nuqtasidan oldingi darslar TALABGA KIRMAYDI,
///     ya'ni o'quvchi kursni OCHA OLADI;
///   • `null` da eski xatti-harakat AYNAN saqlanadi (regressiya qulfi);
///   • dars o'chirilsa guruh O'CHMAYDI (`ON DELETE SET NULL`).
/// </summary>
public sealed class GroupVideoStartEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const int DurationMinutes = 80;
    private const int CourseMonths = 8;
    private const string StartTime = "19:00:00";

    private static readonly string[] MondayWednesday = ["Monday", "Wednesday"];

    // ================================================================== 1) VALIDATSIYA

    /// <summary>
    /// ★★ BEGONA KURSNING DARSI — 400 va sabab <c>problem.errors</c> da.
    ///
    /// Bu eng qimmat invariant: begona kursning darsi guruhda qolsa gating
    /// uni umuman topa olmasdi va o'quvchi uchun butun kurs tushunarsiz
    /// qulflanib qolardi.
    /// </summary>
    [Fact]
    public async Task Create_WithLessonFromAnotherCourse_ReturnsBadRequestWithReason()
    {
        var (ownCourseId, _, _) = await CourseWithLessonsAsync(1);
        var (_, _, foreignLessons) = await CourseWithLessonsAsync(1);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/groups",
            Payload("VS-begona", ownCourseId, foreignLessons[0]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            await response.Content.ReadAsStringAsync());

        var message = await FieldErrorAsync(response);

        message.Should().Contain("kursiga tegishli emas",
            "o'quv bo'limi xodimi sababni tushunishi kerak");
    }

    /// <summary>Tahrirlashda ham AYNI qoida (ikki yo'l ajralib ketmasin).</summary>
    [Fact]
    public async Task Update_WithLessonFromAnotherCourse_ReturnsBadRequest()
    {
        var (courseId, _, lessons) = await CourseWithLessonsAsync(2);
        var (_, _, foreignLessons) = await CourseWithLessonsAsync(1);

        using var admin = await AdminClientAsync();

        var groupId = await CreateGroupAsync(admin, Payload("VS-tahrir", courseId, lessons[0]));

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/groups/{groupId}",
            Payload("VS-tahrir", courseId, foreignLessons[0]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // ★ HECH NARSA yozilmagan bo'lishi kerak — yarim holat bo'lmasin.
        var stored = await StoredStartLessonAsync(groupId);
        stored.Should().Be(lessons[0], "400 dan keyin eski qiymat o'z joyida qolishi kerak");
    }

    /// <summary>
    /// ★ KURSSIZ GURUHDA boshlanish nuqtasi bo'lmaydi: "qaysi kursning qaysi
    /// darsi?" degan savol javobsiz qolardi.
    /// </summary>
    [Fact]
    public async Task Create_WithoutCourse_ButWithVideoStartLesson_ReturnsBadRequest()
    {
        var (_, _, lessons) = await CourseWithLessonsAsync(1);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/groups",
            Payload("VS-kurssiz", courseId: null, videoStartLessonId: lessons[0]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await FieldErrorAsync(response)).Should().Contain("kurs biriktir");
    }

    /// <summary>
    /// Mavjud BO'LMAGAN dars ham AYNI 400 ni oladi (404 emas): javob kodi
    /// boshqa kurslarda qanday dars Id'lari borligini oshkor qilmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Create_WithUnknownLesson_ReturnsBadRequestNotNotFound()
    {
        var (courseId, _, _) = await CourseWithLessonsAsync(1);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/groups",
            Payload("VS-yoq-dars", courseId, videoStartLessonId: 999_999_999));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================== 2) DTO SHAKLI

    /// <summary>
    /// ★ KARTOCHKA UCHUN SHARTNOMA: <c>videoStartLessonName</c> va
    /// <c>videoStartModuleName</c> qaytadi — UI ularni "3-modul · 2-dars"
    /// ko'rinishida ko'rsatadi va qo'shimcha so'rov yubormaydi.
    /// </summary>
    [Fact]
    public async Task Create_WithVideoStartLesson_ReturnsLessonAndModuleNames()
    {
        var (courseId, moduleId, lessons) = await CourseWithLessonsAsync(3);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/groups", Payload("VS-nomlar", courseId, lessons[1]));

        await EnsureStatusAsync(response, HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreateGroupRow>();
        var group = created!.Group;

        group.VideoStartLessonId.Should().Be(lessons[1]);
        group.VideoStartLessonName.Should().Be("Dars 2");
        group.VideoStartModuleName.Should().Be(await ModuleNameAsync(moduleId));

        // Kartochka (GET) ham AYNI qiymatlarni berishi kerak — ro'yxat va
        // kartochka bitta DTO'dan foydalanadi, ikkisi ajralib ketmasin.
        var card = await admin.GetFromJsonAsync<GroupRow>($"/api/v1/groups/{group.Id}");

        card!.VideoStartLessonId.Should().Be(lessons[1]);
        card.VideoStartLessonName.Should().Be("Dars 2");
    }

    /// <summary>Sozlanmagan guruhda uchala maydon ham <c>null</c>.</summary>
    [Fact]
    public async Task Create_WithoutVideoStartLesson_ReturnsNulls()
    {
        var (courseId, _, _) = await CourseWithLessonsAsync(1);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/groups", Payload("VS-bosh", courseId, videoStartLessonId: null));

        await EnsureStatusAsync(response, HttpStatusCode.Created);

        var group = (await response.Content.ReadFromJsonAsync<CreateGroupRow>())!.Group;

        group.VideoStartLessonId.Should().BeNull();
        group.VideoStartLessonName.Should().BeNull();
        group.VideoStartModuleName.Should().BeNull();
    }

    // ================================================================== 3) PUT = TO'LIQ ALMASHTIRISH

    /// <summary>
    /// ★★ <c>PUT</c> TO'LIQ ALMASHTIRADI: maydon yuborilmasa <c>null</c> ga
    /// tushadi. Bu semantika ATAYLAB shunday (loyiha kelishuvi) — yangi
    /// maydon ham unga BO'YSUNISHI kerak.
    ///
    /// ★★ AYNI TEST "KURS ALMASHGANDA MAYDON TOZALANADI" qoidasini ham
    /// qo'riqlaydi: kursni almashtirgan klient boshlanish nuqtasini
    /// yubormasa u tozalanadi, ya'ni begona kursning darsi guruhda QOLIB
    /// KETMAYDI.
    /// </summary>
    [Fact]
    public async Task Update_WhenCourseChangesAndFieldOmitted_ClearsVideoStartLesson()
    {
        var (oldCourseId, _, oldLessons) = await CourseWithLessonsAsync(2);
        var (newCourseId, _, _) = await CourseWithLessonsAsync(2);

        using var admin = await AdminClientAsync();

        var groupId = await CreateGroupAsync(
            admin, Payload("VS-kurs-almashdi", oldCourseId, oldLessons[0]));

        (await StoredStartLessonAsync(groupId)).Should().Be(oldLessons[0]);

        // Kurs almashtiriladi, `videoStartLessonId` YUBORILMAYDI.
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/groups/{groupId}",
            new
            {
                name = "VS-kurs-almashdi",
                startDate = FutureStart().ToString("O", CultureInfo.InvariantCulture),
                weekdays = MondayWednesday,
                startTime = StartTime,
                type = nameof(GroupType.Group),
                durationMinutes = DurationMinutes,
                courseMonths = CourseMonths,
                courseId = newCourseId,
                isActive = true,
            });

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        var group = (await response.Content.ReadFromJsonAsync<UpdateGroupRow>())!.Group;

        group.CourseId.Should().Be(newCourseId);
        group.VideoStartLessonId.Should().BeNull("PUT yuborilmagan maydonni tozalaydi");
        group.VideoStartLessonName.Should().BeNull();

        (await StoredStartLessonAsync(groupId)).Should().BeNull("baza ham tozalangan bo'lishi kerak");
    }

    /// <summary>
    /// Kurs almashsa, YANGI kursning darsi esa qabul qilinadi — cheklov
    /// "kurs almashdi" ga emas, DARSNING KURSIGA bog'liq.
    /// </summary>
    [Fact]
    public async Task Update_WhenCourseChanges_AcceptsLessonFromTheNewCourse()
    {
        var (oldCourseId, _, oldLessons) = await CourseWithLessonsAsync(2);
        var (newCourseId, _, newLessons) = await CourseWithLessonsAsync(3);

        using var admin = await AdminClientAsync();

        var groupId = await CreateGroupAsync(
            admin, Payload("VS-yangi-kurs", oldCourseId, oldLessons[0]));

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/groups/{groupId}",
            Payload("VS-yangi-kurs", newCourseId, newLessons[2]));

        await EnsureStatusAsync(response, HttpStatusCode.OK);

        var group = (await response.Content.ReadFromJsonAsync<UpdateGroupRow>())!.Group;

        group.CourseId.Should().Be(newCourseId);
        group.VideoStartLessonId.Should().Be(newLessons[2]);
        group.VideoStartLessonName.Should().Be("Dars 3");
    }

    // ================================================================== 4) FK: ON DELETE SET NULL

    /// <summary>
    /// ★★ DARS O'CHIRILSA GURUH O'CHMAYDI — faqat cheklov yo'qoladi.
    ///
    /// `Cascade` bo'lganda bitta kurs darsini o'chirish shu darsdan
    /// boshlanadigan BARCHA guruhlarni, ular bilan birga jadval, davomat va
    /// chat tarixini olib ketardi. Bu test aynan shu falokatni qo'riqlaydi.
    /// </summary>
    [Fact]
    public async Task DeleteLesson_SetsVideoStartToNull_AndKeepsTheGroup()
    {
        var (courseId, moduleId, lessons) = await CourseWithLessonsAsync(2);

        using var admin = await AdminClientAsync();

        var groupId = await CreateGroupAsync(
            admin, Payload("VS-dars-ochirildi", courseId, lessons[1]));

        var deleted = await admin.DeleteAsync(
            $"/api/v1/courses/{courseId}/modules/{moduleId}/lessons/{lessons[1]}");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent,
            await deleted.Content.ReadAsStringAsync());

        var exists = await factory.WithDbAsync(db => db.Groups.AnyAsync(g => g.Id == groupId));

        exists.Should().BeTrue("dars o'chirilishi guruhni olib ketmasligi kerak");
        (await StoredStartLessonAsync(groupId)).Should().BeNull("FK SET NULL qilishi kerak");
    }

    // ================================================================== 5) ★★ GATING

    /// <summary>
    /// ★★★ ENG MUHIM TEST: kursning O'RTASIDAN boshlagan guruh o'quvchisi
    /// kursni OCHA OLADI.
    ///
    /// Stsenariy: kursda 4 dars, guruh 3-darsdan (indeks 2) boshlaydi,
    /// yakunlangan ustoz darsi YO'Q (sur'at = 0).
    ///
    /// Kutilgan natija:
    ///   • 1- va 2-dars  -> yopiq, sabab `BeforeGroupStart`;
    ///   • 3-dars        -> ★ OCHIQ (guruh uchun BIRINCHI dars);
    ///   • 4-dars        -> yopiq, sabab `TeacherPace` (ustoz yetmagan).
    ///
    /// ESKI XATTI-HARAKAT bilan bu dars YOPIQ bo'lardi: zanjir 0-darsdan
    /// yurib, hech qachon o'tilmagan darslar uni bloklardi — o'quvchi
    /// kursga umuman kira olmasdi.
    /// </summary>
    [Fact]
    public async Task Gate_WithVideoStart_UnlocksTheStartLessonAndMarksEarlierOnes()
    {
        var (courseId, _, lessons) = await CourseWithLessonsAsync(4);

        var studentId = await StudentInGroupAsync(courseId, videoStartLessonId: lessons[2]);

        var gate = await GateAsync(studentId);

        gate.CourseId.Should().Be(courseId);
        gate.VideoStartLessonId.Should().Be(lessons[2]);
        gate.StartIndex.Should().Be(2, "boshlanish darsi kursda uchinchi (indeks 2)");

        gate.Lessons.Should().HaveCount(4);

        gate.Lessons[0].Unlocked.Should().BeFalse();
        gate.Lessons[0].LockReason.Should().Be(LessonLockReason.BeforeGroupStart);

        gate.Lessons[1].Unlocked.Should().BeFalse();
        gate.Lessons[1].LockReason.Should().Be(LessonLockReason.BeforeGroupStart);

        gate.Lessons[2].Unlocked.Should().BeTrue(
            "guruh SHU darsdan boshlaydi — u guruh uchun birinchi dars");
        gate.Lessons[2].LockReason.Should().BeNull();

        gate.Lessons[3].Unlocked.Should().BeFalse();
        gate.Lessons[3].LockReason.Should().Be(LessonLockReason.TeacherPace,
            "sur'at NISBIY o'lchanadi: guruh hali bitta ham dars o'tmagan");
    }

    /// <summary>
    /// ★★ KURS DARAXTIDA ham AYNI natija: o'quvchi boshlanish darsining
    /// mazmunini KO'RADI, o'tib ketilgan darslarning esa faqat sarlavhasini.
    ///
    /// Bu "gating buzilmagan" degan ikkinchi, MUSTAQIL isbot: daraxt
    /// gating xaritasidan foydalanadi, ya'ni ikki kod yo'li bir xil javob
    /// berishi kerak.
    /// </summary>
    [Fact]
    public async Task Tree_WithVideoStart_ShowsStartLessonContentAndHidesSkippedOnes()
    {
        var (courseId, _, lessons) = await CourseWithLessonsAsync(3);

        var (email, password, studentId) =
            await StudentWithLoginInGroupAsync(courseId, videoStartLessonId: lessons[1]);

        await InvalidateGateAsync(studentId);

        using var student = await ClientAsync(email, password);

        var tree = await student.GetFromJsonAsync<TreeRow>($"/api/v1/courses/{courseId}");

        var shown = tree!.Modules.Single().Lessons;

        shown[0].Unlocked.Should().BeFalse();
        shown[0].LockReason.Should().Be(nameof(LessonLockReason.BeforeGroupStart),
            "sabab JSON'da SATR bo'lib chiqadi (JsonStringEnumConverter)");
        shown[0].Name.Should().Be("Dars 1", "sarlavha ko'rinadi — kursda nima borligini bilsin");
        shown[0].Description.Should().BeNull("o'tib ketilgan darsning MAZMUNI berilmaydi");
        shown[0].Completed.Should().BeFalse(
            "★ qulflangan dars HECH QACHON \"tugatilgan\" emas — progress surati "
            + "o'tilmagan darsni yashil ko'rsatmasligi kerak");

        shown[1].Unlocked.Should().BeTrue("guruh shu darsdan boshlaydi");
        shown[1].Description.Should().Be("Mazmun 2");

        shown[2].Unlocked.Should().BeFalse("ustoz hali yetib kelmagan");
        shown[2].LockReason.Should().Be(nameof(LessonLockReason.TeacherPace));
    }

    /// <summary>
    /// ★★ PROGRESS MAXRAJI: guruh o'tmaydigan darslar hisobdan CHIQADI.
    ///
    /// Frontend maxrajni `lockReason === "BeforeGroupStart"` bo'yicha
    /// ajratadi. Bu test shartnomani qo'riqlaydi: maxraj
    /// <c>jami − startIndex</c> ga TENG bo'lishi shart. Aks holda hech
    /// qachon o'tilmaydigan darslar maxrajda qolib, progress abadiy
    /// pastda (masalan 40% da) qotib turardi.
    /// </summary>
    [Fact]
    public async Task Gate_ProgressDenominatorStartsAtTheGroupStartLesson()
    {
        var (courseId, _, lessons) = await CourseWithLessonsAsync(5);

        var studentId = await StudentInGroupAsync(courseId, videoStartLessonId: lessons[3]);

        var gate = await GateAsync(studentId);

        var denominator = gate.Lessons
            .Count(l => l.LockReason != LessonLockReason.BeforeGroupStart);

        denominator.Should().Be(gate.Lessons.Count - gate.StartIndex);
        denominator.Should().Be(2, "5 ta darsdan guruh faqat oxirgi 2 tasini o'tadi");
    }

    /// <summary>
    /// ★★★ REGRESSIYA QULFI: <c>videoStartLessonId = null</c> da xatti-harakat
    /// BIT-TO-BIT o'zgarmaydi.
    ///
    /// Mavjud barcha guruhlar aynan shu holatda, shuning uchun bu test
    /// yangi maydonning eski ma'lumotga TEGMAGANINI isbotlaydi.
    /// </summary>
    [Fact]
    public async Task Gate_WithoutVideoStart_KeepsTheLegacyBehaviourExactly()
    {
        var (courseId, _, _) = await CourseWithLessonsAsync(3);

        var studentId = await StudentInGroupAsync(courseId, videoStartLessonId: null);

        var gate = await GateAsync(studentId);

        gate.VideoStartLessonId.Should().BeNull();
        gate.StartIndex.Should().Be(0, "cheklov yo'q -> kurs boshidan");

        gate.Lessons.Should().NotContain(l => l.LockReason == LessonLockReason.BeforeGroupStart,
            "cheklovsiz kursda bu sabab hech qachon paydo bo'lmaydi");

        // Eski qoida: faqat BIRINCHI dars ochiq (sur'at = 0).
        gate.Lessons[0].Unlocked.Should().BeTrue();
        gate.Lessons[1].Unlocked.Should().BeFalse();
        gate.Lessons[1].LockReason.Should().Be(LessonLockReason.TeacherPace);
        gate.Lessons[2].Unlocked.Should().BeFalse();
    }

    /// <summary>
    /// ★ QO'LDA OCHISH boshlanish nuqtasidan USTUN: o'tib ketilgan qismni
    /// o'zlashtirmoqchi bo'lgan o'quvchiga o'quv bo'limi darsni ocha oladi
    /// (kasallik, kursga kech qo'shilish — mavjud istisno mexanizmi
    /// yangi qoida bilan buzilmasligi kerak).
    /// </summary>
    [Fact]
    public async Task Gate_WithOverride_UnlocksALessonBeforeTheGroupStart()
    {
        var (courseId, _, lessons) = await CourseWithLessonsAsync(3);

        var studentId = await StudentInGroupAsync(courseId, videoStartLessonId: lessons[2]);

        using var scope = factory.Services.CreateScope();
        var gating = scope.ServiceProvider.GetRequiredService<IGatingService>();

        var beforeOverride = await GateAsync(studentId);

        beforeOverride.Lessons[0].LockReason.Should().Be(LessonLockReason.BeforeGroupStart);

        await gating.SetOverrideAsync(
            studentId, lessons[0], unlocked: true, reason: "Qoldirilgan qismni o'zlashtiradi",
            actorId: await AdminIdAsync());

        var gate = await GateAsync(studentId);

        gate.Lessons[0].Unlocked.Should().BeTrue("qo'lda ochish DOIM ustun");
        gate.Lessons[0].LockReason.Should().BeNull();

        gate.Lessons[1].LockReason.Should().Be(LessonLockReason.BeforeGroupStart,
            "istisno FAQAT ochilgan darsga tegishli");
    }

    /// <summary>
    /// ★ "BITTA DARS" (arzon) yo'li va "BUTUN DARAXT" yo'li AYNI javobni
    /// berishi shart — aks holda o'quvchi ro'yxatda ochiq ko'rgan darsni
    /// bosganda 403 olardi.
    /// </summary>
    [Fact]
    public async Task LessonGate_CheapPathAgreesWithTheTreeForEveryLesson()
    {
        var (courseId, _, lessons) = await CourseWithLessonsAsync(4);

        var studentId = await StudentInGroupAsync(courseId, videoStartLessonId: lessons[2]);

        var tree = await GateAsync(studentId);

        foreach (var lesson in tree.Lessons)
        {
            // HAR dars uchun YANGI scope: `GatingService` so'rov ichida
            // snapshot'ni memolaydi, ya'ni bir scope'da arzon yo'l umuman
            // ishlamasdi va test hech narsani tekshirmagan bo'lardi.
            using var scope = factory.Services.CreateScope();
            var gating = scope.ServiceProvider.GetRequiredService<IGatingService>();

            await gating.InvalidateAsync(studentId);

            var single = await gating.GetLessonGateAsync(studentId, lesson.LessonId);

            single.Unlocked.Should().Be(lesson.Unlocked);
            single.LockReason.Should().Be(lesson.LockReason);
            single.Index.Should().Be(lesson.Index);
        }
    }

    // ================================================================== yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var tokens = await factory.LoginAsync(email, password);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private Task<long> AdminIdAsync() =>
        factory.WithDbAsync(db => db.Users
            .Where(u => u.Email == "admin@zinnur.uz")
            .Select(u => u.Id)
            .FirstAsync());

    /// <summary>
    /// Kurs + BITTA modul + <paramref name="lessonCount"/> ta dars (API orqali,
    /// ya'ni tartib raqamlari haqiqiy oqim bilan bir xil bo'ladi).
    /// </summary>
    private async Task<(long CourseId, long ModuleId, List<long> LessonIds)> CourseWithLessonsAsync(
        int lessonCount)
    {
        using var admin = await AdminClientAsync();

        var course = await admin.PostAsJsonAsync(
            "/api/v1/courses",
            new { name = "VS kurs " + Guid.NewGuid().ToString("N")[..8] });

        await EnsureStatusAsync(course, HttpStatusCode.Created);
        var courseId = (await course.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var module = await admin.PostAsJsonAsync(
            $"/api/v1/courses/{courseId}/modules", new { name = "Modul 1" });

        await EnsureStatusAsync(module, HttpStatusCode.Created);
        var moduleId = (await module.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var lessonIds = new List<long>(lessonCount);

        for (var number = 1; number <= lessonCount; number++)
        {
            var lesson = await admin.PostAsJsonAsync(
                $"/api/v1/courses/{courseId}/modules/{moduleId}/lessons",
                new
                {
                    name = "Dars " + number.ToString(CultureInfo.InvariantCulture),
                    description = "Mazmun " + number.ToString(CultureInfo.InvariantCulture),
                });

            await EnsureStatusAsync(lesson, HttpStatusCode.Created);
            lessonIds.Add((await lesson.Content.ReadFromJsonAsync<IdRow>())!.Id);
        }

        return (courseId, moduleId, lessonIds);
    }

    private Task<string> ModuleNameAsync(long moduleId) =>
        factory.WithDbAsync(db => db.Modules
            .Where(m => m.Id == moduleId)
            .Select(m => m.Name)
            .FirstAsync());

    /// <summary>
    /// Guruh so'rovi tanasi. `videoStartLessonId` ATAYLAB har chaqiruvda
    /// oshkor beriladi — `PUT` TO'LIQ ALMASHTIRISH bo'lgani uchun uni
    /// "eslab qolish" mumkin emas.
    /// </summary>
    private static object Payload(string name, long? courseId, long? videoStartLessonId) => new
    {
        name,
        startDate = FutureStart().ToString("O", CultureInfo.InvariantCulture),
        weekdays = MondayWednesday,
        startTime = StartTime,
        type = nameof(GroupType.Group),
        durationMinutes = DurationMinutes,
        courseMonths = CourseMonths,
        courseId,
        videoStartLessonId,
        isActive = true,
    };

    private static async Task<long> CreateGroupAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/v1/groups", payload);
        await EnsureStatusAsync(response, HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<CreateGroupRow>())!.Group.Id;
    }

    private Task<long?> StoredStartLessonAsync(long groupId) =>
        factory.WithDbAsync(db => db.Groups
            .Where(g => g.Id == groupId)
            .Select(g => g.VideoStartLessonId)
            .FirstAsync());

    /// <summary>
    /// Kursga biriktirilgan YANGI guruh + yangi o'quvchi — TO'G'RIDAN-TO'G'RI
    /// bazaga.
    ///
    /// NIMA UCHUN API ORQALI EMAS: `POST /groups` butun kurs jadvalini
    /// (~70 dars) generatsiya qiladi, bu esa gating testiga umuman kerak
    /// emas. Sur'at YAKUNLANGAN darslardan hisoblanadi, ya'ni jadvalsiz
    /// guruhda u 0 — testga aynan shu kerak.
    /// </summary>
    private async Task<long> StudentInGroupAsync(long courseId, long? videoStartLessonId)
    {
        var (_, _, studentId) = await StudentWithLoginInGroupAsync(courseId, videoStartLessonId);
        return studentId;
    }

    private async Task<(string Email, string Password, long StudentId)> StudentWithLoginInGroupAsync(
        long courseId, long? videoStartLessonId)
    {
        const string password = "Student!2345";
        var email = $"vs-{Guid.NewGuid():N}"[..18] + "@zinnur.uz";

        var hash = await new HasherProxy(factory).HashAsync(password);

        var studentId = await factory.WithDbAsync(async db =>
        {
            var student = new User
            {
                FullName = "Video Start O'quvchisi",
                Email = email,
                PasswordHash = hash,
                Role = UserRole.Student,
                IsActive = true,
            };

            var group = new Group
            {
                Name = "VS guruh " + Guid.NewGuid().ToString("N")[..6],
                CourseId = courseId,
                VideoStartLessonId = videoStartLessonId,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Weekdays = [DayOfWeek.Monday, DayOfWeek.Wednesday],
            };

            db.Users.Add(student);
            db.Groups.Add(group);
            await db.SaveChangesAsync();

            db.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                StudentId = student.Id,
                Status = MemberStatus.Active,
            });

            await db.SaveChangesAsync();
            return student.Id;
        });

        return (email, password, studentId);
    }

    /// <summary>Gating keshini tozalab, daraxtni QAYTA hisoblaydi.</summary>
    private async Task<CourseGateDto> GateAsync(long studentId)
    {
        using var scope = factory.Services.CreateScope();

        var gating = scope.ServiceProvider.GetRequiredService<IGatingService>();

        // Redis dev stack'i testlar bilan BO'LISHILADI va kalit faqat
        // o'quvchi Id'si bo'yicha — boshqa test bazasidagi bir xil Id'li
        // o'quvchining keshi tasodifan mos kelishi mumkin.
        await gating.InvalidateAsync(studentId);

        return await gating.GetCourseGateAsync(studentId);
    }

    private async Task InvalidateGateAsync(long studentId)
    {
        using var scope = factory.Services.CreateScope();

        var gating = scope.ServiceProvider.GetRequiredService<IGatingService>();
        await gating.InvalidateAsync(studentId);
    }

    /// <summary><c>problem.errors["videoStartLessonId"][0]</c> ni o'qiydi.</summary>
    private static async Task<string> FieldErrorAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemRow>();

        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey("videoStartLessonId",
            "frontend xatoni AYNI maydon yoniga qo'yishi kerak");

        return problem.Errors["videoStartLessonId"][0];
    }

    /// <summary>
    /// Holatni tekshiradi va xato bo'lsa JAVOB TANASINI ko'rsatadi.
    /// (Tanani FluentAssertions `because` ga berish MUMKIN EMAS — u satrni
    /// `string.Format` bilan qayta ishlaydi va JSON ichidagi `{` `}`
    /// belgilari testni formatlash xatosi bilan yiqitadi.)
    /// </summary>
    private static async Task EnsureStatusAsync(
        HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected) return;

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail(
            "Kutilgan holat " + expected.ToString()
            + ", olingan " + response.StatusCode.ToString()
            + ". Javob tanasi: " + body);
    }

    /// <summary>Kelasi hafta dushanbasi — o'tgan dars bo'lmasin (sur'at = 0).</summary>
    private static DateOnly FutureStart()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);

        return date;
    }

    // ---------------------------------------------------------------- javob shakllari

    private sealed record IdRow(long Id);

    private sealed record CreateGroupRow(GroupRow Group, int SessionsCreated);

    private sealed record UpdateGroupRow(GroupRow Group);

    private sealed record GroupRow(
        long Id,
        string Name,
        long? CourseId,
        string? CourseName,
        long? VideoStartLessonId,
        string? VideoStartLessonName,
        string? VideoStartModuleName);

    private sealed record TreeRow(long Id, string Name, List<TreeModuleRow> Modules);

    private sealed record TreeModuleRow(long Id, string Name, List<TreeLessonRow> Lessons);

    private sealed record TreeLessonRow(
        long Id,
        string Name,
        string? Description,
        bool Unlocked,
        string? LockReason,
        bool Completed);

    private sealed record ProblemRow(Dictionary<string, string[]> Errors);
}
