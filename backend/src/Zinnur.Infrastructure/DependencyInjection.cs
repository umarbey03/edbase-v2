using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Recordings.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Infrastructure.Options;
using Zinnur.Infrastructure.Persistence;
using Zinnur.Infrastructure.Services;

namespace Zinnur.Infrastructure;

/// <summary>
/// Infrastructure qatlamini DI'ga ulaydi.
/// WebApi faqat SHU metodni biladi — ichkarida qaysi ORM yoki qaysi kesh
/// ishlatilgani unga qorong'i (Clean Architecture: tashqi halqa ichkiga qaram).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Postgres ulanish satri kaliti: <c>ConnectionStrings__Postgres</c>.</summary>
    public const string PostgresConnectionName = "Postgres";

    /// <summary>Redis ulanish satri kaliti: <c>ConnectionStrings__Redis</c>.</summary>
    /// <remarks>SignalR backplane ham AYNAN shu nomdan foydalanadi (SPEC 6).</remarks>
    public const string RedisConnectionName = "Redis";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services, configuration);
        AddPersistence(services, configuration);
        AddRedis(services, configuration);
        AddRuntimeSettings(services, configuration);
        AddStorage(services);
        AddRecordings(services);

        // Token va parol xizmatlari holatsiz (stateless) — Singleton.
        // Har so'rovda qayta yaratish JWT kaliti va HMAC obyektlarini
        // qaytadan tayyorlashni anglatardi (kirish oqimida keraksiz ish).
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ILiveKitTokenService, LiveKitTokenService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        // Jadval zonasi ham holatsiz: zona fayli bir marta o'qiladi va
        // ilova umri davomida keshda qoladi.
        services.AddSingleton<IScheduleTimeZoneProvider, ConfiguredScheduleTimeZone>();

        // ---------------------------------------------------------------- sozlamalar
        //
        // SOZLAMA QATORLARI (`AppSettings` jadvali) — SCOPED: xizmat
        // `ApplicationDbContext` ga tayanadi va o'zgarishlarni AYNI so'rovning
        // ChangeTracker'iga qo'shadi (sozlama va uning audit izi bitta
        // tranzaksiyada saqlanishi uchun).
        services.AddScoped<ISettingsStore, AppSettingsStore>();

        // KONFIGURATSIYA O'QUVCHISI — SINGLETON va holatsiz: `IConfiguration`
        // ning o'zi singleton, o'qish esa lug'atdan olish bilan barobar.
        services.AddSingleton<ISettingsEnvironment, ConfigurationSettingsEnvironment>();

        // Moliya sozlamalari endi UMUMIY registr ustida ishlaydi (kalitlar,
        // standart qiymat va tekshiruv qoidasi `SettingsRegistry` da) —
        // ikkita parallel sozlamalar tizimi bo'lmasligi uchun.
        // SCOPED, chunki `ISettingsStore` scoped.
        services.AddScoped<IFinanceSettingsStore, FinanceSettingsStore>();

        return services;
    }

    /// <summary>
    /// ========================================================================
    /// ISH JARAYONIDA O'QILADIGAN SOZLAMALAR
    /// ========================================================================
    ///
    /// ★ NIMA UCHUN BU BLOK BOR: <c>IOptions&lt;T&gt;</c> qiymatni ilova
    /// ISHGA TUSHGANDA bir marta o'qiydi va singleton xizmatga qotirib
    /// qo'yadi. Bazadan o'zgartirilsa panel "saqlandi" derdi-yu, tizim eski
    /// qiymat bilan ishlayverardi — eng yomon turdagi xato: JIMGINA YOLG'ON.
    ///
    /// Endi zanjir shunday:
    ///   AppSettings (baza) -> ISettingsResolver -> IRuntimeSettings (kesh)
    ///                      -> IRuntimeOptions&lt;T&gt; -> iste'molchi.
    ///
    /// Kesh yangilanishi va uning KAFOLATLANGAN KECHIKISHI (10 s) haqida
    /// batafsil: <see cref="RuntimeSettings"/>.
    /// </summary>
    private static void AddRuntimeSettings(IServiceCollection services, IConfiguration configuration)
    {
        // Kalit MAKONI — Redis kanali uchun (izoh: `AddRedis`). Bitta Redis'ni
        // dev/staging va integratsiya testlari baham ko'radi; makonsiz kanalda
        // ular bir-birining keshini bekordan qayta o'qishga majbur qilardi.
        var keyPrefix = configuration["Redis:KeyPrefix"];

        // BITTA instansiya UCHTA rolda: kesh (`IRuntimeSettings`), fon
        // yangilovchisi (`IHostedService`) va Redis obunachisi. Ular AYNI
        // obyekt bo'lishi SHART — aks holda fon xizmati bir keshni
        // yangilardi, iste'molchilar esa boshqasini o'qirdi va o'zgarish
        // hech qachon ko'rinmasdi (`ChatMessageWriter` bilan bir xil naqsh).
        services.AddSingleton(sp => new RuntimeSettings(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IConnectionMultiplexer>(),
            sp.GetRequiredService<ILogger<RuntimeSettings>>(),
            keyPrefix));

        services.AddSingleton<IRuntimeSettings>(sp => sp.GetRequiredService<RuntimeSettings>());
        services.AddHostedService(sp => sp.GetRequiredService<RuntimeSettings>());

        // ── TAYYORLANGAN SOZLAMA OBYEKTLARI ──────────────────────────────
        //
        // Har biri SINGLETON: ular kesim RAQAMI bo'yicha tayyorlangan
        // obyektni keshlaydi (`RuntimeOptions<T>`), ya'ni kesim o'zgarmasa
        // yangi obyekt umuman yasalmaydi. Scoped bo'lsa bu kesh har so'rovda
        // yo'qolardi va `IsConfigured` ning har chaqirig'i yangi obyekt
        // yasashga olib kelardi.
        //
        // ⚠️ `IOptions<T>` ro'yxatdan OLIB TASHLANMAYDI: u endi BOSHLANG'ICH
        // qiymat (seed) manbai va registrda umuman yo'q maydonlar
        // (`TimeoutSeconds`, `KeyPrefix`) uchun yagona manba bo'lib qoladi.
        services.AddSingleton<IRuntimeOptions<StorageOptions>, RuntimeStorageOptions>();
        services.AddSingleton<IRuntimeOptions<TelegramOptions>, RuntimeTelegramOptions>();
        services.AddSingleton<IRuntimeOptions<LiveKitOptions>, RuntimeLiveKitOptions>();
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart(): xato konfiguratsiyada ilova KO'TARILMAYDI.
        // Eski tizimda bo'sh `Jwt:Secret` bilan server bemalol ishga tushar,
        // muammo esa faqat birinchi kirishda 500 xato ko'rinishida chiqardi.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.Secret) >= JwtOptions.MinSecretLength,
                $"Jwt:Secret kamida {JwtOptions.MinSecretLength} bayt bo'lishi shart (HS256 kaliti).")
            .ValidateOnStart();

        // Jadval zonasi: xato yozilgan id bilan ilova KO'TARILMASIN.
        // Aks holda xato faqat birinchi guruh yaratilganda 500 bo'lib chiqardi
        // va butun jadval moduli ishlamay turardi.
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => AppOptions.TryResolve(o.TimeZone, out _),
                "App:TimeZone IANA vaqt zonasi bo'lishi kerak "
                + $"(masalan '{AppOptions.DefaultTimeZone}'), va konteynerda `tzdata` bo'lishi shart.")
            .ValidateOnStart();

        // LIVEKIT — bu yerda tekshiruv ATAYLAB QOLDIRILDI.
        //
        // ★ NIMA UCHUN `Storage`/`Telegram` dan farqli: LiveKit IXTIYORIY
        //   emas. `LiveKit:Url` — tarmoq topologiyasi, u faqat muhitdan
        //   keladi (sabab: `RuntimeLiveKitOptions`), ya'ni LiveKit baribir
        //   deploy bilan sozlanadi. Kalit va sir esa bazadan USTUN o'qiladi
        //   — panel ularni AYLANTIRISH (rotate) uchun.
        //
        // ⚠️ QABUL QILINGAN CHEKLOV: `LiveKit:ApiKey`/`ApiSecret` muhitda
        //   BO'LISHI SHART (bo'sh bo'lsa ilova ko'tarilmaydi), garchi amalda
        //   bazadagi qiymat ustun bo'lsa ham. Buni yumshatish bo'sh kalit
        //   bilan ko'tarilishga yo'l ochardi — u holda token XATO BERMASDAN
        //   rad etilardi va buni faqat birinchi dars boshlanganda bilardik.
        services.AddOptions<LiveKitOptions>()
            .Bind(configuration.GetSection(LiveKitOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.ApiSecret) >= LiveKitOptions.MinSecretLength,
                $"LiveKit:ApiSecret kamida {LiveKitOptions.MinSecretLength} bayt bo'lishi shart.")
            // ICHKI manzil — server-to-server. Docker tarmog'ida http/https,
            // (ws/wss ham qabul qilinadi: LiveKit ikkalasini ham tushunadi).
            .Validate(
                o => LiveKitOptions.HasSupportedScheme(o.Url, "http", "https", "ws", "wss"),
                "LiveKit:Url absolyut manzil bo'lishi kerak (masalan `http://livekit:7880`).")
            // BRAUZERGA ketadigan manzil. Bo'sh bo'lsa dev'da `Url` ga qaytadi,
            // to'ldirilgan bo'lsa faqat brauzer qabul qiladigan sxemalar.
            .Validate(
                o => string.IsNullOrWhiteSpace(o.PublicUrl)
                     || LiveKitOptions.HasSupportedScheme(o.PublicUrl, "ws", "wss", "https"),
                "LiveKit:PublicUrl `wss://` (prod) yoki `ws://` (dev) bo'lishi kerak — "
                + "HTTPS sahifadan `ws://` ga ulanishni brauzer bloklaydi.")
            .ValidateOnStart();

        // ══════════════════════════════════════════════════════════════════
        // ★★ «TO'LIQ YOKI BO'SH» HIMOYASI BU YERDAN KO'CHIRILDI
        //
        // Ilgari `Storage:*` va `Telegram:*` yarim to'ldirilgan bo'lsa ilova
        // UMUMAN ko'tarilmasdi. Endi bu kalitlar BAZADAN keladi, ya'ni ishga
        // tushish paytida ular hali O'QILGAN ham bo'lmaydi — tekshiruv
        // muhitdagi (boshlang'ich) qiymatlarni ko'rib, YOLG'ON xulosa
        // chiqarardi: baza to'liq sozlangan bo'lsa ham "yarim" deb ilovani
        // yiqitardi, yoki teskarisi.
        //
        // ★ HIMOYA OLIB TASHLANMADI, KO'CHIRILDI — yozish yo'liga:
        //   `SettingCoupling` + `SettingsService.EnsureSetNotBrokenAsync`.
        //   Panel orqali ISHLAB TURGAN to'plamni yarim holatga tushirib
        //   bo'lmaydi (batafsil sabab va assimetriya: `SettingCoupling`).
        //
        // ★ QOLGAN XAVF VA U NIMA UCHUN QABUL QILINDI: kimdir `.env` da
        //   yarim to'ldirilgan to'plam qoldirsa, ilova endi ko'tariladi va
        //   integratsiya jimgina O'CHIQ bo'ladi. Bu XAVFSIZ, chunki
        //   `IsConfigured` BARCHA a'zoni talab qiladi: yarim `Storage:*` —
        //   fayl yuklash 503 (sozlanmagan bilan bir xil), yarim
        //   `Telegram:*` — webhook 404. `ValidateOnStart` qo'riqlagan ENG
        //   XAVFLI holat ("token bor, sir yo'q => webhook OCHIQ") bu kodda
        //   YUZAGA KELMAYDI: controller endpointni to'plam TO'LIQ
        //   bo'lmaguncha 404 qiladi.
        // ══════════════════════════════════════════════════════════════════

        // OBYEKT OMBORI (R2/S3) — IXTIYORIY.
        //
        // Bo'sh bo'lsa ilova ko'tariladi va fayl yuklash 503 qaytaradi
        // (o'quvchi matnli javob topshira oladi). SHAKL tekshiruvi qoladi:
        // u bitta qiymatga tegishli (to'plamga emas) va `.env` dagi xato
        // yozilgan manzilni ishga tushishda, sababi ko'rinib turganda tutadi.
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.HasValidServiceUrl,
                "Storage:ServiceUrl absolyut http(s) manzil bo'lishi kerak "
                + "(masalan `https://<account>.r2.cloudflarestorage.com`).")

            // BRAUZERGA ketadigan manzil (FAZA 5.3, dars yozuvi presigned
            // havolasi). Bo'sh bo'lsa `ServiceUrl` ishlatiladi — shakl
            // tekshiruvi esa `.env` dagi xato yozilgan qiymatni ISHGA
            // TUSHISHDA, sababi ko'rinib turganda tutadi. Aks holda u
            // faqat birinchi yozuv ochilganda, "403 SignatureDoesNotMatch"
            // ko'rinishida chiqardi.
            .Validate(
                o => o.HasValidPublicUrl,
                "Storage:PublicUrl absolyut http(s) manzil bo'lishi kerak "
                + "(dev'da `http://localhost:9010`).")
            .ValidateOnStart();

        // TELEGRAM (FAZA 5.1) — IXTIYORIY.
        //
        // Bo'sh bo'lsa ilova ko'tariladi va Telegram funksiyalari o'chiq
        // bo'ladi: webhook 404, Mini App kirishi 503, xabarlar esa
        // vaqtinchalik log-yuboruvchiga tushadi. Dev mashinasida bot tokeni
        // yo'q va bu butun platformani to'xtatib qo'ymasligi kerak.
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.HasValidBotToken,
                "Telegram:BotToken shakli noto'g'ri (kutilgan `123456:AA...`, bo'shliqsiz).")
            .Validate(
                o => o.HasValidWebhookSecret,
                "Telegram:WebhookSecret faqat A-Z a-z 0-9 _ - belgilaridan iborat bo'lishi va "
                + "256 belgidan oshmasligi kerak (Telegram talabi).")
            .Validate(
                o => o.HasValidMiniAppUrl,
                "Telegram:MiniAppUrl absolyut `https://` manzil bo'lishi kerak — "
                + "Telegram `web_app` tugmasi uchun HTTPS majburiy.")
            .Validate(
                o => o.HasValidApiBaseUrl,
                "Telegram:ApiBaseUrl absolyut http(s) manzil bo'lishi kerak "
                + "(standart `https://api.telegram.org`).")
            .ValidateOnStart();

        // MOLIYA. Bu yerda `ValidateOnStart` KERAK EMAS: barcha maydonlar
        // xavfsiz standart qiymatga ega va bo'lim umuman bo'lmasa ham ilova
        // to'g'ri ishlaydi (chegara/qamrov baribir bazadan o'qiladi).
        // Yagona tekshiruv — qamrov nomi haqiqiy enum bo'lishi.
        services.AddOptions<PaymentsOptions>()
            .Bind(configuration.GetSection(PaymentsOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => Enum.IsDefined(o.DefaultBlockScope) && o.DefaultBlockThreshold >= 0,
                "Payments:DefaultBlockScope None|Video|Live|Platform bo'lishi va "
                + "Payments:DefaultBlockThreshold manfiy bo'lmasligi kerak.")
            .ValidateOnStart();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"'ConnectionStrings:{PostgresConnectionName}' sozlanmagan. " +
                "Docker'da bu `ConnectionStrings__Postgres` muhit o'zgaruvchisi.");
        }

        connectionString = ApplyPoolLimits(connectionString);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                // QAYTA URINISH: docker-compose'da API postgres'dan oldin
                // ko'tariladi va tarmoq qisqa muddatga uzilishi mumkin.
                // Busiz birinchi so'rov 500 xato bilan qaytardi.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);

                npgsql.CommandTimeout(30);
            }));

        // Application qatlami faqat portni ko'radi. AYNI DbContext instansiyasi
        // qaytariladi (yangisi emas) — aks holda bitta so'rov ichida ikki xil
        // ChangeTracker paydo bo'lib, tranzaksiya buzilardi.
        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());
    }

    /// <summary>
    /// Ulanish hovuzini (pool) CHEGARALAYDI.
    ///
    /// NIMA UCHUN: Npgsql'ning defaulti `Maximum Pool Size=100`. Postgres'da
    /// `max_connections` odatda 100 — ya'ni BITTA api instansiyasi butun
    /// bazani band qila oladi. Ikkinchi replika ko'tarilishi bilan
    /// "sorry, too many clients already" xatosi boshlanadi va bu YUKLAMA
    /// ostida, ya'ni eng yomon paytda chiqadi.
    ///
    /// 30 ta ulanish 200 foydalanuvchi uchun yetarli: so'rovlar qisqa
    /// (~ms), ulanish esa so'rov davomida band bo'ladi, dars davomida emas.
    /// Presence va chat Redis'da — ular bazaga umuman tegmaydi.
    ///
    /// Qiymat ulanish satrida OSHKOR ko'rsatilgan bo'lsa, tegilmaydi —
    /// operator uchun oxirgi so'z (masalan bitta katta instance uchun 60).
    /// </summary>
    private static string ApplyPoolLimits(string connectionString)
    {
        const int DefaultMaxPoolSize = 30;
        const int DefaultMinPoolSize = 2;

        // "Maximum Pool Size", "MaxPoolSize", "Minimum Pool Size" — hammasi
        // bo'shliqsiz "poolsize" ko'rinishiga tushadi.
        var normalized = connectionString.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Contains("poolsize", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = DefaultMaxPoolSize,

            // Minimal hovuz: birinchi so'rov "sovuq" ulanish kutmasin.
            MinPoolSize = DefaultMinPoolSize,
        };

        return builder.ConnectionString;
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(RedisConnectionName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"'ConnectionStrings:{RedisConnectionName}' sozlanmagan. " +
                "Docker'da bu `ConnectionStrings__Redis` muhit o'zgaruvchisi.");
        }

        // BITTA multiplexer butun ilovaga (Singleton) — StackExchange.Redis
        // shunday ishlatilishi uchun mo'ljallangan: u ichida bitta ulanish
        // ustidan barcha buyruqlarni multiplekslaydi. Har so'rovda yangi
        // ulanish ochish 200 kishilik darsda Redis'dagi ulanish limitini
        // bir necha daqiqada tugatardi.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);

            // Redis kech ko'tarilsa ham API ishga tushsin va keyin o'zi ulansin.
            // `true` bo'lsa konteyner start paytida qulab tushardi.
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 5;
            options.ClientName = "zinnur-api";

            return ConnectionMultiplexer.Connect(options);
        });

        // Kalit MAKONI sozlanadi: bitta Redis'ni bir nechta muhit baham
        // ko'rganda (dev/staging, integratsiya testlari, ikkinchi instance)
        // bir xil raqamli Id'lar bir-birining yozuviga tushmasin.
        // Berilmasa `RedisCacheService.DefaultPrefix` ishlatiladi.
        var cachePrefix = configuration["Redis:KeyPrefix"];

        services.AddSingleton<ICacheService>(sp =>
            new RedisCacheService(sp.GetRequiredService<IConnectionMultiplexer>(), cachePrefix));

        services.AddSingleton<IPresenceService, RedisPresenceService>();
    }

    /// <summary>
    /// Uy vazifasi ilovalarini saqlash (Cloudflare R2 / S3).
    ///
    /// Servis HOLATSIZ (Singleton) — imzo har so'rovda qaytadan hisoblanadi.
    /// `IHttpClientFactory` ishlatiladi: nomlangan klient socket'larni qayta
    /// ishlatadi va DNS ni vaqti-vaqti bilan yangilaydi (bitta `static
    /// HttpClient` esa DNS o'zgarganini hech qachon sezmaydi).
    /// </summary>
    private static void AddStorage(IServiceCollection services)
    {
        services.AddHttpClient(R2SubmissionStorage.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

            // Timeout MAJBURIY: ombor javob bermay qolsa so'rov mangu
            // osilib turardi va thread pool asta-sekin tugab borardi.
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 300));
        });

        services.AddSingleton<ISubmissionStorage, R2SubmissionStorage>();
    }

    /// <summary>
    /// ========================================================================
    /// DARS YOZUVI (FAZA 5.3): Egress, webhook imzosi va ko'rish havolasi
    /// ========================================================================
    ///
    /// ★ NIMA UCHUN UCHTASI HAM SINGLETON: ular HOLATSIZ. Kalitlar, sirlar
    /// va ombor manzili har chaqiruvda <c>IRuntimeOptions&lt;T&gt;</c> dan
    /// qayta o'qiladi (ular paneldan aylantiriladi), imzo esa har safar
    /// qaytadan hisoblanadi. Scoped bo'lsa hech narsa yutilmasdi, faqat
    /// har so'rovda keraksiz obyekt yasalardi.
    ///
    /// ⚠️ ISTISNO — <see cref="LiveKitWebhookLog"/>: u SCOPED, chunki
    /// jurnal yozuvi JORIY so'rovning <c>DbContext</c> kuzatuvchisiga
    /// tushishi va yozuv holati bilan BITTA tranzaksiyada saqlanishi kerak
    /// (<c>TelegramUpdateLog</c> bilan AYNI naqsh).
    /// </summary>
    private static void AddRecordings(IServiceCollection services)
    {
        // Egress uchun ALOHIDA nomlangan klient: timeout QISQA.
        //
        // ★ NIMA UCHUN ombor klienti (`zinnur-storage`, 60 s) qayta
        //   ishlatilmaydi: Egress chaqiruvi DARS BOSHLANAYOTGAN paytda,
        //   ustoz tugmani bosgan lahzada bo'ladi. LiveKit javob bermay
        //   qolsa ustoz yarim daqiqa "aylanayotgan" tugmaga qarab
        //   o'tirardi. Yozuv boshlanmasligi esa dars uchun halokat emas —
        //   watchdog qayta uradi. Shuning uchun tez taslim bo'lgan afzal.
        services.AddHttpClient(
            LiveKitEgressClient.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddSingleton<ILiveKitEgress, LiveKitEgressClient>();
        services.AddSingleton<ILiveKitWebhookVerifier, LiveKitWebhookVerifier>();
        services.AddSingleton<IRecordingStorage, R2RecordingStorage>();

        services.AddScoped<ILiveKitWebhookLog, LiveKitWebhookLog>();
    }
}
