using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// ★★ DARS VIDEOSIGA KIRISH — RUXSAT MATRITSASI (SERVERDA)
/// ========================================================================
///
/// 🔴 NIMA UCHUN BU ENG MUHIM SINF: video havolasi faqat `assetId` dan
/// iborat va ID'lar KETMA-KET. Ya'ni o'quvchi qulflangan darsning
/// `assetId` sini shunchaki TAXMIN QILA OLADI. UI'da tugmani yashirish
/// hech nimani himoya qilmaydi — to'siq SERVERDA bo'lishi shart.
///
/// Isbotlanadigan qoidalar:
///   • xodim (admin/o'quv bo'limi/ustoz/kurator) -> DOIM 200;
///   • o'quvchi + OCHIQ dars -> 200;
///   • o'quvchi + QULFLANGAN dars -> 403 (gating);
///   • o'quvchi + QARZDOR (`PaymentBlockScope.Video`) -> 403, dars OCHIQ
///     bo'lsa ham;
///   • begona kursning o'quvchisi -> 403;
///   • tokensiz -> 401 ("havolani bilish" hech nima bermaydi);
///   • qulflangan darsning `assets` ro'yxati daraxtda ham BO'SH.
///
/// HAQIQIY MinIO ishlatiladi: fayl OMBORDA BOR, ya'ni 403 "topilmadi"
/// degani emas, "RUXSAT YO'Q" degani.
/// </summary>
[Collection(LessonMediaFixture.Name)]
public sealed class LessonAssetAccessTests(StorageBackedApiFactory factory)
{
    private const string Password = "Media!2345";

    /// <summary>Tarif summasi — chegaradan (540 000) OSHIQ qarz yasash uchun.</summary>
    private const decimal DebtAmount = 900_000m;

    private const string Period = "2026-05";

