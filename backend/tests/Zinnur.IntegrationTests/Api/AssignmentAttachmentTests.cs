using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence.Migrations;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// UY VAZIFASI SHARTINING BIRIKTIRMALARI + `ImageKey` BACKFILL
/// ========================================================================
///
/// Nima isbotlanadi:
///   1) shartga rasm/audio/PDF biriktiriladi va turi MAZMUNDAN aniqlanadi;
///   2) 🔴 `objectKey` javoblarda YO'Q;
///   3) `Range` shart faylida ham ishlaydi (uzun audio namunada seek);
///   4) 🔴 `allowedFormats` bo'sh bo'lsa -> **400** (jimgina tuzoq yopilgan);
///   5) 🔴 BACKFILL: `Assignments.ImageKey` -> `AssignmentAttachments`
///      xaritalashi to'g'ri va TAKROR yozuv yasamaydi;
///   6) o'quvchining AUDIO javobi haqiqatan ishlaydi (mavjud yo'l).
/// </summary>
[Collection(LessonMediaFixture.Name)]
public sealed class AssignmentAttachmentTests(StorageBackedApiFactory factory)
{
    private static readonly byte[] PngMagic =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>OggS — brauzer/telefon ovoz yozuvi formatlaridan biri.</summary>
    private static readonly byte[] OggMagic =
        [(byte)'O', (byte)'g', (byte)'g', (byte)'S'];

    private static readonly byte[] PdfMagic =
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7'];

    /// <summary>EXE (`MZ`) — ruxsat ro'yxatida YO'Q, rad etilishi kerak.</summary>
    private static readonly byte[] ExeMagic = [(byte)'M', (byte)'Z'];

    // ================================================================= YUKLASH

    /// <summary>
    /// Rasm, audio va PDF — uchalasi biriktiriladi, turi MAZMUNDAN
    /// aniqlanadi va tartib ZICH bo'ladi.
    /// </summary>
    [Fact]
    public async Task Upload_AcceptsImageAudioAndDocument()
    {
        var assignmentId = await NewGroupAssignmentAsync("shart-uchta");

        var image = await UploadAsync(assignmentId, "varaq.png", "image/png", Magic(PngMagic, 2048));
        var audio = await UploadAsync(assignmentId, "namuna.ogg", "audio/ogg", Magic(OggMagic, 2048));
        var document = await UploadAsync(assignmentId, "shart.pdf", "application/pdf", Magic(PdfMagic, 2048));

        image.Kind.Should().Be("Image");
        image.ContentType.Should().Be("image/png");
        image.Position.Should().Be(0);

        audio.Kind.Should().Be("Audio");
        audio.ContentType.Should().Be("audio/ogg");
        audio.Position.Should().Be(1);

        document.Kind.Should().Be("Document");
        document.ContentType.Should().Be("application/pdf");
        document.Position.Should().Be(2);
    }

    /// <summary>
    /// 🔴 `.png` deb nomlangan EXE -> 400. Tur MAZMUNDAN aniqlanadi va
    /// ruxsat ro'yxatida bo'lmagan format RAD ETILADI ("noma'lum bo'lsa
    /// ruxsat berish" TAQIQ).
    /// </summary>
    [Fact]
    public async Task Upload_ExeNamedAsPng_ReturnsBadRequest()
    {
        var assignmentId = await NewGroupAssignmentAsync("shart-exe");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            AttachmentsUri(assignmentId),
            Multipart("rasm.png", "image/png", Magic(ExeMagic, 1024)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemText.ReadAsync(response)).Should().Contain("qo'llab-quvvatlanmaydi");
    }

