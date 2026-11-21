using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Jobs;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Progress.Services;

public interface IStreakReminderService
{
    /// <returns>Navbatga qo'yilgan eslatmalar soni.</returns>
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// KECHKI SERIYA ESLATMASI (2026-11-21)
/// ════════════════════════════════════════════════════════════════════
///
/// Mustaqil o'quvchi ketma-ket kamida <see cref="MinStreak"/> kun o'qigan,
/// bugun esa hali hech narsa qilmagan bo'lsa — kechqurun BITTA Telegram
/// xabari. Kuniga bittadan ko'p emas (<c>IdempotencyKey</c> = o'quvchi + sana).
///
/// ★ NEGA 1 KUNLIK SERIYAGA EMAS: bir kun o'qigan odamga har kuni xabar
///   yuborish spamga aylanadi; 2 kundan keyin esa yo'qotadigan narsasi bor.
/// ★ OBUNASI TUGAGANGA YUBORILMAYDI — u darsni ocholmaydi, xabar faqat
///   asabiylashtiradi.
/// </summary>
public sealed class StreakReminderService(
    IApplicationDbContext db,
    IDailyStreakService streaks,
    ISettingsResolver settings,
    INotificationOutbox outbox,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IStreakReminderService
{
    public const int MinStreak = 2;
    public const int FromHour = 19;
    public const int UntilHour = 22;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, timeZone.TimeZone);
        if (local.Hour is < FromHour or >= UntilHour) return 0;

        var resolved = await settings.ResolveManyAsync(
            [Definition(SettingsRegistry.Keys.PointsEnabled), Definition(SettingsRegistry.Keys.StreakReminderEnabled)],
            ct).ConfigureAwait(false);
        if (!IsOn(resolved[0].Value) || !IsOn(resolved[1].Value)) return 0;

        var students = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.CancelledAt == null && p.StartsAt <= now && p.EndsAt > now
                     && p.Student!.IsActive && p.Student.Role == UserRole.Student && p.Student.TelegramId != null)
            .Select(p => new { p.StudentId, p.Student!.FullName, TelegramId = p.Student.TelegramId!.Value })
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (students.Count == 0) return 0;

        var byId = students.GroupBy(s => s.StudentId).ToDictionary(g => g.Key, g => g.First());
        var days = await streaks.ActiveDaysAsync([.. byId.Keys], now - DailyStreakService.History, ct)
            .ConfigureAwait(false);
        var today = streaks.Today();
        var stamp = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var queued = 0;

        foreach (var (studentId, active) in days)
        {
            var result = DailyStreak.Compute(active, today);
            if (result.DoneToday || result.Current < MinStreak) continue;

            var student = byId[studentId];
            var (nextDay, nextBonus) = DailyStreak.NextBonus(result.Current);

            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = studentId,
                    RecipientAddress = student.TelegramId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.StreakReminder,
                    Body = TelegramTemplates.StreakReminderText(student.FullName, result.Current, nextDay, nextBonus),
                    IdempotencyKey = $"streak_reminder:{studentId}:{stamp}",
                },
                ct).ConfigureAwait(false);
            queued++;
        }

        return queued;
    }

    private static bool IsOn(string? value) => SettingValueParser.TryReadBool(value, out var on) && on;

    private static SettingDefinition Definition(string key) =>
        SettingsRegistry.TryGet(key, out var definition)
            ? definition
            : throw new InvalidOperationException($"Sozlama registrda yo'q: {key}");
}

/// <summary>Kechki seriya eslatmasi — har 15 daqiqada; vaqt oynasini servis o'zi tekshiradi.</summary>
public sealed class StreakReminderJob(IStreakReminderService reminders) : IScheduledJob
{
    public string Name => "streak-reminder";

    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var queued = await reminders.RunAsync(ct).ConfigureAwait(false);
        return queued == 0
            ? JobRunResult.Nothing
            : new JobRunResult(Processed: queued, Skipped: 0, Note: "seriya eslatmasi");
    }
}
