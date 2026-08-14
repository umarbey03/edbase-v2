using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Jobs;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Application.Media;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Settings.Services;
using Zinnur.Infrastructure;
using Zinnur.Infrastructure.Services;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Fon vazifalari modulini DI'ga ulaydi (FAZA 5.5).
///
/// ★ NIMA UCHUN <c>Program.cs</c> DA EMAS: kompozitsiya ildizi allaqachon
/// uzun va bu modul beshta ro'yxatdan o'tkazishni talab qiladi. Bitta
/// kengaytma metodi (<c>AddZinnurNotifications</c> bilan bir xil uslub)
/// <c>Program.cs</c> ga bitta qator qo'shadi.
/// </summary>
internal static class JobsSetup
{
    public static IServiceCollection AddZinnurJobs(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = JobsOptions.Read(configuration);

        services.AddSingleton(options);

        // ---------------------------------------------------------------- qulf
        //
        // SINGLETON va o'z ulanishini O'ZI ochadi — EF ning ulanish hovuzi
        // BILAN ISHLAMAYDI (sabab: `PostgresAdvisoryJobLock` izohi).
        //
        // Ro'yxat Infrastructure'ning `AddInfrastructure` ida emas, shu
        // yerda: modul o'z bog'liqliklarini o'zi ulaydi (notifikatsiya
        // moduli `RedisMessageRateLimiter` ni aynan shunday ulaydi).
        var connectionString = configuration
            .GetConnectionString(DependencyInjection.PostgresConnectionName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"'ConnectionStrings:{DependencyInjection.PostgresConnectionName}' sozlanmagan — "
                + "fon vazifalari qulfi baza ulanishisiz ishlay olmaydi.");
        }

        services.AddSingleton<IJobLock>(sp => new PostgresAdvisoryJobLock(
            connectionString,
            sp.GetRequiredService<ILogger<PostgresAdvisoryJobLock>>()));

        // Yurgizuvchi HOLATSIZ va faqat singleton'larga tayanadi
        // (`IJobLock`, `TimeProvider`, `ILogger`) — shuning uchun o'zi ham
        // SINGLETON. Scoped bo'lsa hech narsa yutilmasdi, lekin "captive
        // dependency" savolini har safar qaytadan tekshirish kerak bo'lardi.
        services.AddSingleton<IJobRunner, JobRunner>();

        // ---------------------------------------------------------------- vazifalar
        //
        // SCOPED — ikkalasi ham `DbContext` ga (port orqali) tayanadi.
        // Rejalashtiruvchi HAR aylanishda yangi scope ochadi: aks holda
        // bitta `DbContext` ulanishni ilova umri davomida ushlab turardi va
        // o'zgarish kuzatuvchisi cheksiz o'sardi.
        //
        // Sozlamalar konstruktorga QIYMAT sifatida uzatiladi: Application
        // qatlami konfiguratsiya tizimini bilmaydi (izoh:
        // `SessionAutoCloseSettings`).
        if (options.SessionAutoCloseEnabled)
        {
            services.AddScoped<IScheduledJob>(sp => new SessionAutoCloseJob(
                sp.GetRequiredService<IApplicationDbContext>(),
                sp.GetRequiredService<ILiveSessionService>(),
                sp.GetRequiredService<TimeProvider>(),
                options.SessionAutoClose,
                sp.GetRequiredService<ILogger<SessionAutoCloseJob>>()));
        }

        if (options.MonthlyBillingEnabled)
        {
            services.AddScoped<IScheduledJob>(sp => new MonthlyBillingJob(
                sp.GetRequiredService<IApplicationDbContext>(),
                sp.GetRequiredService<IPaymentService>(),
                options.MonthlyBilling,
                sp.GetRequiredService<ILogger<MonthlyBillingJob>>()));
        }

        // ================================================================
        // 🔴 CHAT TARIXINI TOZALASH — SHARTSIZ RO'YXATDAN O'TADI.
        //
        // Yuqoridagi ikki vazifa MUHIT bayrog'i ostida turibdi, bu esa
        // ATAYLAB turmaydi. Sabab: uni yoqish/o'chirish administratorning
        // paneldagi qarori (`chat.retention_enabled`) va vazifa uni HAR
        // YURISHDA, `RunAsync` ICHIDA o'qiydi. Agar bu yerda ham `if`
        // bo'lsa, ikki xil "o'chiq" holat paydo bo'lardi va panelda
        // "yoqilgan" ko'rinib turgan sozlama HECH QACHON ishlamasligi
        // mumkin edi — muhit bayrog'i vazifani DI'ga umuman qo'shmagani
        // uchun. Bu eng yomon turdagi xato: jimgina yolg'on.
        //
        // Ro'yxatda turgan, lekin sozlamada o'chiq vazifa ARZON: u har
        // yurishda bitta sozlama so'rovi qiladi va darhol chiqadi.
        // ================================================================
        // ⚠️ `IMediaStorage` — R16b BIRIKTIRMALARI uchun: vazifa xabar
        //    qatorlarini o'chirishdan OLDIN ularning ombordagi obyektlarini
        //    o'chiradi, aks holda R2'da pul turadigan YETIM obyektlar
        //    to'planib borardi (sabab `ChatRetentionJob` izohida).
        services.AddScoped<IScheduledJob>(sp => new ChatRetentionJob(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<ISettingsResolver>(),
            sp.GetRequiredService<IMediaStorage>(),
            sp.GetRequiredService<TimeProvider>(),
            options.ChatRetention,
            sp.GetRequiredService<ILogger<ChatRetentionJob>>()));

        // Rejalashtiruvchini O'CHIRIB QO'YISH mumkin (`Jobs:Enabled=false`):
        // vazifalar DI'da qolaveradi va testlar ularni O'ZI chaqiradi —
        // fon xizmatining uyqusini kutmasdan, natijani darhol tekshiradi.
        if (options.Enabled)
            services.AddHostedService<JobSchedulerWorker>();

        return services;
    }
}
