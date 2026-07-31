using Microsoft.Extensions.Configuration;
using Zinnur.Application.Settings;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ISettingsEnvironment"/> port'ining amalga oshirilishi —
/// <see cref="IConfiguration"/> ustida.
///
/// ★ NIMA UCHUN PORT KERAK EDI: <see cref="IConfiguration"/> — ASP.NET
/// dunyosidagi tur, Application qatlami esa uni bilmasligi kerak
/// (bog'liqlik faqat ichkariga). Registrga esa "shu kalitning muhitdagi
/// qiymati nima?" degan savol kerak.
///
/// ★ MUHIM: <see cref="IConfiguration"/> allaqachon TO'G'RI USTUNLIKNI
/// hisoblab beradi — muhit o'zgaruvchisi <c>appsettings.json</c> dan ustun,
/// <c>appsettings.Development.json</c> esa asosiy fayldan ustun. Ya'ni bu
/// yerda o'sha tartibni QAYTA yozish shart emas va yozilmasligi ham kerak:
/// ikkinchi nusxa birinchisidan chetga chiqib ketardi.
/// </summary>
public sealed class ConfigurationSettingsEnvironment(IConfiguration configuration)
    : ISettingsEnvironment
{
    public string? Read(string configurationKey)
    {
        var value = configuration[configurationKey];

        // Bo'sh satr = "sozlanmagan". docker-compose'da `Storage__Bucket=`
        // ko'rinishidagi bo'sh o'zgaruvchi juda ko'p uchraydi va uni
        // "bo'sh qiymat o'rnatilgan" deb tushunish xato bo'lardi: registr
        // standartga qaytishi kerak.
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
