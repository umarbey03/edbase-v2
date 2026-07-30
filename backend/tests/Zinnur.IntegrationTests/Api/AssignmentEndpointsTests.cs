using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Gating.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Uy vazifasi endpointlari: ruxsat, topshirish, FAYL YUKLASH chegaralari,
/// baholash va qayta topshirish qoidasi.
///
/// ★ Eng muhim tekshiruvlar:
///   • fayl hajmi OQIM DAVOMIDA cheklanishi (eski tizimning Q-2 bugi);
///   • fayl turi MAZMUNDAN aniqlanishi (klient sarlavhasiga ishonilmasligi);
///   • ombor sozlanmagan bo'lsa 503 (lokal diskka JIMGINA yozilmasligi);
///   • gating: yopiq darsning vazifasi topshirilmasligi.
/// </summary>
public sealed class AssignmentEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string StudentEmail = "student@zinnur.uz";
    private const string TeacherEmail = "teacher@zinnur.uz";
    private const string DemoPassword = "Demo!2345";

    /// <summary>PNG sehrli baytlari — haqiqiy rasm sifatida tanilishi uchun.</summary>
    private static readonly byte[] PngMagic =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // ================================================================== yaratish / ruxsat

    [Fact]
    public async Task Create_AsAcademic_ForGroup_ReturnsCreated()
    {
        using var admin = await AdminClientAsync();
        var groupId = await SeededGroupIdAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new
            {
                title = "Guruh vazifasi",
                groupId,
                maxScore = 5m,
                allowedFormats = "Text, Image",
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<AssignmentDto>();
        created!.GroupId.Should().Be(groupId);
        created.ModuleLessonId.Should().BeNull();
        created.AllowedFormats.Should().Be("Text, Image");
    }

    /// <summary>
    /// ★ Domain qoidasi: vazifa YOKI guruhga, YOKI darsga biriktiriladi.
    /// Ikkalasi ham bo'sh bo'lsa 409 (bazada ham `CHECK` bor).
    /// </summary>
    [Fact]
    public async Task Create_WithoutTarget_ReturnsConflict()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new { title = "Nishonsiz vazifa" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_WithBothTargets_ReturnsConflict()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new
            {
                title = "Ikki nishon",
                groupId = await SeededGroupIdAsync(),
                moduleLessonId = await FirstLessonIdAsync(),
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>★ Ustoz KURS vazifasini yarata olmaydi — u barcha guruhlarga tegadi.</summary>
    [Fact]
    public async Task Create_AsTeacher_ForCourseLesson_ReturnsForbidden()
    {
        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var response = await teacher.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new { title = "Ustoz kurs vazifasi", moduleLessonId = await FirstLessonIdAsync() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_AsTeacher_ForOwnGroup_ReturnsCreated()
    {
        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var response = await teacher.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new { title = "Ustozning o'z guruhi", groupId = await SeededGroupIdAsync() });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_AsStudent_ReturnsForbidden()
    {
        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var response = await student.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new { title = "O'quvchi vazifasi", groupId = await SeededGroupIdAsync() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================== topshirish (matn)

    [Fact]
    public async Task Submit_WithText_CreatesSubmissionAndShowsInMine()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Matnli javob");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var response = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Mening javobim"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var submission = await response.Content.ReadFromJsonAsync<StudentSubmissionDto>();
        submission!.Text.Should().Be("Mening javobim");
        submission.AttemptNumber.Should().Be(1);
        submission.Status.Should().Be("Submitted");

        var mine = await student.GetFromJsonAsync<List<StudentAssignmentDto>>(
            "/api/v1/assignments/mine");

        var row = mine!.Single(a => a.Id == assignmentId);
        row.MySubmission.Should().NotBeNull();
        row.CanSubmit.Should().BeFalse("ruxsatsiz qayta topshirib bo'lmaydi");
    }

    [Fact]
    public async Task Submit_WithEmptyBody_ReturnsBadRequest()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Bo'sh javob");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var response = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>★ Ruxsatsiz ikkinchi marta topshirish TAQIQ (Domain qoidasi).</summary>
    [Fact]
    public async Task Submit_TwiceWithoutPermission_ReturnsConflict()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Ikki marta topshirish");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var first = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Birinchi"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Ikkinchi"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// To'liq oqim: topshirish -> baholash -> qayta ochish -> qayta topshirish.
    /// Qayta topshirilgach ruxsat AVTOMATIK yopiladi (uchinchi urinish 409).
    /// </summary>
    [Fact]
    public async Task ReopenThenResubmit_IncrementsAttemptAndClosesPermission()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Qayta topshirish oqimi");

        using var student = await ClientAsync(StudentEmail, DemoPassword);
        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var submitted = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Birinchi"));
        var submission = await submitted.Content.ReadFromJsonAsync<StudentSubmissionDto>();

        // Baho qo'yiladi
        var graded = await teacher.PostAsJsonAsync(
            GradeUri(submission!.Id), new { score = 4m, feedback = "Yaxshi" });

        graded.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterGrade = await graded.Content.ReadFromJsonAsync<SubmissionDto>();
        afterGrade!.Score.Should().Be(4m);
        afterGrade.ScorePercent.Should().Be(80m);
        afterGrade.Status.Should().Be("Graded");

        // Kurator qayta topshirishga ruxsat beradi
        var reopened = await teacher.PostAsJsonAsync(
            ReopenUri(submission.Id), new { note = "Xattingiz o'qilmadi" });

        reopened.StatusCode.Should().Be(HttpStatusCode.OK);

        // O'quvchi qayta yuboradi -> urinish 2, eski baho TOZALANADI
        var again = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Ikkinchi"));
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await again.Content.ReadFromJsonAsync<StudentSubmissionDto>();
        second!.AttemptNumber.Should().Be(2);
        second.Text.Should().Be("Ikkinchi");
        second.Score.Should().BeNull("yangi javob kelgach eski baho haqiqiy emas");
        second.AllowResubmit.Should().BeFalse("ruxsat bir martalik");

        // Uchinchi urinish — ruxsat yopilgani uchun 409
        var third = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Uchinchi"));
        third.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Grade_AboveMaxScore_ReturnsConflict()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Baho chegarasi", maxScore: 5m);

        using var student = await ClientAsync(StudentEmail, DemoPassword);
        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var submitted = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Javob"));
        var submission = await submitted.Content.ReadFromJsonAsync<StudentSubmissionDto>();

        var response = await teacher.PostAsJsonAsync(
            GradeUri(submission!.Id), new { score = 6m });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submissions_AsTeacher_ListsStudentAnswers()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Baholash ro'yxati");

        using var student = await ClientAsync(StudentEmail, DemoPassword);
        await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Javob"));

        using var teacher = await ClientAsync(TeacherEmail, DemoPassword);

        var rows = await teacher.GetFromJsonAsync<List<SubmissionDto>>(
            $"/api/v1/assignments/{assignmentId}/submissions");

        rows.Should().ContainSingle();
        rows![0].StudentName.Should().Be("Demo O'quvchi");
    }

    [Fact]
    public async Task Delete_WithSubmissions_ReturnsConflict()
    {
        var assignmentId = await CreateGroupAssignmentAsync("O'chirilmaydigan");

        using var student = await ClientAsync(StudentEmail, DemoPassword);
        await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Javob"));

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(
            new Uri($"/api/v1/assignments/{assignmentId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "baholar bilan birga yo'qolib ketmasin");
    }

    [Fact]
    public async Task Delete_WithoutSubmissions_ReturnsNoContent()
    {
        var assignmentId = await CreateGroupAssignmentAsync("O'chiriladigan");

        using var admin = await AdminClientAsync();

        var response = await admin.DeleteAsync(
            new Uri($"/api/v1/assignments/{assignmentId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ================================================================== ★ FAYL YUKLASH

    /// <summary>
    /// ★★ HAJM CHEGARASI OQIM DAVOMIDA ishlaydi.
    ///
    /// 6 MB rasm yuboriladi (chegara 5 MB). Kutilgan: 400 va ANIQ xabar.
    /// Eski tizim faylni AVVAL to'liq xotiraga o'qib, KEYIN tekshirardi —
    /// ya'ni chegara xotirani himoya qilmasdi.
    /// </summary>
    [Fact]
    public async Task Submit_WithOversizedImage_ReturnsBadRequest()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Katta rasm");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var oversized = FakeImage(6 * 1024 * 1024);

        var response = await student.PostAsync(
            SubmitUri(assignmentId), Multipart(text: null, ("katta.png", "image/png", oversized)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("5 MB");
    }

    /// <summary>
    /// ★ Klient sarlavhasiga ISHONILMAYDI: `image/png` deb yuborilgan, lekin
    /// mazmuni oddiy matn — rad etiladi.
    /// </summary>
    [Fact]
    public async Task Submit_WithFakeContentType_ReturnsBadRequest()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Soxta tur");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var notAnImage = System.Text.Encoding.UTF8.GetBytes("#!/bin/sh\nrm -rf /\n");

        var response = await student.PostAsync(
            SubmitUri(assignmentId),
            Multipart(text: null, ("rasm.png", "image/png", notAnImage)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        // Apostrof JSON'da `\u0027` bo'lib ketadi — shuning uchun
        // apostrofsiz bo'lakni tekshiramiz.
        body.Should().Contain("Faylning turi");
    }

    /// <summary>
    /// ★ Ombor (R2/S3) sozlanmagan: 503 qaytadi va fayl HECH QAYERGA
    /// yozilmaydi. Test muhitida `Storage:*` ataylab bo'sh.
    ///
    /// Eski tizim shu holatda lokal diskka yozardi — natijada fayllar bitta
    /// konteynerga bog'lanib qolgan, ikkinchi replikada 404 bergan va
    /// deploy'da butunlay yo'qolgan edi.
    /// </summary>
    [Fact]
    public async Task Submit_WithFile_WhenStorageNotConfigured_ReturnsServiceUnavailable()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Ombor yo'q");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var response = await student.PostAsync(
            SubmitUri(assignmentId),
            Multipart(text: "Rasm bilan", ("kichik.png", "image/png", FakeImage(2048))));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Storage:", "administrator nima qilishini bilishi kerak");

        // Yarim yozuv QOLMAYDI: javob umuman yaratilmagan.
        var count = await factory.WithDbAsync(db =>
            db.Submissions.CountAsync(s => s.AssignmentId == assignmentId));

        count.Should().Be(0);
    }

    /// <summary>Vazifa faqat MATN qabul qilsa, rasm yuborish rad etiladi (Domain qoidasi).</summary>
    [Fact]
    public async Task Submit_WithDisallowedFormat_ReturnsConflict()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Faqat matn", formats: "Text");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var response = await student.PostAsync(
            SubmitUri(assignmentId),
            Multipart(text: "Matn", ("rasm.png", "image/png", FakeImage(1024))));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("rasm");
    }

    /// <summary>Bitta javobga 5 tadan ortiq fayl ilova qilinmaydi.</summary>
    [Fact]
    public async Task Submit_WithTooManyFiles_ReturnsBadRequest()
    {
        var assignmentId = await CreateGroupAssignmentAsync("Ko'p fayl");

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var files = Enumerable.Range(0, Submission.MaxAttachments + 1)
            .Select(i => (
                Name: string.Create(CultureInfo.InvariantCulture, $"rasm{i}.png"),
                Type: "image/png",
                Bytes: FakeImage(512)))
            .ToArray();

        var response = await student.PostAsync(SubmitUri(assignmentId), Multipart(null, files));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================== ★ GATING

    /// <summary>
    /// ★★ Yopiq darsning KURS vazifasi topshirilmaydi.
    ///
    /// Yangi o'quvchi + yangi guruh (ATF kursi) yaratiladi. Guruhda birorta
    /// ham YAKUNLANGAN dars yo'q, ya'ni ustoz sur'ati = 0 -> faqat BIRINCHI
    /// dars ochiq. Ikkinchi darsning vazifasi 403 bilan rad etilishi kerak.
    /// </summary>
    [Fact]
    public async Task Submit_ForLockedLesson_ReturnsForbidden()
    {
        var secondLessonId = await CreateSecondLessonAsync();
        var assignmentId = await CreateCourseAssignmentAsync("Yopiq dars vazifasi", secondLessonId);

        var (email, password, studentId) = await CreateStudentInCourseGroupAsync();
        await InvalidateGateAsync(studentId);

        using var student = await ClientAsync(email, password);

        var response = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Javob"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ustoz", "sabab: ustoz bu darsga yetib kelmagan");
    }

    /// <summary>Birinchi dars DOIM ochiq — o'quvchi kursni boshlay olishi kerak.</summary>
    [Fact]
    public async Task Submit_ForFirstLesson_IsAllowed()
    {
        var firstLessonId = await FirstLessonIdAsync();
        var assignmentId = await CreateCourseAssignmentAsync("Birinchi dars vazifasi", firstLessonId);

        var (email, password, studentId) = await CreateStudentInCourseGroupAsync();
        await InvalidateGateAsync(studentId);

        using var student = await ClientAsync(email, password);

        var response = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Javob"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>Begona guruhning vazifasi o'quvchiga KO'RINMAYDI va topshirilmaydi.</summary>
    [Fact]
    public async Task Submit_ForForeignGroupAssignment_ReturnsForbidden()
    {
        var foreignGroupId = await factory.WithDbAsync(async db =>
        {
            var group = new Group
            {
                Name = "Begona guruh " + Guid.NewGuid().ToString("N")[..6],
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Weekdays = [DayOfWeek.Tuesday, DayOfWeek.Friday],
            };

            db.Groups.Add(group);
            await db.SaveChangesAsync();
            return group.Id;
        });

        var assignmentId = await CreateGroupAssignmentAsync("Begona vazifa", groupId: foreignGroupId);

        using var student = await ClientAsync(StudentEmail, DemoPassword);

        var response = await student.PostAsync(SubmitUri(assignmentId), Multipart(text: "Javob"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    private Task<long> SeededGroupIdAsync() => factory.WithDbAsync(db =>
        db.Groups.Where(g => g.CourseId != null).OrderBy(g => g.Id).Select(g => g.Id).FirstAsync());

    private Task<long> FirstLessonIdAsync() => factory.WithDbAsync(db =>
        db.ModuleLessons.OrderBy(l => l.Position).ThenBy(l => l.Id).Select(l => l.Id).FirstAsync());

    /// <summary>Seed kursidagi modulga IKKINCHI dars qo'shadi (bir marta).</summary>
    private Task<long> CreateSecondLessonAsync() => factory.WithDbAsync(async db =>
    {
        const string Name = "Ikkinchi dars (test)";

        var existing = await db.ModuleLessons.FirstOrDefaultAsync(l => l.Name == Name);
        if (existing is not null) return existing.Id;

        var moduleId = await db.Modules.OrderBy(m => m.Id).Select(m => m.Id).FirstAsync();

        var lesson = new ModuleLesson { ModuleId = moduleId, Name = Name, Position = 2 };

        db.ModuleLessons.Add(lesson);
        await db.SaveChangesAsync();

        return lesson.Id;
    });

    /// <summary>Kursga biriktirilgan YANGI guruhdagi yangi o'quvchi.</summary>
    private async Task<(string Email, string Password, long StudentId)> CreateStudentInCourseGroupAsync()
    {
        const string password = "Student!2345";
        var email = $"gating-{Guid.NewGuid():N}"[..18] + "@zinnur.uz";

        var hasher = new HasherProxy(factory);
        var hash = await hasher.HashAsync(password);

        var studentId = await factory.WithDbAsync(async db =>
        {
            var courseId = await db.Courses.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();

            var student = new User
            {
                FullName = "Gating O'quvchi",
                Email = email,
                PasswordHash = hash,
                Role = UserRole.Student,
                IsActive = true,
            };

            var group = new Group
            {
                Name = "Gating guruh " + Guid.NewGuid().ToString("N")[..6],
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
    /// Gating keshini tozalaydi.
    ///
    /// NIMA UCHUN KERAK: Redis testlar bilan BO'LISHILADI (dev stack'ining
    /// o'sha instansiyasi). Kalit o'quvchi ID'si bo'yicha, ya'ni boshqa
    /// bazadagi bir xil ID'li o'quvchining keshi tasodifan mos kelishi
    /// mumkin. Testni deterministik qilish uchun keshni oshkor tozalaymiz.
    /// </summary>
    private async Task InvalidateGateAsync(long studentId)
    {
        using var scope = factory.Services.CreateScope();

        var gating = scope.ServiceProvider.GetRequiredService<IGatingService>();
        await gating.InvalidateAsync(studentId);
    }

    private async Task<long> CreateGroupAssignmentAsync(
        string title, decimal maxScore = 5m, string formats = "Text, Image", long? groupId = null)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new
            {
                title,
                groupId = groupId ?? await SeededGroupIdAsync(),
                maxScore,
                allowedFormats = formats,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<AssignmentDto>();
        return created!.Id;
    }

    private async Task<long> CreateCourseAssignmentAsync(string title, long moduleLessonId)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new { title, moduleLessonId, maxScore = 5m, allowedFormats = "Text, Image" });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<AssignmentDto>();
        return created!.Id;
    }

    /// <summary>Sehrli baytlari to'g'ri, qolgani to'ldiruvchi bo'lgan "rasm".</summary>
    private static byte[] FakeImage(int totalBytes)
    {
        var bytes = new byte[totalBytes];
        PngMagic.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent Multipart(
        string? text, params (string Name, string Type, byte[] Bytes)[] files)
    {
        var content = new MultipartFormDataContent();

        if (text is not null)
            content.Add(new StringContent(text), "text");

        foreach (var (name, type, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(type);
            content.Add(part, "files", name);
        }

        return content;
    }

    private static Uri SubmitUri(long assignmentId) =>
        new(string.Create(CultureInfo.InvariantCulture, $"/api/v1/assignments/{assignmentId}/submit"),
            UriKind.Relative);

    private static Uri GradeUri(long submissionId) =>
        new(string.Create(CultureInfo.InvariantCulture, $"/api/v1/submissions/{submissionId}/grade"),
            UriKind.Relative);

    private static Uri ReopenUri(long submissionId) =>
        new(string.Create(CultureInfo.InvariantCulture, $"/api/v1/submissions/{submissionId}/reopen"),
            UriKind.Relative);

    // ---------------------------------------------------------------- javob shakllari

    private sealed record AssignmentDto(
        long Id, long? GroupId, long? ModuleLessonId, string Title,
        decimal MaxScore, string AllowedFormats, int SubmissionCount);

    private sealed record StudentSubmissionDto(
        long Id, string Status, string? Text, decimal? Score, decimal? ScorePercent,
        int AttemptNumber, bool AllowResubmit, bool IsLate);

    private sealed record SubmissionDto(
        long Id, long StudentId, string StudentName, string? Text, string Status,
        decimal? Score, decimal? ScorePercent, int AttemptNumber);

    private sealed record StudentAssignmentDto(
        long Id, string Title, bool IsOverdue, bool LessonUnlocked, bool CanSubmit,
        StudentSubmissionDto? MySubmission);
}
