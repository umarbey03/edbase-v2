using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.IntegrationTests.Infrastructure;

/// <summary>
/// Haqiqiy API'ni xotirada ko'taradi va HAQIQIY Postgres/Redis bilan ishlaydi.
///
/// NIMA UCHUN MOCK EMAS: eng qimmat buglar aynan qatlamlar CHEGARASIDA
/// yashiringan bo'ladi — EF konfiguratsiyasi, indekslar, JWT claim xaritalash,
/// ruxsat tekshiruvi. Mock bilan ular ko'rinmaydi. Masalan `name` claim'i
/// `ClaimTypes.Name` ga xaritalanmagani jonli sinovda topilgan edi.
///
/// HAR TEST SINFI O'Z BAZASINI oladi (nomida tasodifiy qism) va tugagach
/// o'chiradi — testlar bir-biriga xalaqit bermaydi va parallel ishlay oladi.
///
/// Ulanish manzillari muhit o'zgaruvchisidan olinadi:
///   lokal  -> ishlab turgan `zinnur-v2` stack (localhost:5440 / 6390)
///   CI     -> GitHub Actions service konteynerlari (localhost:5432 / 6379)
/// </summary>
public class ZinnurApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Kirish endpointining rate-limit budjeti (`RateLimiting:Auth:PermitLimit`).
    ///
    /// Odatiy fixture'da ATAYLAB juda baland. NEGA: `TestServer` da
    /// `RemoteIpAddress` — null, ya'ni sinfdagi HAMMA so'rov bitta
    /// "unknown" bo'limiga tushadi, bitta test sinfi esa o'nlab marta
    /// kirish qiladi. Prod chegarasi bilan testlar bir-birini bloklab,
    /// sababsiz "flaky" bo'lardi — ya'ni yashil natija hech nima
    /// isbotlamasdi.
    ///
    /// Chegaraning O'ZINI tekshiradigan test uni pasaytirib override qiladi
    /// (`AuthRateLimitTests`) — shuning uchun bu sinf `sealed` emas.
    /// </summary>
    protected virtual int AuthPermitLimit => 1000;

    /// <summary>Token yangilash budjeti — sababi <see cref="AuthPermitLimit"/> bilan bir xil.</summary>
    protected virtual int AuthRefreshPermitLimit => 1000;

    /// <summary>
    /// Oyna uzunligi (sekund). Chegara testida ATAYLAB uzaytiriladi:
    /// qat'iy oyna (fixed window) so'rovlar orasida yopilib qolsa,
    /// hisoblagich nolga qaytib test tasodifan yiqilardi.
    /// </summary>
    protected virtual int AuthWindowSeconds => 60;

    private readonly string _databaseName =
        $"zinnur_test_{Guid.NewGuid():N}"[..24];

    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("TEST_POSTGRES")
        ?? "Host=localhost;Port=5440;Database=postgres;Username=zinnur;Password=zinnur";

    private static string RedisConnectionString =>
        Environment.GetEnvironmentVariable("TEST_REDIS")
        ?? "localhost:6390";

    private string TestConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            {
                Database = _databaseName,
                // Test bazasi uchun kichik pool — 20 ta parallel test sinfi
                // Postgres'ning max_connections'ini tugatib qo'ymasin.
                MaxPoolSize = 5,
                MinPoolSize = 0,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        // Bo'sh baza yaratamiz; sxemani migratsiyalar quradi (app startup'da).
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using var cmd = admin.CreateCommand();
        cmd.CommandText =
            $"""CREATE DATABASE "{_databaseName}";""";
        await cmd.ExecuteNonQueryAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        // Pool'dagi ulanishlar bazani ushlab turmasin
        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"""DROP DATABASE IF EXISTS "{_databaseName}" WITH (FORCE);""";
        await cmd.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        // `UseSetting` ATAYLAB — `ConfigureAppConfiguration` EMAS.
        //
        // NIMA UCHUN: minimal hosting'da `Program.cs` konfiguratsiyani
        // `builder.Configuration[...]` orqali HOST QURILAYOTGANDA o'qiydi
        // (masalan `Jwt:Secret` tekshiruvi, Redis backplane, health check'lar).
        // `ConfigureAppConfiguration` callback'lari esa undan KEYIN ishlaydi,
        // ya'ni override kuchga kirmaydi va ilova `appsettings.json` dagi
        // `postgres`/`redis` Docker DNS nomlarini ishlatib, test konteynerida
        // "Name or service not known" bilan yiqiladi.
        //
        // `UseSetting` qiymatni eng boshida — host qurilishidan oldin —
        // konfiguratsiyaga yozadi.
        foreach (var (key, value) in TestSettings())
            builder.UseSetting(key, value);
    }

    /// <summary>Test muhiti uchun konfiguratsiya qiymatlari.</summary>
    private IEnumerable<KeyValuePair<string, string>> TestSettings() =>
    [
        new("ConnectionStrings:Postgres", TestConnectionString),
        new("ConnectionStrings:Redis", RedisConnectionString),

        // Testlar uchun qat'iy, oldindan ma'lum sirlar (32+ belgi majburiy)
        new("Jwt:Secret", "integration_test_secret_min_32_chars_0123456789"),
        new("Jwt:Issuer", "zinnur"),
        new("Jwt:Audience", "zinnur-web"),
        new("Jwt:AccessMinutes", "15"),
        new("Jwt:RefreshDays", "14"),

        // LiveKit: token IMZOLASH uchun sir kerak, serverga ULANISH kerak emas —
        // testlar tokenning FORMATINI tekshiradi, LiveKit'ni ishga tushirmaydi.
        new("LiveKit:Url", "http://127.0.0.1:7880"),
        new("LiveKit:PublicUrl", "ws://127.0.0.1:7880"),
        new("LiveKit:ApiKey", "devkey"),
        new("LiveKit:ApiSecret", "integration_test_livekit_secret_min_32_ch"),

        // Sentry testlarda o'chiq — tashqi tarmoqqa chiqmasin
        new("Sentry:Dsn", string.Empty),

        // Rate-limit: izoh yuqorida (AuthPermitLimit).
        new("RateLimiting:Auth:PermitLimit",
            AuthPermitLimit.ToString(CultureInfo.InvariantCulture)),
        new("RateLimiting:Auth:RefreshPermitLimit",
            AuthRefreshPermitLimit.ToString(CultureInfo.InvariantCulture)),
        new("RateLimiting:Auth:WindowSeconds",
            AuthWindowSeconds.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>Seed qilingan admin bilan kirib, tokenlarni qaytaradi.</summary>
    public async Task<AuthTokens> LoginAsAdminAsync() =>
        await LoginAsync("admin@zinnur.uz", "Admin!2345");

    public async Task<AuthTokens> LoginAsync(string email, string password)
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AuthTokens>()
               ?? throw new InvalidOperationException("Kirish javobi bo'sh.");
    }

    /// <summary>Berilgan token bilan avtorizatsiyalangan HTTP klient.</summary>
    public HttpClient CreateAuthorizedClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Testda to'g'ridan-to'g'ri bazaga tegish kerak bo'lganda.</summary>
    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }

    public async Task<long> CountUsersAsync() =>
        await WithDbAsync(db => db.Users.LongCountAsync());

    public string DatabaseName => _databaseName;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"ZinnurApiFactory({_databaseName})");
}

/// <summary>`/api/v1/auth/login` javobi.</summary>
public sealed record AuthTokens(string AccessToken, string RefreshToken, AuthUser User);

public sealed record AuthUser(long Id, string FullName, string Email, string Role);
