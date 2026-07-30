using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// <c>/health/ready</c> javobini JSON qilib yozadi.
///
/// NIMA UCHUN STANDART JAVOB YETARLI EMAS: sukut bo'yicha endpoint faqat
/// <c>Healthy</c> degan BITTA so'z qaytaradi. Nosozlikda esa operatorga
/// darhol "QAYSI bog'liqlik va QANCHA vaqtda" degan javob kerak. Aks holda
/// har safar konteynerlarga kirib qo'lda tekshirish kerak bo'ladi.
///
/// Javob shakli:
/// <code>
/// {"status":"Healthy","totalDurationMs":12,
///  "checks":[{"name":"postgres","status":"Healthy","durationMs":4}]}
/// </code>
///
/// XAVFSIZLIK: ataylab FAQAT nom, holat va davomiylik chiqariladi.
/// Istisno matni yoki tavsif QO'SHILMAYDI — Npgsql xatosi ichida ulanish
/// satri (host, foydalanuvchi, ba'zan parol) bo'lishi mumkin, endpoint esa
/// autentifikatsiyasiz ochiq. Sabab logda va Sentry'da qoladi.
/// </summary>
internal static class HealthCheckResponse
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var checks = new List<HealthCheckEntryDto>(report.Entries.Count);

        foreach (var (name, entry) in report.Entries)
        {
            checks.Add(new HealthCheckEntryDto(
                name,
                entry.Status.ToString(),
                ToMilliseconds(entry.Duration)));
        }

        var payload = new HealthReportDto(
            report.Status.ToString(),
            ToMilliseconds(report.TotalDuration),
            checks);

        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static long ToMilliseconds(TimeSpan duration) => (long)duration.TotalMilliseconds;
}

/// <summary>Umumiy sog'liq hisoboti (SPEC 7: qo'lda `dict` emas, DTO).</summary>
internal sealed record HealthReportDto(
    string Status,
    long TotalDurationMs,
    IReadOnlyList<HealthCheckEntryDto> Checks);

/// <summary>Bitta bog'liqlik holati.</summary>
internal sealed record HealthCheckEntryDto(
    string Name,
    string Status,
    long DurationMs);
