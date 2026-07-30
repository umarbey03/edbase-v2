using System.Globalization;
using System.Security.Claims;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// Sentry (xato kuzatuvi) — TO'LIQ IXTIYORIY integratsiya.
///
/// ENG MUHIM SHART: <c>Sentry:Dsn</c> bo'sh yoki umuman berilmagan bo'lsa,
/// ilova ODATDAGIDEK ishga tushadi va ishlaydi. Ishlab chiquvchi mashinasida
/// DSN bo'lmaydi — bunda hech qanday xato, ogohlantirish yoki sekinlashuv
/// bo'lmasligi kerak. Shuning uchun DSN bo'lmasa SDK UMUMAN ishga
/// tushirilmaydi: <c>SentrySdk.*</c> chaqiruvlari o'chirilgan hub'ga
/// tushib, tekinga qaytadi.
///
/// TAKROR XABAR BERMASLIK (double reporting):
/// hodisalar Sentry'ga FAQAT bitta yo'l bilan — Serilog sink'i orqali —
/// ketadi (<see cref="SerilogSetup"/>). Sink'ning eng past darajasi
/// <c>Error</c>. <see cref="Middleware.ExceptionHandlingMiddleware"/>
/// esa 4xx (NotFound/Forbidden/Validation) ni <c>Information</c> darajasida,
/// 5xx ni <c>Error</c> darajasida yozadi. Natijada kutilgan 4xx Sentry'ga
/// TUSHMAYDI, haqiqiy 5xx esa tushadi.
/// </summary>
internal static class SentrySetup
{
    public const string SectionName = "Sentry";

    /// <summary>
    /// Standart tracing namunasi: so'rovlarning 10%. Past qiymat ATAYLAB —
    /// tracing har so'rovda qo'shimcha yuk va Sentry kvotasini yeydi,
    /// 200 foydalanuvchi uchun 10% muammoni ko'rishga yetarli.
    /// </summary>
    private const double DefaultTracesSampleRate = 0.1;

    /// <summary>DSN berilganmi — ya'ni Sentry yoqilganmi.</summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(Dsn(configuration));

    public static void AddZinnurSentry(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var dsn = Dsn(configuration);

        // DSN yo'q -> Sentry butunlay o'chirilgan. Bu NORMAL holat (dev mashinasi).
        if (string.IsNullOrWhiteSpace(dsn))
            return;

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = dsn;

            // Muhit nomi: aniq berilmasa ASPNETCORE_ENVIRONMENT ishlatiladi —
            // shunda Sentry'da "Production"/"Staging" filtri to'g'ri ishlaydi.
            var environment = configuration[$"{SectionName}:Environment"];
            options.Environment = string.IsNullOrWhiteSpace(environment)
                ? builder.Environment.EnvironmentName
                : environment;

            // Reliz: frontend AYNAN shu nomni ishlatadi (zinnur@2.0.0) —
            // shunda backend va brauzer xatolari bitta relizga birlashadi.
            options.Release = AppInfo.Release;

            options.TracesSampleRate = ParseSampleRate(configuration[$"{SectionName}:TracesSampleRate"]);

            // PII (email, IP, foydalanuvchi nomi) YUBORILMAYDI.
            options.SendDefaultPii = false;

            // So'rov tanasi hech qachon o'qilmaydi (parol/telefon bo'lishi mumkin).
            options.MaxRequestBodySize = RequestSize.None;

            // Hodisalar FAQAT Serilog sink'idan keladi. Bu qator ILoggerFactory
            // orqali kelishi mumkin bo'lgan IKKINCHI oqimni yopadi.
            options.MinimumEventLevel = LogLevel.None;
            options.MinimumBreadcrumbLevel = LogLevel.Information;

            options.AttachStacktrace = true;

            // MAJBURIY oxirgi to'siq: har hodisa yuborilishdan oldin tozalanadi.
            options.SetBeforeSend(SentryScrubber.Scrub);
        });
    }

    /// <summary>
    /// Joriy so'rovning <c>traceId</c> sini (va foydalanuvchi id'sini) Sentry
    /// qamroviga (scope) yozadi.
    ///
    /// NIMA UCHUN KERAK: foydalanuvchi shikoyat qilganda bizga faqat ekranda
    /// ko'rsatilgan <c>traceId</c> beriladi. Sentry'da uni <c>traceId:"..."</c>
    /// deb qidirib, aynan o'sha so'rovni topamiz.
    /// </summary>
    public static void ApplyRequestScope(HttpContext context, string traceId)
    {
        // Sentry o'chirilgan bo'lsa — arzon chiqish (delegat ham yaratilmaydi).
        if (!SentrySdk.IsEnabled)
            return;

        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag(RequestTrace.TagName, traceId);

            // FAQAT raqamli id. Ism/email/IP — yo'q (SendDefaultPii = false).
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
                scope.User.Id = userId;
        });
    }

    private static string? Dsn(IConfiguration configuration) =>
        configuration[$"{SectionName}:Dsn"];

    /// <summary>
    /// Qiymatni QO'LDA, <see cref="CultureInfo.InvariantCulture"/> bilan o'qiymiz.
    /// Sabab: bo'sh satr (<c>Sentry__TracesSampleRate=</c>) konfiguratsiya
    /// bog'lagichida (binder) istisno tashlaydi va ilova ko'tarilmay qoladi.
    /// Bu yerda bo'sh/noto'g'ri qiymat shunchaki standartga tushadi.
    /// </summary>
    private static double ParseSampleRate(string? raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            return DefaultTracesSampleRate;

        return rate is >= 0 and <= 1 ? rate : DefaultTracesSampleRate;
    }
}
