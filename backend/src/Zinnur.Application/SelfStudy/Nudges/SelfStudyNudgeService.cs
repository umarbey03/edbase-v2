using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Progress.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Application.Subscriptions;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.SelfStudy.Nudges;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// MUSTAQIL O'QUVCHINI YO'QOTMASLIK — FAOLSIZLIK ESLATMALARI (2026-11-25)
/// ════════════════════════════════════════════════════════════════════════
///
/// Mustaqil tarifda dars jadvali ham, ustoz ham yo'q: o'quvchi to'xtab qolsa,
/// buni hech kim sezmaydi va bir oydan keyin u shunchaki to'lovni
/// yangilamaydi. Bu servis kechqurun (panel: <c>selfstudy.nudge_hour</c>)
/// har Mustaqil o'quvchiga qarab chiqadi va zanjirning keyingi bosqichini
/// yuboradi (qoida — <see cref="NudgeLadder"/>, matnlar —
/// <see cref="NudgeTemplates"/>).
///
/// KIMGA YUBORILMAYDI:
///   • guruhdagi (Milliy) o'quvchiga — uning ustozi va jadvali bor;
///   • ishini topshirib, KURATOR qarorini kutayotganga — u to'xtamagan,
///     navbat kuratorda (bunday xabar adolatsiz va asabiylashtiradi);
///   • kursni tugatganga;
///   • eslatmalarni o'chirganga (kuratorga xabar baribir ketadi).
/// </summary>
public interface ISelfStudyNudgeService
{
    /// <summary>Rejali yurish: yuborilgan eslatmalar soni.</summary>
    Task<int> RunAsync(CancellationToken ct = default);

    /// <summary>Telegram tugmasi (<c>nd:</c>). Bizga tegishli bo'lmasa — <c>null</c>.</summary>
    Task<string?> HandleCallbackAsync(long telegramUserId, string? data, CancellationToken ct = default);

    /// <summary>Kurator ro'yxati uchun: oxirgi faollik va oxirgi tugma javobi.</summary>
    Task<IReadOnlyDictionary<long, StudentEngagementDto>> GetEngagementAsync(
        IReadOnlyCollection<long> studentIds, CancellationToken ct = default);

    /// <summary>Administrator: qaysi bosqich/matn o'quvchini qaytaryapti.</summary>
    Task<NudgeStatsDto> GetStatsAsync(int days, long actorId, CancellationToken ct = default);
}

