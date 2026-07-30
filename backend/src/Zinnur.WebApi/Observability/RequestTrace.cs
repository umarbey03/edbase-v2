using System.Diagnostics;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// So'rovning YAGONA <c>traceId</c> qiymati.
///
/// NIMA UCHUN: foydalanuvchi "xato chiqdi" deganda unga ekranda ko'rsatilgan
/// <c>traceId</c> ni so'raymiz. O'sha bitta satr bo'yicha HAM logdan, HAM
/// Sentry'dan aynan o'sha so'rov topilishi kerak. Agar har joyda alohida
/// hisoblansak (<c>Activity.Current</c> pipeline'ning turli nuqtalarida
/// o'zgarishi mumkin), qiymatlar mos kelmay qoladi va izlash mumkin bo'lmaydi.
///
/// Shuning uchun qiymat BIR MARTA hisoblanadi va <see cref="HttpContext.Items"/>
/// da saqlanadi — kim birinchi so'rasa ham natija bir xil.
/// </summary>
internal static class RequestTrace
{
    private const string ItemKey = "zinnur.traceId";

    /// <summary>Log maydonining nomi (Serilog + Sentry teg nomi bilan bir xil).</summary>
    public const string PropertyName = "TraceId";

    /// <summary>Sentry'dagi teg nomi — u yerda qidiruv <c>traceId:...</c> ko'rinishida.</summary>
    public const string TagName = "traceId";

    public static string GetTraceId(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var existing) && existing is string cached)
            return cached;

        // Activity.Current — W3C trace-context (traceparent header). Reverse-proxy
        // yoki klient bergan bo'lsa, xizmatlararo ham bir xil bo'ladi.
        // Bo'lmasa Kestrel bergan so'rov identifikatori ishlatiladi.
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Items[ItemKey] = traceId;
        return traceId;
    }
}