    /// <summary>
    /// ⚠️ ONGLI CHEKINISH: MP4 konteyneri shart biriktirmasi yo'lida AUDIO
    /// deb qabul qilinadi (400 EMAS).
    ///
    /// SABABI: `ftyp` (ISO-BMFF) konteynerida audio ham, video ham AYNI
    /// sehrli baytlar bilan boshlanadi va ularni faqat konteyner ichini
    /// tahlil qilib ajratish mumkin. Ajratish IMKONSIZ bo'lgan joyda
    /// tanlov shunday qilingan: `Audio` ustun turadi, chunki iOS
    /// Safari'ning OVOZ yozuvi ba'zan VIDEO brendi bilan keladi — teskari
    /// tanlov o'quvchining ovozli javobini jimgina rad etardi
    /// (`MediaSignatures` izohi).
    ///
    /// Bu test shu xatti-harakatni QOTIRIB qo'yadi, ya'ni kelajakda kimdir
    /// uni "tuzatmoqchi" bo'lsa, sabab yonida turadi.
    /// </summary>
    [Fact]
    public async Task Upload_Mp4Container_IsAcceptedAsAudioByDesign()
    {
        var assignmentId = await NewGroupAssignmentAsync("shart-video");

        var mp4 = new byte[]
        {
            0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
        };

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            AttachmentsUri(assignmentId), Multipart("dars.mp4", "video/mp4", Magic(mp4, 2048)));

        // ⚠️ ISO-BMFF konteyneri IKKI MA'NOLI: `Audio` ruxsat etilgani uchun
        //    u AUDIO deb qabul qilinadi (`MediaSignatures` izohi). Ya'ni bu
        //    yerda 400 KUTILMAYDI — natija 201 va turi `Audio`.
        //    Bu ONGLI xatti-harakat: haqiqiy video uchun dars mediasi yo'li
        //    bor, bu esa "audio konteyneri" holatini buzmaslik uchun.
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        var created = (await response.Content.ReadFromJsonAsync<AttachmentRow>())!;

