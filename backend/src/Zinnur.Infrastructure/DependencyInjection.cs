using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Zinnur.Application.Common.Interfaces;
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

        // Token va parol xizmatlari holatsiz (stateless) — Singleton.
        // Har so'rovda qayta yaratish JWT kaliti va HMAC obyektlarini
        // qaytadan tayyorlashni anglatardi (kirish oqimida keraksiz ish).
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ILiveKitTokenService, LiveKitTokenService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        return services;
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

        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IPresenceService, RedisPresenceService>();
    }
}
