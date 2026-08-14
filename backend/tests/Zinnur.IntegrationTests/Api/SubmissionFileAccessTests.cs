using System.Globalization;
using System.Net;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// ★★ TOPSHIRILGAN FAYLGA KIRISH — RUXSAT MATRITSASI
/// ========================================================================
///
/// ESKI TIZIMNING X-6 KAMCHILIGI: fayllar `/media` katalogida
/// AUTENTIFIKATSIYASIZ turardi — havolani bilgan istalgan odam begona
/// o'quvchining ishini ko'ra olardi. Bu sinf aynan o'sha teshik
/// qaytmaganini isbotlaydi.
///
/// ★ OMBOR BU YERDA ATAYLAB SOZLANMAGAN (`ZinnurApiFactory` `Storage:*` ni
/// bo'shatadi). Shu tufayli javob kodi ikki holatni ANIQ ajratadi:
///
///     403  -> ruxsat qoidasi TO'SDI (fayl bor-yo'qligi ahamiyatsiz);
///     503  -> ruxsat qoidasidan O'TDI, keyin ombor yo'qligi bilindi.
///
/// Ya'ni 503 — bu "kira oldi" degan ISBOT, 403 esa "kira olmadi".
/// Haqiqiy o'qish yo'li (baytlar) `SubmissionFileStorageTests` da,
/// jonli MinIO bilan tekshiriladi.
/// </summary>
public sealed class SubmissionFileAccessTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= TAQIQ

    /// <summary>
    /// ★★ ENG MUHIM TEST: BEGONA O'QUVCHI — 403.
    ///
    /// O'quvchi tizimga kirgan va fayl ID'sini biladi (ID'lar ketma-ket,
    /// ya'ni taxmin qilish oson). Baribir ocholmaydi.
    /// </summary>
    [Fact]
    public async Task Download_AsForeignStudent_ReturnsForbidden()
    {
        var owner = await WorldBuilder.CreateAsync(factory, "fayl-ega");
        var stranger = await WorldBuilder.CreateAsync(factory, "fayl-begona");

        var fileId = await AddFileAsync(owner.GroupId, owner.Student.Id);

        using var client = await ClientAsync(stranger.Student);

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "begona o'quvchi boshqa bolaning ishini KO'RMASLIGI kerak");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tegishli emas");
    }

    /// <summary>★ Begona GURUHNING ustozi ham ocholmaydi.</summary>
    [Fact]
    public async Task Download_AsForeignTeacher_ReturnsForbidden()
    {
        var owner = await WorldBuilder.CreateAsync(factory, "fayl-ega2");
        var stranger = await WorldBuilder.CreateAsync(factory, "fayl-begona2");

        var fileId = await AddFileAsync(owner.GroupId, owner.Student.Id);

        using var client = await ClientAsync(stranger.Teacher);

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ustoz FAQAT o'z guruhidagi o'quvchining ishini ko'radi");
    }

    /// <summary>Token umuman bo'lmasa — 401. "Havolani bilish" hech nima bermaydi.</summary>
    [Fact]
    public async Task Download_WithoutToken_ReturnsUnauthorized()
    {
        var owner = await WorldBuilder.CreateAsync(factory, "fayl-anon");

        var fileId = await AddFileAsync(owner.GroupId, owner.Student.Id);

        using var client = factory.CreateClient();

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================= RUXSAT

    /// <summary>
    /// EGASI o'zining faylini ochadi. Ombor yo'q => 503, ya'ni ruxsat
    /// qoidasidan O'TDI (403 EMAS).
    /// </summary>
    [Fact]
    public async Task Download_AsOwner_PassesPermissionAndHitsMissingStorage()
    {
        var owner = await WorldBuilder.CreateAsync(factory, "fayl-men");

        var fileId = await AddFileAsync(owner.GroupId, owner.Student.Id);

        using var client = await ClientAsync(owner.Student);

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Storage:", "administrator nima qilishini bilishi kerak");
    }

    /// <summary>O'Z guruhining ustozi — ruxsat bor (baholash uchun shart).</summary>
    [Fact]
    public async Task Download_AsOwnTeacher_PassesPermission()
    {
        var owner = await WorldBuilder.CreateAsync(factory, "fayl-ustoz");

        var fileId = await AddFileAsync(owner.GroupId, owner.Student.Id);

        using var client = await ClientAsync(owner.Teacher);

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "403 bo'lsa — ustoz o'z o'quvchisini baholay olmasdi");
    }

    /// <summary>
    /// ★ KURATOR ham ocha oladi. Eski tizimda kurator havolasi hisobga
    /// olinmagani uchun (B-8a) kurator o'z o'quvchisining ishini umuman
    /// ko'ra olmasdi.
    /// </summary>
    [Fact]
    public async Task Download_AsCurator_PassesPermission()
    {
        var owner = await WorldBuilder.CreateAsync(factory, "fayl-kurator");

        var fileId = await AddFileAsync(owner.GroupId, owner.Student.Id);

        using var client = await ClientAsync(owner.Curator);

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>O'quv bo'limi/admin — hammasini ko'radi.</summary>
    [Fact]
    public async Task Download_AsAdmin_PassesPermission()
    {
        var owner = await WorldBuilder.CreateAsync(factory, "fayl-admin");

        var fileId = await AddFileAsync(owner.GroupId, owner.Student.Id);

        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await client.GetAsync(FileUri(fileId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ================================================================= YO'Q FAYL

    /// <summary>Mavjud bo'lmagan ID — 404 (503 EMAS: ombor bu yerda ishtirok etmaydi).</summary>
    [Fact]
    public async Task Download_UnknownFile_ReturnsNotFound()
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await client.GetAsync(FileUri(long.MaxValue));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// ★ OBYEKT KALITI SO'ROVDA QABUL QILINMAYDI.
    ///
    /// Endpoint faqat `{fileId:long}` ni oladi. Kalitni yo'l sifatida
    /// yozishga urinish marshrutga UMUMAN tushmaydi (404), ya'ni
    /// `../` yoki begona bucket yo'li bilan chiqib bo'lmaydi.
    /// </summary>
    [Theory]
    [InlineData("submissions/2026-07/1/deadbeefdeadbeef.png")]
    [InlineData("..%2F..%2Fetc%2Fpasswd")]
    [InlineData("1.png")]
    public async Task Download_WithObjectKeyInsteadOfId_IsNotRouted(string key)
    {
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await client.GetAsync(
            new Uri("/api/v1/submissions/files/" + key, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "marshrut faqat son qabul qiladi — kalit tashqaridan kelmaydi");
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var tokens = await factory.LoginAsync(user.Email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    /// <summary>
    /// Javob va unga ilova qilingan fayl yozuvini BAZAGA to'g'ridan-to'g'ri
    /// qo'shadi.
    ///
    /// NIMA UCHUN API ORQALI EMAS: bu sinfda ombor ataylab sozlanmagan,
    /// ya'ni API orqali fayl yuklab bo'lmaydi (503 — bu boshqa testning
    /// mavzusi). Bu yerda tekshiriladigan narsa RUXSAT QOIDASI, u esa
    /// yozuv qanday paydo bo'lganiga bog'liq emas.
    /// </summary>
    private Task<long> AddFileAsync(long groupId, long studentId) =>
        factory.WithDbAsync(async db =>
        {
            var assignment = new Assignment
            {
                GroupId = groupId,
                Title = "Fayl vazifasi " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text | AnswerFormats.Image,
            };

            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var submission = new Submission
            {
                AssignmentId = assignment.Id,
                StudentId = studentId,
                Status = SubmissionStatus.Submitted,
                SubmittedAt = DateTimeOffset.UtcNow,
            };

            db.Submissions.Add(submission);
            await db.SaveChangesAsync();

            var file = new SubmissionFile
            {
                SubmissionId = submission.Id,
                // Omborda bunday obyekt YO'Q — bu testlarda ahamiyatsiz:
                // ruxsat tekshiruvi omborga murojaatdan OLDIN bo'ladi.
                ObjectKey = "submissions/2026-07/"
                    + studentId.ToString(CultureInfo.InvariantCulture)
                    + "/" + Guid.NewGuid().ToString("N")[..16] + ".png",
                Kind = AttachmentKind.Image,
                SizeBytes = 1024,
                ContentType = "image/png",
            };

            db.SubmissionFiles.Add(file);
            await db.SaveChangesAsync();

            return file.Id;
        });

    private static Uri FileUri(long fileId) =>
        new(string.Create(CultureInfo.InvariantCulture, $"/api/v1/submissions/files/{fileId}"),
            UriKind.Relative);
}
