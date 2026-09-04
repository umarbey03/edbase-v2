using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Recordings.Services;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// XOM FAYLLAR YO'LI — HAQIQIY MinIO bilan (SPEC-RECORDING-V2, M4)
/// ════════════════════════════════════════════════════════════════════════
///
/// Tungi yig'uvchi omborga uch xil murojaat qiladi va uchalasi ham AVVAL
/// bu portda umuman yo'q edi: xom faylni O'QISH, tayyor mp4 ni QO'YISH,
/// xomlarni O'CHIRISH. Bu yerda ular BITTA to'liq aylanma sifatida
/// tekshiriladi — <c>PutAsync</c> -> <c>HeadAsync</c> -> <c>OpenReadAsync</c>
/// -> <c>DeleteAsync</c>.
///
/// ★ NIMA UCHUN SOXTA OMBOR EMAS (<c>RecordingStorageTests</c> dagi AYNI
/// mulohaza): tekshiriladigan narsa PROTOKOL — SigV4 SARLAVHA imzosi,
/// tananing SHA-256 xeshi, <c>Content-Length</c> (busiz S3 chunked uzatishni
/// rad etadi), path-style manzil va portli host. Soxta ombor bularning
/// hammasini "to'g'ri" deb qabul qilardi, prod'da esa nosozlik faqat
/// sababsiz <c>403 SignatureDoesNotMatch</c> ko'rinishida chiqardi.
///
/// 🔴 BU YO'L KECHASI, HECH KIM QARAMAGANDA ishlaydi. Yiqilgan yuklash
/// ertalab "yozuv tayyor emas" ko'rinishida bilinadi va sababi faqat logda
/// qoladi — shuning uchun aylanma AYNAN uchdan-uchgacha o'lchanadi.
/// </summary>
public sealed class RecordingRawStorageTests(RecordingFactory factory)
    : IClassFixture<RecordingFactory>
{
    /// <summary>
    /// Xom fayl hajmi — ATAYLAB BIR NECHA MEGABAYT.
    ///
    /// ★ NIMA UCHUN kichik "salom" satri yetarli emas: bir necha kilobayt
    /// bitta TCP paketga sig'adi va <c>Content-Length</c> / chunked uzatish
    /// farqini umuman ko'rsatmasdi. Haqiqiy xom fayl esa yuzlab megabayt,
    /// tayyor mp4 — 1-2 GB.
    /// </summary>
    private const int PayloadBytes = 5 * 1024 * 1024;

    /// <summary>
    /// 🔴 ASOSIY TEST: TO'LIQ AYLANMA. Qo'ydik -> bor va hajmi to'g'ri ->
    /// o'qidik va BAYTLARI AYNI -> o'chirdik -> endi yo'q.
    ///
    /// Har bosqich alohida testga bo'linsa, ular bir-birining obyektiga
    /// tayanardi (yoki har biri o'zinikini yasab, 5 MB ni to'rt marta
    /// yuklardi). Aylanma esa AYNAN yig'uvchining ketma-ketligi.
    /// </summary>
    [Fact]
    public async Task RawObject_SurvivesTheFullPutHeadReadDeleteRoundTrip()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        storage.IsConfigured.Should().BeTrue("test fixture MinIO'ni sozlaydi");

        var payload = RandomNumberGenerator.GetBytes(PayloadBytes);
        var key = NewRawKey(storage, "TR_" + Guid.NewGuid().ToString("N")[..12], "webm");

        // ── QO'YISH ────────────────────────────────────────────────────
        using (var content = new MemoryStream(payload, writable: false))
        {
            await storage.PutAsync(key, content, payload.LongLength, "video/webm");
        }

        // ── BOR-YO'QLIGI VA HAJMI ──────────────────────────────────────
        var info = await storage.HeadAsync(key);

        info.Should().NotBeNull("qo'yilgan obyekt darhol ko'rinishi kerak");
        info!.SizeBytes.Should().Be(payload.LongLength);

        // ── O'QISH ─────────────────────────────────────────────────────
        await using (var stored = await storage.OpenReadAsync(key))
        {
            stored.Should().NotBeNull();
            stored!.SizeBytes.Should().Be(payload.LongLength);

            using var buffer = new MemoryStream(PayloadBytes);

            await stored.Content.CopyToAsync(buffer);

            // ⚠️ BAYTMA-BAYT SOLISHTIRISH ATAYLAB XESH ORQALI: 5 MB
            //    massivni element-element solishtirish tasdiq kutubxonasida
            //    daqiqalab davom etardi va test "osilgan" deb qabul
            //    qilinardi.
            buffer.Length.Should().Be(payload.LongLength);

            Convert.ToHexString(SHA256.HashData(buffer.ToArray()))
                .Should().Be(Convert.ToHexString(SHA256.HashData(payload)),
                    "yuklangan va qaytarilgan baytlar AYNI bo'lishi shart");
        }

        // ── O'CHIRISH ──────────────────────────────────────────────────
        await storage.DeleteAsync(key);

        (await storage.HeadAsync(key)).Should().BeNull("o'chirilgan obyekt qolmasligi kerak");
        (await storage.OpenReadAsync(key)).Should().BeNull();
    }

    /// <summary>
    /// O'chirish IDEMPOTENT: yo'q obyektni o'chirish XATO EMAS.
    ///
    /// ★ NIMA UCHUN MUHIM: tozalash yig'ishdan KEYIN, alohida qadamda
    /// bajariladi va u muvaffaqiyatsiz bo'lsa keyingi kecha QAYTA
    /// urinadi (SPEC 4.5, 9-qadam). Ikkinchi urinishda fayllarning bir
    /// qismi allaqachon o'chgan bo'ladi — agar bu istisno bersa,
    /// tozalash HECH QACHON oxirigacha yetmasdi va xom fayllar uchun pul
    /// to'lanaverardi.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_IsQuietWhenTheObjectIsAlreadyGone()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var key = NewRawKey(storage, "TR_" + Guid.NewGuid().ToString("N")[..12], "webm");

        var act = async () => await storage.DeleteAsync(key);

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// 🔴 OQIM BOSHIDAN O'QILADI. Chaqiruvchi oqimni allaqachon oxirigacha
    /// o'qigan bo'lsa ham fayl TO'LIQ yuklanadi.
    ///
    /// Busiz SigV4 xeshi FAQAT qolgan (bo'sh) qismni qamrab olardi, tana
    /// esa butunlay yuborilardi — ombor javobi
    /// <c>403 SignatureDoesNotMatch</c> bo'lardi va logda uning sababini
    /// ko'rsatadigan hech nima qolmasdi.
    /// </summary>
    [Fact]
    public async Task PutAsync_RewindsAStreamThatWasAlreadyRead()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var payload = RandomNumberGenerator.GetBytes(64 * 1024);
        var key = NewRawKey(storage, "TR_" + Guid.NewGuid().ToString("N")[..12], "ogg");

        using (var content = new MemoryStream(payload, writable: false))
        {
            // Oqimni ATAYLAB oxirigacha o'qib qo'yamiz.
            content.Position = content.Length;

            await storage.PutAsync(key, content, payload.LongLength, "audio/ogg");
        }

        try
        {
            var info = await storage.HeadAsync(key);

            info.Should().NotBeNull();
            info!.SizeBytes.Should().Be(payload.LongLength);
        }
        finally
        {
            await storage.DeleteAsync(key);
        }
    }

    /// <summary>
    /// E'lon qilingan uzunlik oqim hajmiga mos kelmasa — SHU YERDA
    /// to'xtaydi.
    ///
    /// ★ NIMA UCHUN "ombor o'zi aytadi" YETARLI EMAS: mos kelmagan
    /// <c>Content-Length</c> da nosozlik ombor tomonda, tushunarsiz
    /// shaklda chiqadi (yoki so'rov umuman uzilib qoladi) va tungi
    /// yig'uvchi logida faqat "ombor xatosi" qolardi.
    /// </summary>
    [Fact]
    public async Task PutAsync_RefusesALengthThatDoesNotMatchTheStream()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var key = NewRawKey(storage, "TR_" + Guid.NewGuid().ToString("N")[..12], "webm");

        using var content = new MemoryStream(new byte[128], writable: false);

        var act = async () => await storage.PutAsync(key, content, length: 999, "video/webm");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Yo'q obyekt — <c>null</c> (istisno EMAS): yig'uvchi shu segmentni tashlab ketadi.</summary>
    [Fact]
    public async Task OpenReadAsync_ReturnsNullForAMissingObject()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var stored = await storage.OpenReadAsync(
            NewRawKey(storage, "TR_" + Guid.NewGuid().ToString("N")[..12], "webm"));

        stored.Should().BeNull();
    }

    // ================================================================= kalit sxemasi

    /// <summary>
    /// 🔴 KALIT SXEMASI QOTIRILADI — SPEC 2.8.
    ///
    /// Bu shakl UCH joyda bir vaqtda to'g'ri bo'lishi shart:
    ///   1) Egress'ga <c>filepath</c> sifatida beriladi (fayl AYNAN shu
    ///      yerga yoziladi);
    ///   2) bazaga <c>RecordingTrack.ObjectKey</c> ga tushadi va tungi
    ///      yig'uvchi faylni o'sha kalit bilan qidiradi;
    ///   3) tozalash qadami xuddi shu kalit bilan o'chiradi.
    ///
    /// Ya'ni sxemaning jimgina o'zgarishi "fayl bor, lekin uni topib
    /// bo'lmaydi" degan holatga olib kelardi. Shuning uchun bu yerda
    /// "boshlanadi/tugaydi" emas, AYNAN TENGLIK tekshiriladi.
    /// </summary>
    [Fact]
    public void BuildRawObjectKey_PinsTheLayoutFromSpec()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        storage.BuildRawObjectKey(1234, 77, "TR_VCaBc12", "webm")
            .Should().Be("raw/1234/77/TR_VCaBc12.webm");

        // Xona ovozi: sentinel sid va OLDINDAN MA'LUM kengaytma — biz
        // so'rovda `EncodedFileType: OGG` tanlaymiz, ya'ni `.ogg` taxmin
        // emas, FAKT.
        storage.BuildRawObjectKey(1234, 77, "ROOM", "ogg")
            .Should().Be("raw/1234/77/ROOM.ogg");
    }

    /// <summary>
    /// Kengaytma nuqta bilan ham, katta harf bilan ham kelishi mumkin
    /// (u MIME jadvalidan olinadi va jadval prod dalili bo'yicha
    /// tuzatiladi) — kalit baribir BITTA ko'rinishda chiqadi.
    /// </summary>
    [Fact]
    public void BuildRawObjectKey_NormalisesTheExtension()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        storage.BuildRawObjectKey(9, 3, "TR_x", ".MP4")
            .Should().Be("raw/9/3/TR_x.mp4");
    }

    /// <summary>
    /// Xom fayllar YAKUNIY yozuvlar bilan BIR PREFIKSDA turmaydi va kalit
    /// TASODIFIY qismsiz, ya'ni TAKRORLANADIGAN.
    ///
    /// Takrorlanish shart: kalit Egress'ga oldindan beriladi va webhook
    /// yo'qolsa faylni ombordan aynan shu nom bilan topish kerak
    /// (<c>BuildObjectKey</c> izohidagi AYNI mulohaza).
    /// </summary>
    [Fact]
    public void BuildRawObjectKey_HasItsOwnRootAndIsDeterministic()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var key = storage.BuildRawObjectKey(1234, 77, "TR_a", "webm");

        key.Should().StartWith("raw/");
        key.Should().NotStartWith("recordings/", "xom fayllar admin ro'yxatiga tushmasligi kerak");
        key.Should().Be(storage.BuildRawObjectKey(1234, 77, "TR_a", "webm"));
    }

    /// <summary>
    /// 🔴 NOL YOZUV ID'si — HAQIQIY TUZOQ: identifikator faqat
    /// <c>SaveChangesAsync</c> dan keyin beriladi. Kalit undan oldin
    /// yasalsa HAR darsning HAR treki <c>raw/{dars}/0/…</c> ga tushardi
    /// va bir-birining ustidan yozilardi. Nosozlik esa faqat kechasi,
    /// "video yarim joyda uzilib qoldi" ko'rinishida chiqardi.
    /// </summary>
    [Fact]
    public void BuildRawObjectKey_RefusesAnUnsavedRecordingId()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var act = () => storage.BuildRawObjectKey(1234, recordingId: 0, "TR_a", "webm");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// <c>/</c> — yo'l ajratgichi. Sid ichida uchrasa kalit jimgina
    /// qo'shimcha "papka" olardi va prefiks bo'yicha ishlaydigan tozalash
    /// o'sha faylni HECH QACHON topa olmasdi.
    /// </summary>
    [Fact]
    public void BuildRawObjectKey_RefusesASeparatorInsideTheTrackSid()
    {
        var storage = factory.Services.GetRequiredService<IRecordingStorage>();

        var act = () => storage.BuildRawObjectKey(1234, 77, "TR_a/b", "webm");

        act.Should().Throw<ArgumentException>();
    }

    // ================================================================= yordamchilar

    /// <summary>
    /// Har test uchun TASODIFIY dars/yozuv ID'si.
    ///
    /// ⚠️ Xom kalitlar <c>Storage:KeyPrefix</c> dan TASHQARIDA
    /// (<c>raw/…</c>), ya'ni yugurishlar bir xil bucket'ni bo'lishadi.
    /// Qotirilgan ID'lar bilan parallel yurgan ikki test bir-birining
    /// obyektini o'chirib qo'yardi.
    /// </summary>
    private static string NewRawKey(IRecordingStorage storage, string trackSid, string extension) =>
        storage.BuildRawObjectKey(
            Random.Shared.NextInt64(1_000_000, 9_999_999),
            Random.Shared.NextInt64(1_000_000, 9_999_999),
            trackSid,
            extension);
}
