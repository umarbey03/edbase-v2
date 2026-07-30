using System.Reflection;

namespace Zinnur.WebApi.Observability;

/// <summary>
/// Ilova haqidagi O'ZGARMAS ma'lumot: nom va versiya.
///
/// NIMA UCHUN ALOHIDA JOY: bu qiymatlar UCH joyda kerak bo'ladi —
/// Serilog boyituvchisida (<c>Version</c> maydoni), Sentry reliz (release)
/// nomida va frontend bilan solishtirishda. Uch joyda uch xil yozilsa,
/// xatolar Sentry'da turli relizlarga bo'linib ketadi va "qaysi versiyada
/// buzildi?" degan savolga javob topib bo'lmaydi.
/// </summary>
internal static class AppInfo
{
    /// <summary>Loglardagi <c>Application</c> maydoni (bir nechta xizmat bo'lsa ajratadi).</summary>
    public const string ServiceName = "zinnur-api";

    /// <summary>Yig'ilma (assembly) versiyasi, masalan <c>2.0.0</c>.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// Sentry reliz nomi. Frontend AYNAN shu formatni ishlatadi
    /// (<c>zinnur@2.0.0</c>) — shunda bitta relizning backend va brauzer
    /// xatolari Sentry'da bir joyda ko'rinadi.
    /// </summary>
    public static string Release { get; } = $"zinnur@{Version}";

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        // "2.0.0+9a1b2c3" ko'rinishidagi commit hash'ni kesib tashlaymiz:
        // reliz nomi har commitda o'zgarib ketmasin.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}
