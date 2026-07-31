using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// OMBOR YO'LI — HAQIQIY MinIO bilan
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN SOXTA OMBOR EMAS: bu yerda tekshiriladigan narsa aynan
/// PROTOKOL — SigV4 QUERY-STRING imzosi, path-style manzil, portli host
/// va kalitni URL uchun kodlash. Soxta ombor bularning hammasini "to'g'ri"
/// deb qabul qilardi. Imzo xatosi esa prod'da faqat sababsiz
/// <c>403 SignatureDoesNotMatch</c> ko'rinishida chiqadi.
///
/// 🔴 ENG MUHIM TASDIQ: presigned havola BRAUZER kabi — ya'ni HECH QANDAY
/// sarlavhasiz — ochilishi kerak. Aynan shu sabab yozuv uchun proxy emas,
/// presigned tanlangan (<c>IRecordingStorage</c> izohi): <c>&lt;video
/// src&gt;</c> `Authorization` sarlavhasini YUBORMAYDI.
///
/// MinIO R2 bilan AYNI protokolni gapiradi, ya'ni bu testlar prod yo'lini
/// ham qoplaydi (kod bitta, faqat `Storage:*` qiymatlari boshqa).
/// </summary>
public sealed class RecordingStorageTests(RecordingFactory factory)
    : IClassFixture<RecordingFactory>
{
    /// <summary>
    /// 🔴 Imzolangan havola SARLAVHASIZ ochiladi va AYNI baytlarni beradi.
    /// </summary>
    [Fact]
    public async Task CreateViewLink_ProducesAUrlThatOpensWithoutAnyHeader()
    {
        var payload = Encoding.UTF8.GetBytes("zin-nur dars yozuvi sinovi " + Guid.NewGuid());
        var key = await UploadAsync(payload);

        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        storage.IsConfigured.Should().BeTrue("test fixture MinIO'ni sozlaydi");

        var link = storage.CreateViewLink(key, TimeSpan.FromMinutes(5));

        link.Query.Should().Contain("X-Amz-Signature=");
        link.Query.Should().Contain("X-Amz-Expires=");

        // ⚠️ ATAYLAB YALANG'OCH KLIENT: hech qanday `Authorization` yo'q —
        //    brauzer ham aynan shunday so'raydi.
        using var browser = new HttpClient();

        var response = await browser.GetAsync(link);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var downloaded = await response.Content.ReadAsByteArrayAsync();

        downloaded.Should().Equal(payload);
    }

    /// <summary>
    /// Muddat CHEKLANADI: juda uzun TTL so'ralsa ham havola 12 soatdan
    /// oshmaydi — uzun muddat havolani ulashishga yaroqli qilardi (eski
    /// tizimning 4 soatlik havolasi aynan shunday ishlatilgan).
    /// </summary>
    [Fact]
    public async Task CreateViewLink_ClampsAnAbsurdlyLongTtl()
    {
        var key = await UploadAsync(Encoding.UTF8.GetBytes("ttl"));

        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var link = storage.CreateViewLink(key, TimeSpan.FromDays(30));

        link.Query.Should().Contain("X-Amz-Expires=43200", "12 soat = 43200 sekund");
    }

    /// <summary>
    /// ★ WATCHDOG UCHUN HAYOTIY MUHIM: fayl HAQIQATAN bormi. Webhook
    /// yo'qolganda haqiqat manbai LiveKit hodisasi emas, OMBORNING O'ZI.
    /// </summary>
    [Fact]
    public async Task HeadAsync_FindsAnExistingObjectAndReportsItsSize()
    {
        var payload = Encoding.UTF8.GetBytes(new string('z', 1234));
        var key = await UploadAsync(payload);

        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var info = await storage.HeadAsync(key);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(payload.Length);
    }

    /// <summary>Yo'q obyekt — <c>null</c> (istisno EMAS).</summary>
    [Fact]
    public async Task HeadAsync_ReturnsNullForAMissingObject()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var info = await storage.HeadAsync($"recordings/yoq/{Guid.NewGuid():N}.mp4");

        info.Should().BeNull();
    }

    /// <summary>
    /// Kalit shakli: <c>recordings/YYYY-MM/{dars}/…mp4</c>. Dars ID'si
    /// kalitda — muammo tekshirilganda fayl qaysi darsga tegishli ekani
    /// omborning O'ZIDAN ko'rinadi.
    /// </summary>
    [Fact]
    public void BuildObjectKey_UsesItsOwnFolderWithMonthAndSession()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var key = storage.BuildObjectKey(sessionId: 4242);

        key.Should().StartWith("recordings/");
        key.Should().Contain("/4242/");
        key.Should().EndWith(".mp4");

        // Ikkita kalit HECH QACHON bir xil bo'lmasin (taxmin qilib
        // bo'lmasligi va ustidan yozib yubormaslik uchun).
        storage.BuildObjectKey(4242).Should().NotBe(key);
    }

    /// <summary>
    /// Obyektni omborga qo'yadi va kalitini qaytaradi.
    ///
    /// ⚠️ Yuklash uchun vazifa fayllari porti ishlatiladi
    /// (<see cref="ISubmissionStorage"/>): yozuv porti ATAYLAB faqat
    /// O'QIYDI — Egress faylni bizsiz, to'g'ridan-to'g'ri yozadi va
    /// "yuklash" metodi u yerda BO'LISHI ham mumkin emas.
    /// </summary>
    private async Task<string> UploadAsync(byte[] payload)
    {
        var uploads = factory.Services.GetRequiredService<ISubmissionStorage>();

        return await uploads.SaveAsync(new SubmissionUpload(
            StudentId: 1,
            Kind: AttachmentKind.Document,
            Extension: "bin",
            ContentType: "application/octet-stream",
            Content: payload));
    }
}
