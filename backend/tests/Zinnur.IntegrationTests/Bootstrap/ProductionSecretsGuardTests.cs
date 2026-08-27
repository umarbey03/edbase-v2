using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Zinnur.WebApi.Services;

namespace Zinnur.IntegrationTests.Bootstrap;

/// <summary>
/// ========================================================================
/// 🔴 PROD'DA NAMUNA SIRI BILAN KO'TARILMASLIK — DARVOZA TESTI
/// ========================================================================
///
/// ★ NIMA UCHUN BU TESTLAR BOR (2026-08-22 auditi)
///
/// Ilova ilgari sirning MAVJUDLIGINI va UZUNLIGINI tekshirardi, QIYMATINI
/// esa yo'q. `.env.example` dagi namuna `Jwt:Secret` ataylab 32 belgidan
/// uzun qilib yozilgan, ya'ni u ikkala tekshiruvdan ham bemalol o'tardi.
///
/// 🔴 SHU TURDAGI NOSOZLIK HECH QACHON O'ZINI KO'RSATMAYDI: tizim
///    "ishlab turgan" bo'ladi, log toza bo'ladi, birorta so'rov
///    yiqilmaydi. Bilinadigan yagona payt — kimdir ommaviy sir bilan
///    administrator tokenini yasagandan KEYIN. Aynan shuning uchun
///    darvoza kod bilan qulflanadi va bu yerda test bilan qo'riqlanadi.
///
/// ⚠️ BAZA KERAK EMAS: <see cref="ProductionSecretsGuard"/> — sof
/// konfiguratsiya mantig'i. Test integratsiya loyihasida turibdi faqat
/// shuning uchunki `Zinnur.WebApi` ga havola AYNAN shu yerda bor (unit
/// loyihasi ataylab Domain + Application bilan cheklangan) —
/// <c>BootstrapAdminTests</c> bilan AYNI mulohaza.
/// </summary>
public sealed class ProductionSecretsGuardTests
{
    // ================================================================= muhit

    /// <summary>
    /// ★★ BIRINCHI VA ENG MUHIM SHART: darvoza FAQAT <c>Production</c> da
    /// ishlaydi.
    ///
    /// Aks holda u butun integratsiya to'plamini (600+ test) va har bir
    /// dev mashinasini qulflab qo'yardi — ular ataylab dev qiymatlari
    /// bilan ishlaydi.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Validate_OutsideProduction_IgnoresSampleSecrets(string environmentName)
    {
        var act = () => Run(environmentName, Sample());

        act.Should().NotThrow(
            "dev va staging ataylab namuna qiymatlari bilan ishlaydi");
    }

    // ================================================================= sirlar

