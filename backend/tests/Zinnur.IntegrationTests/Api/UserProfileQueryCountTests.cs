using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// N+1 QO'RIQCHISI: PROFIL AGREGATI QANCHA SO'ROV YUBORADI
/// ========================================================================
///
/// NIMA UCHUN BUNDAY TEST KERAK: "har blok bitta so'rov" degan qoidani kod
/// o'qib tekshirish ishonchsiz. Ro'yxat proyeksiyasiga qo'shilgan bitta
/// beg'ubor ko'rinadigan navigatsiya (masalan sikl ichida ism olish) SQL
/// sonini o'nlab barobar oshiradi va buni FAQAT o'lchov ko'rsatadi.
///
/// O'LCHOV USULI: EF Core `IInterceptor` — DI'ga qo'shilgan
/// <see cref="DbCommandInterceptor"/> ni EF O'ZI topib ishlatadi, ya'ni
/// hisoblagich HAQIQIY bajarilgan SQL buyruqlarini sanaydi.
///
/// ⚠️ Nima uchun LOG orqali EMAS: ilova Serilog'ga o'tgan
/// (<c>builder.Host.UseSerilog(...)</c>), u esa <c>ConfigureLogging</c>
/// orqali qo'shilgan <c>ILoggerProvider</c> larni CHETLAB o'tadi — test
/// o'lchovi jimgina nol qaytarardi (bu shu testni yozishda yuz berdi).
///
/// ★ CHEGARA ANIQ SON EMAS, YUQORI CHEGARA: maqsad — arxitektura buzilishini
/// ushlash, kod refaktoringini to'sish emas. Ma'lumot ATAYLAB ko'p (3 guruh,
/// 3 oylik davr, 5 vazifa, 5 test, 5 izoh): N+1 paydo bo'lsa son chegaradan
/// bir necha barobar oshib ketadi.
/// </summary>
public sealed class UserProfileQueryCountTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>
    /// Yuqori chegara.
    ///
    /// O'LCHANGAN QIYMAT — 13 ta so'rov, va u AYNAN bloklar soniga teng:
    /// aktor · profil egasi · guruhlar · uzish izi · balans · oylik davrlar ·
    /// darslar soni · to'lov jurnali · moliya sozlamasi · vazifalar ·
    /// testlar · davomat · izohlar.
    ///
    /// Chegarada kichik zaxira bor: sessiya holati keshi (Redis) o'tkazib
    /// yuborilsa qo'shimcha bir-ikki so'rov bo'lishi mumkin. Zaxira N+1 ni
    /// yashira olmaydi — testdagi ma'lumot hajmida (3 guruh, 3 davr, 5 vazifa,
    /// 5 test, 5 izoh) ro'yxat ichidagi bitta qo'shimcha so'rov ham sonni
    /// 25+ ga olib chiqadi.
    /// </summary>
    private const int MaxQueries = 16;

    [Fact]
    public async Task Profile_WithMuchData_StaysUnderQueryBudget()
    {
        var world = await ProfileWorldBuilder.CreateWithFinanceAsync(factory, "n1-profil");

        // Yana ikkita guruh (jami 3) — har biri ustoz ismini talab qiladi.
        for (var i = 0; i < 2; i++)
        {
            var extra = await WorldBuilder.CreateAsync(factory, "n1-guruh" + i.ToString(CultureInfo.InvariantCulture));

            using var admin = await WorldBuilder.AdminClientAsync(factory);

            var add = await admin.PostAsJsonAsync(
                $"/api/v1/groups/{extra.GroupId}/members", new { studentId = world.Student.Id });

            add.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(add));
        }

        // Yana ikki oylik davr (jami 3) — har biri "o'sha oydagi darslar
        // soni" ni talab qiladi.
        await OpenPeriodAsync(world.GroupId, "2026-02");
        await OpenPeriodAsync(world.GroupId, "2026-03");

        await ProfileWorldBuilder.AddTwoEndedSessionsAsync(factory, world.GroupId, world.Student.Id);

        // Vazifalar, testlar va izohlar — har biri ro'yxat elementi uchun
        // qo'shimcha nom (guruh, dars, muallif) talab qiladi.
        for (var i = 0; i < 5; i++)
        {
            await ProfileWorldBuilder.AddSubmissionWithFileAsync(
                factory, world.GroupId, world.Student.Id);

            await WorldBuilder.AddTestAttemptAsync(
                factory, world.Student.Id, score: 8m, maxScore: 10m,
                submittedAtUtc: new DateTimeOffset(2026, 1, 20, 10, 0, 0, TimeSpan.Zero));
        }

        using (var admin = await WorldBuilder.AdminClientAsync(factory))
        {
            for (var i = 0; i < 5; i++)
            {
                var note = await admin.PostAsJsonAsync(
                    $"/api/v1/users/{world.Student.Id}/notes",
                    new { body = "Izoh " + i.ToString(CultureInfo.InvariantCulture) });

                note.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(note));
            }
        }

        // ── O'LCHOV ──────────────────────────────────────────────────────
        var counter = new SqlCommandCounter();

        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // `DbContextOptions` ni QAYTA ro'yxatga olamiz: interceptor'ni
                // faqat DI'ga singleton qilib qo'shish YETARLI EMAS —
                // `AddDbContext` allaqachon yasab bo'lgan options'ga u
                // tushmaydi (bu shu testni yozishda o'lchov nol qaytarib
                // isbotlandi). Shuning uchun eski options olib tashlanadi.
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();

                services.AddDbContext<ApplicationDbContext>((sp, options) =>
                {
                    var connectionString = sp.GetRequiredService<IConfiguration>()
                        .GetConnectionString("Postgres");

                    options.UseNpgsql(connectionString);
                    options.AddInterceptors(new CountingInterceptor(counter));
                });
            }));

        var accessToken = await LoginAsAdminAsync(host);

        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Isitish: birinchi so'rov model qurish va keshlarni to'ldiradi.
        (await client.GetAsync(ProfileUri(world.Student.Id))).EnsureSuccessStatusCode();

        counter.Reset();

        var response = await client.GetAsync(ProfileUri(world.Student.Id));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var executed = counter.Count;

        executed.Should().BeGreaterThan(0, "o'lchov ishlayotganini isbotlash uchun");

        executed.Should().BeLessThanOrEqualTo(MaxQueries,
            "profil agregatida har blok BITTA so'rov bo'lishi kerak — "
            + "son oshib ketsa ro'yxat ichida N+1 paydo bo'lgan");
    }

    private static Uri ProfileUri(long userId) =>
        new("/api/v1/users/" + userId.ToString(CultureInfo.InvariantCulture) + "/profile",
            UriKind.Relative);

    private async Task OpenPeriodAsync(long groupId, string period)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/payments/periods/open", new { period, groupId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
    }

    /// <summary>
    /// Admin uchun kirish tokeni.
    ///
    /// ⚠️ 2026-08-13: ilgari bu yerda `POST /api/v1/auth/login` chaqirilardi —
    /// endpoint olib tashlandi (email va parol bilan kirish yo'q). Token
    /// endi `ZinnurApiFactory.LoginAsAdminAsync` bilan AYNI yo'ldan,
    /// haqiqiy `IJwtTokenService` orqali yasaladi.
    ///
    /// ★ BU SINF UCHUN AYNIQSA MUHIM: u SQL so'rovlarini SANAYDI. Kirish
    ///   HTTP orqali bo'lganda o'lchovga kirishning o'z so'rovlari ham
    ///   tushib, chegara sababsiz "oshib ketardi".
    /// </summary>
    private static async Task<string> LoginAsAdminAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var admin = await db.Users
            .AsNoTracking()
            .FirstAsync(u => u.Email == DbInitializer.AdminEmail);

        return jwt.CreateAccessToken(admin);
    }

    // ---------------------------------------------------------------- o'lchov vositalari

    /// <summary>
    /// Bajarilgan SQL buyruqlarini sanaydi. Thread-safe: bir so'rov ichida
    /// EF buyruqlarni turli oqimlarda bajarishi mumkin.
    /// </summary>
    private sealed class SqlCommandCounter
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public int Count => _commands.Count;

        public void Add(string sql) => _commands.Enqueue(sql);

        public void Reset() => _commands.Clear();
    }

    /// <summary>
    /// Har SQL buyrug'ini hisoblagichga yozadi.
    ///
    /// Sinxron va asinxron shoxlar ham qoplangan: EF ba'zi ichki
    /// so'rovlarni (masalan tranzaksiya) sinxron bajaradi va faqat asinxron
    /// metodni ushlash sonni kam ko'rsatib qo'yardi.
    /// </summary>
    private sealed class CountingInterceptor(SqlCommandCounter counter) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Track(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Track(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Track(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Track(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Track(command);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Track(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Track(DbCommand command) => counter.Add(command.CommandText);
    }
}