    private static readonly byte[] Mp4Magic =
        [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
         (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    // ================================================================= RUXSAT BOR

    /// <summary>Admin — har doim ko'radi.</summary>
    [Fact]
    public async Task Download_AsAdmin_ReturnsOk()
    {
        var world = await NewCourseWorldAsync("ruxsat-admin");

        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(AssetUri(world.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// USTOZ ham ko'radi (gatingsiz): darsni o'tishdan oldin materialni
    /// ko'rib chiqishi ISH TALABI. Gating faqat o'quvchiga tegishli.
    /// </summary>
    [Fact]
    public async Task Download_AsTeacher_ReturnsOkEvenForLockedLesson()
    {
        var world = await NewCourseWorldAsync("ruxsat-ustoz");

        using var teacher = await ClientAsync(world.TeacherEmail);

        // Ikkinchi dars O'QUVCHI uchun QULFLANGAN (ustoz sur'ati 0), lekin
        // ustoz uchun ochiq.
        var response = await teacher.GetAsync(AssetUri(world.LockedAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "xodim uchun gating qo'llanmaydi");
    }

    /// <summary>O'quvchi OCHIQ darsning videosini ko'radi (birinchi dars doim ochiq).</summary>
    [Fact]
    public async Task Download_AsStudent_UnlockedLesson_ReturnsOk()
    {
        var world = await NewCourseWorldAsync("ruxsat-ochiq");

        using var student = await ClientAsync(world.StudentEmail);

        var response = await student.GetAsync(AssetUri(world.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    // ================================================================= TAQIQ

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 QULFLANGAN DARSNING VIDEOSI -> 403
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// O'quvchi tizimga kirgan, kursi TO'G'RI, `assetId` ni biladi. Baribir
    /// ocholmaydi — chunki gating darsni hali ochmagan (ustoz sur'ati 0,
    /// ya'ni faqat 0-indeksli dars ochiq).
    ///
    /// Fayl OMBORDA HAQIQATAN bor (yuqoridagi testlar buni ko'rsatadi),
    /// ya'ni 403 "topilmadi" degani EMAS.
    /// </summary>
    [Fact]
    public async Task Download_AsStudent_LockedLesson_ReturnsForbidden()
    {
        var world = await NewCourseWorldAsync("ruxsat-qulf");

        using var student = await ClientAsync(world.StudentEmail);

        var response = await student.GetAsync(AssetUri(world.LockedAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "qulflangan darsning videosi o'quvchiga BERILMASLIGI kerak");
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 QARZDOR O'QUVCHI (`PaymentBlockScope.Video`) -> 403
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Dars OCHIQ (birinchi dars), kurs TO'G'RI — lekin qarz chegaradan
    /// oshgan va bloklash qamrovi `Video`. `Video` — bloklashda ENG AVVAL
    /// yopiladigan qamrov, ya'ni bu eng ko'p ishlaydigan holat.
    /// </summary>
    [Fact]
    public async Task Download_AsDebtorStudent_ReturnsForbidden()
    {
        var world = await NewCourseWorldAsync("ruxsat-qarz", makeDebtor: true);

        using var student = await ClientAsync(world.StudentEmail);

        var response = await student.GetAsync(AssetUri(world.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "qarzdor o'quvchi video darsni ochmasligi kerak");

        var body = await ProblemText.ReadAsync(response);

        // Xabar FOYDALI bo'lishi kerak: qarz haqida aytilsin, "ruxsat yo'q"
        // degan quruq matn emas.
        body.Should().Contain("qarz");
    }

    /// <summary>BEGONA kursning o'quvchisi -> 403 (gating `NotInCourse`).</summary>
    [Fact]
    public async Task Download_AsForeignStudent_ReturnsForbidden()
    {
        var mine = await NewCourseWorldAsync("ruxsat-mening");
        var stranger = await NewCourseWorldAsync("ruxsat-begona");

        using var student = await ClientAsync(stranger.StudentEmail);

        var response = await student.GetAsync(AssetUri(mine.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Token yo'q -> 401. "Havolani bilish" hech nima bermaydi.</summary>
    [Fact]
    public async Task Download_WithoutToken_ReturnsUnauthorized()
    {
        var world = await NewCourseWorldAsync("ruxsat-anonim");

        using var client = factory.CreateClient();

        var response = await client.GetAsync(AssetUri(world.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Ustoz media YUKLAY olmaydi (403): dars kontenti BARCHA guruhlarga
    /// tegishli — bitta ustoz uni o'zgartirsa o'ntalab guruhga ta'sir
    /// qilardi.
    /// </summary>
    [Fact]
    public async Task Upload_AsTeacher_ReturnsForbidden()
    {
        var world = await NewCourseWorldAsync("ruxsat-yuklash");

        using var teacher = await ClientAsync(world.TeacherEmail);

        var response = await teacher.PostAsync(
            AssetsUri(world.FirstLessonId), Multipart("a.mp4", "video/mp4", RandomVideo(512)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ustoz o'chira ham olmaydi va tartibni ham o'zgartira olmaydi.</summary>
    [Fact]
    public async Task ModifyingAssets_AsTeacher_ReturnsForbidden()
    {
        var world = await NewCourseWorldAsync("ruxsat-tahrir");

        using var teacher = await ClientAsync(world.TeacherEmail);

        (await teacher.DeleteAsync(AssetUri(world.FirstAssetId))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var reorder = await teacher.PostAsJsonAsync(
            ReorderUri(world.FirstLessonId), new { orderedIds = new[] { world.FirstAssetId } });

        reorder.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= DARAXTDAGI KO'RINISH

    /// <summary>
    /// 🔴 QULFLANGAN DARSNING `assets` RO'YXATI DARAXTDA HAM BO'SH.
    ///
    /// Bu IKKINCHI qatlam (fayl oqimi baribir 403 beradi), lekin u zarur:
    /// aks holda o'quvchi qulflangan darsning video qismlarini nomlari va
    /// davomiyligi bilan ko'rardi — ya'ni gating "mazmunni yashirish"
    /// vazifasini yarim bajarardi.
    ///
    /// ★ Dars TURI (`kind`) esa QULFLANGAN darsda ham ko'rinadi: o'quvchi
    /// oldinda imtihon turganini bilishi kerak (bu MAZMUN emas, YO'L
    /// xaritasi).
    /// </summary>
    [Fact]
    public async Task CourseTree_HidesAssetsOfLockedLessons()
    {
        var world = await NewCourseWorldAsync("daraxt-qulf");

        using var student = await ClientAsync(world.StudentEmail);

        var tree = await student.GetFromJsonAsync<TreeRow>(CourseUri(world.CourseId));

        var lessons = tree!.Modules.Single().Lessons;

        var unlocked = lessons.Single(l => l.Id == world.FirstLessonId);
        var locked = lessons.Single(l => l.Id == world.LockedLessonId);

        unlocked.Unlocked.Should().BeTrue();
        unlocked.Assets.Should().HaveCount(1, "ochiq darsning mediasi ko'rinadi");

        locked.Unlocked.Should().BeFalse();
        locked.Assets.Should().BeEmpty("qulflangan darsning mediasi BERILMAYDI");

        // Sarlavha va TUR baribir ko'rinadi.
        locked.Name.Should().NotBeNullOrWhiteSpace();
        locked.Kind.Should().Be("Normal");

        // Mazmun esa yo'q (mavjud qoida).
        locked.Description.Should().BeNull();
    }

    /// <summary>Xodim uchun daraxtda hamma media ko'rinadi.</summary>
    [Fact]
    public async Task CourseTree_ShowsAllAssetsForStaff()
    {
        var world = await NewCourseWorldAsync("daraxt-xodim");

        using var admin = await AdminClientAsync();

        var tree = await admin.GetFromJsonAsync<TreeRow>(CourseUri(world.CourseId));

        var lessons = tree!.Modules.Single().Lessons;

        lessons.Should().OnlyContain(l => l.Unlocked);
        lessons.Sum(l => l.Assets.Count).Should().Be(2);
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        var tokens = await factory.LoginAsync(email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    /// <summary>
    /// Kurs + modul + IKKI dars (har birida bitta video) + guruh + ustoz +
    /// o'quvchi.
    ///
    /// ★ IKKI DARS ATAYLAB: ustoz sur'ati 0 bo'lgani uchun 0-indeksli dars
    /// OCHIQ, 1-indeksli QULFLANGAN. Ya'ni bitta dunyoda ham "ruxsat bor",
    /// ham "ruxsat yo'q" holatini tekshirish mumkin va ular AYNI
    /// ma'lumotdan kelib chiqadi.
    /// </summary>
    private async Task<CourseWorld> NewCourseWorldAsync(string prefix, bool makeDebtor = false)
    {
        using var admin = await AdminClientAsync();

        // ---- kurs -> modul -> ikki dars ----
        var course = await admin.PostAsJsonAsync(
            new Uri("/api/v1/courses", UriKind.Relative),
            new { name = $"{prefix} kursi " + Guid.NewGuid().ToString("N")[..6] });

        course.StatusCode.Should().Be(HttpStatusCode.Created,
            await course.Content.ReadAsStringAsync());

        var courseId = (await course.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var module = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/courses/{courseId}/modules", UriKind.Relative),
            new { name = "Modul" });

        module.StatusCode.Should().Be(HttpStatusCode.Created);

        var moduleId = (await module.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var firstLesson = await CreateLessonAsync(admin, courseId, moduleId, "Birinchi");
        var lockedLesson = await CreateLessonAsync(admin, courseId, moduleId, "Ikkinchi");

        var firstAsset = await UploadAsync(admin, firstLesson, "birinchi.mp4");
        var lockedAsset = await UploadAsync(admin, lockedLesson, "ikkinchi.mp4");

        // ---- ustoz, o'quvchi va kursga BIRIKTIRILGAN guruh ----
        var teacher = await CreateUserAsync(admin, UserRole.Teacher, prefix);
        var student = await CreateUserAsync(admin, UserRole.Student, prefix);

        var group = await admin.PostAsJsonAsync("/api/v1/groups", new
        {
            name = $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}",
            startDate = "2026-01-05",
            weekdays = new[] { "Monday", "Wednesday" },
            startTime = "19:00:00",
            courseId,
            teacherId = teacher.Id,

            // 1 oy — jadval qisqa bo'lsin (test tezligi uchun).
            courseMonths = 1,
        });

        group.StatusCode.Should().Be(HttpStatusCode.Created,
            await group.Content.ReadAsStringAsync());

        var groupId = (await group.Content.ReadFromJsonAsync<CreatedGroupRow>())!.Group.Id;

        var member = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{groupId}/members", new { studentId = student.Id });

        member.StatusCode.Should().Be(HttpStatusCode.Created,
            await member.Content.ReadAsStringAsync());

        if (makeDebtor)
            await MakeDebtorAsync(admin, groupId);

        return new CourseWorld(
            courseId, firstLesson, lockedLesson, firstAsset, lockedAsset,
            teacher.Email, student.Email);
    }

    /// <summary>
    /// Guruhga tarif qo'yib, oy yozuvini ochadi — natijada o'quvchining
    /// qarzi chegaradan (540 000) oshadi va `Video` qamrovi bloklaydi.
    /// </summary>
    private async Task MakeDebtorAsync(HttpClient admin, long groupId)
    {
        var tariff = await admin.PostAsJsonAsync("/api/v1/payments/tariffs", new
        {
            name = "Media blok tarifi",
            amount = DebtAmount,
            activeFrom = "2020-01-01",
            groupId,
        });

        tariff.StatusCode.Should().Be(HttpStatusCode.Created,
            await tariff.Content.ReadAsStringAsync());

        var opened = await admin.PostAsJsonAsync(
            "/api/v1/payments/periods/open", new { period = Period, groupId });

        opened.StatusCode.Should().Be(HttpStatusCode.OK,
            await opened.Content.ReadAsStringAsync());

        // ★ BOSQICHMA-BOSQICH HISOBLASH (2026-08-16): `OpenPeriodAsync` endi
        // 0 so'mda ochadi — bu qamrov sinovi "qarz allaqachon bor" holatini
        // boshlang'ich nuqta sifatida oladi, shuning uchun `Payment.Accrue`
        // bilan TO'G'RIDAN-TO'G'RI to'ldiramiz — xuddi dars allaqachon
        // o'tilgandek.
        await factory.WithDbAsync(async db =>
        {
            var payment = await db.Payments.FirstAsync(p =>
                p.GroupId == groupId && p.Period == Period);

            payment.Accrue(DebtAmount, 0m, DateTimeOffset.UtcNow);
            payment.Validate();

            return await db.SaveChangesAsync();
        });
    }

    private static async Task<long> CreateLessonAsync(
        HttpClient admin, long courseId, long moduleId, string name)
    {
        var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/courses/{courseId}/modules/{moduleId}/lessons", UriKind.Relative),
            new { name });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<long> UploadAsync(HttpClient admin, long lessonId, string fileName)
    {
        var response = await admin.PostAsync(
            AssetsUri(lessonId), Multipart(fileName, "video/mp4", RandomVideo(2048)));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<CreatedUser> CreateUserAsync(
        HttpClient admin, UserRole role, string prefix)
    {
        var email = $"{prefix[..Math.Min(prefix.Length, 8)]}-{Guid.NewGuid():N}"[..20]
                    + "@zinnur.uz";

        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = $"{role} {prefix}",
            email,
            role = role.ToString(),
            phone = TestPhones.Next(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        // Javob shakli: `{ "user": { "id": … } }` — `WorldBuilder` bilan AYNI.
        var created = (await response.Content.ReadFromJsonAsync<CreatedUserRow>())!;

        return new CreatedUser(created.User.Id, email);
    }

    private static byte[] RandomVideo(int totalBytes)
    {
        var bytes = RandomNumberGenerator.GetBytes(totalBytes);
        Mp4Magic.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent Multipart(
        string fileName, string contentType, byte[] payload)
    {
        var content = new MultipartFormDataContent();

        var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        content.Add(part, "file", fileName);

        return content;
    }

    private static Uri AssetsUri(long lessonId) =>
        Relative($"/api/v1/lessons/{lessonId}/assets");

    private static Uri AssetUri(long assetId) =>
        Relative($"/api/v1/lessons/assets/{assetId}");

    private static Uri ReorderUri(long lessonId) =>
        Relative($"/api/v1/lessons/{lessonId}/assets/reorder");

    private static Uri CourseUri(long courseId) =>
        Relative($"/api/v1/courses/{courseId}");

    private static Uri Relative(FormattableString path) =>
        new(FormattableString.Invariant(path), UriKind.Relative);

    private sealed record CourseWorld(
        long CourseId,
        long FirstLessonId,
        long LockedLessonId,
        long FirstAssetId,
        long LockedAssetId,
        string TeacherEmail,
        string StudentEmail);

    private sealed record CreatedUser(long Id, string Email);

    private sealed record CreatedUserRow(IdRow User);

    private sealed record IdRow(long Id);

    private sealed record CreatedGroupRow(IdRow Group);

    private sealed record TreeRow(long Id, List<ModuleRow> Modules);

    private sealed record ModuleRow(long Id, List<LessonRow> Lessons);

    private sealed record LessonRow(
        long Id,
        string Name,
        string? Description,
        string Kind,
        List<IdRow> Assets,
        bool Unlocked);
}
