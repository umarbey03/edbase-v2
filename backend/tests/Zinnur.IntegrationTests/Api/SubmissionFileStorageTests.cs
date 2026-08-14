using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// ★★ FAYL OMBORI — TO'LIQ YO'L (yuklash -> kalit -> o'qish)
/// ========================================================================
///
/// HAQIQIY MinIO bilan ishlaydi (<see cref="StorageBackedApiFactory"/>).
/// MinIO R2 bilan BIR XIL protokolni gapiradi — ya'ni bu testlar prod
/// yo'lini ham qoplaydi: kod bitta, faqat `Storage:*` qiymatlari boshqa.
///
/// Nima isbotlanadi:
///   1) yuklangan baytlar AYNAN o'sha holicha qaytadi (SigV4 imzosi
///      PUT va GET uchun ham to'g'ri);
///   2) bazada TO'LIQ URL emas, faqat OBYEKT KALITI saqlanadi;
///   3) kalitni TAXMIN QILIB bo'lmaydi (tasodifiy qism);
///   4) fayl HAQIQATAN mavjud bo'lganda ham begona o'quvchi 403 oladi —
///      ya'ni 403 "fayl yo'q" degani emas, "ruxsat yo'q" degani.
///
/// MinIO ishlamayotgan bo'lsa bu sinf YIQILADI (o'tkazib yuborilmaydi):
/// "sinalmagan, lekin yashil" natija eng qimmat yolg'on.
/// Ishga tushirish: `docker compose up -d minio` yoki `TEST_STORAGE_URL`.
/// </summary>
public sealed class SubmissionFileStorageTests(StorageBackedApiFactory factory)
    : IClassFixture<StorageBackedApiFactory>
{
    /// <summary>PNG sehrli baytlari — tur MAZMUNDAN aniqlanadi.</summary>
    private static readonly byte[] PngMagic =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Kalit shakli: <c>&lt;prefiks&gt;/YYYY-MM/&lt;studentId&gt;/&lt;16 hex&gt;.png</c>.
    /// </summary>
    private static readonly Regex KeyShape = new(
        @"^[a-z0-9/\-]+/\d{4}-\d{2}/(?<student>\d+)/(?<random>[0-9a-f]{16})\.png$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    // ================================================================= to'liq yo'l

    /// <summary>
    /// ★★ ASOSIY TEST: o'quvchi rasm yuklaydi -> bazada KALIT paydo bo'ladi
    /// -> ustoz o'sha faylni AYNAN o'sha baytlar bilan ochadi.
    ///
    /// Baytlar TASODIFIY: agar biror joyda javob keshlansa yoki noto'g'ri
    /// obyekt qaytsa, solishtiruv darrov yiqiladi.
    /// </summary>
    [Fact]
    public async Task Submit_ThenTeacherDownloads_ReturnsExactSameBytes()
    {
        var world = await WorldBuilder.CreateAsync(factory, "ombor");
        var assignmentId = await CreateAssignmentAsync(world.GroupId);

        var payload = RandomImage(64 * 1024);

        using var student = await ClientAsync(world.Student);

        var submitted = await student.PostAsync(
            SubmitUri(assignmentId), Multipart("Rasmli javob", ("ish.png", "image/png", payload)));

        submitted.StatusCode.Should().Be(HttpStatusCode.OK,
            await submitted.Content.ReadAsStringAsync());

        var fileId = await SingleFileIdAsync(assignmentId, world.Student.Id);

        using var teacher = await ClientAsync(world.Teacher);

        var response = await teacher.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var downloaded = await response.Content.ReadAsByteArrayAsync();

        downloaded.Should().Equal(payload, "ombordan AYNAN o'sha baytlar qaytishi kerak");

        // Tur BAZADAN keladi (yuklashda mazmundan aniqlangan), ombor
        // sarlavhasidan emas.
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");

        // Nom obyekt kalitini OSHKOR QILMAYDI.
        var disposition = response.Content.Headers.ContentDisposition;
        disposition!.FileName.Should().NotBeNull();
        disposition.FileName.Should().EndWith(".png");
        disposition.FileName.Should().NotContain("/", "kalit yo'li nomga chiqmasin");

        // Brauzer turni O'ZI taxmin qilib, faylni HTML deb ko'rsatmasin.
        response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff!.Should().ContainSingle().Which.Should().Be("nosniff");

        // O'quvchining ishi shaxsiy — oraliq proksi ham, brauzer diski ham
        // saqlab qolmasin.
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    /// <summary>
    /// ★ BAZADA TO'LIQ URL EMAS, FAQAT KALIT.
    ///
    /// Eski tizim bazaga `/media/...` URL yozardi va manzil o'zgargan kuni
    /// barcha eski havolalar o'lardi. Presigned URL bo'lsa yanada yomon:
    /// u BIR SOATDAN keyin o'zi o'lardi ("linkim ishlamayapti").
    /// </summary>
    [Fact]
    public async Task Submit_StoresObjectKeyOnly_NotUrl()
    {
        var world = await WorldBuilder.CreateAsync(factory, "kalit");
        var assignmentId = await CreateAssignmentAsync(world.GroupId);

        using var student = await ClientAsync(world.Student);

        var submitted = await student.PostAsync(
            SubmitUri(assignmentId), Multipart(null, ("ish.png", "image/png", RandomImage(2048))));

        submitted.StatusCode.Should().Be(HttpStatusCode.OK,
            await submitted.Content.ReadAsStringAsync());

        var key = await SingleObjectKeyAsync(assignmentId, world.Student.Id);

        key.Should().NotContain("http", "bazada MANZIL emas, KALIT turishi kerak");
        key.Should().StartWith(factory.KeyPrefix + "/");
        key.Should().MatchRegex(KeyShape.ToString());

        // Kalitda o'quvchi ID'si bor — nosozlik tekshirilganda kimning
        // fayli ekani darrov ko'rinadi.
        KeyShape.Match(key).Groups["student"].Value
            .Should().Be(world.Student.Id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// ★★ KALITNI TAXMIN QILIB BO'LMAYDI.
    ///
    /// Ikki fayl ayni o'quvchi tomonidan, ayni oyda, ayni nom bilan
    /// yuklanadi. Kalitlar BOSHQA-BOSHQA bo'lishi shart: aks holda
    /// (masalan kalit `submissions/2026-07/42/ish.png` bo'lganda) begona
    /// odam o'quvchi ID'sini bilsa, kalitni yozib chiqib, omborga
    /// to'g'ridan-to'g'ri murojaat qilardi.
    ///
    /// Tasodifiy qism 8 bayt = 64 bit: taxmin qilish amalda imkonsiz.
    /// </summary>
    [Fact]
    public async Task Submit_TwoFiles_ProducesUnguessableDistinctKeys()
    {
        var world = await WorldBuilder.CreateAsync(factory, "taxmin");
        var assignmentId = await CreateAssignmentAsync(world.GroupId);

        using var student = await ClientAsync(world.Student);

        var response = await student.PostAsync(
            SubmitUri(assignmentId),
            Multipart(
                null,
                ("ish.png", "image/png", RandomImage(1024)),
                ("ish.png", "image/png", RandomImage(1024))));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var keys = await factory.WithDbAsync(db => db.SubmissionFiles
            .AsNoTracking()
            .Where(f => f.Submission!.AssignmentId == assignmentId)
            .Select(f => f.ObjectKey)
            .ToListAsync());

        keys.Should().HaveCount(2);
        keys.Should().OnlyHaveUniqueItems("kalit takrorlansa fayl ustiga yozilardi");

        foreach (var key in keys)
            key.Should().MatchRegex(KeyShape.ToString());

        // Faqat TASODIFIY qism farq qiladi — qolgan hammasi bir xil.
        // Ya'ni "boshqa fayl -> boshqa kalit" xususiyati aynan tasodifdan
        // kelayotganini isbotlaymiz (sana yoki ID'dan emas).
        var randoms = keys.ConvertAll(k => KeyShape.Match(k).Groups["random"].Value);

        randoms.Should().OnlyHaveUniqueItems();
        randoms.Should().OnlyContain(r => r.Length == 16);
    }

    // ================================================================= ruxsat (fayl HAQIQATAN bor)

    /// <summary>
    /// ★★ Fayl OMBORDA HAQIQATAN mavjud, lekin begona o'quvchi 403 oladi.
    ///
    /// Bu <see cref="SubmissionFileAccessTests"/> dagi 403 dan MUHIMROQ:
    /// u yerda ombor umuman yo'q edi, ya'ni "topilmagani uchun rad etdi"
    /// degan shubha qolardi. Bu yerda fayl bor, o'qish yo'li ishlaydi —
    /// va baribir TAQIQ.
    /// </summary>
    [Fact]
    public async Task Download_ExistingFile_AsForeignStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "ombor-taqiq");
        var stranger = await WorldBuilder.CreateAsync(factory, "ombor-begona");

        var assignmentId = await CreateAssignmentAsync(world.GroupId);

        using var owner = await ClientAsync(world.Student);

        var submitted = await owner.PostAsync(
            SubmitUri(assignmentId), Multipart(null, ("ish.png", "image/png", RandomImage(4096))));

        submitted.StatusCode.Should().Be(HttpStatusCode.OK,
            await submitted.Content.ReadAsStringAsync());

        var fileId = await SingleFileIdAsync(assignmentId, world.Student.Id);

        // Egasi ocha oladi — ya'ni fayl HAQIQATAN o'qiladigan holatda.
        var mine = await owner.GetAsync(FileUri(fileId));
        mine.StatusCode.Should().Be(HttpStatusCode.OK);

        using var client = await ClientAsync(stranger.Student);

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "fayl bor bo'lsa ham begona o'quvchi UNI OCHOLMAYDI");
    }

    /// <summary>
    /// Bazada yozuv bor, omborda obyekt YO'Q (ombor qo'lda tozalangan
    /// holat) -> 404. Bu 503 EMAS: ombor sog'lom, faqat obyekt yo'q.
    /// </summary>
    [Fact]
    public async Task Download_WhenObjectMissingInStorage_ReturnsNotFound()
    {
        var world = await WorldBuilder.CreateAsync(factory, "ombor-yo-q");
        var assignmentId = await CreateAssignmentAsync(world.GroupId);

        using var student = await ClientAsync(world.Student);

        var submitted = await student.PostAsync(
            SubmitUri(assignmentId), Multipart(null, ("ish.png", "image/png", RandomImage(1024))));

        submitted.StatusCode.Should().Be(HttpStatusCode.OK,
            await submitted.Content.ReadAsStringAsync());

        var fileId = await SingleFileIdAsync(assignmentId, world.Student.Id);

        // Kalitni omborda mavjud bo'lmagan qiymatga o'zgartiramiz.
        await factory.WithDbAsync(async db =>
        {
            var file = await db.SubmissionFiles.FirstAsync(f => f.Id == fileId);
            file.ObjectKey = factory.KeyPrefix + "/2026-07/1/00000000deadbeef.png";
            await db.SaveChangesAsync();
            return 0;
        });

        var response = await student.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var tokens = await factory.LoginAsync(user.Email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<long> CreateAssignmentAsync(long groupId)
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var admin = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new
            {
                title = "Ombor vazifasi " + Guid.NewGuid().ToString("N")[..6],
                groupId,
                maxScore = 5m,
                allowedFormats = "Text, Image",
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<CreatedAssignment>();
        return created!.Id;
    }

    private Task<long> SingleFileIdAsync(long assignmentId, long studentId) =>
        factory.WithDbAsync(db => db.SubmissionFiles
            .AsNoTracking()
            .Where(f => f.Submission!.AssignmentId == assignmentId
                     && f.Submission.StudentId == studentId)
            .Select(f => f.Id)
            .SingleAsync());

    private Task<string> SingleObjectKeyAsync(long assignmentId, long studentId) =>
        factory.WithDbAsync(db => db.SubmissionFiles
            .AsNoTracking()
            .Where(f => f.Submission!.AssignmentId == assignmentId
                     && f.Submission.StudentId == studentId)
            .Select(f => f.ObjectKey)
            .SingleAsync());

    /// <summary>Sehrli baytlari PNG, qolgani TASODIFIY — solishtiruv ma'noli bo'lsin.</summary>
    private static byte[] RandomImage(int totalBytes)
    {
        var bytes = RandomNumberGenerator.GetBytes(totalBytes);
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

    private static Uri FileUri(long fileId) =>
        new(string.Create(CultureInfo.InvariantCulture, $"/api/v1/submissions/files/{fileId}"),
            UriKind.Relative);

    private sealed record CreatedAssignment(long Id);
}
