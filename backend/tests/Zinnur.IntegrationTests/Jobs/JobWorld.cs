using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Jobs;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure;
using Zinnur.Infrastructure.Services;
using Zinnur.IntegrationTests.Api;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Jobs;

/// <summary>
/// Fon vazifalari testlari uchun umumiy fixture (FAZA 5.5).
///
/// ── NIMA UCHUN REJALASHTIRUVCHI O'CHIRILGAN ────────────────────────────
/// <c>Jobs:Enabled=false</c> (bazaviy fixture'da) — testlar vazifani O'ZI
/// chaqiradi va natijani darhol tekshiradi. Fon sikli parallel ishlab
/// tursa, u test yaratgan darsni "o'g'irlab" yakunlab qo'yishi mumkin edi
/// va test tasodifiy (flaky) bo'lardi. Rejalashtiruvchining O'ZI yupqa
/// sikl, uning butun mantiqi <see cref="IJobRunner"/> va vazifalarda —
/// aynan shu yerda sinaladi.
///
/// ── NIMA UCHUN HAQIQIY SIGNALR EMAS, JOSUS ─────────────────────────────
/// Tekshiriladigan narsa transport emas: avto-yakunlash xabarni yuborishga
/// BUYURDIMI. Bu muhim, chunki fon vazifasi bazaga o'zi yozib qo'ysa,
/// o'quvchilar ekranida dars tugagani ko'rinmasdi (ogohlantirish
/// <c>ILiveSessionNotifier</c> izohida yozilgan).
/// </summary>
public class JobFactory : ZinnurApiFactory
{
    /// <summary>Xabar yuborilganini yozib boradigan josus (Api testlaridan qayta ishlatiladi).</summary>
    public LiveSessionNotifierSpy Notifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            // Haqiqiy (SignalR) implementatsiyani OLIB TASHLAYMIZ — aks holda
            // qaysi biri ishlashini ro'yxat tartibi hal qilardi.
            services.RemoveAll<ILiveSessionNotifier>();

            services.AddSingleton(sp =>
            {
                Notifier.UseScopes(sp.GetRequiredService<IServiceScopeFactory>());
                return Notifier;
            });

            services.AddScoped<ILiveSessionNotifier>(sp =>
                sp.GetRequiredService<LiveSessionNotifierSpy>());
        });
    }

    /// <summary>Scope ichida vazifa servislari bilan ishlash.</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Dars avto-yakunlash vazifasining bitta yurishi.</summary>
    public Task<JobRunResult> RunSessionJobAsync() => RunAsync<SessionAutoCloseJob>();

    /// <summary>Oylik hisob vazifasining bitta yurishi.</summary>
    public Task<JobRunResult> RunBillingJobAsync() => RunAsync<MonthlyBillingJob>();

    /// <summary>
    /// Vazifani AYNAN rejalashtiruvchi kabi yurgizadi: yangi scope +
    /// <see cref="IJobRunner"/> (ya'ni QULF OSTIDA).
    ///
    /// ★ NIMA UCHUN VAZIFA TO'G'RIDAN-TO'G'RI CHAQIRILMAYDI: qulfsiz
    /// chaqiruv haqiqiy yo'ldan farq qilardi va "ikki instance bir vaqtda"
    /// testi hech nimani isbotlamasdi — u qulfga umuman tegmagan bo'lardi.
    /// </summary>
    private Task<JobRunResult> RunAsync<T>()
        where T : IScheduledJob =>
        WithScopeAsync(async sp =>
        {
            var execution = await Services
                .GetRequiredService<IJobRunner>()
                .RunAsync(JobOf<T>(sp));

            // Yiqilgan vazifa JIM qolmasin: yurgizuvchi istisnoni ataylab
            // yutadi (prod'da bu to'g'ri), lekin testda haqiqiy sabab
            // ko'rinishi kerak — aks holda tasdiqlashning yiqilishi
            // chalg'ituvchi bo'lardi.
            return execution.Outcome == JobOutcome.Failed
                ? throw new InvalidOperationException(
                    $"Fon vazifasi yiqildi: {execution.Name} — {execution.ErrorMessage}")
                : execution.Result;
        });

    /// <summary>Ilovaning O'Z qulf xizmati.</summary>
    public IJobLock Lock => Services.GetRequiredService<IJobLock>();

    /// <summary>
    /// MUSTAQIL qulf xizmati — "ikkinchi API konteyneri" ning modeli.
    ///
    /// ★ NIMA UCHUN BU HAQIQIY IKKINCHI INSTANCE BILAN TENG: advisory lock
    /// PostgreSQL SESSIYASIGA (ulanishga) bog'langan, jarayonga emas. Har
    /// <c>TryAcquireAsync</c> o'z ulanishini ochadi, ya'ni ikki obyekt
    /// Postgres uchun ikki mustaqil klient — xuddi ikki konteyner kabi.
    /// (Ikki konteynerli JONLI isbot alohida bajarilgan.)
    /// </summary>
    public IJobLock CreateIndependentLock() => new PostgresAdvisoryJobLock(
        Services.GetRequiredService<IConfiguration>()
            .GetConnectionString(DependencyInjection.PostgresConnectionName)!,
        Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<PostgresAdvisoryJobLock>());

    /// <summary>MUSTAQIL yurgizuvchi (o'z qulf xizmati bilan) — ikkinchi instance.</summary>
    public IJobRunner CreateIndependentRunner() => new JobRunner(
        CreateIndependentLock(),
        Services.GetRequiredService<TimeProvider>(),
        Services.GetRequiredService<ILoggerFactory>().CreateLogger<JobRunner>());

    /// <summary>Seed qilingan demo guruhning Id'si.</summary>
    public Task<long> SeededGroupIdAsync() =>
        WithDbAsync(db => db.Groups.OrderBy(g => g.Id).Select(g => g.Id).FirstAsync());

    /// <summary>Seed qilingan demo o'quvchining Id'si.</summary>
    public Task<long> SeededStudentIdAsync() =>
        WithDbAsync(db => db.Users
            .Where(u => u.Role == UserRole.Student)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .FirstAsync());

    private static IScheduledJob JobOf<T>(IServiceProvider sp)
        where T : IScheduledJob =>
        sp.GetServices<IScheduledJob>().OfType<T>().Single();
}

