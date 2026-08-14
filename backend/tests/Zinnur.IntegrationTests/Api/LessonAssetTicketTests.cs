using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ★★ O'YNATISH CHIPTASI — `&lt;video src&gt;` YO'LI XAVFSIZ QOLDIMI?
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 NIMA UCHUN BU SINF ZARUR: chipta qo'shilishi bilan dars videosiga
/// KIRISHNING IKKINCHI yo'li paydo bo'ldi — `Authorization` sarlavhasisiz
/// yo'l. Har qanday ikkinchi yo'l — bu birinchi yo'ldagi darvozalarni
/// chetlab o'tish IMKONIYATI. `LessonAssetAccessTests` birinchi yo'lni
/// qulflagan; bu sinf AYNI qoidalar ikkinchi yo'lda ham amal qilishini
/// isbotlaydi.
///
/// Isbotlanadigan qoidalar:
///   • chipta OCHIQ darsda beriladi va u bilan video `Authorization`SIZ
///     o'ynaydi;
///   • QULFLANGAN darsda chipta UMUMAN berilmaydi (403) — o'quvchi
///     sababni darhol ko'radi;
///   • QARZDORGA chipta berilmaydi (403);
///   • 🔴🔴 CHIPTA OLINGANDAN KEYIN qarz paydo bo'lsa — oqim TO'XTAYDI.
///     Bu presigned havoladan farqning BUTUN MA'NOSI;
///   • boshqa faylning chiptasi ISHLAMAYDI (imzo `assetId` ga bog'langan);
///   • buzilgan / o'zgartirilgan / bo'sh chipta — 401;
///   • chipta OLDIDA sessiya turadi (ikkalasi bo'lsa sarlavha ustun).
///
/// HAQIQIY MinIO ishlatiladi (`LessonMediaFixture`): 403 "topilmadi"
/// degani emas, "RUXSAT YO'Q" degani.
/// </summary>
[Collection(LessonMediaFixture.Name)]
public sealed class LessonAssetTicketTests(StorageBackedApiFactory factory)
{
    /// <summary>Tarif summasi — chegaradan (540 000) OSHIQ qarz yasash uchun.</summary>
    private const decimal DebtAmount = 900_000m;

    private const string Period = "2026-05";

