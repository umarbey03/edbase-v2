using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// ★★ DARS MEDIASI — TO'LIQ YO'L (yuklash -> oqim -> tartib -> o'chirish)
/// ========================================================================
///
/// HAQIQIY MinIO bilan ishlaydi (<see cref="StorageBackedApiFactory"/>):
/// soxta ombor SigV4 imzosini, `Range` uzatishni va oqim bilan yozishni
/// "to'g'ri" deb qabul qilardi va yashil natija HECH NIMANI isbotlamasdi.
///
/// Nima isbotlanadi:
///   1) 🔴 `Range: bytes=100-199` -> **206**, `Content-Range` to'g'ri, tana
///      AYNAN 100 bayt va AYNAN o'sha baytlar (JONLI isbot);
///   2) `Range` yo'q -> **200** + `Accept-Ranges: bytes`;
///   3) 🔴 `objectKey` HECH BIR javobda YO'Q;
///   4) tur MAZMUNDAN aniqlanadi (`.mp4` deb nomlangan PDF -> **400**);
///   5) hajm chegarasi SOZLAMADAN keladi (oshsa -> **413**);
///   6) `reorder` TO'LIQ ro'yxat kutadi (yetishmasa -> **400**);
///   7) dars turini almashtirish: mos kelmaydigan media bo'lsa -> **409**.
///
/// MinIO ishlamayotgan bo'lsa bu sinf YIQILADI (o'tkazib yuborilmaydi):
/// "sinalmagan, lekin yashil" natija eng qimmat yolg'on.
/// </summary>
[Collection(LessonMediaFixture.Name)]
public sealed class LessonAssetEndpointsTests(StorageBackedApiFactory factory)
{
    /// <summary>
    /// ISO-BMFF (MP4) sarlavhasi: 4 bayt o'lcham, `ftyp`, so'ng BREND.
    /// `isom` — video brendi, ya'ni dars videosi yo'lida qabul qilinadi.
    /// </summary>
    private static readonly byte[] Mp4Magic =
        [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
         (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    private static readonly byte[] PngMagic =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>`%PDF-` — hujjat. Video yo'lida RAD ETILISHI kerak.</summary>
    private static readonly byte[] PdfMagic =
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7'];

    // ================================================================= YUKLASH

    /// <summary>
    /// ★★ ASOSIY TEST: video yuklanadi -> daraxtda `assets` paydo bo'ladi ->
    /// baytlar AYNAN o'sha holicha qaytadi.
    /// </summary>
    [Fact]
    public async Task UploadVideo_ThenDownload_ReturnsExactSameBytes()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Video dars");

        var payload = RandomVideo(8 * 1024);

        var asset = await UploadAsync(lessonId, "dars.mp4", "video/mp4", payload, title: "1-qism");

        asset.Kind.Should().Be("Video");
        asset.Position.Should().Be(0);
        asset.Title.Should().Be("1-qism");
        asset.ContentType.Should().Be("video/mp4");
        asset.SizeBytes.Should().Be(payload.Length);

        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(AssetUri(asset.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var downloaded = await response.Content.ReadAsByteArrayAsync();

        downloaded.Should().Equal(payload, "ombordan AYNAN o'sha baytlar qaytishi kerak");

        // Tur BAZADAN keladi (yuklashda mazmundan aniqlangan), ombor
        // sarlavhasidan emas.
        response.Content.Headers.ContentType?.MediaType.Should().Be("video/mp4");
    }

    /// <summary>
    /// 🔴 `Range` YO'Q -> 200 + `Accept-Ranges: bytes`.
    ///
    /// `Accept-Ranges` MAJBURIY: brauzer AYNAN shu sarlavhadan "bu faylda
    /// seek qilsa bo'ladi" degan xulosaga keladi. U bo'lmasa pleyer oldinga
    /// o'tish imkonini UMUMAN ko'rsatmaydi — hatto server `Range` ni
    /// qo'llasa ham.
    /// </summary>
    [Fact]
    public async Task Download_WithoutRange_ReturnsOkAndAdvertisesRanges()
    {
        var (assetId, payload) = await NewVideoAssetAsync(4096);

        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(AssetUri(assetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.AcceptRanges.Should().Contain("bytes",
            "busiz brauzer videoda seek imkonini ko'rsatmaydi");

        response.Content.Headers.ContentLength.Should().Be(payload.Length);

        // To'liq javobda `Content-Range` BO'LMASLIGI kerak.
        response.Content.Headers.ContentRange.Should().BeNull();

        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(payload);
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴🔴 ENG MUHIM TEST: `Range: bytes=100-199` -> 206, JONLI ISBOT
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Tekshiriladi:
    ///   • holat kodi AYNAN 206 (200 EMAS — aks holda brauzer butun faylni
    ///     boshidan oqizardi);
    ///   • `Content-Range: bytes 100-199/&lt;hajm&gt;` — chegaralar ham,
    ///     TO'LIQ hajm ham to'g'ri;
    ///   • `Content-Length` = 100;
    ///   • tana AYNAN 100 bayt VA AYNAN o'sha baytlar (100..199).
    ///
    /// Oxirgi band ENG NOZIK joyi: birlik xato (off-by-one) bo'lsa uzunlik
    /// baribir 100 chiqishi mumkin, lekin baytlar SURILGAN bo'ladi va video
    /// jimgina buzilardi.
    /// </summary>
    [Fact]
    public async Task Download_WithRange_Returns206WithExactBytes()
    {
        var (assetId, payload) = await NewVideoAssetAsync(4096);

        using var admin = await AdminClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, AssetUri(assetId));
        request.Headers.Range = new RangeHeaderValue(100, 199);

        var response = await admin.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent,
            "206 bo'lmasa brauzer videoni oxiriga o'tolmaydi");

        var contentRange = response.Content.Headers.ContentRange;

        contentRange.Should().NotBeNull();
        contentRange!.Unit.Should().Be("bytes");
        contentRange.From.Should().Be(100);
        contentRange.To.Should().Be(199);
        contentRange.Length.Should().Be(payload.Length, "TO'LIQ hajm ko'rsatilishi shart");

        response.Content.Headers.ContentLength.Should().Be(100);

        var body = await response.Content.ReadAsByteArrayAsync();

        body.Should().HaveCount(100);

        // 🔴 AYNAN O'SHA BAYTLAR (surilmagan).
        body.Should().Equal(payload[100..200],
            "bir baytga surilish ham videoni jimgina buzardi");

        // Qisman javobda ham `Accept-Ranges` qoladi.
        response.Headers.AcceptRanges.Should().Contain("bytes");
    }

    /// <summary>
    /// `bytes=-N` (OXIRGI N bayt) — MP4 pleyerlari `moov` atomini aynan
    /// shunday, fayl oxiridan o'qiydi.
    /// </summary>
    [Fact]
    public async Task Download_WithSuffixRange_ReturnsTail()
    {
        var (assetId, payload) = await NewVideoAssetAsync(2048);

        using var admin = await AdminClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, AssetUri(assetId));
        request.Headers.TryAddWithoutValidation("Range", "bytes=-128");

        var response = await admin.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var body = await response.Content.ReadAsByteArrayAsync();

        body.Should().Equal(payload[^128..]);
    }

    /// <summary>Oraliq fayl chegarasidan tashqarida -> 416 + `Content-Range: bytes * /hajm`.</summary>
    [Fact]
    public async Task Download_WithUnsatisfiableRange_Returns416()
    {
        var (assetId, payload) = await NewVideoAssetAsync(1024);

        using var admin = await AdminClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, AssetUri(assetId));
        request.Headers.TryAddWithoutValidation("Range", "bytes=99999-");

        var response = await admin.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);

        // Klient haqiqiy hajmni bilib, to'g'ri oraliq bilan qayta so'rasin.
        response.Content.Headers.TryGetValues("Content-Range", out var values)
            .Should().BeTrue("416 da `Content-Range` MAJBURIY");

        values!.Should().ContainSingle().Which.Should().Be(
            "bytes */" + payload.Length.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Tushunarsiz `Range` — TO'LIQ javob (200), XATO EMAS. HTTP standarti
    /// aynan shunday talab qiladi.
    /// </summary>
    [Fact]
    public async Task Download_WithUnsupportedRangeUnit_ReturnsFullContent()
    {
        var (assetId, payload) = await NewVideoAssetAsync(512);

        using var admin = await AdminClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, AssetUri(assetId));
        request.Headers.TryAddWithoutValidation("Range", "items=0-10");

        var response = await admin.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(payload);
    }

    // ================================================================= objectKey

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴 `objectKey` HECH BIR JAVOBDA YO'Q (`DAVOM_ETTIRISH.md` 16-tuzoq)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Uch joyda tekshiriladi: yuklash javobi, kurs daraxti va fayl nomi
    /// (`Content-Disposition`). Kalit bazada BOR — ya'ni test "kalit
    /// yo'q"ni emas, "kalit TASHQARIGA CHIQMAYDI"ni isbotlaydi.
    /// </summary>
    [Fact]
    public async Task ObjectKey_IsNeverExposed()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Kalit darsi");

        var asset = await UploadAsync(lessonId, "dars.mp4", "video/mp4", RandomVideo(1024));

        // Bazadagi HAQIQIY kalit — solishtirish uchun.
        var storedKey = await factory.WithDbAsync(db => db.LessonAssets
            .AsNoTracking()
            .Where(a => a.Id == asset.Id)
            .Select(a => a.ObjectKey)
            .SingleAsync());

        storedKey.Should().StartWith(factory.KeyPrefix + "/",
            "kalit haqiqatan bazada va prefiks bilan yozilgan");

        using var admin = await AdminClientAsync();

        // 1) Kurs daraxti javobi.
        var tree = await admin.GetStringAsync(CourseUri(courseId));

        tree.Should().NotContain("objectKey", "maydon nomi ham chiqmasin");
        tree.Should().NotContain(storedKey, "kalit QIYMATI ham chiqmasin");

        // 2) Fayl nomi kalitni oshkor qilmasin.
        var file = await admin.GetAsync(AssetUri(asset.Id));

        var disposition = file.Content.Headers.ContentDisposition;

        disposition.Should().NotBeNull();
        (disposition!.FileNameStar ?? disposition.FileName).Should().NotContain("/",
            "kalit yo'li nomga chiqmasin");
    }

    // ================================================================= MIME (magic bytes)

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴 `.mp4` DEB NOMLANGAN PDF -> 400 (magic bytes ISHLAYAPTI)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Fayl nomi ham, `Content-Type` sarlavhasi ham VIDEO deb aytadi —
    /// ikkalasini istalgan klient yozib yuboradi. Mazmun esa PDF.
    /// Kengaytmaga ishonilsa, bu yerga ixtiyoriy fayl (masalan bajariladigan
    /// kod) yuklanardi va u keyinchalik `video/mp4` deb tarqatilardi.
    /// </summary>
    [Fact]
    public async Task Upload_PdfNamedAsMp4_ReturnsBadRequest()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Yolg'on dars");

        using var admin = await AdminClientAsync();

        var payload = WithMagic(PdfMagic, 2048);

        var response = await admin.PostAsync(
            AssetsUri(lessonId), Multipart("dars.mp4", "video/mp4", payload));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "tur MAZMUNDAN aniqlanadi, fayl nomidan emas");