/// <summary>
/// Testlar boshqaradigan soxta vazifa: tanasi ham, nechta marta
/// chaqirilgani ham test qo'lida.
///
/// ★ NIMA UCHUN KERAK: leader lock va xatolarni ajratish
/// (<see cref="IJobRunner"/>) HAQIQIY vazifadan MUSTAQIL kafolat. Uni
/// haqiqiy vazifa orqali sinash "dars yopildimi" bilan "qulf ishladimi"
/// ni bitta testga qorishtirardi va yiqilganda sababni topish qiyin
/// bo'lardi.
/// </summary>
public sealed class FakeJob(string name, Func<CancellationToken, Task<JobRunResult>> body)
    : IScheduledJob
{
    private int _runs;

    public string Name => name;

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    /// <summary>Tana necha marta HAQIQATAN chaqirildi.</summary>
    public int Runs => Volatile.Read(ref _runs);

    public Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _runs);
        return body(ct);
    }

    /// <summary>Har chaqiruvda YIQILADIGAN vazifa.</summary>
    public static FakeJob Failing(string name) =>
        new(name, _ => throw new InvalidOperationException("Ataylab yiqilgan vazifa."));

    /// <summary>Darhol muvaffaqiyat bilan tugaydigan vazifa.</summary>
    public static FakeJob Succeeding(string name, int processed = 1) =>
        new(name, _ => Task.FromResult(new JobRunResult(processed, 0)));

    /// <summary>Test uchun takrorlanmas nom (qulf makonlari aralashmasin).</summary>
    public static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..24];
}

/// <summary>
/// Vaqtni test boshqaradigan soat.
///
/// NIMA UCHUN KERAK: "oy boshi" MAHALLIY vaqtda hisoblanadi va bu qoidani
/// haqiqiy soat bilan sinab bo'lmaydi — oy chegarasidagi 30 daqiqani
/// kutishga to'g'ri kelardi.
/// </summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private long _ticks = DateTimeOffset.UtcNow.UtcTicks;

    public void Set(DateTimeOffset utcNow) =>
        Interlocked.Exchange(ref _ticks, utcNow.UtcDateTime.Ticks);

    public override DateTimeOffset GetUtcNow() =>
        new(new DateTime(Volatile.Read(ref _ticks), DateTimeKind.Utc));
}

/// <summary>
/// Soati boshqariladigan API — oylik hisob vazifasining vaqt mintaqasi
/// xatti-harakatini sinash uchun.
/// </summary>
public sealed class BillingJobFactory : JobFactory
{
    public MutableTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        // `TimeProvider` singleton sifatida `AddApplication` da ro'yxatdan
        // o'tgan; oxirgi ro'yxat g'olib bo'ladi, lekin ikkilanish qolmasin
        // deb avvalgisi OLIB TASHLANADI.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Tarif bo'lmasa oy ochilmaydi — har testdan oldin bir marta.</summary>
    public async Task EnsureTariffAsync(decimal amount = 500_000m)
    {
        await WithDbAsync(async db =>
        {
            if (await db.Tariffs.AnyAsync()) return 0;

            db.Tariffs.Add(new Tariff
            {
                Name = "Umumiy tarif (test)",
                Amount = amount,
                LessonsCount = 8,

                // Uzoq o'tmishdan: tarif oyning BIRINCHI kuniga qarab
                // tanlanadi, ya'ni sinaladigan har qanday oyni qamrasin.
                ActiveFrom = new DateOnly(2020, 1, 1),
                IsActive = true,
            });

            return await db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// Yangi o'quvchi + yangi guruh + faol a'zolik.
    ///
    /// ★ API ORQALI EMAS, TO'G'RIDAN-TO'G'RI BAZAGA: guruh endpointi
    /// yaratish paytida butun kurs jadvalini generatsiya qiladi (o'nlab
    /// dars). Bu yerda dars kerak emas, faqat a'zolik kerak.
    /// </summary>
    public Task<(long StudentId, long GroupId)> CreateBillableStudentAsync(string prefix) =>
        WithDbAsync(async db =>
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];

            var student = new User
            {
                FullName = $"To'lovchi {suffix}",
                Email = $"{prefix}-{suffix}@zinnur.uz",
                PasswordHash = "not-used-in-this-test",
                Role = UserRole.Student,
                IsActive = true,
            };

            db.Users.Add(student);

            var group = new Group
            {
                Name = $"{prefix}-{suffix}",
                StartDate = new DateOnly(2026, 1, 5),
                IsActive = true,
            };

            db.Groups.Add(group);
            await db.SaveChangesAsync();

            db.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                StudentId = student.Id,
                Status = MemberStatus.Active,
                JoinedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();

            return (student.Id, group.Id);
        });
}
