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
/// ════════════════════════════════════════════════════════════════════════
/// "VIDEO KO'RILDI" — GATING'NING VIDEO OYOG'I NIHOYAT ULANDI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN BU ENDPOINT PAYDO BO'LDI: `IGatingService` da
/// <c>MarkVideoWatchedAsync</c> ALLAQACHON yozilgan va testlangan edi,
/// lekin uni chaqiradigan HECH KIM yo'q edi — na controller, na fon
/// vazifasi. Ya'ni <c>LessonProgress.VideoWatchedAt</c> bazada MANGU
/// <c>null</c> qolardi.
///
/// Isbotlanadigan qoidalar:
///   • o'quvchi OCHIQ darsni "ko'rilgan" deb belgilay oladi va bu BAZAGA
///     yoziladi;
///   • takroriy chaqiruv progressni ORQAGA QAYTARMAYDI (idempotent);
///   • 🔴 QULFLANGAN darsni belgilab bo'lmaydi (403) — aks holda o'quvchi
///     Id'larni ketma-ket yuborib gating'ni O'ZI ochib olardi;
///   • begona kursning darsi — 403;
///   • XODIM bu endpointdan foydalanmaydi (403): "dars progressi"
///     o'quvchining holati.
/// </summary>
public sealed class LessonVideoWatchedTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= YOZILADI

    /// <summary>
    /// ★★ ASOSIY TEST: ochiq darsni belgilash BAZAGA yozadi.
    ///
    /// Tekshiruv javobga EMAS, BAZAGA qaraydi (<c>VideoWatchedAt</c>):
    /// aynan shu ustun avval hech qachon to'lmasdi.
    /// </summary>
    [Fact]
    public async Task MarkVideoWatched_ForUnlockedLesson_PersistsTimestamp()
    {
        var world = await NewWorldAsync("video-ochiq");

        using var student = await ClientAsync(world.StudentEmail);

        var response = await student.PostAsync(WatchedUri(world.FirstLessonId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var gate = (await response.Content.ReadFromJsonAsync<GateRow>())!;

        gate.LessonId.Should().Be(world.FirstLessonId);
        gate.VideoWatched.Should().BeTrue("javob YANGILANGAN holatni qaytaradi");

        (await WatchedAtAsync(world.StudentId, world.FirstLessonId))
            .Should().NotBeNull("`LessonProgress.VideoWatchedAt` BAZAGA yozilishi kerak");
    }

    /// <summary>
    /// IDEMPOTENT: qayta chaqirilsa BIRINCHI ko'rilgan payt saqlanadi.
    ///
    /// ⚠️ Shu tufayli pleyer "yubordimmi?" degan holatni saqlashi shart
    /// emas — u xohlagancha yuboraveradi.
    /// </summary>
    [Fact]
    public async Task MarkVideoWatched_CalledTwice_KeepsFirstTimestamp()
    {
        var world = await NewWorldAsync("video-takror");

        using var student = await ClientAsync(world.StudentEmail);

        (await student.PostAsync(WatchedUri(world.FirstLessonId), content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await WatchedAtAsync(world.StudentId, world.FirstLessonId);

        (await student.PostAsync(WatchedUri(world.FirstLessonId), content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await WatchedAtAsync(world.StudentId, world.FirstLessonId);

        second.Should().Be(first, "birinchi ko'rilgan payt o'zgarmaydi");
    }

    // ================================================================= VIDEO SHARTI

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// ★★ "VIDEOSI BOR" FAKTI BAZADAN KELADI (2026-08-14)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Ilgari `GatingService` bu yerga QOTIB qolgan `false` uzatardi
    /// (`VideoContentModelled`), ya'ni video sharti HECH QACHON
    /// tekshirilmasdi — o'quvchi hech narsa ko'rmasdan darsni "tugatgan"
    /// bo'lardi.
    ///
    /// ★ NIMA UCHUN TEST HTTP EMAS, SERVIS DARAJASIDA: aynan EF ifodasi
    ///   o'zgardi. Servis chaqiruvi HAQIQIY Postgres'ga boradi, ya'ni test
    ///   yangi `EXISTS` ichki so'rovi SQL'ga TARJIMA BO'LISHINI ham
    ///   isbotlaydi (tarjima qilinmasa `InvalidOperationException` bo'lardi).
    /// </summary>
    [Fact]
    public async Task Gate_WhenLessonHasVideoAsset_RequiresWatching()
    {
        var world = await NewWorldAsync("video-fakt");

        await AddAssetAsync(world.FirstLessonId, LessonAssetKind.Video);
        await InvalidateGateAsync(world.StudentId);

        var before = await GateAsync(world.StudentId, world.FirstLessonId);

        before.HasVideo.Should().BeTrue("darsda `Kind = Video` asset bor");
        before.Unlocked.Should().BeTrue("birinchi dars baribir ochiq");
        before.Completed.Should().BeFalse(
            "video ko'rilmagan -> dars TUGATILMAGAN (ilgari bu yerda `true` edi)");

        using var student = await ClientAsync(world.StudentEmail);

        (await student.PostAsync(WatchedUri(world.FirstLessonId), content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await GateAsync(world.StudentId, world.FirstLessonId);

        after.Completed.Should().BeTrue("video ko'rildi -> dars tugatildi");
    }

    /// <summary>
    /// 🔴 IMTIHON DARSINING RASMI VIDEO EMAS.
    ///
    /// Rasm ham AYNI `LessonAssets` jadvalida yotadi (`LessonAsset` izohi:
    /// bitta jadval, ikkita tur). Tur bo'yicha filtr tushib qolsa, imtihon
    /// darsi "videosi bor, ko'rilmagan" bo'lib ABADIY tugatilmagan qolardi
    /// va butun zanjirni qulflab qo'yardi — o'quvchi ko'ra olmaydigan
    /// narsani kutib qolardi.
    /// </summary>
    [Fact]
    public async Task Gate_WhenLessonHasOnlyImageAsset_DoesNotRequireVideo()
    {
        var world = await NewWorldAsync("rasm-fakt");

        await AddAssetAsync(world.FirstLessonId, LessonAssetKind.Image);
        await InvalidateGateAsync(world.StudentId);

        var gate = await GateAsync(world.StudentId, world.FirstLessonId);

        gate.HasVideo.Should().BeFalse("rasm — video emas");
        gate.Completed.Should().BeTrue("mavjud bo'lmagan shart talab qilinmaydi");
    }

    // ================================================================= TAQIQ

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 QULFLANGAN DARSNI "KO'RILGAN" DEB BELGILAB BO'LMAYDI
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Busiz o'quvchi dars Id'larini ketma-ket yuborib butun kursning
    /// video shartini "bajarilgan" qilib qo'yardi — ya'ni gating'ni O'ZI
    /// ochib olardi. `assetId` kabi `lessonId` ham ketma-ket va uni
    /// taxmin qilish oson.
    /// </summary>
    [Fact]
    public async Task MarkVideoWatched_ForLockedLesson_ReturnsForbidden()
    {
        var world = await NewWorldAsync("video-qulf");

        using var student = await ClientAsync(world.StudentEmail);

        var response = await student.PostAsync(WatchedUri(world.LockedLessonId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await WatchedAtAsync(world.StudentId, world.LockedLessonId))
            .Should().BeNull("rad etilgan so'rov HECH NARSA yozmasligi kerak");
    }

    /// <summary>Begona kursning darsi — 403 (gating `NotInCourse`).</summary>
    [Fact]
    public async Task MarkVideoWatched_ForForeignCourseLesson_ReturnsForbidden()
    {
        var mine = await NewWorldAsync("video-mening");
        var stranger = await NewWorldAsync("video-begona");

        using var student = await ClientAsync(stranger.StudentEmail);

        var response = await student.PostAsync(WatchedUri(mine.FirstLessonId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// XODIM bu endpointga kirmaydi (403).
    ///
    /// ★ "Dars progressi" — O'QUVCHINING holati. Xodimda
    ///   <c>LessonProgress</c> yozuvi umuman bo'lmaydi va uni yaratish
    ///   ustozning materialni ko'rishini o'quvchi progressiga aylantirardi.
    /// </summary>
    [Fact]
    public async Task MarkVideoWatched_AsAdmin_ReturnsForbidden()
    {
        var world = await NewWorldAsync("video-xodim");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(WatchedUri(world.FirstLessonId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Tokensiz — 401.</summary>
    [Fact]
    public async Task MarkVideoWatched_WithoutToken_ReturnsUnauthorized()
    {
        var world = await NewWorldAsync("video-anonim");

        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync(WatchedUri(world.FirstLessonId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// <c>LessonProgress.VideoWatchedAt</c> ni TO'G'RIDAN-TO'G'RI bazadan
    /// o'qiydi — API javobiga ishonmasdan.
    /// </summary>
    private Task<DateTimeOffset?> WatchedAtAsync(long studentId, long lessonId) =>
        factory.WithDbAsync(db => db.LessonProgress.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.ModuleLessonId == lessonId)
            .Select(p => p.VideoWatchedAt)
            .FirstOrDefaultAsync());

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
    /// Kurs + modul + IKKI dars + guruh + o'quvchi.
    ///
    /// ★ IKKI DARS ATAYLAB: ustoz sur'ati 0, ya'ni 0-indeksli dars OCHIQ,
    ///   1-indeksli QULFLANGAN (`LessonAssetAccessTests` bilan AYNI naqsh).
    ///   Bitta dunyoda ham "ruxsat bor", ham "ruxsat yo'q" holati bo'ladi.
    /// </summary>
    private async Task<World> NewWorldAsync(string prefix)
    {
        using var admin = await AdminClientAsync();

        var course = await admin.PostAsJsonAsync("/api/v1/courses", new
        {
            name = $"{prefix} kursi " + Guid.NewGuid().ToString("N")[..6],
        });

        course.StatusCode.Should().Be(HttpStatusCode.Created,
            await course.Content.ReadAsStringAsync());

        var courseId = (await course.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var module = await admin.PostAsJsonAsync(
            $"/api/v1/courses/{courseId}/modules", new { name = "Modul" });

        module.StatusCode.Should().Be(HttpStatusCode.Created);

        var moduleId = (await module.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var firstLesson = await CreateLessonAsync(admin, courseId, moduleId, "Birinchi");
        var lockedLesson = await CreateLessonAsync(admin, courseId, moduleId, "Ikkinchi");

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
            courseMonths = 1,
        });

        group.StatusCode.Should().Be(HttpStatusCode.Created,
            await group.Content.ReadAsStringAsync());

        var groupId = (await group.Content.ReadFromJsonAsync<CreatedGroupRow>())!.Group.Id;

        var member = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{groupId}/members", new { studentId = student.Id });

        member.StatusCode.Should().Be(HttpStatusCode.Created,
            await member.Content.ReadAsStringAsync());

        // Redis dev stack'i testlar bilan BO'LISHILADI va gating kaliti
        // faqat o'quvchi Id'si bo'yicha — boshqa test bazasidagi bir xil
        // Id'li o'quvchining keshi tasodifan mos kelishi mumkin
        // (`CourseEndpointsTests.GateAsync` dagi AYNI ehtiyot chorasi).
        await InvalidateGateAsync(student.Id);

        return new World(firstLesson, lockedLesson, student.Id, student.Email);
    }

    private async Task InvalidateGateAsync(long studentId)
    {
        using var scope = factory.Services.CreateScope();

        var gating = scope.ServiceProvider.GetRequiredService<IGatingService>();
        await gating.InvalidateAsync(studentId);
    }

    /// <summary>
    /// Darsning gating holatini SERVISDAN o'qiydi (HTTP orqali emas).
    ///
    /// ★ HAR SAFAR YANGI SCOPE: `GatingService` so'rov ichida natijani
    ///   xotirada eslab qoladi (`_snapshot`), ya'ni bitta instansiya bilan
    ///   "oldin/keyin" farqini umuman ko'ra olmasdik.
    /// </summary>
    private async Task<LessonGateDto> GateAsync(long studentId, long lessonId)
    {
        using var scope = factory.Services.CreateScope();

        var gating = scope.ServiceProvider.GetRequiredService<IGatingService>();
        return await gating.GetLessonGateAsync(studentId, lessonId);
    }

    /// <summary>
    /// Darsga media qatorini TO'G'RIDAN-TO'G'RI bazaga qo'shadi.
    ///
    /// ★ NIMA UCHUN HTTP ORQALI YUKLASH EMAS: yuklash oqimi HAQIQIY
    ///   ombor (MinIO) talab qiladi va u alohida fixture'da
    ///   (`LessonMediaFixture`) sinaladi. Bu testga esa faqat QATORNING
    ///   O'ZI kerak — gating omborga umuman tegmaydi, u `LessonAssets`
    ///   jadvalidan `EXISTS` o'qiydi.
    /// </summary>
    /// <returns>Yozilgan qatorlar soni (analizator aniq turni talab qiladi).</returns>
    private Task<int> AddAssetAsync(long lessonId, LessonAssetKind kind) =>
        factory.WithDbAsync(async db =>
        {
            db.LessonAssets.Add(new LessonAsset
            {
                LessonId = lessonId,
                Kind = kind,
                Position = 0,
                ObjectKey = $"test/{Guid.NewGuid():N}",
                ContentType = kind == LessonAssetKind.Video ? "video/mp4" : "image/jpeg",
                SizeBytes = 1024,
            });

            return await db.SaveChangesAsync();
        });

    private static async Task<long> CreateLessonAsync(
        HttpClient admin, long courseId, long moduleId, string name)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/courses/{courseId}/modules/{moduleId}/lessons", new { name });

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

        var created = (await response.Content.ReadFromJsonAsync<CreatedUserRow>())!;

        return new CreatedUser(created.User.Id, email);
    }

    private static Uri WatchedUri(long lessonId) =>
        new(
            FormattableString.Invariant($"/api/v1/progress/lessons/{lessonId}/video-watched"),
            UriKind.Relative);

    private sealed record World(
        long FirstLessonId, long LockedLessonId, long StudentId, string StudentEmail);

    private sealed record GateRow(long LessonId, bool Unlocked, bool Completed, bool VideoWatched);

    private sealed record CreatedUser(long Id, string Email);

    private sealed record CreatedUserRow(IdRow User);

    private sealed record IdRow(long Id);

    private sealed record CreatedGroupRow(IdRow Group);
}