        var body = await ProblemText.ReadAsync(response);

        body.Should().Contain("qo'llab-quvvatlanmaydi");

        // Baza ham toza qolishi kerak (yarim yozuv yo'q).
        var count = await factory.WithDbAsync(db => db.LessonAssets
            .AsNoTracking()
            .CountAsync(a => a.LessonId == lessonId));

        count.Should().Be(0, "rad etilgan fayl uchun yozuv yaratilmasin");
    }

    /// <summary>
    /// ODATIY darsga RASM yuklab bo'lmaydi: rasm haqiqiy PNG, lekin dars
    /// turi video kutadi -> 400.
    /// </summary>
    [Fact]
    public async Task Upload_ImageToNormalLesson_ReturnsBadRequest()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Odatiy dars");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            AssetsUri(lessonId), Multipart("rasm.png", "image/png", WithMagic(PngMagic, 1024)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemText.ReadAsync(response)).Should().Contain("Video");
    }

    /// <summary>IMTIHON darsiga rasm yuklanadi va turi `Image` bo'ladi.</summary>
    [Fact]
    public async Task Upload_ImageToExamLesson_Succeeds()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Imtihon", kind: "Exam");

        var asset = await UploadAsync(
            lessonId, "varaq.png", "image/png", WithMagic(PngMagic, 2048));

        asset.Kind.Should().Be("Image");
        asset.ContentType.Should().Be("image/png");
    }

    /// <summary>IMTIHON darsiga VIDEO yuklab bo'lmaydi -> 400.</summary>
    [Fact]
    public async Task Upload_VideoToExamLesson_ReturnsBadRequest()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Imtihon-2", kind: "Exam");

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            AssetsUri(lessonId), Multipart("dars.mp4", "video/mp4", RandomVideo(1024)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemText.ReadAsync(response)).Should().Contain("Rasm");
    }

    // ================================================================= HAJM CHEGARASI

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴 CHEGARA SOZLAMADAN KELADI -> 413
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 1 GB fayl bilan test qilish mumkin emas, shuning uchun AKSINCHA
    /// yo'l: chegara paneldan 1 MB ga TUSHIRILADI va 2 MB fayl yuboriladi.
    /// Bu chegaraning HAQIQATAN sozlamadan o'qilayotganini isbotlaydi
    /// (kodda qotib qolmaganini).
    ///
    /// ★ NIMA UCHUN 413, 400 EMAS: AYNI shartni Kestrel ham tekshiradi va
    /// u 413 beradi. Ikki xil kod bo'lsa frontend ikki tarmoqli mantiq
    /// yozardi (batafsil: `PayloadTooLargeException` izohi).
    /// </summary>
    [Fact]
    public async Task Upload_LargerThanConfiguredLimit_Returns413()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Chegara darsi");

        await SetSettingAsync("lesson.video_max_mb", "1");

        try
        {
            using var admin = await AdminClientAsync();

            var response = await admin.PostAsync(
                AssetsUri(lessonId),
                Multipart("katta.mp4", "video/mp4", RandomVideo(2 * 1024 * 1024)));

            response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

            var body = await ProblemText.ReadAsync(response);

            // Xabar CHEGARANI va uni kim o'zgartira olishini aytadi.
            body.Should().Contain("1 MB");
            body.Should().Contain("lesson.video_max_mb");
        }
        finally
        {
            // Boshqa testlarga ta'sir qilmasin (sozlama BAZADA saqlanadi).
            await ResetSettingAsync("lesson.video_max_mb");
        }
    }

    /// <summary>Chegara ostidagi fayl — o'tadi (chegara "hammani to'smaydi").</summary>
    [Fact]
    public async Task Upload_WithinConfiguredLimit_Succeeds()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Chegara-2");

        await SetSettingAsync("lesson.video_max_mb", "1");

        try
        {
            var asset = await UploadAsync(
                lessonId, "kichik.mp4", "video/mp4", RandomVideo(64 * 1024));

            asset.SizeBytes.Should().Be(64 * 1024);
        }
        finally
        {
            await ResetSettingAsync("lesson.video_max_mb");
        }
    }

    // ================================================================= TARTIB

    /// <summary>Yangi fayllar oxiriga ZICH raqam bilan qo'shiladi (0,1,2).</summary>
    [Fact]
    public async Task Upload_AssignsDenseAscendingPositions()
    {
        var lessonId = await NewLessonAsync("Tartib darsi");

        var first = await UploadAsync(lessonId, "a.mp4", "video/mp4", RandomVideo(512));
        var second = await UploadAsync(lessonId, "b.mp4", "video/mp4", RandomVideo(512));
        var third = await UploadAsync(lessonId, "c.mp4", "video/mp4", RandomVideo(512));

        first.Position.Should().Be(0);
        second.Position.Should().Be(1);
        third.Position.Should().Be(2);
    }

    /// <summary>`reorder` yuborilgan ketma-ketlikni 0,1,2... qilib yozadi.</summary>
    [Fact]
    public async Task Reorder_RenumbersDenselyAndPersists()
    {
        var lessonId = await NewLessonAsync("Reorder darsi");

        var a = (await UploadAsync(lessonId, "a.mp4", "video/mp4", RandomVideo(512))).Id;
        var b = (await UploadAsync(lessonId, "b.mp4", "video/mp4", RandomVideo(512))).Id;
        var c = (await UploadAsync(lessonId, "c.mp4", "video/mp4", RandomVideo(512))).Id;

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            ReorderUri(lessonId), new { orderedIds = new[] { c, a, b } });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var positions = await response.Content.ReadFromJsonAsync<List<PositionRow>>();

        positions!.ConvertAll(p => p.Id).Should().Equal(c, a, b);
        positions.ConvertAll(p => p.Position).Should().Equal(0, 1, 2);

        // Baza ham o'zgargan bo'lishi kerak (javob "yolg'on" bo'lmasin).
        var stored = await factory.WithDbAsync(db => db.LessonAssets
            .AsNoTracking()
            .Where(x => x.LessonId == lessonId)
            .OrderBy(x => x.Position)
            .Select(x => x.Id)
            .ToListAsync());

        stored.Should().Equal(c, a, b);
    }

    /// <summary>
    /// 🔴 TO'LIQ BO'LMAGAN ro'yxat -> 400 va HECH NARSA yozilmaydi
    /// (`DAVOM_ETTIRISH.md` 6-bo'lim, 7-tuzoq).
    /// </summary>
    [Fact]
    public async Task Reorder_WithIncompleteList_ReturnsBadRequestAndChangesNothing()
    {
        var lessonId = await NewLessonAsync("Chala reorder");

        var a = (await UploadAsync(lessonId, "a.mp4", "video/mp4", RandomVideo(512))).Id;
        var b = (await UploadAsync(lessonId, "b.mp4", "video/mp4", RandomVideo(512))).Id;
        var c = (await UploadAsync(lessonId, "c.mp4", "video/mp4", RandomVideo(512))).Id;

        using var admin = await AdminClientAsync();

        // Uchtadan faqat IKKITASI yuborildi.
        var response = await admin.PutAsJsonAsync(
            ReorderUri(lessonId), new { orderedIds = new[] { c, a } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await ProblemText.ReadAsync(response);

        body.Should().Contain("to'liq emas");
        body.Should().Contain("orderedIds", "xato AYNAN shu maydon ostida ko'rinsin");

        // Tartib O'ZGARMAGAN.
        var stored = await factory.WithDbAsync(db => db.LessonAssets
            .AsNoTracking()
            .Where(x => x.LessonId == lessonId)
            .OrderBy(x => x.Position)
            .Select(x => x.Id)
            .ToListAsync());

        stored.Should().Equal(new[] { a, b, c }, "yarim tartib yozilmasligi kerak");
    }

    /// <summary>Begona Id (boshqa darsning fayli) -> 400.</summary>
    [Fact]
    public async Task Reorder_WithForeignId_ReturnsBadRequest()
    {
        var first = await NewLessonAsync("Reorder A");
        var second = await NewLessonAsync("Reorder B");

        var mine = (await UploadAsync(first, "a.mp4", "video/mp4", RandomVideo(512))).Id;
        var foreign = (await UploadAsync(second, "b.mp4", "video/mp4", RandomVideo(512))).Id;

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            ReorderUri(first), new { orderedIds = new[] { mine, foreign } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================= O'CHIRISH

    /// <summary>
    /// O'chirish: baza yozuvi ham, OMBORDAGI OBYEKT ham ketadi, qolganlar
    /// tartibi ZICH qoladi.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesRowObjectAndClosesPositionGap()
    {
        var lessonId = await NewLessonAsync("O'chirish darsi");

        var a = (await UploadAsync(lessonId, "a.mp4", "video/mp4", RandomVideo(512))).Id;
        var b = (await UploadAsync(lessonId, "b.mp4", "video/mp4", RandomVideo(512))).Id;
        var c = (await UploadAsync(lessonId, "c.mp4", "video/mp4", RandomVideo(512))).Id;

        using var admin = await AdminClientAsync();

        var deleted = await admin.DeleteAsync(AssetUri(b));

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Yozuv yo'q.
        var remaining = await factory.WithDbAsync(db => db.LessonAssets
            .AsNoTracking()
            .Where(x => x.LessonId == lessonId)
            .OrderBy(x => x.Position)
            .Select(x => new { x.Id, x.Position })
            .ToListAsync());

        remaining.ConvertAll(x => x.Id).Should().Equal(a, c);
        remaining.ConvertAll(x => x.Position).Should().Equal(0, 1);

        // Endi o'qib ham bo'lmaydi.
        (await admin.GetAsync(AssetUri(b))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================= DARS TURI

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// 🔴 TURNI ALMASHTIRISH: MOS KELMAYDIGAN MEDIA BO'LSA -> 409
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Jimgina o'chirish YO'Q. Xato xabari NECHTA fayl borligini aytadi va
    /// dars turi O'ZGARMAY qoladi.
    /// </summary>
    [Fact]
    public async Task ChangeKind_WithExistingVideos_ReturnsConflict()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Tur darsi");

        await UploadAsync(lessonId, "a.mp4", "video/mp4", RandomVideo(512));
        await UploadAsync(lessonId, "b.mp4", "video/mp4", RandomVideo(512));

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            LessonUri(courseId, moduleId, lessonId),
            new { name = "Tur darsi", kind = "Exam" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await ProblemText.ReadAsync(response);

        body.Should().Contain("2", "nechta fayl borligi aytilishi kerak");
        body.Should().Contain("o'chiring");

        // Dars turi O'ZGARMAGAN va videolar JOYIDA.
        var lesson = await factory.WithDbAsync(db => db.ModuleLessons
            .AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new { l.Kind, Count = l.Assets.Count })
            .SingleAsync());

        lesson.Kind.Should().Be(Zinnur.Domain.Enums.LessonKind.Normal);
        lesson.Count.Should().Be(2, "media JIMGINA o'chirilmasligi kerak");
    }

    /// <summary>Media yo'q — tur erkin almashadi (200).</summary>
    [Fact]
    public async Task ChangeKind_WithoutAssets_Succeeds()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Bo'sh dars");

        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            LessonUri(courseId, moduleId, lessonId),
            new { name = "Bo'sh dars", kind = "Exam" });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var lesson = await response.Content.ReadFromJsonAsync<LessonRow>();

        lesson!.Kind.Should().Be("Exam");
        lesson.Assets.Should().BeEmpty();
    }

    /// <summary>
    /// Videolar o'chirilgach tur almashadi — ya'ni 409 "abadiy qulf" emas,
    /// FOYDALANUVCHIGA aniq yo'l ko'rsatadigan to'siq.
    /// </summary>
    [Fact]
    public async Task ChangeKind_AfterDeletingAssets_Succeeds()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        var lessonId = await CreateLessonAsync(courseId, moduleId, "Tozalangan dars");

        var asset = await UploadAsync(lessonId, "a.mp4", "video/mp4", RandomVideo(512));

        using var admin = await AdminClientAsync();

        (await admin.DeleteAsync(AssetUri(asset.Id))).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var response = await admin.PutAsJsonAsync(
            LessonUri(courseId, moduleId, lessonId),
            new { name = "Tozalangan dars", kind = "Exam" });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>Noma'lum dars turi -> 400 (jimgina `Normal` ga tushmaydi).</summary>
    [Fact]
    public async Task CreateLesson_WithUnknownKind_ReturnsBadRequest()
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            LessonsUri(courseId, moduleId), new { name = "Noma'lum", kind = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<long> NewLessonAsync(string name)
    {
        var (courseId, moduleId) = await NewCourseWithModuleAsync();
        return await CreateLessonAsync(courseId, moduleId, name);
    }

    /// <summary>
    /// Yangi video asset VA uning baytlari (Range testlari uchun).
    ///
    /// Baytlar ham qaytariladi: `Range` javobini tekshirish uchun AYNAN
    /// yuklangan baytlar bilan solishtirish kerak.
    /// </summary>
    private async Task<(long AssetId, byte[] Payload)> NewVideoAssetAsync(int size)
    {
        var payload = RandomVideo(size);
        var lessonId = await NewLessonAsync("Range darsi");

        var asset = await UploadAsync(lessonId, "dars.mp4", "video/mp4", payload);

        return (asset.Id, payload);
    }

    private async Task<AssetRow> UploadAsync(
        long lessonId, string fileName, string contentType, byte[] payload, string? title = null)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            AssetsUri(lessonId), Multipart(fileName, contentType, payload, title));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<AssetRow>())!;
    }

    private async Task<(long CourseId, long ModuleId)> NewCourseWithModuleAsync()
    {
        using var admin = await AdminClientAsync();

        var course = await admin.PostAsJsonAsync(
            new Uri("/api/v1/courses", UriKind.Relative),
            new { name = "Media kursi " + Guid.NewGuid().ToString("N")[..6] });

        course.StatusCode.Should().Be(HttpStatusCode.Created,
            await course.Content.ReadAsStringAsync());

        var courseId = (await course.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var module = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/courses/{courseId}/modules", UriKind.Relative),
            new { name = "Modul" });

        module.StatusCode.Should().Be(HttpStatusCode.Created,
            await module.Content.ReadAsStringAsync());

        var moduleId = (await module.Content.ReadFromJsonAsync<IdRow>())!.Id;

        return (courseId, moduleId);
    }

    private async Task<long> CreateLessonAsync(
        long courseId, long moduleId, string name, string kind = "Normal")
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            LessonsUri(courseId, moduleId), new { name, kind });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private async Task SetSettingAsync(string key, string value)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            new Uri("/api/v1/settings/" + key, UriKind.Relative), new { value });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    private async Task ResetSettingAsync(string key)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            new Uri($"/api/v1/settings/{key}/reset", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>Sehrli baytlari MP4, qolgani TASODIFIY — solishtiruv ma'noli bo'lsin.</summary>
    private static byte[] RandomVideo(int totalBytes) => WithMagic(Mp4Magic, totalBytes);

    private static byte[] WithMagic(byte[] magic, int totalBytes)
    {
        var bytes = RandomNumberGenerator.GetBytes(totalBytes);
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent Multipart(
        string fileName, string contentType, byte[] payload, string? title = null)
    {
        var content = new MultipartFormDataContent();

        var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        // Maydon nomi AYNAN `file` — endpoint shartnomasi.
        content.Add(part, "file", fileName);

        if (title is not null)
            content.Add(new StringContent(title), "title");

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

    private static Uri LessonsUri(long courseId, long moduleId) =>
        Relative($"/api/v1/courses/{courseId}/modules/{moduleId}/lessons");

    private static Uri LessonUri(long courseId, long moduleId, long lessonId) =>
        Relative($"/api/v1/courses/{courseId}/modules/{moduleId}/lessons/{lessonId}");

    private static Uri Relative(FormattableString path) =>
        new(FormattableString.Invariant(path), UriKind.Relative);

    private sealed record IdRow(long Id);

    private sealed record PositionRow(long Id, int Position);

    private sealed record AssetRow(
        long Id,
        long LessonId,
        string Kind,
        int Position,
        string? Title,
        string ContentType,
        long SizeBytes,
        int? DurationSec,
        int? Width,
        int? Height);

    private sealed record LessonRow(
        long Id, string Name, string Kind, List<AssetRow> Assets);
}
