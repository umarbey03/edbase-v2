using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Domain.Exceptions;
using Zinnur.WebApi.Observability;

namespace Zinnur.WebApi.Middleware;

/// <summary>
/// GLOBAL xato ushlagichi — tizimni yiqilishdan himoya qiladi.
///
/// Vazifasi:
///  1) Har qanday ushlanmagan istisnoni RFC 7807 ProblemDetails ga aylantiradi
///     (frontend bitta formatni kutadi).
///  2) Domain/Application istisnolarini TO'G'RI HTTP kodiga xaritalaydi —
///     shu tufayli servis qatlami HTTP haqida hech narsa bilmaydi.
///  3) Ichki tafsilotni (stack trace, SQL) TASHQARIGA CHIQARMAYDI —
///     faqat logda qoladi. Aks holda bu ma'lumot sizishi bo'lardi.
///  4) Har javobga `traceId` qo'shadi — foydalanuvchi shikoyat qilganda
///     logdan aynan o'sha so'rovni topish mumkin.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment env)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var traceId = RequestTrace.GetTraceId(context);

        var (status, title, detail) = Map(ex);

        // 5xx — kutilmagan xato, to'liq log. 4xx — kutilgan, qisqa log.
        //
        // SENTRY BILAN BOG'LIQLIK (takroriy xabar bermaslik):
        // Sentry sink'i FAQAT Error darajasini hodisa qilib yuboradi.
        // Ya'ni quyidagi shart Sentry uchun ham "filtr" vazifasini bajaradi:
        // NotFound/Forbidden/Validation (4xx) — Information => YUBORILMAYDI,
        // haqiqiy 5xx — Error => YUBORILADI. Ikkinchi joyda alohida
        // `CaptureException` chaqirilmaydi, aks holda har xato IKKI marta tushardi.
        if (status >= StatusCodes.Status500InternalServerError)
        {
            // Avval qamrovga traceId yoziladi, KEYIN log — tartib muhim,
            // aks holda hodisa tegsiz ketadi va uni traceId bo'yicha topib bo'lmaydi.
            SentrySetup.ApplyRequestScope(context, traceId);
            ApiLog.UnhandledError(logger, ex, traceId);
        }
        else
        {
            ApiLog.RequestRejected(logger, status, ex.Message, traceId);
        }

        if (context.Response.HasStarted)
        {
            // Javob allaqachon boshlangan — o'zgartira olmaymiz (masalan SSE/stream)
            ApiLog.ResponseAlreadyStarted(logger, traceId);
            return;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Ichki tafsilot faqat dev muhitida
            Detail = status >= StatusCodes.Status500InternalServerError && !env.IsDevelopment()
                ? "Serverda kutilmagan xato yuz berdi. Iltimos, keyinroq urinib ko'ring."
                : detail,
            Type = $"https://httpstatuses.io/{status.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        };
        problem.Extensions["traceId"] = traceId;

        if (ex is ValidationException validation)
            problem.Extensions["errors"] = validation.Errors;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(problem, JsonOptions))
            .ConfigureAwait(false);
    }

    /// <summary>Istisno turini HTTP holatiga xaritalaydi (yagona joy — DRY).</summary>
    private static (int Status, string Title, string Detail) Map(Exception ex) => ex switch
    {
        NotFoundException =>
            (StatusCodes.Status404NotFound, "Topilmadi", ex.Message),

        UnauthorizedException =>
            (StatusCodes.Status401Unauthorized, "Autentifikatsiya talab qilinadi", ex.Message),

        ForbiddenException =>
            (StatusCodes.Status403Forbidden, "Ruxsat yo'q", ex.Message),

        ConflictException =>
            (StatusCodes.Status409Conflict, "Amal bajarilmadi", ex.Message),

        ValidationException =>
            (StatusCodes.Status400BadRequest, "Ma'lumot noto'g'ri", ex.Message),

        // Biznes qoidasi buzilgan (Domain qatlamidan)
        DomainException =>
            (StatusCodes.Status409Conflict, "Amal bajarilmadi", ex.Message),

        OperationCanceledException =>
            (StatusCodesExtra.Status499ClientClosedRequest, "So'rov bekor qilindi", "Klient so'rovni uzdi."),

        _ => (StatusCodes.Status500InternalServerError, "Ichki server xatosi", ex.Message),
    };
}

/// <summary>Nginx'ning 499 kodi .NET'da yo'q — qo'shamiz.</summary>
internal static class StatusCodesExtra
{
    public const int Status499ClientClosedRequest = 499;
}