public sealed class SelfStudyNudgeService(
    IApplicationDbContext db,
    IStudentActivityReader activity,
    IGatingService gating,
    ISubscriptionAccess subscriptions,
    ISettingsResolver settings,
    IDailyStreakService streaks,
    INotificationOutbox outbox,
    INotificationNotifier notifier,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : ISelfStudyNudgeService
{
    /// <summary>Yuborish oynasi uzunligi (soat): vazifa 15 daqiqada yuradi, bitta aniq daqiqa kafolatlanmaydi.</summary>
    public const int SendWindowHours = 2;

    /// <summary>«3 kundan keyin eslating».</summary>
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(3);

    /// <summary>«Vaqtim yo'q» — kichikroq tanaffus.</summary>
    public static readonly TimeSpan BusySnoozeDuration = TimeSpan.FromDays(2);

    /// <summary>«Qaytdimi» o'lchovi oynasi.</summary>
    public static readonly TimeSpan ReturnWindow = TimeSpan.FromHours(48);

    /// <summary>Bir yurishda ko'pi bilan nechta xabar (Telegram tezlik chegarasi va xatoda zarar chegarasi).</summary>
    private const int MaxPerRun = 500;

    private static readonly SettingDefinition EnabledSetting = Definition(SettingsRegistry.Keys.SelfStudyNudgesEnabled);
    private static readonly SettingDefinition HourSetting = Definition(SettingsRegistry.Keys.SelfStudyNudgeHour);
    private static readonly SettingDefinition PointsSetting = Definition(SettingsRegistry.Keys.PointsEnabled);
    private static readonly SettingDefinition StreakReminderSetting = Definition(SettingsRegistry.Keys.StreakReminderEnabled);

    private readonly NudgeThresholds thresholds = NudgeThresholds.Default;

    // ================================================================ rejali yurish

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var resolved = await settings
            .ResolveManyAsync([EnabledSetting, HourSetting, PointsSetting, StreakReminderSetting], ct)
            .ConfigureAwait(false);

        if (!(SettingValueParser.TryReadBool(resolved[0].Value, out var enabled) && enabled))
            return 0;

        var hour = SettingValueParser.TryReadDecimal(HourSetting, resolved[1].Value, out var h)
            ? (int)Math.Clamp(h, 8m, 20m)
            : 19;

        var zone = timeZone.TimeZone;
        var now = clock.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, zone);

        if (localNow.Hour < hour || localNow.Hour >= hour + SendWindowHours) return 0;

        var students = await LoadCandidatesAsync(now, ct).ConfigureAwait(false);
        if (students.Count == 0) return 0;

        var ids = students.Keys.ToList();
        var lastActivity = await activity.LastActivityAsync(ids, ct).ConfigureAwait(false);

        var since = now.AddDays(-(thresholds.MaxIdleDays + thresholds.Final + 7));
        var history = await db.SelfStudyNudges.AsNoTracking()
            .Where(n => ids.Contains(n.StudentId) && n.SentAt >= since)
            .Select(n => new { n.StudentId, n.Stage, n.AnchorAt, n.SentAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var byStudent = history.ToLookup(n => n.StudentId);

        var prefs = await db.SelfStudyNudgePreferences.AsTracking()
            .Where(p => ids.Contains(p.StudentId))
            .ToDictionaryAsync(p => p.StudentId, ct)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(localNow.DateTime);
        var sent = 0;

        // ★ SERIYA ESLATMASI BILAN KELISHUV (2026-11-25): `StreakReminderService`
        //   AYNI kechada «seriyangiz kuymasin» yuboradi (seriya ≥ 2, bugun
        //   o'qilmagan, Telegram bor). Shunday o'quvchiga bizning o'quvchi
        //   xabarimiz O'SHA KUNI ketmaydi — bir kechada ikki xabar spam bo'lardi.
        //   Kuratorga ogohlantirish esa ketaveradi (u o'quvchiga bormaydi).
        var streakCovered = await StreakReminderCoveredAsync(students.Values, resolved, now, today, ct)
            .ConfigureAwait(false);

        foreach (var student in students.Values)
        {
            if (sent >= MaxPerRun) break;

            var anchor = lastActivity.TryGetValue(student.Id, out var last) && last > student.SubscriptionStart
                ? last
                : student.SubscriptionStart;

            var mine = byStudent[student.Id].ToList();
            prefs.TryGetValue(student.Id, out var pref);

            var gotStudentMessageToday = streakCovered.Contains(student.Id) || mine.Any(n =>
                NudgeLadder.IsStudentFacing(n.Stage)
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(n.SentAt, zone).DateTime) == today);

            // «Keyinroq eslating» muddati: o'quvchi o'zi qaytgan bo'lsa — jimgina
            // bekor; tugagan bo'lsa — bitta «qaytdik» eslatmasi.
            NudgeStage? stage = null;
            var stageAnchor = anchor;
            var snoozed = false;

            if (pref?.SnoozedUntil is { } until)
            {
                // ★ «Qaytdimi» — snooze QO'YILGAN paytdan (u `UpdatedAt` ga yoziladi)
                //   keyin faollik bo'lganmi. `until` bilan solishtirilsa, snooze
                //   davomida qaytib dars qilgan o'quvchiga ham «eslatishimizni
                //   so'ragan edingiz» ketardi.
                if (anchor > (pref.UpdatedAt ?? pref.CreatedAt))
                {
                    pref.ClearSnooze(now);
                }
                else if (until > now)
                {
                    snoozed = true;
                }
                else if (!pref.IsOptedOut && !gotStudentMessageToday)
                {
                    stage = NudgeStage.SnoozeReturn;
                    stageAnchor = until;
                }
            }

            if (stage is null)
            {
                var idle = NudgeLadder.IdleDays(anchor, now, zone);
                var sentForAnchor = mine.Where(n => n.AnchorAt == anchor).Select(n => n.Stage).ToHashSet();
                var studentAllowed = pref?.IsOptedOut != true && !snoozed && !gotStudentMessageToday;

                stage = NudgeLadder.Next(idle, sentForAnchor, studentAllowed, thresholds);
            }

            if (stage is not { } chosen) continue;

            // ★ TRY/CATCH YO'Q — ATAYLAB: vazifa `IJobLock` ostida yuradi, ya'ni
            //   parallel yurish bo'lmaydi; bosqich esa yuqorida tekshirilgan.
            //   Unikal indeks — faqat oxirgi himoya chizig'i: u yiqilsa yurish
            //   to'xtaydi va keyingi tikda (15 daqiqa) qolganlari davom etadi.
            if (await SendAsync(student, chosen, stageAnchor, anchor, mine.Count(n => n.Stage == chosen), now, zone, ct)
                    .ConfigureAwait(false))
            {
                sent++;
            }

            if (chosen == NudgeStage.SnoozeReturn) pref?.ClearSnooze(now);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Faqat snooze tozalanishi bo'lgan (xabarsiz) o'zgarishlar ham saqlansin.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return sent;
    }

    /// <summary>
    /// Bitta bosqichni yozadi va yuboradi. <c>false</c> — bosqich yozildi, lekin
    /// xabar hech kimga ketmadi (masalan, o'quvchi kurator qarorini kutyapti).
    /// </summary>
    private async Task<bool> SendAsync(
        Candidate student,
        NudgeStage stage,
        DateTimeOffset stageAnchor,
        DateTimeOffset activityAnchor,
        int previousSameStage,
        DateTimeOffset now,
        TimeZoneInfo zone,
        CancellationToken ct)
    {
        var progress = await LoadProgressAsync(student.Id, ct).ConfigureAwait(false);

        // Kurs tugagan yoki o'quvchi kurator qarorini kutyapti — eslatma noo'rin.
        // Hech narsa yozilmaydi: holat o'zgarsa (kurator qaror chiqarsa),
        // zanjir ertaga o'z-o'zidan davom etadi.
        if (progress.Skip) return false;

        var staff = await LoadCuratorAsync(student.Id, ct).ConfigureAwait(false);

        var context = new NudgeContext(
            student.FullName,
            progress.LessonName,
            progress.Completed,
            progress.Total,
            progress.NeedsWork,
            staff?.FullName,
            NudgeLadder.IdleDays(activityAnchor, now, zone));

        if (stage == NudgeStage.CuratorAlert)
        {
            var alert = SelfStudyNudge.Create(student.Id, staff?.Id, stage, stageAnchor, 0, staff?.TelegramId is not null, now);
            db.SelfStudyNudges.Add(alert);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (staff is null) return false;

            await NotifyStaffAsync(staff.Id, staff.TelegramId, student.Id, NudgeTemplates.CuratorIdleAlert(context),
                $"nudge:{alert.Id.ToString(CultureInfo.InvariantCulture)}", ct).ConfigureAwait(false);
            return true;
        }

        var variant = NudgeLadder.PickVariant(previousSameStage, NudgeTemplates.VariantCount(stage));
        var message = NudgeTemplates.ForStudent(stage, variant, context);

        var nudge = SelfStudyNudge.Create(student.Id, student.Id, stage, stageAnchor, variant, student.TelegramId is not null, now);
        db.SelfStudyNudges.Add(nudge);

        // ★ AVVAL SAQLANADI: tugmalar va navbat kaliti yozuv `Id` siga tayanadi.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (student.TelegramId is { } chatId)
        {
            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = student.Id,
                    RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.SelfStudyNudge,
                    Body = message.TelegramHtml,
                    IdempotencyKey = $"nudge:{nudge.Id.ToString(CultureInfo.InvariantCulture)}",
                    CallbackData = NudgeTemplates.StudentButtons(stage, nudge.Id),
                },
                ct).ConfigureAwait(false);
        }
        else
        {
            // Telegram bog'lanmagan — hech bo'lmasa ilovadagi qo'ng'iroqchada.
            var row = Notification.Create(student.Id, NotificationKind.StudyReminder, message.Title, message.PlainBody, null, now);
            db.Notifications.Add(row);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.NotificationCreatedAsync(student.Id, NotificationFeed.ToDto(row), ct).ConfigureAwait(false);
        }

        return true;
    }

    // ================================================================ tugmalar

    public async Task<string?> HandleCallbackAsync(long telegramUserId, string? data, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(data) || !data.StartsWith(NudgeTemplates.CallbackPrefix, StringComparison.Ordinal))
            return null;

        // 🔴 EGALIK: `callback_data` — ochiq matn. O'quvchi Telegram akkaunti
        //    bo'yicha topiladi va eslatma AYNAN unga tegishli bo'lishi shart
        //    (`AbsenceNoticeService.HandleCallbackAsync` dagi AYNI falsafa).
        var student = await db.Users.AsNoTracking()
            .Where(u => u.TelegramId == telegramUserId && u.Role == UserRole.Student && u.IsActive)
            .Select(u => new { u.Id, u.FullName })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (student is null) return null;

        var now = clock.GetUtcNow();

        if (string.Equals(data, NudgeTemplates.CallbackOptIn, StringComparison.Ordinal))
        {
            var p = await LoadOrCreatePreferenceAsync(student.Id, now, ct).ConfigureAwait(false);
            p.OptIn(now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return "🔔 Eslatmalar yoqildi";
        }

        var parts = data.Split(':');
        if (parts.Length != 3
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var nudgeId))
        {
            return null;
        }

        var nudge = await db.SelfStudyNudges.AsTracking()
            .FirstOrDefaultAsync(n => n.Id == nudgeId, ct)
            .ConfigureAwait(false);

        if (nudge is null || nudge.StudentId != student.Id) return null;

        var code = parts[2];
        var pref = await LoadOrCreatePreferenceAsync(student.Id, now, ct).ConfigureAwait(false);

        // O'chirish — har qanday eslatmadan va har doim (oldin boshqa javob berilgan bo'lsa ham).
        if (string.Equals(code, NudgeTemplates.CodeOff, StringComparison.Ordinal))
        {
            pref.OptOut(now);
            nudge.Respond(NudgeResponse.OptOut, now);
            await ReplyAsync(student.Id, telegramUserId, NudgeTemplates.OptOutReplyText(),
                TelegramTemplates.SelfStudyNudgeOptedOut, NudgeTemplates.OptInButtons(), $"nudge_off:{nudge.Id}", ct)
                .ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return "🔕 Eslatmalar o'chirildi";
        }

        NudgeResponse? response = code switch
        {
            NudgeTemplates.CodeToday => NudgeResponse.ComingToday,
            NudgeTemplates.CodeSnooze => NudgeResponse.Snooze,
            NudgeTemplates.CodeHard => NudgeResponse.Hard,
            NudgeTemplates.CodeBusy => NudgeResponse.Busy,
            _ => null,
        };

        if (response is not { } answer) return null;

        if (!nudge.Respond(answer, now)) return "Javobingiz allaqachon qabul qilingan 👍";

        string toast;

        switch (answer)
        {
            case NudgeResponse.ComingToday:
                toast = "Zo'r! Bugun kutamiz 💪";
                break;

            case NudgeResponse.Snooze:
                pref.Snooze(now + SnoozeDuration, now);
                toast = "Mayli, 3 kundan keyin eslatamiz ⏰";
                break;

            case NudgeResponse.Busy:
                pref.Snooze(now + BusySnoozeDuration, now);
                await ReplyAsync(student.Id, telegramUserId, NudgeTemplates.BusyReplyText(),
                    TelegramTemplates.SelfStudyNudgeReply, null, $"nudge_busy:{nudge.Id}", ct).ConfigureAwait(false);
                toast = "Tushunamiz 🙌";
                break;

            default: // Hard
                var curatorName = await AlertCuratorHardAsync(student.Id, student.FullName, nudge.Id, now, ct)
                    .ConfigureAwait(false);
                await ReplyAsync(student.Id, telegramUserId, NudgeTemplates.HardReplyText(curatorName),
                    TelegramTemplates.SelfStudyNudgeReply, null, $"nudge_hard:{nudge.Id}", ct).ConfigureAwait(false);
                toast = "Xabar berdik — yordam beramiz 🤝";
                break;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return toast;
    }

    /// <summary>«Qiyin bo'lyapti» — kuratorga DARHOL (zanjirni kutmasdan). Kurator ismini qaytaradi.</summary>
    private async Task<string?> AlertCuratorHardAsync(
        long studentId, string studentName, long nudgeId, DateTimeOffset now, CancellationToken ct)
    {
        var staff = await LoadCuratorAsync(studentId, ct).ConfigureAwait(false);

        if (staff is null) return null;

        var progress = await LoadProgressAsync(studentId, ct).ConfigureAwait(false);
        var last = await activity.LastActivityAsync([studentId], ct).ConfigureAwait(false);
        var idle = last.TryGetValue(studentId, out var at) ? NudgeLadder.IdleDays(at, now, timeZone.TimeZone) : 0;

        var context = new NudgeContext(studentName, progress.LessonName, progress.Completed, progress.Total,
            progress.NeedsWork, staff.FullName, idle);

        await NotifyStaffAsync(staff.Id, staff.TelegramId, studentId, NudgeTemplates.CuratorHardAlert(context),
            $"nudge_hard_staff:{nudgeId.ToString(CultureInfo.InvariantCulture)}", ct).ConfigureAwait(false);

        return staff.FullName;
    }

    // ================================================================ kurator ro'yxati

    public async Task<IReadOnlyDictionary<long, StudentEngagementDto>> GetEngagementAsync(
        IReadOnlyCollection<long> studentIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(studentIds);
        if (studentIds.Count == 0) return new Dictionary<long, StudentEngagementDto>();

        var ids = studentIds.Distinct().ToList();
        var last = await activity.LastActivityAsync(ids, ct).ConfigureAwait(false);
        var since = clock.GetUtcNow().AddDays(-30);

        var responses = await db.SelfStudyNudges.AsNoTracking()
            .Where(n => ids.Contains(n.StudentId) && n.RespondedAt != null && n.RespondedAt >= since)
            .Select(n => new { n.StudentId, n.Response, n.RespondedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var optedOut = await db.SelfStudyNudgePreferences.AsNoTracking()
            .Where(p => ids.Contains(p.StudentId) && p.OptedOutAt != null)
            .Select(p => p.StudentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var zone = timeZone.TimeZone;

        return ids.ToDictionary(id => id, id =>
        {
            DateTimeOffset? lastAt = last.TryGetValue(id, out var v) ? v : null;

            // Faqat OXIRGI faollikdan KEYINGI javob dolzarb: o'quvchi «qiyin» deb,
            // keyin qaytib dars qilgan bo'lsa — belgi o'chadi.
            var answer = responses
                .Where(r => r.StudentId == id && (lastAt is null || r.RespondedAt > lastAt))
                .OrderByDescending(r => r.RespondedAt)
                .Select(r => r.Response)
                .FirstOrDefault();

            return new StudentEngagementDto(
                lastAt,
                lastAt is { } a ? NudgeLadder.IdleDays(a, now, zone) : null,
                answer?.ToString(),
                optedOut.Contains(id));
        });
    }

    // ================================================================ o'lchov

    public async Task<NudgeStatsDto> GetStatsAsync(int days, long actorId, CancellationToken ct = default)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), actorId);
        if (!actor.IsActive || actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Eslatmalar hisobotini faqat o'quv bo'limi yoki administrator ko'radi.");

        days = Math.Clamp(days, 1, 180);
        var now = clock.GetUtcNow();
        var from = now.AddDays(-days);

        var rows = await db.SelfStudyNudges.AsNoTracking()
            .Where(n => n.SentAt >= from && n.Stage != NudgeStage.CuratorAlert)
            .Select(n => new { n.StudentId, n.Stage, n.Variant, n.SentAt, n.Response })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var curatorAlerts = await db.SelfStudyNudges.AsNoTracking()
            .CountAsync(n => n.SentAt >= from && n.Stage == NudgeStage.CuratorAlert, ct)
            .ConfigureAwait(false);

        var optedOut = await db.SelfStudyNudgePreferences.AsNoTracking()
            .CountAsync(p => p.OptedOutAt != null, ct)
            .ConfigureAwait(false);

        var acts = await activity.ActivitySinceAsync(rows.Select(r => r.StudentId).Distinct().ToList(), from, ct)
            .ConfigureAwait(false);

        bool Returned(long studentId, DateTimeOffset sentAt) =>
            acts.TryGetValue(studentId, out var list)
            && list.Any(t => t > sentAt && t <= sentAt + ReturnWindow);

        var lines = rows
            .GroupBy(r => (r.Stage, r.Variant))
            .OrderBy(g => g.Key.Stage).ThenBy(g => g.Key.Variant)
            .Select(g =>
            {
                var sent = g.Count();
                var returned = g.Count(r => Returned(r.StudentId, r.SentAt));
                return new NudgeStatsLineDto(
                    g.Key.Stage.ToString(),
                    g.Key.Variant,
                    sent,
                    returned,
                    sent == 0 ? 0 : Math.Round(returned * 100.0 / sent, 1),
                    g.Count(r => r.Response is not null));
            })
            .ToList();

        var responses = rows
            .Where(r => r.Response is not null)
            .GroupBy(r => r.Response!.Value)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        return new NudgeStatsDto(days, lines, responses, curatorAlerts, optedOut);
    }

    // ================================================================ ichki

    private sealed record Candidate(long Id, string FullName, long? TelegramId, DateTimeOffset SubscriptionStart);

    private sealed record ProgressInfo(bool Skip, string? LessonName, int Completed, int Total, bool NeedsWork);

    /// <summary>
    /// Bugun kechqurun seriya eslatmasi oladigan o'quvchilar (uning qoidasi AYNAN
    /// takrorlanadi: ball tizimi va seriya eslatmasi yoqiq, Telegram bor,
    /// seriya ≥ <see cref="StreakReminderService.MinStreak"/>, bugun o'qilmagan).
    /// </summary>
    private async Task<HashSet<long>> StreakReminderCoveredAsync(
        IEnumerable<Candidate> students,
        IReadOnlyList<ResolvedSetting> resolved,
        DateTimeOffset now,
        DateOnly today,
        CancellationToken ct)
    {
        static bool On(string? value) => SettingValueParser.TryReadBool(value, out var on) && on;

        var covered = new HashSet<long>();
        if (!On(resolved[2].Value) || !On(resolved[3].Value)) return covered;

        var withTelegram = students.Where(s => s.TelegramId is not null).Select(s => s.Id).ToList();
        if (withTelegram.Count == 0) return covered;

        var days = await streaks.ActiveDaysAsync(withTelegram, now - DailyStreakService.History, ct)
            .ConfigureAwait(false);

        foreach (var (studentId, active) in days)
        {
            var streak = DailyStreak.Compute(active, today);
            if (!streak.DoneToday && streak.Current >= StreakReminderService.MinStreak) covered.Add(studentId);
        }

        return covered;
    }

    /// <summary>Faol obunali, guruhda bo'lmagan (ya'ni Mustaqil) o'quvchilar.</summary>
    private async Task<Dictionary<long, Candidate>> LoadCandidatesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var active = subscriptions.ActiveStudentIds();

        var rows = await db.Users.AsNoTracking()
            .Where(u => active.Contains(u.Id))
            .Where(u => !db.GroupMembers.Any(m => m.StudentId == u.Id && m.Status == MemberStatus.Active))
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.TelegramId,
                Start = db.SubscriptionPeriods
                    .Where(p => p.StudentId == u.Id && p.CancelledAt == null && p.StartsAt <= now && p.EndsAt > now)
                    .Min(p => (DateTimeOffset?)p.StartsAt),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Where(r => r.Start is not null)
            .ToDictionary(r => r.Id, r => new Candidate(r.Id, r.FullName, r.TelegramId, r.Start!.Value));
    }

    /// <summary>Hozirgi dars va o'zlashtirilganlar soni (kurs daraxtidan).</summary>
    private async Task<ProgressInfo> LoadProgressAsync(long studentId, CancellationToken ct)
    {
        var course = await gating.GetCourseGateAsync(studentId, ct).ConfigureAwait(false);
        var lessons = course.Lessons;

        var completed = lessons.Count(l => l.Completed);

        // Hozirgi dars — o'quvchidan ISH kutayotgan birinchi ochiq dars.
        // Ishini topshirib kurator qarorini kutayotgan dars o'tkazib
        // yuboriladi: «bitta dars oldinga» (2026-11-25) bilan undan keyingisi
        // ochiq bo'lishi mumkin — eslatma o'sha darsga chaqiradi. Bunday dars
        // bo'lmasa o'quvchida «qarz» yo'q, eslatma yuborilmaydi.
        static bool AwaitingCurator(LessonGateDto l) =>
            l.RequiresMastery && l.Mastery is null
            && (!l.HasVideo || l.VideoWatched)
            && (!l.HasAssignment || l.AssignmentSubmitted)
            && (!l.HasTest || l.TestTaken);

        var current = lessons.FirstOrDefault(l => l.Unlocked && !l.Completed && !AwaitingCurator(l));

        if (current is null) return new ProgressInfo(Skip: true, null, completed, lessons.Count, false);

        var name = await db.ModuleLessons.AsNoTracking()
            .Where(l => l.Id == current.LessonId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return new ProgressInfo(
            Skip: false,
            name,
            completed,
            lessons.Count,
            current.Mastery == MasteryStatus.NeedsWork);
    }

    private sealed record StaffContact(long Id, string FullName, long? TelegramId);

    /// <summary>
    /// O'quvchining faol kuratori: asosiy faol bo'lsa — u, aks holda zaxira.
    /// ★ TEKIS PROYEKSIYA: shartli anonim obyektlar EF'da har doim ham
    /// tarjima qilinmaydi, tanlov esa xotirada qilinadi.
    /// </summary>
    private async Task<StaffContact?> LoadCuratorAsync(long studentId, CancellationToken ct)
    {
        var row = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.EndedAt == null)
            .Select(a => new
            {
                a.CuratorId,
                MainName = a.Curator!.FullName,
                MainActive = a.Curator.IsActive,
                MainTelegram = a.Curator.TelegramId,
                a.BackupCuratorId,
                BackupName = a.BackupCurator != null ? a.BackupCurator.FullName : null,
                BackupActive = a.BackupCurator != null && a.BackupCurator.IsActive,
                BackupTelegram = a.BackupCurator != null ? a.BackupCurator.TelegramId : null,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null) return null;
        if (row.MainActive) return new StaffContact(row.CuratorId, row.MainName, row.MainTelegram);
        if (row.BackupActive && row.BackupCuratorId is { } backupId)
            return new StaffContact(backupId, row.BackupName ?? string.Empty, row.BackupTelegram);
        return null;
    }

    private async Task NotifyStaffAsync(
        long staffId, long? telegramId, long studentId, NudgeMessage message, string key, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // Kuratorga IKKALASI: qo'ng'iroqcha (ish ro'yxati) va Telegram (darhol ko'rsin).
        var row = Notification.Create(staffId, NotificationKind.CuratorStudentIdle, message.Title, message.PlainBody, studentId, now);
        db.Notifications.Add(row);

        if (telegramId is { } chatId)
        {
            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = staffId,
                    RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.SelfStudyNudgeCurator,
                    Body = message.TelegramHtml,
                    IdempotencyKey = key,
                },
                ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotificationCreatedAsync(staffId, NotificationFeed.ToDto(row), ct).ConfigureAwait(false);
    }

    private async Task ReplyAsync(
        long studentId, long chatId, string body, string templateKey, string? buttons, string key, CancellationToken ct) =>
        await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientUserId = studentId,
                RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                TemplateKey = templateKey,
                Body = body,
                IdempotencyKey = key,
                CallbackData = buttons,
            },
            ct).ConfigureAwait(false);

    private async Task<SelfStudyNudgePreference> LoadOrCreatePreferenceAsync(
        long studentId, DateTimeOffset now, CancellationToken ct)
    {
        var pref = await db.SelfStudyNudgePreferences.AsTracking()
            .FirstOrDefaultAsync(p => p.StudentId == studentId, ct)
            .ConfigureAwait(false);

        if (pref is not null) return pref;

        pref = SelfStudyNudgePreference.Create(studentId, now);
        db.SelfStudyNudgePreferences.Add(pref);
        return pref;
    }

    private static SettingDefinition Definition(string key) =>
        SettingsRegistry.TryGet(key, out var definition)
            ? definition
            : throw new InvalidOperationException($"Sozlama registrda yo'q: {key}");
}

/// <summary>Kurator ro'yxatidagi o'quvchi faolligi.</summary>
/// <param name="LastActivityAt">Oxirgi o'quv faolligi.</param>
/// <param name="IdleDays">Necha kundan beri faolsiz (hech qachon faol bo'lmagan — <c>null</c>).</param>
/// <param name="LastResponse">Oxirgi faollikdan keyingi tugma javobi (<see cref="NudgeResponse"/> nomi).</param>
/// <param name="NudgesOff">O'quvchi avtomatik eslatmalarni o'chirgan.</param>
public sealed record StudentEngagementDto(
    DateTimeOffset? LastActivityAt,
    int? IdleDays,
    string? LastResponse,
    bool NudgesOff);

public sealed record NudgeStatsLineDto(
    string Stage,
    int Variant,
    int Sent,
    int ReturnedWithin48h,
    double ReturnRatePercent,
    int Responded);

public sealed record NudgeStatsDto(
    int Days,
    IReadOnlyList<NudgeStatsLineDto> Lines,
    IReadOnlyDictionary<string, int> Responses,
    int CuratorAlerts,
    int StudentsOptedOut);
