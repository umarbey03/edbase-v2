using System.Security.Claims;
using Serilog.Core;
using Serilog.Events;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// Har LOG YOZUVIGA joriy so'rovning <c>TraceId</c> va <c>UserId</c> maydonlarini qo'shadi.
///
/// NIMA UCHUN MIDDLEWARE EMAS, BOYITUVCHI (enricher):
/// middleware <c>LogContext.PushProperty</c> qilsa, maydon faqat SHU
/// middleware ichida yozilgan loglarga tushadi. Ammo <c>userId</c>
/// autentifikatsiyadan KEYIN ma'lum bo'ladi, <c>UseSerilogRequestLogging</c>
/// ning yakuniy yozuvi esa undan TASHQARIDA yoziladi — natijada eng muhim
/// yozuvda aynan <c>userId</c> bo'lmasdi.
///
/// Boyituvchi esa yozuv YARATILAYOTGAN paytda ishlaydi va
/// <see cref="IHttpContextAccessor"/> orqali o'sha lahzadagi holatni oladi —
/// pipeline tartibiga umuman bog'liq emas.
/// </summary>
internal sealed class RequestContextEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    /// <summary>Log maydonining nomi (SPEC: `userId`).</summary>
    public const string UserPropertyName = "UserId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = accessor.HttpContext;

        // Fon xizmatlari (ChatMessageWriter) va startup loglarida so'rov yo'q —
        // bu normal holat, shunchaki qo'shadigan narsa bo'lmaydi.
        if (context is null)
            return;

        Add(logEvent, propertyFactory, RequestTrace.PropertyName, RequestTrace.GetTraceId(context));

        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
            Add(logEvent, propertyFactory, UserPropertyName, userId);
    }

    private static void Add(
        LogEvent logEvent,
        ILogEventPropertyFactory propertyFactory,
        string name,
        string value) =>
        // AddPropertyIfAbsent: agar kod o'zi shu nomli maydon bergan bo'lsa,
        // uni ustidan yozmaymiz.
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(name, value));
}