    /// <summary>Namuna sirlari bilan prod'da ilova UMUMAN ko'tarilmaydi.</summary>
    [Fact]
    public void Validate_InProduction_WithSampleSecrets_Throws()
    {
        var act = () => Run("Production", Sample());

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Har bir kalit ALOHIDA qo'riqlanadi.
    ///
    /// ★ NIMA UCHUN `Theory`: bitta "hammasi yomon" testi yashil bo'lishi
    ///   uchun BITTA tekshiruv yetardi — qolgan uchtasi jimgina o'chib
    ///   qolsa ham test buni sezmasdi.
    /// </summary>
    [Theory]
    [InlineData("Jwt:Secret", "dev_only_zinnur_jwt_secret_change_me_min_32_bytes_long")]
    [InlineData("LiveKit:ApiSecret", "dev_only_livekit_secret_change_me_min_32_chars_ok")]
    [InlineData("Storage:AccessKey", "zinnur_dev_only_minio")]
    [InlineData("Storage:SecretKey", "please_change_me")]
    public void Validate_InProduction_FlagsEachSampleKey(string key, string sampleValue)
    {
        var settings = Good();
        settings[key] = sampleValue;

        var act = () => Run("Production", settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*" + key + "*", "xabar QAYSI kalit ekanini aytishi kerak");
    }

    /// <summary>
    /// 🔴 `devkey` — LiveKit hujjatlaridagi OMMAVIY kalit nomi.
    ///
    /// Unda `dev_only` / `change_me` markeri YO'Q (u bizning standartimiz
    /// emas), shuning uchun alohida tekshiriladi. Marker qoidasiga
    /// tayanib qolsak, aynan shu — eng xavflisi — o'tib ketardi.
    /// </summary>
    [Fact]
    public void Validate_InProduction_WithDevKey_Throws()
    {
        var settings = Good();
        settings["LiveKit:ApiKey"] = "devkey";

        var act = () => Run("Production", settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*devkey*");
    }

    // ================================================================= CORS

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("https://0.0.0.0")]
    public void Validate_InProduction_WithLocalCorsOrigin_Throws(string origin)
    {
        var settings = Good();
        settings["Cors:AllowedOrigins:0"] = origin;

        var act = () => Run("Production", settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cors*");
    }

    [Fact]
    public void Validate_InProduction_WithRealCorsOrigin_Passes()
    {
        var settings = Good();
        settings["Cors:AllowedOrigins:0"] = "https://app.zinnur.uz";

        var act = () => Run("Production", settings);

        act.Should().NotThrow();
    }

    // ================================================================= ombor

    /// <summary>Prod'da fayllar R2 da — MinIO manzili qolib ketmasin.</summary>
    [Fact]
    public void Validate_InProduction_WithMinioStorageUrl_Throws()
    {
        var settings = Good();
        settings["Storage:ServiceUrl"] = "http://minio:9000";

        var act = () => Run("Production", settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Storage:ServiceUrl*");
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 BRAUZER KO'RADIGAN OMBOR MANZILI MAHALLIY BO'LMASIN
    /// ════════════════════════════════════════════════════════════════
    ///
    /// ★ NIMA UCHUN BU TEST BOR (2026-08-24). `Storage:PublicUrl`
    ///   darvozada UMUMAN tekshirilmasdi va `docker-compose.prod.yml`
    ///   uni qayta yozmasdi — natijada bazaviy `.env` dagi
    ///   `http://localhost:9010` prod'ga o'z holicha o'tardi.
    ///
    /// 🔴 BU NOSOZLIKNI HECH QANDAY LOG KO'RSATMASDI: imzolangan havola
    ///    BRAUZERGA beriladi, brauzer `localhost` ga boradi va u yerda
    ///    hech narsa yo'q. So'rov bizning serverimizga umuman kelmaydi.
    ///    Ya'ni yagona "monitoring" — o'quvchining shikoyati.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:9010")]
    [InlineData("http://127.0.0.1:9010")]
    public void Validate_InProduction_WithLocalStoragePublicUrl_Throws(string publicUrl)
    {
        var settings = Good();
        settings["Storage:PublicUrl"] = publicUrl;

        var act = () => Run("Production", settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Storage:PublicUrl*", "xabar QAYSI kalit ekanini aytishi kerak");
    }

    /// <summary>
    /// `ServiceUrl` ham mahalliy manzilga tekshiriladi.
    ///
    /// ★ NIMA UCHUN ALOHIDA: yuqoridagi "minio" markeri faqat DOCKER
    ///   xizmat nomini tutadi. `http://localhost:9000` — AYNI dev
    ///   ombori, lekin host porti orqali — undan bemalol o'tib ketardi.
    /// </summary>
    [Fact]
    public void Validate_InProduction_WithLocalStorageServiceUrl_Throws()
    {
        var settings = Good();
        settings["Storage:ServiceUrl"] = "http://localhost:9000";

        var act = () => Run("Production", settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Storage:ServiceUrl*");
    }

    /// <summary>
    /// BO'SH `PublicUrl` — xato EMAS, aksincha TAVSIYA ETILGAN holat.
    ///
    /// Uning ma'nosi "ko'rish havolasi ham `ServiceUrl` dan qurilsin"
    /// (`StorageOptions.EffectivePublicUrl`). R2 da ombor manzili ikkala
    /// tomondan bir xil ko'ringani uchun bu HAR DOIM to'g'ri javob —
    /// imzo va havola bitta xostga tegishli bo'ladi.
    ///
    /// ⚠️ Darvoza buni xato deb hisoblasa, prod uchun eng ishonchli
    ///    sozlama IMKONSIZ bo'lardi.
    /// </summary>
    [Fact]
    public void Validate_InProduction_WithEmptyStoragePublicUrl_Passes()
    {
        var settings = Good();
        settings["Storage:PublicUrl"] = string.Empty;

        var act = () => Run("Production", settings);

        act.Should().NotThrow();
    }

    /// <summary>
    /// BO'SH ombor — xato EMAS.
    ///
    /// `docker-compose.prod.yml` `R2_*` sozlanmaganda to'rttasini ham
    /// bo'sh qoldiradi va bu QONUNIY holat: ombor "sozlanmagan" bo'ladi
    /// va fayl yuklash ochiq 503 beradi. Darvoza buni xato deb hisoblasa,
    /// omborsiz (lekin ishlaydigan) deploy imkonsiz bo'lardi.
    /// </summary>
    [Fact]
    public void Validate_InProduction_WithEmptyStorage_Passes()
    {
        var settings = Good();
        settings["Storage:ServiceUrl"] = string.Empty;
        settings["Storage:AccessKey"] = string.Empty;
        settings["Storage:SecretKey"] = string.Empty;

        var act = () => Run("Production", settings);

        act.Should().NotThrow();
    }

    // ================================================================= to'liqlik

    /// <summary>
    /// ★ HAMMA MUAMMO BIRDANIGA aytiladi — operator sirlarni bittalab
    ///   tuzatib, har safar qayta deploy qilishga majbur bo'lmasin.
    /// </summary>
    [Fact]
    public void Validate_InProduction_ReportsEveryProblemAtOnce()
    {
        var settings = Good();
        settings["Jwt:Secret"] = "dev_only_secret_change_me_0123456789012345";
        settings["LiveKit:ApiKey"] = "devkey";
        settings["Cors:AllowedOrigins:0"] = "http://localhost:5173";

        var act = () => Run("Production", settings);

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().Contain("Jwt:Secret");
        message.Should().Contain("devkey");
        message.Should().Contain("Cors");
    }

    /// <summary>To'g'ri sozlangan prod — hech qanday xato yo'q.</summary>
    [Fact]
    public void Validate_InProduction_WithRealSecrets_Passes()
    {
        var act = () => Run("Production", Good());

        act.Should().NotThrow();
    }

    // ================================================================= yordamchi

    private static void Run(string environmentName, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ProductionSecretsGuard.Validate(
            configuration, new StubEnvironment(environmentName));
    }

    /// <summary>Prod uchun YAROQLI sozlama (har test undan boshlanadi).</summary>
    private static Dictionary<string, string?> Good() => new(StringComparer.Ordinal)
    {
        ["Jwt:Secret"] = "T0kIq2vJ8sVn4pQwXr7mZaLcYd1eFgHb",
        ["LiveKit:ApiKey"] = "zinnura1b2c3",
        ["LiveKit:ApiSecret"] = "9PmR3xKt6WqZ2nLv5cJyBd8sHfGa1eUo",
        ["Storage:ServiceUrl"] = "https://hisob.r2.cloudflarestorage.com",
        ["Storage:AccessKey"] = "4f2c9a1b7e3d",
        ["Storage:SecretKey"] = "Qb7Xm2Ld9Rt4Wp1Zc6Ns3Vy8Kf5Hj0G",
        ["Cors:AllowedOrigins:0"] = "https://app.zinnur.uz",
    };

    /// <summary><c>.env.example</c> dan AYNAN ko'chirilgan holat.</summary>
    private static Dictionary<string, string?> Sample() => new(StringComparer.Ordinal)
    {
        ["Jwt:Secret"] = "dev_only_zinnur_jwt_secret_change_me_min_32_bytes_long",
        ["LiveKit:ApiKey"] = "devkey",
        ["LiveKit:ApiSecret"] = "dev_only_livekit_secret_change_me_min_32_chars_ok",
        ["Storage:ServiceUrl"] = "http://minio:9000",
        ["Storage:AccessKey"] = "zinnur_dev_minio",
        ["Storage:SecretKey"] = "zinnur_dev_minio_secret",
        ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
    };

    /// <summary>
    /// Faqat NOMI muhim bo'lgan muhit.
    ///
    /// `IsProduction()` — kengaytma metod va u aynan
    /// <see cref="IHostEnvironment.EnvironmentName"/> ga qaraydi, ya'ni
    /// soxta obyekt uchun boshqa a'zolar ahamiyatsiz.
    /// </summary>
    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Zinnur.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider
        {
            get => new Microsoft.Extensions.FileProviders.NullFileProvider();
            set => throw new NotSupportedException();
        }
    }
}
