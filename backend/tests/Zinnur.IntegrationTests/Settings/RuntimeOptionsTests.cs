using Microsoft.Extensions.Options;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.IntegrationTests.Settings;

/// <summary>
/// ========================================================================
/// <c>IOptions&lt;T&gt;</c> NING ISH JARAYONIDAGI O'RINBOSARI — SOF TESTLAR
/// ========================================================================
///
/// Bazasiz va HTTP'siz: bu yerda faqat KESIMDAN OBYEKT YIG'ISH qoidasi
/// tekshiriladi. Uchdan-uchgacha oqim (panel -&gt; baza -&gt; xatti-harakat)
/// <c>SettingsRuntimeTests</c> da.
///
/// ★ NIMA UCHUN INTEGRATSIYA LOYIHASIDA, unit loyihasida emas:
/// <c>RuntimeOptions&lt;T&gt;</c> — Infrastructure turi, unit loyihasi esa
/// ataylab faqat Domain va Application'ga bog'langan.
/// </summary>
public class RuntimeOptionsTests
{
    /// <summary>Muhitdan (appsettings/env) kelgan BOSHLANG'ICH qiymatlar.</summary>
    private static readonly StorageOptions Seed = new()
    {
        ServiceUrl = "http://muhit:9000",
        Bucket = "muhit-bucket",
        AccessKey = "muhit-kalit",
        SecretKey = "muhit-sir",
        Region = "us-east-1",
        KeyPrefix = "muhit-prefiks",
        TimeoutSeconds = 42,
    };

    // ================================================================= sovuq start

    /// <summary>
    /// 🔴 SOVUQ START: baza hali BIR MARTA ham o'qilmagan. Bunday paytda
    /// muhitdagi qiymat AYNAN to'g'ri javob — u registrda ham BOSHLANG'ICH
    /// manba sifatida turadi.
    ///
    /// "Sozlanmagan" deb xulosa qilish konteyner ko'tarilgan birinchi
    /// soniyalarda fayl yuklashni SABABSIZ 503 qilardi va bu faqat deploy
    /// paytida chiqadigan, takrorlash qiyin nosozlik bo'lardi.
    /// </summary>
    [Fact]
    public void ColdStart_FallsBackToEnvironmentSeed()
    {
        var runtime = new FakeRuntimeSettings(SettingsSnapshot.Empty);
        var options = new RuntimeStorageOptions(runtime, Options.Create(Seed));

        var current = options.Current;

        current.Should().BeSameAs(Seed);
        current.IsConfigured.Should().BeTrue();
        current.Bucket.Should().Be("muhit-bucket");
    }

    // ================================================================= kesimdan yig'ish