    private static readonly byte[] Mp4Magic =
        [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
         (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    // ================================================================= CHIPTA BERILADI

    /// <summary>
    /// OCHIQ darsda chipta beriladi va u bilan video `Authorization`
    /// SARLAVHASISIZ o'ynaydi — ya'ni `&lt;video src&gt;` haqiqatan ishlaydi.
    ///
    /// ★ AYNAN SHU TEST R6 dagi "video umuman o'ynatilmaydi" muammosining
    ///   hal bo'lganini isbotlaydi.
    /// </summary>
    [Fact]
    public async Task Ticket_ThenStreamWithoutAuthorizationHeader_ReturnsOk()
    {
        var world = await NewCourseWorldAsync("chipta-ochiq");

        using var student = await ClientAsync(world.StudentEmail);

        var ticket = await IssueTicketAsync(student, world.FirstAssetId);

        ticket.Token.Should().NotBeNullOrWhiteSpace();
        ticket.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        // ⚠️ ATAYLAB TOZA KLIENT: `Authorization` sarlavhasi UMUMAN yo'q —
        //    brauzerning `<video src>` elementi aynan shunday so'raydi.
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(AssetUri(world.FirstAssetId, ticket.Token));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Chipta bilan `Range` ham ishlaydi (206) — videoda oldinga o'tish
    /// (seek) shu so'rovga tayanadi.
    ///
    /// ★ Bu `IRecordingStorage` da presigned tanlashning sabablaridan biri
    ///   edi ("proxy'da `Range` yo'q"). Dars videosi yo'lida esa `Range`
    ///   BOR — shuning uchun u yerdagi dalil bu yerda AMAL QILMAYDI.
    /// </summary>
    [Fact]
    public async Task Ticket_WithRangeHeader_ReturnsPartialContent()
    {
        var world = await NewCourseWorldAsync("chipta-range");

        using var student = await ClientAsync(world.StudentEmail);

        var ticket = await IssueTicketAsync(student, world.FirstAssetId);

        using var anonymous = factory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Get, AssetUri(world.FirstAssetId, ticket.Token));

        request.Headers.Range = new RangeHeaderValue(0, 99);

        var response = await anonymous.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentLength.Should().Be(100);
    }

    /// <summary>Xodim ham chipta oladi (gatingsiz) — materialni ko'rish ish talabi.</summary>
    [Fact]
    public async Task Ticket_AsAdmin_ForLockedLesson_ReturnsOk()
    {
        var world = await NewCourseWorldAsync("chipta-admin");

        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(TicketUri(world.LockedAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "xodim uchun gating qo'llanmaydi");
    }

    // ================================================================= CHIPTA BERILMAYDI

    /// <summary>
    /// 🔴 QULFLANGAN DARSDA CHIPTA UMUMAN BERILMAYDI (403).
    ///
    /// ★ NIMA UCHUN CHIPTA BOSQICHIDA TO'SILADI, oqim bosqichida emas:
    ///   ikkalasida ham to'siladi (oqim baribir tekshiradi), lekin bu
    ///   yerda o'quvchi SABABNI o'qiy oladigan JSON javob oladi. Oqim
    ///   bosqichidagi 403 esa brauzer uchun shunchaki "buzuq video".
    /// </summary>
    [Fact]
    public async Task Ticket_ForLockedLesson_ReturnsForbidden()
    {
        var world = await NewCourseWorldAsync("chipta-qulf");

        using var student = await ClientAsync(world.StudentEmail);

        var response = await student.GetAsync(TicketUri(world.LockedAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "qulflangan darsning chiptasi berilmasligi kerak");
    }

    /// <summary>QARZDOR o'quvchiga chipta berilmaydi — dars OCHIQ bo'lsa ham.</summary>
    [Fact]
    public async Task Ticket_AsDebtorStudent_ReturnsForbidden()
    {
        var world = await NewCourseWorldAsync("chipta-qarz", makeDebtor: true);

        using var student = await ClientAsync(world.StudentEmail);

        var response = await student.GetAsync(TicketUri(world.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Xabar FOYDALI bo'lsin: qarz haqida aytilsin.
        (await ProblemText.ReadAsync(response)).Should().Contain("qarz");
    }

    /// <summary>Begona kursning o'quvchisi chipta ololmaydi.</summary>
    [Fact]
    public async Task Ticket_AsForeignStudent_ReturnsForbidden()
    {
        var mine = await NewCourseWorldAsync("chipta-mening");
        var stranger = await NewCourseWorldAsync("chipta-begona");

        using var student = await ClientAsync(stranger.StudentEmail);

        var response = await student.GetAsync(TicketUri(mine.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Tokensiz chipta so'rab bo'lmaydi (401).</summary>
    [Fact]
    public async Task Ticket_WithoutSession_ReturnsUnauthorized()
    {
        var world = await NewCourseWorldAsync("chipta-anonim");

        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(TicketUri(world.FirstAssetId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= 🔴🔴 BEKOR QILISH

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 CHIPTA OLINGANDAN KEYIN QARZ PAYDO BO'LSA — OQIM TO'XTAYDI
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// ★★ BU SINFDAGI ENG MUHIM TEST. U chiptaning presigned havoladan
    /// FARQINI isbotlaydi va `IMediaStorage` port izohidagi qarorni
    /// (qaytarib bo'ladigan ruxsat — revocability) jonli qulflaydi.
    ///
    /// Ssenariy:
    ///   1) o'quvchi sog'lom — chipta oladi va video ochiladi;
    ///   2) qarz paydo bo'ladi (tarif + oy yozuvi);
    ///   3) AYNI chipta bilan keyingi so'rov -> 403.
    ///
    /// Brauzer video davomida o'nlab `Range` so'rovi yuboradi, ya'ni
    /// amalda bloklash bir necha soniyada kuchga kiradi. PRESIGNED
    /// havolada bu MUTLAQO mumkin emas edi: havola muddati tugagunicha
    /// (15 daqiqa) o'quvchi butun videoni ko'rib bo'lardi.
    /// </summary>
    [Fact]
    public async Task Ticket_AfterDebtAppears_StopsStreaming()
    {
        var world = await NewCourseWorldAsync("chipta-bekor");

        using var student = await ClientAsync(world.StudentEmail);

        var ticket = await IssueTicketAsync(student, world.FirstAssetId);

        using var anonymous = factory.CreateClient();

        // 1) Hozircha ochiladi.
        (await anonymous.GetAsync(AssetUri(world.FirstAssetId, ticket.Token)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // 2) Qarz paydo bo'ldi.
        using (var admin = await AdminClientAsync())
            await MakeDebtorAsync(admin, world.GroupId);

        // 3) AYNI chipta — endi ishlamaydi.
        var blocked = await anonymous.GetAsync(AssetUri(world.FirstAssetId, ticket.Token));

        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "chipta RUXSAT bermaydi — ruxsat har bayt so'rovida qayta tekshiriladi");
    }

    // ================================================================= CHIPTANING O'ZI

    /// <summary>
    /// 🔴 BOSHQA FAYLNING CHIPTASI ISHLAMAYDI.
    ///
    /// Imzo ichida `assetId` bor: aks holda bitta ochiq darsning chiptasi
    /// butun kutubxonani ochib berardi (`assetId` lar ketma-ket!).
    /// </summary>
    [Fact]
    public async Task Ticket_BoundToAnotherAsset_ReturnsUnauthorized()
    {
        var world = await NewCourseWorldAsync("chipta-bogliq");

        using var admin = await AdminClientAsync();

        // Xodim uchun IKKALA fayl ham ochiq — ya'ni rad etish sabab
        // GATING emas, aynan CHIPTANING BOG'LANISHI.
        var ticket = await IssueTicketAsync(admin, world.FirstAssetId);

        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(AssetUri(world.LockedAssetId, ticket.Token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "chipta FAQAT o'zi berilgan faylga yaraydi");
    }

    /// <summary>
    /// Buzilgan / o'zgartirilgan / bo'sh chipta — hammasi 401.
    ///
    /// ★ Imzosi noto'g'ri, shakli buzuq va muddati o'tgan chipta BIR XIL
    ///   javob oladi: farqli xabar hujumchiga qaysi qadamda adashganini
    ///   o'rgatardi.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("qalbaki")]
    [InlineData("1.1.99999999999.AAAA")]      // imzo noto'g'ri
    [InlineData("1.1.1.AAAA")]                // muddati 1970-yilda o'tgan
    [InlineData("2.1.99999999999.AAAA")]      // noma'lum format versiyasi
    public async Task Stream_WithInvalidTicket_ReturnsUnauthorized(string token)
    {
        var world = await NewCourseWorldAsync("chipta-qalbaki-" + token.Length);

        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(AssetUri(world.FirstAssetId, token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Imzoning BIRGINA belgisi o'zgartirilsa ham chipta o'ladi.
    ///
    /// ★ Yuqoridagi `[Theory]` sun'iy qiymatlarni sinaydi; bu test esa
    ///   HAQIQIY, server yasagan chiptani buzadi — ya'ni "imzo umuman
    ///   tekshirilmayapti" degan xatoni ham ushlaydi.
    /// </summary>
    [Fact]
    public async Task Stream_WithTamperedSignature_ReturnsUnauthorized()
    {
        var world = await NewCourseWorldAsync("chipta-buzilgan");

        using var student = await ClientAsync(world.StudentEmail);

        var ticket = await IssueTicketAsync(student, world.FirstAssetId);

        // Oxirgi belgini almashtiramiz (imzo `base64url` — `A` va `B` ham unda bor).
        var last = ticket.Token[^1];
        var tampered = string.Concat(ticket.Token[..^1], last == 'A' ? "B" : "A");

        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(AssetUri(world.FirstAssetId, tampered));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// ★ SESSIYA CHIPTADAN USTUN: `Authorization` bo'lsa chipta umuman
    ///   o'qilmaydi. Shu tufayli qalbaki chipta bilan kelgan HAQIQIY
    ///   foydalanuvchi jazolanmaydi.
    /// </summary>
    [Fact]
    public async Task Stream_WithSessionAndGarbageTicket_UsesSession()
    {
        var world = await NewCourseWorldAsync("chipta-sessiya");

        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(AssetUri(world.FirstAssetId, "qalbaki"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ================================================================= yordamchi

    private static async Task<TicketRow> IssueTicketAsync(HttpClient client, long assetId)
    {
        var response = await client.GetAsync(TicketUri(assetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        // 🔴 Chipta KESHLANMASLIGI kerak — javobda `no-store` turishi shart.
        response.Headers.CacheControl?.NoStore.Should().BeTrue();

        return (await response.Content.ReadFromJsonAsync<TicketRow>())!;
    }

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
    /// Kurs + modul + IKKI dars (har birida bitta video) + guruh + o'quvchi.
    ///
    /// ★ IKKI DARS ATAYLAB: ustoz sur'ati 0, ya'ni 0-indeksli dars OCHIQ,
    ///   1-indeksli QULFLANGAN (`LessonAssetAccessTests` bilan AYNI naqsh).
    /// </summary>
    private async Task<CourseWorld> NewCourseWorldAsync(string prefix, bool makeDebtor = false)
    {
        using var admin = await AdminClientAsync();

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

        var firstAsset = await UploadAsync(admin, firstLesson);
        var lockedAsset = await UploadAsync(admin, lockedLesson);

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

        if (makeDebtor)
            await MakeDebtorAsync(admin, groupId);

        return new CourseWorld(
            courseId, groupId, firstLesson, lockedLesson,
            firstAsset, lockedAsset, student.Id, student.Email);
    }

    private static async Task MakeDebtorAsync(HttpClient admin, long groupId)
    {
        var tariff = await admin.PostAsJsonAsync("/api/v1/payments/tariffs", new
        {
            name = "Chipta blok tarifi",
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

    private static async Task<long> UploadAsync(HttpClient admin, long lessonId)
    {
        var content = new MultipartFormDataContent();

        var part = new ByteArrayContent(RandomVideo(2048));
        part.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

        content.Add(part, "file", "dars.mp4");

        var response = await admin.PostAsync(AssetsUri(lessonId), content);

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

    private static byte[] RandomVideo(int totalBytes)
    {
        var bytes = RandomNumberGenerator.GetBytes(totalBytes);
        Mp4Magic.CopyTo(bytes, 0);
        return bytes;
    }

    private static Uri AssetsUri(long lessonId) =>
        Relative($"/api/v1/lessons/{lessonId}/assets");

    private static Uri TicketUri(long assetId) =>
        Relative($"/api/v1/lessons/assets/{assetId}/ticket");

    private static Uri AssetUri(long assetId, string ticket) =>
        Relative($"/api/v1/lessons/assets/{assetId}?ticket={Uri.EscapeDataString(ticket)}");

    private static Uri Relative(FormattableString path) =>
        new(FormattableString.Invariant(path), UriKind.Relative);

    private sealed record CourseWorld(
        long CourseId,
        long GroupId,
        long FirstLessonId,
        long LockedLessonId,
        long FirstAssetId,
        long LockedAssetId,
        long StudentId,
        string StudentEmail);

    private sealed record TicketRow(string Token, DateTimeOffset ExpiresAt);

    private sealed record CreatedUser(long Id, string Email);

    private sealed record CreatedUserRow(IdRow User);

    private sealed record IdRow(long Id);

    private sealed record CreatedGroupRow(IdRow Group);
}
