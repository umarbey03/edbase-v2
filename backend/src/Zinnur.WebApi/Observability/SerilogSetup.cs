using System.Globalization;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// Serilog sozlamasi — kompozitsiya ildizidan ajratilgan.
///
/// IKKI XIL CHIQISH, ATAYLAB:
///  * Development — odam o'qiy oladigan matn. Terminalda JSON o'qish azob.
///  * Production  — <c>CompactJsonFormatter</c> (JSON). Docker stdout'ni
///    yig'adi; JSON bo'lsa <c>docker logs | jq</c>, Loki, ELK yoki
///    <c>grep</c> bilan MAYDON bo'yicha qidirish mumkin:
///        docker logs zinnur-v2-api | jq 'select(.TraceId=="00-abc...")'
///    Oddiy matnda bunday qidiruv imkonsiz — eski tizimning aynan shu
///    kamchiligi tufayli nosozlikni topish soatlab vaqt olardi.
///
/// HAR YOZUVDA BO'LADIGAN MAYDONLAR:
///   TraceId, UserId (autentifikatsiya bo'lsa) — <see cref="RequestContextEnricher"/>,
///   Application, Environment, Version — quyidagi doimiy maydonlar.
/// </summary>
internal static class SerilogSetup
{
    /// <summary>Dev uchun qisqa, o'qiladigan shablon.</summary>
    private const string DevelopmentTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// <c>UseSerilog</c> uchun sozlagich. <see cref="IServiceProvider"/> li
    /// versiya ATAYLAB: boyituvchiga <see cref="IHttpContextAccessor"/> kerak.
    /// </summary>
    public static void Configure(
        HostBuilderContext context,
        IServiceProvider services,
        LoggerConfiguration configuration)
    {
        var environment = context.HostingEnvironment;

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.With(new RequestContextEnricher(services.GetRequiredService<IHttpContextAccessor>()))
            .Enrich.WithProperty("Application", AppInfo.ServiceName)
            .Enrich.WithProperty("Environment", environment.EnvironmentName)
            .Enrich.WithProperty("Version", AppInfo.Version);

        if (environment.IsDevelopment())
        {
            configuration.WriteTo.Console(
                outputTemplate: DevelopmentTemplate,
                formatProvider: CultureInfo.InvariantCulture);
        }
        else
        {
            // CompactJsonFormatter — har satr bitta JSON obyekt (CLEF formati).
            configuration.WriteTo.Console(new CompactJsonFormatter());
        }

        if (!SentrySetup.IsEnabled(context.Configuration))
            return;

        configuration.WriteTo.Sentry(sentry =>
        {
            // SDK allaqachon UseSentry() bilan ishga tushirilgan. Bu yerda
            // qayta ishga tushirsak, sozlamalar (scrubber!) yo'qoladi.
            sentry.InitializeSdk = false;

            // FAQAT Error va undan yuqorisi hodisa bo'lib ketadi.
            // Kutilgan 4xx lar Information darajasida yoziladi => yuborilmaydi.
            sentry.MinimumEventLevel = LogEventLevel.Error;

            // Pastroq darajadagi yozuvlar "breadcrumb" bo'lib qoladi: xato
            // hodisasi bilan birga UNDAN OLDIN nima bo'lganini ko'rsatadi.
            sentry.MinimumBreadcrumbLevel = LogEventLevel.Information;
        });
    }
}