        created.Kind.Should().Be("Audio");
    }

    // ================================================================= objectKey

    /// <summary>🔴 `objectKey` NA yuklash javobida, NA vazifa kartochkasida YO'Q.</summary>
    [Fact]
    public async Task ObjectKey_IsNeverExposed()
    {
        var assignmentId = await NewGroupAssignmentAsync("shart-kalit");

        var attachment = await UploadAsync(
            assignmentId, "varaq.png", "image/png", Magic(PngMagic, 1024));

        var storedKey = await factory.WithDbAsync(db => db.AssignmentAttachments
            .AsNoTracking()
            .Where(a => a.Id == attachment.Id)
            .Select(a => a.ObjectKey)
            .SingleAsync());

        storedKey.Should().StartWith(factory.KeyPrefix + "/");

        using var admin = await AdminClientAsync();

        var card = await admin.GetStringAsync(AssignmentUri(assignmentId));

        card.Should().NotContain("objectKey");
        card.Should().NotContain(storedKey);

        // Vazifa ro'yxatida ham.
        var list = await admin.GetStringAsync(
            new Uri("/api/v1/assignments?pageSize=100", UriKind.Relative));

        list.Should().NotContain(storedKey);
    }

    /// <summary>Kartochkada `attachments` haqiqatan qaytadi (bo'sh emas).</summary>
    [Fact]
    public async Task AssignmentCard_ContainsAttachments()
    {
        var assignmentId = await NewGroupAssignmentAsync("shart-karta");

        var attachment = await UploadAsync(
            assignmentId, "varaq.png", "image/png", Magic(PngMagic, 1024));

        using var admin = await AdminClientAsync();

        var card = await admin.GetFromJsonAsync<AssignmentRow>(AssignmentUri(assignmentId));

        card!.Attachments.Should().ContainSingle()
            .Which.Id.Should().Be(attachment.Id);
    }

    // ================================================================= Range

    /// <summary>
    /// `Range` shart faylida ham ishlaydi: uzun audio namunada oldinga
    /// o'tish kerak bo'ladi.
    /// </summary>
    [Fact]
    public async Task Download_WithRange_Returns206()
    {
        var assignmentId = await NewGroupAssignmentAsync("shart-range");

        var payload = Magic(OggMagic, 4096);

        var attachment = await UploadAsync(assignmentId, "uzun.ogg", "audio/ogg", payload);

        using var admin = await AdminClientAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Get, AttachmentUri(attachment.Id));

        request.Headers.Range = new RangeHeaderValue(10, 59);

        var response = await admin.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var body = await response.Content.ReadAsByteArrayAsync();

        body.Should().HaveCount(50);
        body.Should().Equal(payload[10..60]);
    }

    // ================================================================= allowedFormats

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 `allowedFormats` BO'SH -> 400 (JIMGINA TUZOQ YOPILGAN)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Ilgari bunday vazifa MUVAFFAQIYATLI yaratilardi (yoki 409 berardi) va
    /// o'quvchi uni ko'rardi — lekin HAR QANDAY javob rad etilardi. Ya'ni
    /// vazifa mavjud, muddati ketmoqda, topshirish esa TEXNIK JIHATDAN
    /// imkonsiz.
    ///
    /// Xato AYNAN `allowedFormats` maydoni ostida ko'rinishi kerak — forma
    /// uni to'g'ri katakcha yonida ko'rsatsin.
    /// </summary>
    [Fact]
    public async Task Create_WithNoAnswerFormats_ReturnsBadRequest()
    {
        var groupId = await NewGroupAsync("format-bosh");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/assignments", new
        {
            title = "Formatsiz vazifa",
            groupId,
            maxScore = 5m,
            allowedFormats = "None",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "409 EMAS: bu maydon validatsiyasi");

        var body = await ProblemText.ReadAsync(response);

        body.Should().Contain("allowedFormats");
        body.Should().Contain("Kamida bitta");
    }

    /// <summary>Tahrirlashda ham AYNI to'siq: mavjud vazifani "o'lik" qilib bo'lmaydi.</summary>
    [Fact]
    public async Task Update_WithNoAnswerFormats_ReturnsBadRequest()
    {
        var assignmentId = await NewGroupAssignmentAsync("format-tahrir");

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(AssignmentUri(assignmentId), new
        {
            title = "Formatsiz",
            maxScore = 5m,
            allowedFormats = "None",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemText.ReadAsync(response)).Should().Contain("allowedFormats");
    }

    // ================================================================= BACKFILL

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 BACKFILL: `Assignments.ImageKey` -> `AssignmentAttachments`
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// ★ NIMA UCHUN SQL TO'G'RIDAN-TO'G'RI BAJARILADI: migratsiya test
    /// bazasi yaratilganda ALLAQACHON o'tgan, ya'ni "migratsiyadan oldin"
    /// holatini qayta yasash mumkin emas. Shuning uchun test AYNAN
    /// migratsiyada ishlatilgan SQL doimiysini
    /// (<see cref="Wave1_GroupVideoStart_UserProfile_LessonAssets.ImageKeyBackfillSql"/>)
    /// oladi va uni eski shakldagi qator ustida bajaradi. Nusxa
    /// saqlanmaydi: migratsiya SQL'i o'zgarsa test AVTOMATIK yangisini
    /// tekshiradi.
    ///
    /// Isbotlanadi: xaritalash to'g'ri (kind, contentType, position) VA
    /// takroriy bajarish IKKINCHI nusxa yasamaydi (idempotent).
    /// </summary>
    [Fact]
    public async Task Backfill_MovesImageKeyIntoAttachments()
    {
        var groupId = await NewGroupAsync("backfill");

        // Eski shakldagi vazifa: rasm FAQAT `ImageKey` da (biriktirma yo'q).
        var assignmentId = await factory.WithDbAsync(async db =>
        {
            var assignment = new Assignment
            {
                GroupId = groupId,
                Title = "Eski vazifa " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text | AnswerFormats.Image,
                ImageKey = "submissions/2026-01/7/deadbeefdeadbeef.png",
            };

            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            return assignment.Id;
        });

        // Migratsiyadagi AYNI SQL.
        await factory.WithDbAsync(async db =>
            await db.Database.ExecuteSqlRawAsync(
                Wave1_GroupVideoStart_UserProfile_LessonAssets.ImageKeyBackfillSql));

        var attachments = await factory.WithDbAsync(db => db.AssignmentAttachments
            .AsNoTracking()
            .Where(a => a.AssignmentId == assignmentId)
            .ToListAsync());

        var moved = attachments.Should().ContainSingle().Subject;

        moved.Kind.Should().Be(AttachmentKind.Image, "eski ustun ATAYLAB faqat rasm uchun edi");
        moved.Position.Should().Be(0);
        moved.ObjectKey.Should().Be("submissions/2026-01/7/deadbeefdeadbeef.png");

        // MIME kalitning KENGAYTMASIDAN aniqlanadi (eski yozuvda saqlanmagan).
        moved.ContentType.Should().Be("image/png");

        // Hajm NOMA'LUM — nol "bilmayman" degan ROSTGO'Y qiymat.
        moved.SizeBytes.Should().Be(0);

        // ---- IDEMPOTENTLIK: ikkinchi marta bajarilsa TAKROR yozuv yo'q ----
        await factory.WithDbAsync(async db =>
            await db.Database.ExecuteSqlRawAsync(
                Wave1_GroupVideoStart_UserProfile_LessonAssets.ImageKeyBackfillSql));

        var again = await factory.WithDbAsync(db => db.AssignmentAttachments
            .AsNoTracking()
            .CountAsync(a => a.AssignmentId == assignmentId));

        again.Should().Be(1, "migratsiya qayta yurgizilsa nusxa paydo bo'lmasligi kerak");

        // ---- Ko'chirilgan rasm API javobida KO'RINADI ----
        using var admin = await AdminClientAsync();

        var card = await admin.GetFromJsonAsync<AssignmentRow>(AssignmentUri(assignmentId));

        card!.Attachments.Should().ContainSingle()
            .Which.Kind.Should().Be("Image");

        // `imageKey` HAM qaytadi (deprecated, mavjud klientlar uchun).
        card.ImageKey.Should().Be("submissions/2026-01/7/deadbeefdeadbeef.png");
    }

    /// <summary>Kengaytmasi noma'lum kalit — `application/octet-stream`.</summary>
    [Fact]
    public async Task Backfill_UnknownExtension_UsesOctetStream()
    {
        var groupId = await NewGroupAsync("backfill-noma");

        var assignmentId = await factory.WithDbAsync(async db =>
        {
            var assignment = new Assignment
            {
                GroupId = groupId,
                Title = "Kengaytmasiz " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text,
                ImageKey = "submissions/2026-01/7/abcdef1234567890",
            };

            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            return assignment.Id;
        });

        await factory.WithDbAsync(async db =>
            await db.Database.ExecuteSqlRawAsync(
                Wave1_GroupVideoStart_UserProfile_LessonAssets.ImageKeyBackfillSql));

        var contentType = await factory.WithDbAsync(db => db.AssignmentAttachments
            .AsNoTracking()
            .Where(a => a.AssignmentId == assignmentId)
            .Select(a => a.ContentType)
            .SingleAsync());

        contentType.Should().Be("application/octet-stream");
    }

    /// <summary>Bo'sh/probel `ImageKey` — KO'CHIRILMAYDI (yolg'on yozuv yasalmasin).</summary>
    [Fact]
    public async Task Backfill_SkipsBlankImageKeys()
    {
        var groupId = await NewGroupAsync("backfill-bosh");

        var assignmentId = await factory.WithDbAsync(async db =>
        {
            var assignment = new Assignment
            {
                GroupId = groupId,
                Title = "Bo'sh kalit " + Guid.NewGuid().ToString("N")[..6],
                MaxScore = 5m,
                AllowedFormats = AnswerFormats.Text,
                ImageKey = "   ",
            };

            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            return assignment.Id;
        });

        await factory.WithDbAsync(async db =>
            await db.Database.ExecuteSqlRawAsync(
                Wave1_GroupVideoStart_UserProfile_LessonAssets.ImageKeyBackfillSql));

        var count = await factory.WithDbAsync(db => db.AssignmentAttachments
            .AsNoTracking()
            .CountAsync(a => a.AssignmentId == assignmentId));

        count.Should().Be(0);
    }

    // ================================================================= O'QUVCHI AUDIO JAVOBI

    /// <summary>
    /// ★ MAVJUD YO'L TEKSHIRUVI: o'quvchining AUDIO javobi haqiqatan
    /// ishlaydimi (topshiriqda shu so'ralgan).
    ///
    /// Natija: ISHLAYDI. `SubmissionAttachmentReader` OggS/webm/m4a/mp3/wav
    /// ni taniydi va `SubmissionFile.Kind = Audio` deb yozadi. Bu test shu
    /// xulosani QOTIRIB qo'yadi — kelajakda sniffer o'zgarganda audio javob
    /// jimgina ishlamay qolmasin.
    /// </summary>
    [Fact]
    public async Task StudentAudioSubmission_IsAcceptedAndStoredAsAudio()
    {
        var world = await WorldBuilder.CreateAsync(factory, "audio-javob");

        using var admin = await AdminClientAsync();

        var created = await admin.PostAsJsonAsync("/api/v1/assignments", new
        {
            title = "Talaffuz " + Guid.NewGuid().ToString("N")[..6],
            groupId = world.GroupId,
            maxScore = 5m,

            // FAQAT audio — ya'ni matn yoki rasm rad etiladi.
            allowedFormats = "Audio",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());

        var assignmentId = (await created.Content.ReadFromJsonAsync<AssignmentRow>())!.Id;

        var tokens = await factory.LoginAsync(world.Student.Email);
        using var student = factory.CreateAuthorizedClient(tokens.AccessToken);

        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(Magic(OggMagic, 4096));
        part.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        content.Add(part, "files", "javob.ogg");

        var submitted = await student.PostAsync(
            Relative($"/api/v1/assignments/{assignmentId}/submit"), content);

        submitted.StatusCode.Should().Be(HttpStatusCode.OK,
            await submitted.Content.ReadAsStringAsync());

        var kind = await factory.WithDbAsync(db => db.SubmissionFiles
            .AsNoTracking()
            .Where(f => f.Submission!.AssignmentId == assignmentId)
            .Select(f => f.Kind)
            .SingleAsync());

        kind.Should().Be(AttachmentKind.Audio,
            "audio javob `Audio` deb yozilishi kerak, aks holda formatlar tekshiruvi buzilardi");
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<long> NewGroupAsync(string prefix)
    {
        var world = await WorldBuilder.CreateAsync(factory, prefix);
        return world.GroupId;
    }

    /// <summary>Guruh vazifasi (kurs vazifasi emas — u faqat o'quv bo'limiga).</summary>
    private async Task<long> NewGroupAssignmentAsync(string prefix)
    {
        var groupId = await NewGroupAsync(prefix);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/assignments", new
        {
            title = prefix + " " + Guid.NewGuid().ToString("N")[..6],
            groupId,
            maxScore = 5m,
            allowedFormats = "Text, Image",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<AssignmentRow>())!.Id;
    }

    private async Task<AttachmentRow> UploadAsync(
        long assignmentId, string fileName, string contentType, byte[] payload)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            AttachmentsUri(assignmentId), Multipart(fileName, contentType, payload));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<AttachmentRow>())!;
    }

    private static byte[] Magic(byte[] magic, int totalBytes)
    {
        var bytes = RandomNumberGenerator.GetBytes(totalBytes);
        magic.CopyTo(bytes, 0);
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

    private static Uri AttachmentsUri(long assignmentId) =>
        Relative($"/api/v1/assignments/{assignmentId}/attachments");

    private static Uri AttachmentUri(long attachmentId) =>
        Relative($"/api/v1/assignments/attachments/{attachmentId}");

    private static Uri AssignmentUri(long assignmentId) =>
        Relative($"/api/v1/assignments/{assignmentId}");

    private static Uri Relative(FormattableString path) =>
        new(FormattableString.Invariant(path), UriKind.Relative);

    private sealed record AttachmentRow(
        long Id,
        long AssignmentId,
        string Kind,
        int Position,
        string ContentType,
        long SizeBytes,
        int? DurationSec);

    private sealed record AssignmentRow(
        long Id,
        string Title,
        string? ImageKey,
        List<AttachmentRow> Attachments);
}
