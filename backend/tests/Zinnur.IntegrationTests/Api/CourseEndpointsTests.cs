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
/// Kurs kontenti endpointlari (FAZA 3.1): kurs -> modul -> dars.
///
/// ★ Eng muhim tekshiruvlar:
///   • TARTIB (`Position`) zich, noyob va BARQAROR bo'lishi;
///   • daraxt tartibi `GatingService` tartibi bilan AYNAN bir xilligi;
///   • o'quvchi javobi bor kontent o'chirilmasligi (409);
///   • ustoz kontentni o'zgartira olmasligi (403);
///   • o'quvchi QULFLANGAN darsning mazmunini ko'rmasligi.
/// </summary>
public sealed class CourseEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string StudentEmail = "student@zinnur.uz";
    private const string TeacherEmail = "teacher@zinnur.uz";
    private const string DemoPassword = "Demo!2345";

    // ================================================================== TARTIB

    /// <summary>Yangi darslar oxiriga ZICH raqam bilan qo'shiladi (0,1,2...).</summary>
    [Fact]
    public async Task CreateLessons_AssignsDenseAscendingPositions()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        await CreateLessonAsync(courseId, moduleId, "A");
        await CreateLessonAsync(courseId, moduleId, "B");
        await CreateLessonAsync(courseId, moduleId, "C");

        var lessons = await LessonsOfAsync(courseId, moduleId);

        lessons.ConvertAll(l => l.Position).Should().Equal(0, 1, 2);
        lessons.ConvertAll(l => l.Name).Should().Equal("A", "B", "C");
    }

    /// <summary>
    /// ★★ REORDER: yuborilgan ketma-ketlik AYNAN 0,1,2... bo'lib yoziladi va
    /// daraxt o'sha tartibda qaytadi.
    /// </summary>
    [Fact]
    public async Task ReorderLessons_RenumbersDenselyAndPersists()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        var a = await CreateLessonAsync(courseId, moduleId, "A");
        var b = await CreateLessonAsync(courseId, moduleId, "B");
        var c = await CreateLessonAsync(courseId, moduleId, "C");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            LessonReorderUri(courseId, moduleId),
            new { orderedIds = new[] { c, a, b } });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var positions = await response.Content.ReadFromJsonAsync<List<PositionRow>>();

        positions!.ConvertAll(p => p.Id).Should().Equal(c, a, b);
        positions.ConvertAll(p => p.Position).Should().Equal(0, 1, 2);

        // Bazadagi holat ham o'zgargan bo'lishi kerak (javob "yolg'on" bo'lmasin).
        var lessons = await LessonsOfAsync(courseId, moduleId);

        lessons.ConvertAll(l => l.Id).Should().Equal(c, a, b);
        lessons.ConvertAll(l => l.Position).Should().Equal(0, 1, 2);
    }

    /// <summary>Modullar tartibi ham daraxtga darhol ta'sir qiladi.</summary>
    [Fact]
    public async Task ReorderModules_ChangesTreeOrder()
    {
        var courseId = await CreateCourseAsync("Modul tartibi");

        var first = await CreateModuleAsync(courseId, "Birinchi");
        var second = await CreateModuleAsync(courseId, "Ikkinchi");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            ModuleReorderUri(courseId),
            new { orderedIds = new[] { second, first } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tree = await TreeAsync(admin, courseId);

        tree.Modules.ConvertAll(m => m.Id).Should().Equal(second, first);
        tree.Modules.ConvertAll(m => m.Position).Should().Equal(0, 1);
    }

    /// <summary>★ To'liq bo'lmagan ro'yxat — 400 va HECH NARSA o'zgarmaydi.</summary>
    [Fact]
    public async Task ReorderLessons_WithIncompleteList_ReturnsBadRequestAndKeepsOrder()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        var a = await CreateLessonAsync(courseId, moduleId, "A");
        await CreateLessonAsync(courseId, moduleId, "B");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            LessonReorderUri(courseId, moduleId), new { orderedIds = new[] { a } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Yarim tartib YOZILMAGAN bo'lishi kerak.
        var lessons = await LessonsOfAsync(courseId, moduleId);
        lessons.ConvertAll(l => l.Position).Should().Equal(0, 1);
    }

    [Fact]
    public async Task ReorderLessons_WithForeignId_ReturnsBadRequest()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        var a = await CreateLessonAsync(courseId, moduleId, "A");
        var b = await CreateLessonAsync(courseId, moduleId, "B");

        // `b` o'rniga umuman boshqa moduldagi dars yuboriladi.
        var (otherCourse, otherModule) = await NewCourseWithModuleAsync();
        var foreign = await CreateLessonAsync(otherCourse, otherModule, "Begona");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            LessonReorderUri(courseId, moduleId),
            new { orderedIds = new[] { a, foreign } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var lessons = await LessonsOfAsync(courseId, moduleId);
        lessons.ConvertAll(l => l.Id).Should().Equal(a, b);
    }

    [Fact]
    public async Task ReorderLessons_WithDuplicateId_ReturnsBadRequest()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        var a = await CreateLessonAsync(courseId, moduleId, "A");
        await CreateLessonAsync(courseId, moduleId, "B");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            LessonReorderUri(courseId, moduleId),
            new { orderedIds = new[] { a, a } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>O'chirilgan dars "teshik" qoldirmaydi — qolganlari qayta raqamlanadi.</summary>
    [Fact]
    public async Task DeleteLesson_ClosesPositionGap()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        var a = await CreateLessonAsync(courseId, moduleId, "A");
        var b = await CreateLessonAsync(courseId, moduleId, "B");
        var c = await CreateLessonAsync(courseId, moduleId, "C");

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(LessonUri(courseId, moduleId, b));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var lessons = await LessonsOfAsync(courseId, moduleId);

        lessons.ConvertAll(l => l.Id).Should().Equal(a, c);

        // Tartib ZICH qolishi kerak — o'chirilgan dars "teshik" qoldirmasin.
        lessons.ConvertAll(l => l.Position).Should().Equal(0, 1);
    }

    /// <summary>Modul o'chirilganda qolgan modullar ham qayta raqamlanadi.</summary>
    [Fact]
    public async Task DeleteModule_ClosesPositionGap()
    {
        var courseId = await CreateCourseAsync("Modul o'chirish");

        var first = await CreateModuleAsync(courseId, "1");
        var second = await CreateModuleAsync(courseId, "2");
        var third = await CreateModuleAsync(courseId, "3");

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(ModuleUri(courseId, second));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tree = await TreeAsync(admin, courseId);

        tree.Modules.ConvertAll(m => m.Id).Should().Equal(first, third);
        tree.Modules.ConvertAll(m => m.Position).Should().Equal(0, 1);
    }

    // ================================================================== ★ GATING BILAN MOSLIK

    /// <summary>
    /// ★★★ DARAXT TARTIBI = GATING TARTIBI.
    ///
    /// `GatingService.OrderedLessons` darslarni
    /// `Modul.Position -> ModuleId -> Dars.Position -> Dars.Id` bo'yicha
    /// o'qiydi. API daraxti AYNAN shu ketma-ketlikni qaytarishi SHART —
    /// aks holda o'quvchi ko'rgan "3-dars" bilan gating ochgan "3-dars"
    /// boshqa-boshqa dars bo'lib qolardi.
    ///
    /// Tekshiruv modullar va darslar ATAYLAB aralashtirilgandan keyin
    /// bajariladi (tartib raqamlari yaratilish tartibiga teng bo'lmasin).
    /// </summary>
    [Fact]
    public async Task CourseTree_LessonOrder_MatchesGatingOrderingExpression()
    {
        var courseId = await CreateCourseAsync("Gating tartibi");

        var moduleOne = await CreateModuleAsync(courseId, "M1");
        var moduleTwo = await CreateModuleAsync(courseId, "M2");

        var a1 = await CreateLessonAsync(courseId, moduleOne, "A1");
        var a2 = await CreateLessonAsync(courseId, moduleOne, "A2");
        var b1 = await CreateLessonAsync(courseId, moduleTwo, "B1");
        var b2 = await CreateLessonAsync(courseId, moduleTwo, "B2");

        using var admin = await AdminClientAsync();

        // Aralashtiramiz: modullar teskari, birinchi modulning darslari teskari.
        await admin.PostAsJsonAsync(ModuleReorderUri(courseId),
            new { orderedIds = new[] { moduleTwo, moduleOne } });

        await admin.PostAsJsonAsync(LessonReorderUri(courseId, moduleOne),
            new { orderedIds = new[] { a2, a1 } });

        var tree = await TreeAsync(admin, courseId);

        var fromApi = tree.Modules.SelectMany(m => m.Lessons).Select(l => l.Id).ToList();

        // AYNAN `GatingService.OrderedLessons` ifodasi.
        var fromGatingExpression = await factory.WithDbAsync(db => db.ModuleLessons
            .AsNoTracking()
            .Where(l => l.Module!.CourseId == courseId)
            .OrderBy(l => l.Module!.Position)
            .ThenBy(l => l.ModuleId)
            .ThenBy(l => l.Position)
            .ThenBy(l => l.Id)
            .Select(l => l.Id)
            .ToListAsync());

        fromApi.Should().Equal(fromGatingExpression);
        fromApi.Should().Equal(b1, b2, a2, a1);
    }

    /// <summary>
    /// ★★★ HAQIQIY `GatingService` bilan tekshiruv: uning `Index` bo'yicha
    /// saflangan darslari API daraxti bilan bir xil bo'lishi kerak.
    ///
    /// Bu — "gating buzilmagan" degan ENG kuchli isbot: ikki mustaqil kod
    /// yo'li bitta ketma-ketlikka kelishi.
    /// </summary>
    [Fact]
    public async Task CourseTree_LessonOrder_MatchesGatingServiceIndexes()
    {
        var courseId = await CreateCourseAsync("Gating servisi tartibi");

        var moduleOne = await CreateModuleAsync(courseId, "M1");
        var moduleTwo = await CreateModuleAsync(courseId, "M2");

        await CreateLessonAsync(courseId, moduleOne, "A1");
        await CreateLessonAsync(courseId, moduleOne, "A2");
        await CreateLessonAsync(courseId, moduleTwo, "B1");

        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(ModuleReorderUri(courseId),
            new { orderedIds = new[] { moduleTwo, moduleOne } });

        var (_, _, studentId) = await CreateStudentInCourseAsync(courseId);

        var gate = await GateAsync(studentId);

        gate.CourseId.Should().Be(courseId);

        var fromGating = gate.Lessons.OrderBy(l => l.Index).Select(l => l.LessonId).ToList();

        var tree = await TreeAsync(admin, courseId);
        var fromApi = tree.Modules.SelectMany(m => m.Lessons).Select(l => l.Id).ToList();

        fromApi.Should().Equal(fromGating);
    }

    // ================================================================== ★ O'CHIRISH HIMOYASI

    /// <summary>
    /// ★★ O'quvchi javob topshirgan darsni o'chirib bo'lmaydi.
    /// Cascade bilan javob ham, baho ham yo'qolib ketardi.
    /// </summary>
    [Fact]
    public async Task DeleteLesson_WithSubmission_ReturnsConflict()
    {
        var (courseId, moduleId, lessonId) = await CourseWithSubmittedWorkAsync();

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(LessonUri(courseId, moduleId, lessonId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("topshirilgan vazifa");
    }

    /// <summary>Modul ichidagi dars himoyalangan bo'lsa, modul ham o'chmaydi.</summary>
    [Fact]
    public async Task DeleteModule_WithSubmissionInside_ReturnsConflict()
    {
        var (courseId, moduleId, _) = await CourseWithSubmittedWorkAsync();

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(ModuleUri(courseId, moduleId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Kurs ham xuddi shunday himoyalanadi.</summary>
    [Fact]
    public async Task DeleteCourse_WithSubmissionInside_ReturnsConflict()
    {
        var (courseId, _, _) = await CourseWithSubmittedWorkAsync();

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(CourseUri(courseId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// ★ Guruhga biriktirilgan kurs o'chirilmaydi: `Group.CourseId` FK'si
    /// `SetNull`, ya'ni guruhlar JIMGINA kurssiz qolib, ularning barcha
    /// o'quvchilari uchun darslar qulflanib qolardi.
    /// </summary>
    [Fact]
    public async Task DeleteCourse_WithAttachedGroup_ReturnsConflict()
    {
        var courseId = await CreateCourseAsync("Guruhli kurs");

        await CreateStudentInCourseAsync(courseId);

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(CourseUri(courseId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("guruh");
    }

    /// <summary>Mehnat bo'lmasa o'chirish ODDIY ishlaydi (himoya haddan oshmasin).</summary>
    [Fact]
    public async Task DeleteCourse_WithoutWorkOrGroups_ReturnsNoContent()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        await CreateLessonAsync(courseId, moduleId, "Oddiy dars");

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(CourseUri(courseId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var exists = await factory.WithDbAsync(db => db.Courses.AnyAsync(c => c.Id == courseId));
        exists.Should().BeFalse();
    }

    // ================================================================== ★ RUXSAT

    /// <summary>★★ USTOZ kontentni O'ZGARTIRA OLMAYDI — u barcha guruhlarga tegishli.</summary>
    [Fact]
    public async Task Create_AsTeacher_ReturnsForbidden()
    {
        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var response = await teacher.PostAsJsonAsync(
            new Uri("/api/v1/courses", UriKind.Relative),
            new { name = "Ustoz kursi" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReorderLessons_AsTeacher_ReturnsForbidden()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        var a = await CreateLessonAsync(courseId, moduleId, "A");
        var b = await CreateLessonAsync(courseId, moduleId, "B");

        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var response = await teacher.PostAsJsonAsync(
            LessonReorderUri(courseId, moduleId), new { orderedIds = new[] { b, a } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Tartib O'ZGARMAGAN bo'lishi kerak.
        var lessons = await LessonsOfAsync(courseId, moduleId);
        lessons.ConvertAll(l => l.Id).Should().Equal(a, b);
    }

    [Fact]
    public async Task DeleteCourse_AsTeacher_ReturnsForbidden()
    {
        var courseId = await CreateCourseAsync("Ustoz o'chira olmaydi");

        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var response = await teacher.DeleteAsync(CourseUri(courseId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var exists = await factory.WithDbAsync(db => db.Courses.AnyAsync(c => c.Id == courseId));
        exists.Should().BeTrue();
    }

    /// <summary>Ustoz KO'RA oladi — faqat o'zgartira olmaydi.</summary>
    [Fact]
    public async Task Get_AsTeacher_ReturnsFullTree()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        await CreateLessonAsync(courseId, moduleId, "Ko'rinadigan dars", "Mazmun bor");

        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var tree = await TreeAsync(teacher, courseId);

        var lesson = tree.Modules.Single().Lessons.Single();

        lesson.Unlocked.Should().BeTrue("xodimga gating qo'llanmaydi");
        lesson.Description.Should().Be("Mazmun bor");
    }

    [Fact]
    public async Task Create_AsStudent_ReturnsForbidden()
    {
        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var response = await student.PostAsJsonAsync(
            new Uri("/api/v1/courses", UriKind.Relative),
            new { name = "O'quvchi kursi" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================== ★ O'QUVCHI KO'RINISHI

    /// <summary>
    /// ★★ QULFLANGAN DARS: sarlavha KO'RINADI, mazmun YO'Q.
    ///
    /// Yangi guruhda yakunlangan ustoz darsi yo'q -> sur'at = 0 -> faqat
    /// BIRINCHI dars ochiq. Ikkinchisining `description` maydoni `null`
    /// bo'lishi SHART.
    /// </summary>
    [Fact]
    public async Task Tree_AsStudent_HidesLockedLessonContent()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        await CreateLessonAsync(courseId, moduleId, "Ochiq dars", "Birinchi mazmun");
        await CreateLessonAsync(courseId, moduleId, "Yopiq dars", "Sir mazmun");

        var (email, password, studentId) = await CreateStudentInCourseAsync(courseId);
        await InvalidateGateAsync(studentId);

        using var student = await ClientAsync(email, password);

        var tree = await TreeAsync(student, courseId);
        var lessons = tree.Modules.Single().Lessons;

        lessons.Should().HaveCount(2);

        lessons[0].Unlocked.Should().BeTrue("birinchi dars DOIM ochiq");
        lessons[0].Description.Should().Be("Birinchi mazmun");

        lessons[1].Unlocked.Should().BeFalse();
        lessons[1].Name.Should().Be("Yopiq dars", "sarlavha ko'rinishi kerak");
        lessons[1].Description.Should().BeNull("qulflangan darsning MAZMUNI berilmaydi");
        lessons[1].LockReason.Should().NotBeNull();

        // ★★ QULFLANGAN DARS HECH QACHON "tugatilgan" emas.
        //
        // Ichki gating qoidasi uchun bu dars TEXNIK jihatdan "tugatilgan"
        // (unda vazifa ham, test ham yo'q — ya'ni talab qolmagan), lekin
        // tashqi shartnomada `completed` OCHIQLIKKA bo'ysunadi: o'quvchi
        // darsni ocha ham olmagan bo'lsa, uni yashil ko'rsatish yolg'on
        // bo'lardi va vazifasi yo'q kurs butunlay yashil chiqardi.
        lessons[1].Completed.Should().BeFalse("qulflangan darsni tugatib bo'lmaydi");
    }

    // ================================================================== ★ TUGATILGANMI (Completed)

    /// <summary>
    /// ★★ <c>Completed</c> ("TUGATILGAN") <c>Unlocked</c> ("OCHIQ") DAN
    /// ALOHIDA maydon: dars ochiq, lekin hali tugatilmagan bo'lishi
    /// mumkin — o'quvchi hozir o'tirgan dars aynan shunday.
    ///
    /// Stsenariy: darsga KURS VAZIFASI biriktirilgan.
    ///   • topshirilmagan -> `unlocked: true`, `completed: false`;
    ///   • topshirilgan   -> `completed: true`.
    ///
    /// Frontend "ochilgan"ni "tugatilgan" deb ko'rsatsa, o'quvchi
    /// yo'lakchani yashil ko'rib vazifasini topshirmasdi va keyingi dars
    /// nega ochilmayotganini tushunmasdi.
    /// </summary>
    [Fact]
    public async Task Tree_AsStudent_MarksLessonCompletedOnlyAfterTheWorkIsSubmitted()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Vazifali dars", "Mazmun");

        var (email, password, studentId) = await CreateStudentInCourseAsync(courseId);

        using var admin = await AdminClientAsync();

        var created = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new { title = "Kurs vazifasi", moduleLessonId = lessonId, maxScore = 5m });

        created.StatusCode.Should().Be(HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());

        var assignment = await created.Content.ReadFromJsonAsync<AssignmentRow>();

        await InvalidateGateAsync(studentId);

        using var student = await ClientAsync(email, password);

        var before = (await TreeAsync(student, courseId)).Modules.Single().Lessons.Single();

        before.HasAssignment.Should().BeTrue();
        before.Unlocked.Should().BeTrue("birinchi dars DOIM ochiq");
        before.Completed.Should().BeFalse("vazifa hali topshirilmagan");

        await factory.WithDbAsync(async db =>
        {
            db.Submissions.Add(Submission.Create(
                assignment!.Id, studentId, "Mening javobim", isLate: false, DateTimeOffset.UtcNow));

            return await db.SaveChangesAsync();
        });

        await InvalidateGateAsync(studentId);

        var after = (await TreeAsync(student, courseId)).Modules.Single().Lessons.Single();

        after.Unlocked.Should().BeTrue();
        after.Completed.Should().BeTrue("vazifa topshirildi -> dars tugatildi");
    }

    /// <summary>
    /// ★ SHARTNOMA UCHUN MUHIM: TALABI YO'Q **OCHIQ** dars DARHOL
    /// "tugatilgan" bo'ladi (vazifasi ham, e'lon qilingan testi ham yo'q;
    /// video kontenti hali modellashtirilmagan — `GatingService` izohiga
    /// qarang).
    ///
    /// Bu g'alati emas, AYNI qoida: gating keyingi darsni ochish uchun
    /// ham "oldingisi tugatilgan" shartini AYNAN shunday hisoblaydi.
    /// Boshqacha bo'lganda kurs birinchi darsdayoq abadiy qulflanardi.
    /// Ya'ni frontend "tugatilgan" belgisini shu ta'rifga qarab chizishi
    /// kerak: u "o'quvchi mehnat qildi" emas, "shu darsda TALAB
    /// qilinadigan hech nima qolmadi" degani.
    /// </summary>
    [Fact]
    public async Task Tree_AsStudent_UnlockedLessonWithoutAnyRequirement_IsAlreadyCompleted()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        await CreateLessonAsync(courseId, moduleId, "Talabsiz dars", "Mazmun");

        var (email, password, studentId) = await CreateStudentInCourseAsync(courseId);
        await InvalidateGateAsync(studentId);

        using var student = await ClientAsync(email, password);

        var lesson = (await TreeAsync(student, courseId)).Modules.Single().Lessons.Single();

        lesson.HasAssignment.Should().BeFalse();
        lesson.HasTest.Should().BeFalse();
        lesson.Completed.Should().BeTrue();
    }

    /// <summary>
    /// ★ XODIM uchun <c>completed</c> DOIM <c>false</c>: "tugatish" —
    /// o'quvchi progressi, xodimda esa progress yozuvi umuman bo'lmaydi.
    /// (<c>unlocked</c> esa aksincha, xodimda doim <c>true</c>.)
    /// </summary>
    [Fact]
    public async Task Tree_AsTeacher_NeverMarksLessonsCompleted()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        await CreateLessonAsync(courseId, moduleId, "Xodim ko'radigan dars", "Mazmun");

        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var lesson = (await TreeAsync(teacher, courseId)).Modules.Single().Lessons.Single();

        lesson.Unlocked.Should().BeTrue();
        lesson.Completed.Should().BeFalse();
    }

    /// <summary>O'quvchi faqat O'Z kursini ko'radi.</summary>
    [Fact]
    public async Task List_AsStudent_ReturnsOnlyOwnCourse()
    {
        var (courseId, _) = await NewCourseWithModuleAsync();

        var (email, password, _) = await CreateStudentInCourseAsync(courseId);

        // Begona kurs — ro'yxatda CHIQMASLIGI kerak.
        await CreateCourseAsync("Begona kurs");

        using var student = await ClientAsync(email, password);

        var page = await student.GetFromJsonAsync<PagedRows>("/api/v1/courses");

        page!.Items.Should().ContainSingle();
        page.Items[0].Id.Should().Be(courseId);
    }

    /// <summary>Begona kursning daraxti o'quvchiga BERILMAYDI.</summary>
    [Fact]
    public async Task Get_AsStudent_ForeignCourse_ReturnsForbidden()
    {
        var (ownCourseId, _) = await NewCourseWithModuleAsync();
        var foreignCourseId = await CreateCourseAsync("Begona daraxt");

        var (email, password, _) = await CreateStudentInCourseAsync(ownCourseId);

        using var student = await ClientAsync(email, password);

        var response = await student.GetAsync(CourseUri(foreignCourseId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================== CRUD asoslari

    [Fact]
    public async Task Update_ChangesNameButKeepsPosition()
    {
        var courseId = await CreateCourseAsync("Eski nom");

        using var admin = await AdminClientAsync();

        var before = await admin.GetFromJsonAsync<CourseRow>(CourseUri(courseId));

        var response = await admin.PutAsJsonAsync(
            CourseUri(courseId), new { name = "Yangi nom", isActive = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<CourseRow>();

        updated!.Name.Should().Be("Yangi nom");
        updated.Position.Should().Be(before!.Position, "tartib faqat reorder bilan o'zgaradi");
    }

    [Fact]
    public async Task Create_WithBlankName_ReturnsBadRequest()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/courses", UriKind.Relative), new { name = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Modul boshqa kursning manzili orqali tahrirlanmaydi (ierarxiya haqiqiy).</summary>
    [Fact]
    public async Task UpdateModule_ThroughWrongCourse_ReturnsNotFound()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var otherCourseId = await CreateCourseAsync("Boshqa kurs");

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            ModuleUri(otherCourseId, moduleId), new { name = "O'g'irlangan modul" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================== yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var tokens = await factory.LoginAsync(email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<long> CreateCourseAsync(string name)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/courses", UriKind.Relative),
            new { name = name + " " + Guid.NewGuid().ToString("N")[..6] });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<CourseRow>();
        return created!.Id;
    }

    private async Task<long> CreateModuleAsync(long courseId, string name)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(ModulesUri(courseId), new { name });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<ModuleRow>();
        return created!.Id;
    }

    private async Task<long> CreateLessonAsync(
        long courseId, long moduleId, string name, string? description = null)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            LessonsUri(courseId, moduleId), new { name, description });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<LessonRow>();
        return created!.Id;
    }

    private async Task<(long CourseId, long ModuleId)> NewCourseWithModuleAsync()
    {
        var courseId = await CreateCourseAsync("Kurs");
        var moduleId = await CreateModuleAsync(courseId, "Modul");
        return (courseId, moduleId);
    }

    private static async Task<TreeRow> TreeAsync(HttpClient client, long courseId) =>
        await client.GetFromJsonAsync<TreeRow>(CourseUri(courseId))
        ?? throw new InvalidOperationException("Kurs daraxti bo'sh keldi.");

    private async Task<List<LessonRow>> LessonsOfAsync(long courseId, long moduleId)
    {
        using var admin = await AdminClientAsync();

        var tree = await TreeAsync(admin, courseId);

        return tree.Modules.Single(m => m.Id == moduleId).Lessons;
    }

    /// <summary>
    /// Kursga biriktirilgan YANGI guruhdagi yangi o'quvchi
    /// (`AssignmentEndpointsTests` dagi naqsh bilan bir xil).
    /// </summary>
    private async Task<(string Email, string Password, long StudentId)> CreateStudentInCourseAsync(
        long courseId)
    {
        const string password = "Student!2345";
        var email = $"course-{Guid.NewGuid():N}"[..18] + "@zinnur.uz";

        var hasher = new HasherProxy(factory);
        var hash = await hasher.HashAsync(password);

        var studentId = await factory.WithDbAsync(async db =>
        {
            var student = new User
            {
                FullName = "Kurs O'quvchisi",
                Email = email,
                PasswordHash = hash,
                Role = UserRole.Student,
                IsActive = true,
            };

            var group = new Group
            {
                Name = "Kurs guruh " + Guid.NewGuid().ToString("N")[..6],
                CourseId = courseId,
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

    /// <summary>
    /// Kurs + modul + dars + KURS VAZIFASI + o'quvchining TOPSHIRILGAN javobi.
    ///
    /// Javob to'g'ridan-to'g'ri bazaga yoziladi: bu yerda topshirish OQIMI
    /// emas, o'chirish HIMOYASI tekshiriladi — guruh/gating shartlarini
    /// qurish testni bekorga murakkablashtirardi.
    /// </summary>
    private async Task<(long CourseId, long ModuleId, long LessonId)> CourseWithSubmittedWorkAsync()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Mehnatli dars");

        using var admin = await AdminClientAsync();

        var created = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new { title = "Kurs vazifasi", moduleLessonId = lessonId, maxScore = 5m });

        created.StatusCode.Should().Be(HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());

        var assignment = await created.Content.ReadFromJsonAsync<AssignmentRow>();

        await factory.WithDbAsync(async db =>
        {
            var studentId = await db.Users
                .Where(u => u.Email == StudentEmail)
                .Select(u => u.Id)
                .FirstAsync();

            db.Submissions.Add(Submission.Create(
                assignment!.Id, studentId, "Mening javobim", isLate: false, DateTimeOffset.UtcNow));

            return await db.SaveChangesAsync();
        });

        return (courseId, moduleId, lessonId);
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

    // ---------------------------------------------------------------- manzillar

    private static Uri CourseUri(long courseId) =>
        Relative($"/api/v1/courses/{courseId}");

    private static Uri ModulesUri(long courseId) =>
        Relative($"/api/v1/courses/{courseId}/modules");

    private static Uri ModuleUri(long courseId, long moduleId) =>
        Relative($"/api/v1/courses/{courseId}/modules/{moduleId}");

    private static Uri ModuleReorderUri(long courseId) =>
        Relative($"/api/v1/courses/{courseId}/modules/reorder");

    private static Uri LessonsUri(long courseId, long moduleId) =>
        Relative($"/api/v1/courses/{courseId}/modules/{moduleId}/lessons");

    private static Uri LessonUri(long courseId, long moduleId, long lessonId) =>
        Relative($"/api/v1/courses/{courseId}/modules/{moduleId}/lessons/{lessonId}");

    private static Uri LessonReorderUri(long courseId, long moduleId) =>
        Relative($"/api/v1/courses/{courseId}/modules/{moduleId}/lessons/reorder");

    private static Uri Relative(FormattableString path) =>
        new(path.ToString(CultureInfo.InvariantCulture), UriKind.Relative);

    // ---------------------------------------------------------------- javob shakllari

    private sealed record CourseRow(
        long Id, string Name, string? Description, bool IsActive, int Position,
        int ModuleCount, int LessonCount, int GroupCount);

    private sealed record TreeRow(
        long Id, string Name, string? Description, bool IsActive, int Position,
        List<ModuleRow> Modules);

    private sealed record ModuleRow(
        long Id, long CourseId, string Name, int Position, List<LessonRow> Lessons);

    /// <summary>
    /// ★ <c>Completed</c> — "dars TUGATILGANMI". <c>Unlocked</c> ("dars
    /// OCHIQMI") dan alohida maydon; ikkisi boshqa-boshqa savol.
    /// </summary>
    private sealed record LessonRow(
        long Id, long ModuleId, string Name, string? Description, int Position,
        int? DurationMin, bool Unlocked, string? LockReason, bool Completed,
        bool HasAssignment, bool HasTest);

    private sealed record PositionRow(long Id, int Position);

    private sealed record PagedRows(List<CourseRow> Items, int Page, int PageSize, int Total);

    private sealed record AssignmentRow(long Id);
}