    /// <summary>
    /// Kesim yuklangach BAZA USTUN bo'ladi — butun ishning ma'nosi shu.
    /// </summary>
    [Fact]
    public void LoadedSnapshot_OverridesEnvironment()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.StorageServiceUrl] = "http://baza:9000",
            [SettingsRegistry.Keys.StorageBucket] = "baza-bucket",
            [SettingsRegistry.Keys.StorageAccessKey] = "baza-kalit",
            [SettingsRegistry.Keys.StorageSecretKey] = "baza-sir",
            [SettingsRegistry.Keys.StorageRegion] = "auto",
        }));

        var current = new RuntimeStorageOptions(runtime, Options.Create(Seed)).Current;

        current.ServiceUrl.Should().Be("http://baza:9000");
        current.Bucket.Should().Be("baza-bucket");
        current.AccessKey.Should().Be("baza-kalit");
        current.SecretKey.Should().Be("baza-sir");
        current.Region.Should().Be("auto");

        // Registrda UMUMAN yo'q maydonlar baribir muhitdan keladi.
        current.KeyPrefix.Should().Be("muhit-prefiks");
        current.TimeoutSeconds.Should().Be(42);
    }

    /// <summary>
    /// 🔴 PANELDAN TOZALANGAN QIYMAT MUHITGA QAYTMAYDI.
    ///
    /// Kesimdagi qiymat ALLAQACHON ustunlik qoidasidan o'tgan
    /// (baza -&gt; muhit -&gt; standart). Bu yerda yana muhitga qarash
    /// operator ataylab o'chirgan omborni JIMGINA qayta yoqib yuborardi.
    /// </summary>
    [Fact]
    public void ClearedValue_DoesNotFallBackToEnvironment()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.StorageServiceUrl] = null,
            [SettingsRegistry.Keys.StorageBucket] = null,
            [SettingsRegistry.Keys.StorageAccessKey] = null,
            [SettingsRegistry.Keys.StorageSecretKey] = null,
        }));

        var current = new RuntimeStorageOptions(runtime, Options.Create(Seed)).Current;

        current.IsConfigured.Should().BeFalse();
        current.ServiceUrl.Should().BeEmpty();
        current.Bucket.Should().BeEmpty();

        // Region — YAGONA istisno: u imzo zanjiriga kiradi va bo'sh bo'lsa
        // HAR so'rov 403 bilan qaytardi, sababi esa hech qayerda ko'rinmasdi.
        current.Region.Should().Be("us-east-1");
    }

    /// <summary>
    /// Yarim to'ldirilgan to'plam INERT: <c>IsConfigured</c> BARCHA a'zoni
    /// talab qiladi, ya'ni fayl yuklash "sozlanmagan" holatdagidek 503
    /// qaytaradi. Aynan shuning uchun `ValidateOnStart` dagi tekshiruvni
    /// olib tashlash xavfsiz bo'ldi.
    /// </summary>
    [Fact]
    public void PartialSnapshot_IsTreatedAsNotConfigured()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.StorageServiceUrl] = "http://baza:9000",
            [SettingsRegistry.Keys.StorageBucket] = "baza-bucket",
        }));

        new RuntimeStorageOptions(runtime, Options.Create(Seed))
            .Current.IsConfigured.Should().BeFalse();
    }

    // ================================================================= kesh

    /// <summary>
    /// Kesim RAQAMI o'zgarmasa AYNI obyekt qaytadi: <c>IsConfigured</c>
    /// fayl yuklashning HAR bosqichida so'raladi va har safar yangi
    /// obyekt yasash keraksiz allokatsiya bo'lardi.
    /// </summary>
    [Fact]
    public void SameVersion_ReturnsSameInstance()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(5, new()
        {
            [SettingsRegistry.Keys.StorageBucket] = "birinchi",
        }));

        var options = new RuntimeStorageOptions(runtime, Options.Create(Seed));

        options.Current.Should().BeSameAs(options.Current);
    }

    /// <summary>
    /// 🔴 ENG MUHIM KESH TESTI: raqam o'zgarsa YANGI qiymat ko'rinadi.
    /// Busiz kesh aynan biz tuzatayotgan muammoni qaytarardi — panel
    /// "saqlandi" derdi-yu, tizim eskisi bilan ishlayverardi.
    /// </summary>
    [Fact]
    public void NewVersion_IsVisibleImmediately()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.StorageBucket] = "eski",
        }));

        var options = new RuntimeStorageOptions(runtime, Options.Create(Seed));

        options.Current.Bucket.Should().Be("eski");

        runtime.Current = Snapshot(2, new()
        {
            [SettingsRegistry.Keys.StorageBucket] = "yangi",
        });

        options.Current.Bucket.Should().Be("yangi");
    }

    // ================================================================= telegram / livekit

    /// <summary>
    /// 🔴 XAVFSIZLIK: Bot API manzili KESIMDAN OLINMAYDI. Token so'rov
    /// MANZILINING ichida ketadi (`/bot&lt;token&gt;/sendMessage`) — manzil
    /// bazadan boshqarilsa, panelga kirgan odam uni o'z serveriga
    /// yo'naltirib, BIRINCHI xabar bilan birga TOKENNI qo'lga kiritardi.
    /// </summary>
    [Fact]
    public void Telegram_ApiBaseUrl_AlwaysComesFromEnvironment()
    {
        var seed = new TelegramOptions
        {
            BotToken = "111:muhit",
            WebhookSecret = "muhit-siri",
            ApiBaseUrl = "https://api.telegram.org",
            TimeoutSeconds = 9,
            InitDataMaxAgeHours = 3,
        };

        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.TelegramBotToken] = "222:baza",
            [SettingsRegistry.Keys.TelegramWebhookSecret] = "baza-siri",
            [SettingsRegistry.Keys.TelegramMiniAppUrl] = "https://app.zinnur.uz",
            [SettingsRegistry.Keys.TelegramBotUsername] = "zinnur_bot",
        }));

        var current = new RuntimeTelegramOptions(runtime, Options.Create(seed)).Current;

        current.BotToken.Should().Be("222:baza");
        current.WebhookSecret.Should().Be("baza-siri");
        current.MiniAppUrl.Should().Be("https://app.zinnur.uz");
        current.BotUsername.Should().Be("zinnur_bot");

        current.ApiBaseUrl.Should().Be("https://api.telegram.org");
        current.TimeoutSeconds.Should().Be(9);
        current.InitDataMaxAgeHours.Should().Be(3);
    }

    /// <summary>
    /// ★ 2026-08-14: ICHKI manzil ham bazadan o'qiladi.
    ///
    /// Ilgari u muhitga qotirilgan edi, chunki `LiveKitHealthCheck`
    /// `IConfiguration` dan to'g'ridan-to'g'ri o'qib, probe bir manzilga,
    /// token esa boshqasiga qarab qolardi. Endi sog'liq tekshiruvi ham
    /// AYNI shu obyektni o'qiydi, ya'ni ajralish MUMKIN EMAS.
    ///
    /// BRAUZER manzili (`PublicUrl`) esa muhitda qoladi — u sertifikat va
    /// DNS bilan juftlashgan.
    /// </summary>
    [Fact]
    public void LiveKit_UrlComesFromDatabase_PublicUrlStaysInEnvironment()
    {
        var seed = new LiveKitOptions
        {
            Url = "http://livekit:7880",
            PublicUrl = "wss://livekit.zinnur.uz",
            ApiKey = "muhit-kalit",
            ApiSecret = "muhit-siri-kamida-32-belgi-0123456789",
        };

        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.LiveKitUrl] = "http://livekit-yangi:7880",
            [SettingsRegistry.Keys.LiveKitApiKey] = "baza-kalit",
            [SettingsRegistry.Keys.LiveKitApiSecret] = "baza-siri-kamida-32-belgi-0123456789",
        }));

        var current = new RuntimeLiveKitOptions(runtime, Options.Create(seed)).Current;

        current.Url.Should().Be("http://livekit-yangi:7880", "baza USTUN");
        current.PublicUrl.Should().Be("wss://livekit.zinnur.uz");

        current.ApiKey.Should().Be("baza-kalit");
        current.ApiSecret.Should().Be("baza-siri-kamida-32-belgi-0123456789");
    }

    /// <summary>
    /// Kesimda manzil YO'Q (yoki bo'sh) bo'lsa MUHITDAGI qiymat ishlaydi.
    ///
    /// 🔴 NIMA UCHUN MUHIM: bo'sh manzil sog'liq tekshiruvini `Unhealthy`
    /// qilib, ilovani sababsiz "nosoz" ko'rsatardi va Egress mijozi
    /// so'rovni umuman yubora olmasdi.
    /// </summary>
    [Fact]
    public void LiveKit_WithoutDatabaseUrl_FallsBackToEnvironment()
    {
        var seed = new LiveKitOptions
        {
            Url = "http://livekit:7880",
            ApiKey = "muhit-kalit",
            ApiSecret = "muhit-siri-kamida-32-belgi-0123456789",
        };

        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.LiveKitUrl] = "   ",
        }));

        var current = new RuntimeLiveKitOptions(runtime, Options.Create(seed)).Current;

        current.Url.Should().Be("http://livekit:7880");
    }

    // ================================================================= LiveKit Cloud (2026-09-21)

    private static readonly LiveKitOptions SelfHostedSeed = new()
    {
        Url = "http://livekit:7880",
        PublicUrl = "wss://livekit.zinnur.uz",
        ApiKey = "muhit-kalit",
        ApiSecret = "muhit-siri-kamida-32-belgi-0123456789",
    };

    /// <summary>
    /// Cloud tanlanganda obyekt BUTUNLAY Cloud'dan: token, egress, webhook
    /// va sog'liq tekshiruvi AYNI obyektni o'qiydi, ya'ni ular ham Cloud'ga
    /// o'tadi. Brauzer — `wss://`, backend — `https://`.
    /// </summary>
    [Theory]
    [InlineData("wss://zinnur-abc.livekit.cloud")]
    [InlineData("https://zinnur-abc.livekit.cloud/")]
    [InlineData("  wss://zinnur-abc.livekit.cloud  ")]
    public void LiveKit_ProviderCloud_UsesCloudSetForEverything(string cloudUrl)
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.LiveKitUrl] = "http://livekit:7880",
            [SettingsRegistry.Keys.LiveKitApiKey] = "baza-kalit",
            [SettingsRegistry.Keys.LiveKitApiSecret] = "baza-siri-kamida-32-belgi-0123456789",
            [SettingsRegistry.Keys.LiveKitProvider] = "Cloud",
            [SettingsRegistry.Keys.LiveKitCloudUrl] = cloudUrl,
            [SettingsRegistry.Keys.LiveKitCloudApiKey] = "APIcloud",
            [SettingsRegistry.Keys.LiveKitCloudApiSecret] = "cloud-siri-kamida-32-belgi-0123456789",
        }));

        var current = new RuntimeLiveKitOptions(runtime, Options.Create(SelfHostedSeed)).Current;

        current.Url.Should().Be("https://zinnur-abc.livekit.cloud");
        current.PublicUrl.Should().Be("wss://zinnur-abc.livekit.cloud");
        current.EffectivePublicUrl.Should().Be("wss://zinnur-abc.livekit.cloud");
        current.ApiKey.Should().Be("APIcloud");
        current.ApiSecret.Should().Be("cloud-siri-kamida-32-belgi-0123456789");
    }

    /// <summary>Cloud to'plami to'ldirilgan, lekin tanlov o'z serverimizda — Cloud ishlatilmaydi.</summary>
    [Fact]
    public void LiveKit_ProviderSelfHosted_IgnoresCloudSet()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.LiveKitApiKey] = "baza-kalit",
            [SettingsRegistry.Keys.LiveKitApiSecret] = "baza-siri-kamida-32-belgi-0123456789",
            [SettingsRegistry.Keys.LiveKitProvider] = "SelfHosted",
            [SettingsRegistry.Keys.LiveKitCloudUrl] = "wss://zinnur-abc.livekit.cloud",
            [SettingsRegistry.Keys.LiveKitCloudApiKey] = "APIcloud",
            [SettingsRegistry.Keys.LiveKitCloudApiSecret] = "cloud-siri-kamida-32-belgi-0123456789",
        }));

        var current = new RuntimeLiveKitOptions(runtime, Options.Create(SelfHostedSeed)).Current;

        current.Url.Should().Be("http://livekit:7880");
        current.PublicUrl.Should().Be("wss://livekit.zinnur.uz");
        current.ApiKey.Should().Be("baza-kalit");
    }

    /// <summary>
    /// 🔴 ZAXIRA: Cloud tanlangan, lekin to'plam yarim (panel buni to'sadi —
    /// bu faqat bazani qo'lda tahrirlash holati). Bo'sh kalitli token HAMMA
    /// darsni o'chirardi, shuning uchun o'z serverimizga qaytiladi.
    /// </summary>
    [Fact]
    public void LiveKit_ProviderCloudWithPartialSet_FallsBackToSelfHosted()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.LiveKitApiKey] = "baza-kalit",
            [SettingsRegistry.Keys.LiveKitApiSecret] = "baza-siri-kamida-32-belgi-0123456789",
            [SettingsRegistry.Keys.LiveKitProvider] = "Cloud",
            [SettingsRegistry.Keys.LiveKitCloudUrl] = "wss://zinnur-abc.livekit.cloud",
            [SettingsRegistry.Keys.LiveKitCloudApiKey] = "APIcloud",
        }));

        var current = new RuntimeLiveKitOptions(runtime, Options.Create(SelfHostedSeed)).Current;

        current.Url.Should().Be("http://livekit:7880");
        current.ApiKey.Should().Be("baza-kalit");
    }

    /// <summary>
    /// Faol bo'lmagan serverning juftligi — webhook tekshiruvi uchun zaxira
    /// (almashtirishdan keyin kechikkan `egress_ended` rad etilmasin).
    /// </summary>
    [Theory]
    [InlineData("Cloud", "APIcloud", "baza-kalit")]
    [InlineData("SelfHosted", "baza-kalit", "APIcloud")]
    public void LiveKit_InactiveServerKeys_AreKeptAsStandby(string provider, string active, string standby)
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.LiveKitApiKey] = "baza-kalit",
            [SettingsRegistry.Keys.LiveKitApiSecret] = "baza-siri-kamida-32-belgi-0123456789",
            [SettingsRegistry.Keys.LiveKitProvider] = provider,
            [SettingsRegistry.Keys.LiveKitCloudUrl] = "wss://zinnur-abc.livekit.cloud",
            [SettingsRegistry.Keys.LiveKitCloudApiKey] = "APIcloud",
            [SettingsRegistry.Keys.LiveKitCloudApiSecret] = "cloud-siri-kamida-32-belgi-0123456789",
        }));

        var current = new RuntimeLiveKitOptions(runtime, Options.Create(SelfHostedSeed)).Current;

        current.ApiKey.Should().Be(active);
        current.StandbyApiKey.Should().Be(standby);
        current.StandbyApiSecret.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Cloud to'plami bo'sh — zaxira ham bo'sh (webhook faqat faol juftlik bilan).</summary>
    [Fact]
    public void LiveKit_WithoutCloudSet_HasNoStandby()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.LiveKitApiKey] = "baza-kalit",
            [SettingsRegistry.Keys.LiveKitApiSecret] = "baza-siri-kamida-32-belgi-0123456789",
        }));

        var current = new RuntimeLiveKitOptions(runtime, Options.Create(SelfHostedSeed)).Current;

        current.StandbyApiKey.Should().BeEmpty();
        current.StandbyApiSecret.Should().BeEmpty();
    }

    /// <summary>Almashtirish DEPLOYSIZ: yangi kesim darhol boshqa serverni beradi.</summary>
    [Fact]
    public void LiveKit_SwitchingProvider_IsVisibleImmediately()
    {
        var values = new Dictionary<string, string?>
        {
            [SettingsRegistry.Keys.LiveKitApiKey] = "baza-kalit",
            [SettingsRegistry.Keys.LiveKitApiSecret] = "baza-siri-kamida-32-belgi-0123456789",
            [SettingsRegistry.Keys.LiveKitProvider] = "SelfHosted",
            [SettingsRegistry.Keys.LiveKitCloudUrl] = "wss://zinnur-abc.livekit.cloud",
            [SettingsRegistry.Keys.LiveKitCloudApiKey] = "APIcloud",
            [SettingsRegistry.Keys.LiveKitCloudApiSecret] = "cloud-siri-kamida-32-belgi-0123456789",
        };

        var runtime = new FakeRuntimeSettings(Snapshot(1, values));
        var options = new RuntimeLiveKitOptions(runtime, Options.Create(SelfHostedSeed));

        options.Current.ApiKey.Should().Be("baza-kalit");

        values[SettingsRegistry.Keys.LiveKitProvider] = "Cloud";
        runtime.Current = Snapshot(2, values);

        options.Current.ApiKey.Should().Be("APIcloud");

        values[SettingsRegistry.Keys.LiveKitProvider] = "SelfHosted";
        runtime.Current = Snapshot(3, values);

        options.Current.ApiKey.Should().Be("baza-kalit");
    }

    // ================================================================= yordamchilar

    private static SettingsSnapshot Snapshot(long version, Dictionary<string, string?> values) =>
        new(new Dictionary<string, string?>(values, StringComparer.Ordinal), version);

    /// <summary>
    /// Soxta kesh: kesimni testdan boshqarish uchun. Redis ham, baza ham
    /// aralashmaydi — tekshiriladigan narsa faqat YIG'ISH qoidasi.
    /// </summary>
    private sealed class FakeRuntimeSettings(SettingsSnapshot current) : IRuntimeSettings
    {
        public SettingsSnapshot Current { get; set; } = current;

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
