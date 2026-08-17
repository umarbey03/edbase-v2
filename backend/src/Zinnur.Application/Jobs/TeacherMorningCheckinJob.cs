using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.TeacherAvailability.Services;

namespace Zinnur.Application.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ERTALABKI "DARSGA O'TA OLASIZMI?" SAVOLI (2026-08-17)
/// ════════════════════════════════════════════════════════════════════════
///
/// Har 15 daqiqada tekshiradi: joriy MAHALLIY vaqt <see cref="AskWindowStart"/>
/// va <see cref="AskWindowEnd"/> orasidami. Shu oynada
/// <see cref="ITeacherAvailabilityService.RequestConfirmationsAsync"/>
/// chaqiriladi — u o'zi allaqachon so'ralgan (yoki ko'p-kunlik oyna bilan
/// qamrab olingan) ustozlarni O'TKAZIB YUBORADI, ya'ni bu vazifa oyna
/// ICHIDA necha marta yursa ham xavfsiz (idempotentlik xizmat qatlamida).
///
/// ★ NIMA UCHUN OYNA (BITTA ANIQ VAQT EMAS): vazifa 15 daqiqalik intervalda
/// yuradi va aynan 07:00 da ishga tushishi KAFOLATLANMAYDI (server band
/// bo'lishi, oldingi yurish davom etayotgan bo'lishi mumkin). Bir soatlik
/// oyna — bironta tik davr albatta shu ichiga tushishi uchun yetarli zaxira.
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TeacherMorningCheckinJob(
    ITeacherAvailabilityService availability,
    IScheduleTimeZoneProvider timeZoneProvider,
    TimeProvider clock,
    TeacherMorningCheckinSettings settings) : IScheduledJob
{
    /// <summary>Mahalliy vaqtda savol yuborish oynasi boshlanishi (soat, 24-soatlik).</summary>
    private const int AskWindowStart = 7;

    /// <summary>Mahalliy vaqtda savol yuborish oynasi tugashi (soat, 24-soatlik, ochiq chegara).</summary>
    private const int AskWindowEnd = 8;

    /// <inheritdoc />
    public string Name => "teacher-morning-checkin";

    /// <inheritdoc />
    public TimeSpan Interval => settings.Interval;

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var localHour = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZoneProvider.TimeZone).Hour;

        if (localHour < AskWindowStart || localHour >= AskWindowEnd)
            return JobRunResult.Nothing;

        var sent = await availability.RequestConfirmationsAsync(ct).ConfigureAwait(false);

        return sent == 0 ? JobRunResult.Nothing : new JobRunResult(Processed: sent, Skipped: 0, Note: null);
    }
}

/// <summary>Ertalabki tasdiqlash vazifasining sozlamasi.</summary>
/// <param name="Interval">Ikki yurish orasidagi masofa.</param>
public sealed record TeacherMorningCheckinSettings(TimeSpan Interval);
