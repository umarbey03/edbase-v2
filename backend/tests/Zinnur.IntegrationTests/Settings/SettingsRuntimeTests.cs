using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Assignments.Services;
using Zinnur.Infrastructure.Options;
using Zinnur.IntegrationTests.Api;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Settings;

/// <summary>
/// ========================================================================
/// 🔴 UCHDAN-UCHGACHA: PANELDAN O'ZGARTIRILGAN SOZLAMA TIZIM XATTI-HARAKATINI
///    HAQIQATAN O'ZGARTIRADIMI?
/// ========================================================================
///
/// ★ BUTUN ISHNING MA'NOSI SHU TESTDA. Sozlamalar paneli ilgari ham bor
/// edi, lekin qiymatlar <c>IOptions&lt;T&gt;</c> orqali ishga tushishda
/// singleton xizmatlarga QOTIB QOLARDI: panel "saqlandi" derdi-yu, tizim
/// eski qiymat bilan ishlayverardi. Bu eng yomon turdagi xato —
/// JIMGINA YOLG'ON, va uni faqat shunday test tutadi.
///
/// ★ NIMA UCHUN "sozlama saqlandi" ni tekshirish YETARLI EMAS: aynan
/// shunday testlar ilgari ham YASHIL edi (<c>SettingsEndpointsTests</c>) —
/// qiymat bazaga yozilardi, tizim esa uni o'qimasdi. Shuning uchun bu yerda
/// tekshiriladigan narsa QATOR emas, XATTI-HARAKAT: HAQIQIY MinIO'ga
/// HAQIQIY fayl yuklash muvaffaqiyati.
///
/// HAQIQIY ombor bilan ishlaydi (<see cref="StorageBackedApiFactory"/>).
/// MinIO ishlamayotgan bo'lsa sinf YIQILADI (o'tkazib yuborilmaydi):
/// "sinalmagan, lekin yashil" natija eng qimmat yolg'on.
///
/// ⚠️ HAR TEST O'ZIDAN KEYIN HOLATNI TIKLAYDI: sinf ichidagi testlar BITTA
/// ombor sozlamasini baham ko'radi va biri buzuq qiymat qoldirsa,
/// keyingisi sababsiz yiqilardi.
/// </summary>
public sealed class SettingsRuntimeTests(StorageBackedApiFactory factory)
    : IClassFixture<StorageBackedApiFactory>
{
    private const string AccessKeyKey = "storage.access_key";
    private const string SecretKeyKey = "storage.secret_key";
    private const string BucketKey = "storage.bucket";
    private const string ServiceUrlKey = "storage.service_url";

    /// <summary>PNG sehrli baytlari — fayl turi MAZMUNDAN aniqlanadi.</summary>
    private static readonly byte[] PngMagic =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // ================================================================= 1) jonli isbot

    /// <summary>
    /// ★★★ ASOSIY TEST — UCHDAN-UCHGACHA.
    ///
    ///   1) o'quvchi fayl yuklaydi        -> 200 (ombor ishlaydi)
    ///   2) admin PANELDAN kirish kalitini buzadi
    ///   3) o'quvchi fayl yuklaydi        -> 503 (ombor RAD ETADI)
    ///   4) admin PANELDAN standartga qaytaradi
    ///   5) o'quvchi fayl yuklaydi        -> 200 (yana ishlaydi)
    ///
    /// ★ HECH QAYERDA KUTISH (`Task.Delay`) YO'Q — va bu ataylab: saqlagan
    /// instansiyada kesh <c>SaveChanges</c> dan KEYIN, HTTP javob
    /// qaytishidan OLDIN yangilanadi. Agar kimdir bu tartibni buzsa yoki
    /// keshni faqat TTL ga qoldirsa, test darhol qizaradi.
    ///
    /// ★ 2-qadam AYNAN SIR (`access_key`) ustida: sir javobda qaytmaydi va
    /// auditga yozilmaydi, ya'ni uning HAQIQATAN kuchga kirganini boshqa
    /// hech qanday yo'l bilan isbotlab bo'lmaydi — faqat xatti-harakat orqali.
    /// </summary>
    [Fact]
    public async Task PanelChange_ChangesRealBehaviour_UploadBreaksThenRecovers()
    {
        var world = await WorldBuilder.CreateAsync(factory, "runtime");

        using var student = await ClientAsync(world.Student);
        using var admin = await AdminAsync();

        // ── 1) BOSHLANG'ICH HOLAT: ombor ishlaydi ────────────────────────
        var before = await UploadAsync(student, world.GroupId);

        before.Should().Be(HttpStatusCode.OK, "sinov boshida ombor ishlashi kerak");

        try
        {
            // ── 2) PANEL ORQALI KIRISH KALITINI BUZAMIZ ──────────────────
            var saved = await admin.PutAsJsonAsync(
                KeyUri(AccessKeyKey), new { value = "panel-orqali-buzilgan-kalit" });

            saved.StatusCode.Should().Be(HttpStatusCode.OK, await Body(saved));

            // Javobda SIR qaytmasligi — shu yerda ham qotiriladi.
            (await Body(saved)).Should().NotContain("panel-orqali-buzilgan-kalit");

            // ── 3) XATTI-HARAKAT O'ZGARDIMI? ─────────────────────────────
            //
            // 🔴 BUTUN ISHNING YAGONA HAQIQIY DALILI. Ombor endi imzoni
            // tanimaydi (403 `InvalidAccessKeyId`) va API buni 503 ga
            // o'giradi. Eski (`IOptions`) yo'lida bu qator 200 qaytarardi.
            var broken = await UploadAsync(student, world.GroupId);

            broken.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                "paneldan buzilgan kalit TIZIMGA DARHOL yetib borishi kerak "
                + "— aks holda sozlama o'zgarishi jimgina e'tiborsiz qolardi");
        }
        finally
        {
            // ── 4) TIKLASH: qator o'chiriladi, qiymat muhitga qaytadi ────
            var reset = await admin.PostAsJsonAsync(ResetUri(AccessKeyKey), new { });

            reset.StatusCode.Should().Be(HttpStatusCode.OK, await Body(reset));
        }

        // ── 5) YANA ISHLAYDI ─────────────────────────────────────────────
        var after = await UploadAsync(student, world.GroupId);

        after.Should().Be(
            HttpStatusCode.OK,
            "standartga qaytarish ham DARHOL kuchga kirishi kerak");
    }

    // ================================================================= 2) kesh bekor qilinishi

    /// <summary>
    /// ★ KESH BEKOR QILINISHI: o'zgartirgandan keyin ESKI qiymat qaytmasligi.
    ///
    /// Bu yerda tizimning ICHIGA qaraymiz — <c>IRuntimeOptions&lt;StorageOptions&gt;</c>
    /// aynan fayl yuklash yo'li o'qiydigan obyektni beradi. Yuqoridagi test
    /// XATTI-HARAKATNI isbotlaydi, bu esa MEXANIZMNI: kesh o'zgarishni
    /// KUTMASDAN ko'radi.
    ///
    /// ⚠️ `Task.Delay` YO'Q. 10 sekundlik orqa tayanch bor, lekin u AYNAN
    /// shu yo'l uchun emas: saqlagan instansiyada kechikish NOL bo'lishi
    /// shart. Kutish qo'shilsa test orqa tayanchni sinardi va asosiy
    /// kafolatning yo'qolganini payqamasdi.
    /// </summary>
    [Fact]
    public async Task Update_IsVisibleToRuntimeImmediately_WithoutWaiting()
    {
        using var admin = await AdminAsync();

        var runtime = factory.Services.GetRequiredService<IRuntimeOptions<StorageOptions>>();
        var original = runtime.Current.Bucket;

        original.Should().NotBeNullOrWhiteSpace();

        var target = original + "-kesh-sinovi";

        try
        {
            var saved = await admin.PutAsJsonAsync(KeyUri(BucketKey), new { value = target });

            saved.StatusCode.Should().Be(HttpStatusCode.OK, await Body(saved));

            runtime.Current.Bucket.Should().Be(
                target, "kesh saqlash bilan BIR PAYTDA yangilanishi shart");
        }
        finally
        {
            var reset = await admin.PostAsJsonAsync(ResetUri(BucketKey), new { });
            reset.StatusCode.Should().Be(HttpStatusCode.OK, await Body(reset));
        }

        runtime.Current.Bucket.Should().Be(
            original, "standartga qaytarish ham keshni DARHOL yangilashi kerak");
    }

    /// <summary>
    /// Sir ham AYNI yo'ldan o'tadi: javobda qaytmaydi, lekin tizim ichida
    /// DARHOL kuchga kiradi. Bu ikki talab bir-biriga zid ko'rinadi va
    /// shuning uchun aynan birga tekshiriladi.
    /// </summary>
    [Fact]
    public async Task SecretUpdate_TakesEffectImmediately_ButIsNeverReturned()
    {
        const string NewSecret = "panel-orqali-yozilgan-ombor-siri-9f2a";

        using var admin = await AdminAsync();

        var runtime = factory.Services.GetRequiredService<IRuntimeOptions<StorageOptions>>();
        var original = runtime.Current.SecretKey;

        try
        {
            var saved = await admin.PutAsJsonAsync(KeyUri(SecretKeyKey), new { value = NewSecret });

            saved.StatusCode.Should().Be(HttpStatusCode.OK, await Body(saved));

            // 🔴 SIR JAVOBGA CHIQMAYDI — na saqlash javobida, na ro'yxatda.
            (await Body(saved)).Should().NotContain(NewSecret);

            var page = await admin.GetAsync(SettingsUri);
            (await Body(page)).Should().NotContain(NewSecret);

            // ...LEKIN tizim ichida ALLAQACHON yangi qiymat bilan ishlaydi.
            runtime.Current.SecretKey.Should().Be(NewSecret);
        }
        finally
        {
            var reset = await admin.PostAsJsonAsync(ResetUri(SecretKeyKey), new { });
            reset.StatusCode.Should().Be(HttpStatusCode.OK, await Body(reset));
        }

        runtime.Current.SecretKey.Should().Be(original);
    }

    // ================================================================= 3) sir va audit

    /// <summary>
    /// 🔴 SIR AUDITGA TUSHMAYDI. O'zgarish FAKTI yoziladi (kim, qachon,
    /// qaysi kalit), QIYMAT esa yo'q.
    ///
    /// ★ NIMA UCHUN MUHIM: maskalash faqat HTTP javobini himoya qiladi.
    /// Audit jadvalini o'qiy oladigan xodim (yoki `pg_dump` olgan odam)
    /// sirni o'sha yerdan olardi — ya'ni maskalash ma'nosiz bo'lardi.
    /// </summary>
    [Fact]
    public async Task SecretChange_IsAudited_ButValueIsNotStoredInAudit()
    {
        const string NewSecret = "audit-sinovi-uchun-ombor-siri-4b7c";

        using var admin = await AdminAsync();

        try
        {
            var saved = await admin.PutAsJsonAsync(KeyUri(SecretKeyKey), new { value = NewSecret });
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await Body(saved));

            var audit = await factory.WithDbAsync(db => db.PaymentAudits
                .AsNoTracking()
                .Where(a => a.Entity == "settings" && a.Field == SecretKeyKey)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync());

            audit.Should().NotBeNull("o'zgarish FAKTI yozilishi kerak");
            audit!.ActorId.Should().NotBeNull("kim o'zgartirgani ko'rinishi kerak");

            audit.OldValue.Should().BeNull();
            audit.NewValue.Should().BeNull();

            // Izoh bo'lishi kerak — "nega qiymat yo'q?" degan savol
            // auditni o'qiyotgan odamda tug'ilmasin.
            audit.Note.Should().NotBeNullOrWhiteSpace();
            audit.Note.Should().NotContain(NewSecret);
        }
        finally
        {
            await admin.PostAsJsonAsync(ResetUri(SecretKeyKey), new { });
        }
    }

    // ================================================================= 4) to'plam butunligi

    /// <summary>
    /// ★★ «TO'LIQ YOKI BO'SH» HIMOYASI ENDI YOZISH PAYTIDA.
    ///
    /// Ilgari bu qoidani <c>ValidateOnStart</c> qo'riqlardi: yarim
    /// to'ldirilgan `Storage:*` da ilova UMUMAN ko'tarilmasdi. Qiymatlar
    /// bazadan kela boshlagach o'sha tekshiruv ma'nosini yo'qotdi (ishga
    /// tushish paytida baza hali o'qilmagan), shuning uchun qoida yozish
    /// yo'liga ko'chirildi.
    ///
    /// Bu test aynan KO'CHISHNI qotiradi: ishlab turgan omborni bitta bo'sh
    /// saqlash bilan o'chirib qo'yish MUMKIN EMAS.
    /// </summary>
    [Fact]
    public async Task Update_ThatWouldHalfConfigureWorkingSet_IsRejected()
    {
        using var admin = await AdminAsync();

        var runtime = factory.Services.GetRequiredService<IRuntimeOptions<StorageOptions>>();

        runtime.Current.IsConfigured.Should().BeTrue("sinov to'liq sozlangan holatdan boshlanadi");

        var response = await admin.PutAsJsonAsync(KeyUri(ServiceUrlKey), new { value = string.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await Body(response);

        // Frontend `problem.errors[<kalit>]` ni o'qiydi va sababni maydon
        // yonida ko'rsatadi — shakl AYNAN shunday bo'lishi shart.
        body.Should().Contain("errors");
        body.Should().Contain(ServiceUrlKey);

        // Va eng muhimi: ombor HAMON ishlaydi.
        runtime.Current.IsConfigured.Should().BeTrue();
        runtime.Current.ServiceUrl.Should().NotBeNullOrWhiteSpace();

        var rows = await factory.WithDbAsync(db =>
            db.AppSettings.CountAsync(s => s.Key == ServiceUrlKey));

        rows.Should().Be(0, "rad etilgan o'zgarish bazaga umuman yozilmasin");
    }

    // ================================================================= 5) faqat o'qish

    /// <summary>
    /// Tizimni QULFLAY yoki huquqni KENGAYTIRA oladigan kalitlar HAMON rad
    /// etiladi. Sozlamalar ish jarayonida o'zgaradigan bo'lgani bu qoidani
    /// SUSAYTIRMAYDI — aksincha, endi u yagona to'siq.
    /// </summary>
    [Theory]
    [InlineData("security.jwt_secret")]
    [InlineData("security.postgres_connection")]
    [InlineData("security.redis_connection")]
    [InlineData("general.time_zone")]
    public async Task ReadOnlyKeys_AreStillRejected(string key)
    {
        using var admin = await AdminAsync();

        var update = await admin.PutAsJsonAsync(
            KeyUri(key), new { value = "paneldan_yozilgan_qiymat_kamida_32_belgi_012345" });

        var reset = await admin.PostAsJsonAsync(ResetUri(key), new { });

        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rows = await factory.WithDbAsync(db => db.AppSettings.CountAsync(s => s.Key == key));

        rows.Should().Be(0, "faqat o'qish kaliti bazaga UMUMAN tushmasligi kerak");
    }

    // ================================================================= 6) sim ulanishi

    /// <summary>
    /// Fayl ombori porti HAQIQATAN ish jarayonidagi sozlamalardan oziqlanadi.
    ///
    /// ★ NIMA UCHUN ALOHIDA: agar kimdir <c>R2SubmissionStorage</c> ni
    /// yana <c>IOptions&lt;StorageOptions&gt;</c> ga qaytarsa, yuqoridagi
    /// testlar ham yiqilardi — lekin sabab "ombor javob bermadi" bo'lib
    /// ko'rinardi. Bu test sababni TO'G'RIDAN-TO'G'RI ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task SubmissionStorage_ReadsRuntimeSettings_NotFrozenOptions()
    {
        using var admin = await AdminAsync();

        var storage = factory.Services.GetRequiredService<ISubmissionStorage>();

        storage.IsConfigured.Should().BeTrue();

        try
        {
            // Manzilni o'zgartirish to'plamni buzmaydi (qiymat bo'sh emas),
            // lekin ombor endi BOSHQA joyda bo'ladi.
            var saved = await admin.PutAsJsonAsync(
                KeyUri(ServiceUrlKey), new { value = "http://127.0.0.1:59999" });

            saved.StatusCode.Should().Be(HttpStatusCode.OK, await Body(saved));

            var runtime = factory.Services.GetRequiredService<IRuntimeOptions<StorageOptions>>();

            runtime.Current.ServiceUrl.Should().Be("http://127.0.0.1:59999");
            storage.IsConfigured.Should().BeTrue("to'plam hamon to'liq");
        }
        finally
        {
            var reset = await admin.PostAsJsonAsync(ResetUri(ServiceUrlKey), new { });
            reset.StatusCode.Should().Be(HttpStatusCode.OK, await Body(reset));
        }
    }

    // ================================================================= yordamchilar

    private static readonly Uri SettingsUri = new("/api/v1/settings", UriKind.Relative);

    private static Uri KeyUri(string key) =>
        new($"/api/v1/settings/{key}", UriKind.Relative);

    private static Uri ResetUri(string key) =>
        new($"/api/v1/settings/{key}/reset", UriKind.Relative);

    private async Task<HttpClient> AdminAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var tokens = await factory.LoginAsync(user.Email, user.Password);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<long> CreateAssignmentAsync(long groupId)
    {
        using var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/assignments", UriKind.Relative),
            new
            {
                title = "Runtime vazifasi " + Guid.NewGuid().ToString("N")[..6],
                groupId,
                maxScore = 5m,
                allowedFormats = "Text, Image",
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await Body(response));

        var created = await response.Content.ReadFromJsonAsync<CreatedAssignment>();
        return created!.Id;
    }

    /// <summary>
    /// Fayl yuklash urinishi — javob KODINI qaytaradi.
    ///
    /// ⚠️ HAR URINISH UCHUN YANGI VAZIFA YARATILADI: bitta vazifaga ikki
    /// marta javob yuborib bo'lmaydi (409 "allaqachon yuborilgan"). Bu
    /// qoida sozlamalarga umuman aloqasi yo'q, lekin uni hisobga olmasak
    /// test 503 o'rniga 409 ko'rib, BUTUNLAY BOSHQA narsani "isbotlardi".
    /// </summary>
    private async Task<HttpStatusCode> UploadAsync(HttpClient student, long groupId)
    {
        var assignmentId = await CreateAssignmentAsync(groupId);

        var bytes = RandomNumberGenerator.GetBytes(2048);
        PngMagic.CopyTo(bytes, 0);

        using var content = new MultipartFormDataContent();
        using var part = new ByteArrayContent(bytes);

        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(part, "files", "ish.png");

        var uri = new Uri(
            string.Create(CultureInfo.InvariantCulture, $"/api/v1/assignments/{assignmentId}/submit"),
            UriKind.Relative);

        using var response = await student.PostAsync(uri, content);

        return response.StatusCode;
    }

    private static async Task<string> Body(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    private sealed record CreatedAssignment(long Id);
}
