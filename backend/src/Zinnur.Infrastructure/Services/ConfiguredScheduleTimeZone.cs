using Microsoft.Extensions.Options;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IScheduleTimeZoneProvider"/> — zonani <c>App:TimeZone</c> dan oladi.
///
/// Zona BIR MARTA (konstruktor'da) topiladi va keshlanadi:
/// <c>FindSystemTimeZoneById</c> diskdan zona faylini o'qiydi, jadval
/// generatsiyasi esa bir chaqiruvda yuzlab dars uchun konvertatsiya qiladi.
///
/// Konfiguratsiya xato bo'lsa ilova UMUMAN KO'TARILMAYDI — tekshiruv
/// <c>AddInfrastructure</c> ichida <c>ValidateOnStart()</c> bilan qo'yilgan.
/// Aks holda xato faqat birinchi guruh yaratilganda 500 bo'lib chiqardi.
/// </summary>
public sealed class ConfiguredScheduleTimeZone : IScheduleTimeZoneProvider
{
    public ConfiguredScheduleTimeZone(IOptions<AppOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var id = options.Value.TimeZone;

        if (!AppOptions.TryResolve(id, out var timeZone))
        {
            throw new InvalidOperationException(
                $"App:TimeZone ('{id}') topilmadi. IANA identifikatori kutiladi "
                + $"(masalan '{AppOptions.DefaultTimeZone}'). Konteynerda `tzdata` "
                + "paketi o'rnatilganini tekshiring.");
        }

        TimeZone = timeZone;
    }

    /// <inheritdoc />
    public TimeZoneInfo TimeZone { get; }
}
