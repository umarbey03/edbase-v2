using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.TeacherAvailability.Dtos;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.TeacherAvailability.Services;

/// <inheritdoc cref="ITeacherAvailabilityService"/>
public sealed class TeacherAvailabilityService(
    IApplicationDbContext db,
    INotificationOutbox outbox,
    IScheduleTimeZoneProvider timeZoneProvider,
    TimeProvider clock) : ITeacherAvailabilityService
{
    // ================================================================ so'rov

    /// <inheritdoc />
    public async Task<int> RequestConfirmationsAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var today = LocalToday(now);
        var (dayStart, dayEnd) = LocalDayRangeUtc(today, 1);

        var sessions = await db.LiveSessions
            .Where(s => s.Type == SessionType.Teacher
                && s.Status == SessionStatus.Scheduled
                && s.HostId != null
                && s.ScheduledStart >= dayStart && s.ScheduledStart < dayEnd)
            .Include(s => s.Group)
            .OrderBy(s => s.ScheduledStart)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (sessions.Count == 0) return 0;

        var teacherIds = sessions.Select(s => s.HostId!.Value).Distinct().ToList();

        var teachers = await db.Users
            .Where(u => teacherIds.Contains(u.Id)
                && u.Role == UserRole.Teacher && u.IsActive && u.TelegramId != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (teachers.Count == 0) return 0;

        var alreadyAsked = (await db.TeacherDailyCheckins
                .Where(c => c.CheckinDate == today && teacherIds.Contains(c.TeacherId))
                .Select(c => c.TeacherId)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToHashSet();

        // ★ AVTOMATIK KO'P KUNLIK OYNA (foydalanuvchi qarori): agar ustoz
        // oldinroq "N kunga yo'q" degan bo'lsa va bugun o'sha oynaga tushsa —
        // savol QAYTA berilmaydi (u AVVAL javob bergan).
        var suppressed = (await db.TeacherDailyCheckins
                .Where(c => c.Status == TeacherCheckinStatus.Declined
                    && teacherIds.Contains(c.TeacherId)
                    && c.UnavailableDays != null
                    && c.CheckinDate <= today)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Where(c => today < c.CheckinDate.AddDays(c.UnavailableDays!.Value))
            .Select(c => c.TeacherId)
            .ToHashSet();

        var processed = 0;

        foreach (var teacher in teachers)
        {
            if (alreadyAsked.Contains(teacher.Id) || suppressed.Contains(teacher.Id)) continue;

            var teacherSessions = sessions.Where(s => s.HostId == teacher.Id).ToList();
            if (teacherSessions.Count == 0) continue;

            var checkin = new TeacherDailyCheckin
            {
                TeacherId = teacher.Id,
                CheckinDate = today,
                SentAt = now,
            };

            db.TeacherDailyCheckins.Add(checkin);

            // Id kerak (callback_data va idempotentlik kaliti ichida) — shu
            // ustoz uchun alohida SaveChanges, boshqasining muvaffaqiyatsizligi
            // bilan bog'lanmasin.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var body = TelegramTemplates.AvailabilityAskText(
                [.. teacherSessions.Select(s => (FormatTime(s), s.Group?.Name ?? ""))]);

            var callbackData = TelegramTemplates.EncodeButtons(
            [
                [
                    ("✅ Ha, o'taman", $"av:yes:{checkin.Id}"),
                    ("❌ Yo'q, o'ta olmayman", $"av:no:{checkin.Id}"),
                ],
            ]);

            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = teacher.Id,
                    RecipientAddress = teacher.TelegramId!.Value.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.AvailabilityAsk,
                    Body = body,
                    CallbackData = callbackData,
                    IdempotencyKey = $"teacher_checkin_ask:{checkin.Id}",
                },
                ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    // ================================================================ callback

    /// <inheritdoc />
    public async Task<string?> HandleCallbackAsync(
        long senderTelegramId, string data, CancellationToken ct = default)
    {
        var parts = data.Split(':');
        if (parts.Length < 3) return null;

        var sender = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.TelegramId == senderTelegramId, ct)
            .ConfigureAwait(false);

        if (sender is null) return "Profilingiz topilmadi.";

        return parts[0] switch
        {
            "av" => await HandleAvailabilityCallbackAsync(sender, parts, ct).ConfigureAwait(false),
            "of" => await HandleOfferCallbackAsync(sender, parts, ct).ConfigureAwait(false),
            _ => null,
        };
    }

    private async Task<string?> HandleAvailabilityCallbackAsync(User sender, string[] parts, CancellationToken ct)
    {
        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var checkinId))
            return null;

        var checkin = await db.TeacherDailyCheckins
            .Include(c => c.AffectedSessions)
            .AsTracking()
            .FirstOrDefaultAsync(c => c.Id == checkinId, ct)
            .ConfigureAwait(false);

        if (checkin is null) return "Bu so'rov topilmadi.";
        if (checkin.TeacherId != sender.Id) return "Bu tugma sizga tegishli emas.";

        var now = clock.GetUtcNow();

        switch (parts[1])
        {
            case "yes":
                if (checkin.Status != TeacherCheckinStatus.Pending) return "Bu savolga allaqachon javob berilgan.";
                checkin.Confirm(now);
                return "✅ Qabul qilindi, rahmat!";

            case "no":
                if (checkin.Status != TeacherCheckinStatus.Pending) return "Bu savolga allaqachon javob berilgan.";
                checkin.StartDecline(now);
                await SendSessionSelectionAsync(checkin, ct).ConfigureAwait(false);
                return "Davom eting 👇";

            case "sess":
                if (checkin.Status != TeacherCheckinStatus.SelectingSessions) return "Bu bosqichda emas.";
                if (parts.Length < 4
                    || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionId))
                    return null;

                ToggleAffectedSession(checkin, sessionId);
                await SendSessionSelectionAsync(checkin, ct).ConfigureAwait(false);
                return null;

            case "all":
                if (checkin.Status != TeacherCheckinStatus.SelectingSessions) return "Bu bosqichda emas.";
                await SelectAllSessionsAsync(checkin, ct).ConfigureAwait(false);
                await SendSessionSelectionAsync(checkin, ct).ConfigureAwait(false);
                return null;

            case "go":
                if (checkin.Status != TeacherCheckinStatus.SelectingSessions) return "Bu bosqichda emas.";
                if (checkin.AffectedSessions.Count == 0) return "Kamida bitta dars belgilang.";

                checkin.ConfirmSessionSelection(now);

                await SendPlainAsync(
                    sender, TelegramTemplates.AvailabilityReason,
                    TelegramTemplates.AvailabilityReasonPromptText(),
                    $"av_reason:{checkin.Id}", ct).ConfigureAwait(false);

                return "Endi sababni yozib yuboring.";

            case "days":
                if (checkin.Status != TeacherCheckinStatus.AwaitingDays) return "Bu bosqichda emas.";
                if (parts.Length < 4
                    || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
                    return null;

                await FinalizeDeclineAsync(checkin, days, ct).ConfigureAwait(false);
                return "✅ Qabul qilindi, rahmat.";

            default:
                return null;
        }
    }

    private async Task<string?> HandleOfferCallbackAsync(User sender, string[] parts, CancellationToken ct)
    {
        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offerId))
            return null;

        var offer = await db.SubstituteOffers
            .Include(o => o.CoverageRequest!).ThenInclude(r => r.Session!).ThenInclude(s => s.Group)
            .Include(o => o.CoverageRequest!).ThenInclude(r => r.Offers)
            .AsTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId, ct)
            .ConfigureAwait(false);

        if (offer is null) return "Taklif topilmadi.";
        if (offer.CandidateTeacherId != sender.Id) return "Bu tugma sizga tegishli emas.";

        var now = clock.GetUtcNow();
        var request = offer.CoverageRequest!;

        if (parts[1] == "no")
        {
            if (offer.Status != SubstituteOfferStatus.Sent) return "Allaqachon javob berilgan.";
            offer.Decline(now);
            return "Tushunarli, rahmat.";
        }

        if (parts[1] != "yes") return null;

        if (request.Status != CoverageRequestStatus.Open)
            return "Kechirasiz, bu darsga allaqachon boshqa ustoz topilgan.";

        if (offer.Status != SubstituteOfferStatus.Sent) return "Allaqachon javob berilgan.";

        offer.Accept(now);
        request.Resolve(sender.Id, now);
        request.Session!.AssignSubstitute(sender.Id, now);

        foreach (var other in request.Offers.Where(o => o.Id != offer.Id && o.Status == SubstituteOfferStatus.Sent))
        {
            other.Withdraw(now);

            var otherCandidate = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == other.CandidateTeacherId, ct)
                .ConfigureAwait(false);

            if (otherCandidate is not null)
            {
                await SendPlainAsync(
                    otherCandidate, TelegramTemplates.SubstituteOfferWithdrawn,
                    TelegramTemplates.SubstituteOfferWithdrawnText(),
                    $"substitute_offer_withdrawn:{other.Id}", ct).ConfigureAwait(false);
            }
        }

        var originalTeacher = request.OriginalHostId == 0
            ? null
            : await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.OriginalHostId, ct)
                .ConfigureAwait(false);

        await NotifyAcademicSubstituteFoundAsync(
            sender, originalTeacher?.FullName ?? "", request.Session!.Group?.Name ?? "", ct)
            .ConfigureAwait(false);

        return "✅ Rahmat! Siz darsni oldingiz.";
    }

    // ================================================================ erkin matn

    /// <inheritdoc />
    public async Task<bool> HandleFreeTextAsync(long senderTelegramId, string text, CancellationToken ct = default)
    {
        var sender = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.TelegramId == senderTelegramId, ct)
            .ConfigureAwait(false);

        if (sender is null) return false;

        var checkin = await db.TeacherDailyCheckins
            .Include(c => c.AffectedSessions)
            .AsTracking()
            .Where(c => c.TeacherId == sender.Id
                && (c.Status == TeacherCheckinStatus.AwaitingReason
                    || c.Status == TeacherCheckinStatus.AwaitingDays))
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (checkin is null) return false;

        var now = clock.GetUtcNow();

        if (checkin.Status == TeacherCheckinStatus.AwaitingReason)
        {
            checkin.SubmitReason(text, now);

            await SendPlainAsync(
                sender, TelegramTemplates.AvailabilityDays,
                TelegramTemplates.AvailabilityDaysPromptText(),
                $"av_days:{checkin.Id}",
                ct,
                DaysQuickButtons(checkin.Id)).ConfigureAwait(false);

            return true;
        }

        // AwaitingDays — matn orqali kelsa faqat butun son qabul qilinadi.
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            || days is < 1 or > 30)
        {
            await SendPlainAsync(
                sender, TelegramTemplates.AvailabilityDaysInvalid,
                TelegramTemplates.AvailabilityDaysInvalidText(),
                $"av_days_invalid:{checkin.Id}:{Guid.NewGuid():N}", ct).ConfigureAwait(false);

            return true;
        }

        await FinalizeDeclineAsync(checkin, days, ct).ConfigureAwait(false);
        return true;
    }

    // ================================================================ bosqich yordamchilari

    private static void ToggleAffectedSession(TeacherDailyCheckin checkin, long sessionId)
    {
        var existing = checkin.AffectedSessions.FirstOrDefault(a => a.SessionId == sessionId);

        if (existing is not null)
            checkin.AffectedSessions.Remove(existing);
        else
            checkin.AffectedSessions.Add(new TeacherCheckinAffectedSession { CheckinId = checkin.Id, SessionId = sessionId });
    }

    private async Task SelectAllSessionsAsync(TeacherDailyCheckin checkin, CancellationToken ct)
    {
        var (dayStart, dayEnd) = LocalDayRangeUtc(checkin.CheckinDate, 1);

        var sessionIds = await db.LiveSessions
            .Where(s => s.HostId == checkin.TeacherId && s.Status == SessionStatus.Scheduled
                && s.ScheduledStart >= dayStart && s.ScheduledStart < dayEnd)
            .Select(s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var existingIds = checkin.AffectedSessions.Select(a => a.SessionId).ToHashSet();

        foreach (var id in sessionIds.Where(id => !existingIds.Contains(id)))
            checkin.AffectedSessions.Add(new TeacherCheckinAffectedSession { CheckinId = checkin.Id, SessionId = id });
    }

    private async Task SendSessionSelectionAsync(TeacherDailyCheckin checkin, CancellationToken ct)
    {
        var (dayStart, dayEnd) = LocalDayRangeUtc(checkin.CheckinDate, 1);

        var sessions = await db.LiveSessions
            .Where(s => s.HostId == checkin.TeacherId && s.Status == SessionStatus.Scheduled
                && s.ScheduledStart >= dayStart && s.ScheduledStart < dayEnd)
            .Include(s => s.Group)
            .OrderBy(s => s.ScheduledStart)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (sessions.Count == 0) return;

        var selectedIds = checkin.AffectedSessions.Select(a => a.SessionId).ToHashSet();

        var body = TelegramTemplates.AvailabilitySessionsText(
            [.. sessions.Select(s => (FormatTime(s), s.Group?.Name ?? "", selectedIds.Contains(s.Id)))]);

        List<IReadOnlyList<(string Label, string CallbackData)>> rows =
        [
            .. sessions.Select(s => (IReadOnlyList<(string, string)>)
            [
                ($"{(selectedIds.Contains(s.Id) ? "☑" : "☐")} {FormatTime(s)} {s.Group?.Name}",
                    $"av:sess:{checkin.Id}:{s.Id}"),
            ]),
            [("✅ Barchasi", $"av:all:{checkin.Id}"), ("▶ Davom etish", $"av:go:{checkin.Id}")],
        ];

        var callbackData = TelegramTemplates.EncodeButtons(rows);
        var selectionKey = string.Join(',', selectedIds.OrderBy(x => x));

        await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientUserId = checkin.TeacherId,
                RecipientAddress = (await TelegramAddressAsync(checkin.TeacherId, ct).ConfigureAwait(false)),
                TemplateKey = TelegramTemplates.AvailabilitySessions,
                Body = body,
                CallbackData = callbackData,
                IdempotencyKey = $"av_sessions:{checkin.Id}:{selectionKey}",
            },
            ct).ConfigureAwait(false);
    }

    private async Task FinalizeDeclineAsync(TeacherDailyCheckin checkin, int days, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        checkin.SubmitDays(days, now);

        var sessionIds = new HashSet<long>(checkin.AffectedSessions.Select(a => a.SessionId));

        if (days > 1)
        {
            var (windowStart, windowEnd) = LocalDayRangeUtc(checkin.CheckinDate, days);

            var extra = await db.LiveSessions
                .Where(s => s.HostId == checkin.TeacherId && s.Status == SessionStatus.Scheduled
                    && s.ScheduledStart >= windowStart && s.ScheduledStart < windowEnd)
                .Select(s => s.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var id in extra) sessionIds.Add(id);
        }

        var teacher = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Id == checkin.TeacherId, ct).ConfigureAwait(false);

        foreach (var sessionId in sessionIds)
            await OpenCoverageRequestAsync(checkin, sessionId, teacher, ct).ConfigureAwait(false);

        await NotifyAcademicDeclinedAsync(teacher, sessionIds.Count, checkin, ct).ConfigureAwait(false);
    }

    // ================================================================ o'rinbosar

    private async Task OpenCoverageRequestAsync(
        TeacherDailyCheckin checkin, long sessionId, User originalTeacher, CancellationToken ct)
    {
        var alreadyOpen = await db.SessionCoverageRequests
            .AnyAsync(r => r.SessionId == sessionId && r.Status == CoverageRequestStatus.Open, ct)
            .ConfigureAwait(false);

        if (alreadyOpen) return;

        var session = await db.LiveSessions
            .Include(s => s.Group)
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);

        if (session is null || session.Status != SessionStatus.Scheduled) return;

        var request = new SessionCoverageRequest
        {
            SessionId = sessionId,
            CheckinId = checkin.Id,
            OriginalHostId = originalTeacher.Id,
            Reason = checkin.DeclineReason ?? string.Empty,
            Status = CoverageRequestStatus.Open,
        };

        db.SessionCoverageRequests.Add(request);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var candidates = await FindFreeTeachersAsync(session, originalTeacher.Id, ct).ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            var offer = new SubstituteOffer
            {
                CoverageRequestId = request.Id,
                CandidateTeacherId = candidate.Id,
                SentAt = clock.GetUtcNow(),
            };

            db.SubstituteOffers.Add(offer);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (candidate.TelegramId is null) continue;

            var body = TelegramTemplates.SubstituteOfferText(
                originalTeacher.FullName, session.Group?.Name ?? "", FormatTime(session), request.Reason);

            var callbackData = TelegramTemplates.EncodeButtons(
            [
                [
                    ("✅ Ha, o'taman", $"of:yes:{offer.Id}"),
                    ("❌ Yo'q", $"of:no:{offer.Id}"),
                ],
            ]);

            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = candidate.Id,
                    RecipientAddress = candidate.TelegramId.Value.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.SubstituteOfferAsk,
                    Body = body,
                    CallbackData = callbackData,
                    IdempotencyKey = $"substitute_offer:{offer.Id}",
                },
                ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Bo'sh ustozlarni topadi (plandagi <c>IFreeTeacherFinder</c> — bu
    /// servisga ICHKI metod sifatida qo'shildi, sabab interfeys izohida).
    /// Reyting YO'Q (MVP): birinchi rozi bo'lgan darsni oladi.
    /// </summary>
    private async Task<List<User>> FindFreeTeachersAsync(LiveSession session, long excludeTeacherId, CancellationToken ct)
    {
        var busyTeacherIds = (await db.LiveSessions
                .Where(s => s.Id != session.Id
                    && s.HostId != null
                    && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.Live)
                    && s.ScheduledStart < session.ScheduledEnd
                    && s.ScheduledEnd > session.ScheduledStart)
                .Select(s => s.HostId!.Value)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToHashSet();

        busyTeacherIds.Add(excludeTeacherId);

        return await db.Users
            .Where(u => u.Role == UserRole.Teacher && u.IsActive && !busyTeacherIds.Contains(u.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    // ================================================================ bildirishnoma (o'quv bo'limi)

    private async Task NotifyAcademicDeclinedAsync(
        User teacher, int affectedCount, TeacherDailyCheckin checkin, CancellationToken ct)
    {
        var reason = checkin.DeclineReason ?? string.Empty;
        var days = checkin.UnavailableDays ?? 1;

        await NotifyAcademicAsync(
            NotificationKind.TeacherDeclinedSession,
            NotificationTemplates.TeacherDeclinedSessionTitle(),
            NotificationTemplates.TeacherDeclinedSessionBody(teacher.FullName, affectedCount, reason, days),
            TelegramTemplates.TeacherDeclinedSession,
            TelegramTemplates.TeacherDeclinedSessionText(teacher.FullName, affectedCount, reason, days),
            entityId: checkin.Id,
            idempotencyPrefix: $"teacher_declined:{checkin.Id}",
            ct).ConfigureAwait(false);
    }

    private async Task NotifyAcademicSubstituteFoundAsync(
        User substitute, string originalTeacherName, string groupName, CancellationToken ct)
    {
        await NotifyAcademicAsync(
            NotificationKind.SubstituteFound,
            NotificationTemplates.SubstituteFoundTitle(),
            NotificationTemplates.SubstituteFoundBody(substitute.FullName, originalTeacherName, groupName),
            TelegramTemplates.SubstituteFound,
            TelegramTemplates.SubstituteFoundText(substitute.FullName, originalTeacherName, groupName),
            entityId: substitute.Id,
            idempotencyPrefix: $"substitute_found:{substitute.Id}:{Guid.NewGuid():N}",
            ct).ConfigureAwait(false);
    }

    private async Task NotifyAcademicAsync(
        NotificationKind kind,
        string title,
        string plainBody,
        string telegramTemplateKey,
        string telegramBody,
        long? entityId,
        string idempotencyPrefix,
        CancellationToken ct)
    {
        var recipients = await db.Users
            .Where(u => u.IsActive && (u.Role == UserRole.Academic || u.Role == UserRole.Admin))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();

        foreach (var recipient in recipients)
        {
            db.Notifications.Add(Notification.Create(recipient.Id, kind, title, plainBody, entityId, now));

            if (recipient.TelegramId is not null)
            {
                await outbox.EnqueueAsync(
                    new NotificationRequest
                    {
                        Channel = NotificationChannel.Telegram,
                        RecipientUserId = recipient.Id,
                        RecipientAddress = recipient.TelegramId.Value.ToString(CultureInfo.InvariantCulture),
                        TemplateKey = telegramTemplateKey,
                        Body = telegramBody,
                        IdempotencyKey = $"{idempotencyPrefix}:{recipient.Id}",
                    },
                    ct).ConfigureAwait(false);
            }
        }
    }

    // ================================================================ o'quv bo'limi paneli

    /// <inheritdoc />
    public async Task<PagedResult<TeacherAvailabilityRowDto>> ListAsync(
        TeacherAvailabilityListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = Filter(query);

        var total = await rows.CountAsync(ct).ConfigureAwait(false);

        // ★ AVVAL SAHIFALASH, KEYIN QAMROV MA'LUMOTI: qamrov (o'rinbosar)
        //   so'rovlari FAQAT shu sahifadagi darslar uchun olinadi. Ilgari
        //   (bugungi ko'rinishda) butun to'plam xotiraga yuklanardi — 11
        //   kunlik tarixda bu yuzlab keraksiz qator degani edi.
        var checkins = await Sort(rows, query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(c => c.Teacher)
            .Include(c => c.AffectedSessions).ThenInclude(a => a.Session!).ThenInclude(s => s.Group)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = await MapRowsAsync(checkins, ct).ConfigureAwait(false);

        return new PagedResult<TeacherAvailabilityRowDto>(items, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<TeacherAvailabilitySummaryDto> GetSummaryAsync(
        TeacherAvailabilityListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = Filter(query);

        // Holatlar bo'yicha sanoq — BITTA `GROUP BY` so'rovi.
        var byStatus = await rows
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int CountOf(TeacherCheckinStatus status) =>
            byStatus.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var total = byStatus.Sum(x => x.Count);

        // Suhbat yarim qolgan holatlar — uchtasi bitta ko'rsatkichga yig'iladi:
        // o'quv bo'limi uchun ular bir xil ma'noda ("ustoz javobni tugatmagan").
        var inProgress = CountOf(TeacherCheckinStatus.SelectingSessions)
            + CountOf(TeacherCheckinStatus.AwaitingReason)
            + CountOf(TeacherCheckinStatus.AwaitingDays);

        // Ta'sirlangan darslar va ularning qamrov holati.
        var sessionIds = await rows
            .SelectMany(c => c.AffectedSessions.Select(a => a.SessionId))
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var coverageByStatus = sessionIds.Count == 0
            ? []
            : await db.SessionCoverageRequests
                .Where(r => sessionIds.Contains(r.SessionId))
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct)
                .ConfigureAwait(false);

        int CoverageOf(CoverageRequestStatus status) =>
            coverageByStatus.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        return new TeacherAvailabilitySummaryDto(
            Total: total,
            Confirmed: CountOf(TeacherCheckinStatus.Confirmed),
            Declined: CountOf(TeacherCheckinStatus.Declined),
            Pending: CountOf(TeacherCheckinStatus.Pending),
            InProgress: inProgress,
            AffectedSessions: sessionIds.Count,
            CoverageResolved: CoverageOf(CoverageRequestStatus.Resolved),
            CoverageOpen: CoverageOf(CoverageRequestStatus.Open));
    }

    /// <inheritdoc />
    public async Task<TeacherAvailabilityDetailDto> GetDetailAsync(
        long checkinId, CancellationToken ct = default)
    {
        var checkin = await db.TeacherDailyCheckins
            .Where(c => c.Id == checkinId)
            .Include(c => c.Teacher)
            .Include(c => c.AffectedSessions).ThenInclude(a => a.Session!).ThenInclude(s => s.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(TeacherDailyCheckin), checkinId);

        var sessionIds = checkin.AffectedSessions.Select(a => a.SessionId).ToList();

        var requests = sessionIds.Count == 0
            ? []
            : await db.SessionCoverageRequests
                .Where(r => sessionIds.Contains(r.SessionId))
                .Include(r => r.Offers)
                .AsNoTracking()
                .ToListAsync(ct)
                .ConfigureAwait(false);

        // Barcha kerakli ismlar BITTA so'rovda (nomzodlar + o'rinbosarlar).
        var userIds = requests
            .SelectMany(r => r.Offers.Select(o => o.CandidateTeacherId))
            .Concat(requests.Where(r => r.ResolvedByUserId is not null).Select(r => r.ResolvedByUserId!.Value))
            .Distinct()
            .ToList();

        var names = userIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct)
                .ConfigureAwait(false);

        string NameOf(long id) => names.TryGetValue(id, out var name) ? name : "Noma'lum xodim";

        var coverages = checkin.AffectedSessions
            .Select(a =>
            {
                // Bitta darsga bir nechta TARIXIY so'rov bo'lishi mumkin —
                // eng OXIRGISI ko'rsatiladi (sabab `SessionCoverageRequest` izohida).
                var request = requests
                    .Where(r => r.SessionId == a.SessionId)
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefault();

                return new CoverageDetailDto(
                    a.SessionId,
                    a.Session?.Group?.Name ?? "",
                    a.Session?.ScheduledStart ?? default,
                    request?.Status.ToString(),
                    request?.ResolvedByUserId is { } resolverId ? NameOf(resolverId) : null,
                    request?.Reason,
                    request is null
                        ? []
                        : [.. request.Offers
                            .OrderBy(o => o.SentAt)
                            .ThenBy(o => o.Id)
                            .Select(o => new SubstituteOfferRowDto(
                                o.Id,
                                o.CandidateTeacherId,
                                NameOf(o.CandidateTeacherId),
                                o.Status.ToString(),
                                o.SentAt,
                                o.RespondedAt))]);
            })
            .OrderBy(c => c.ScheduledStart)
            .ToList();

        return new TeacherAvailabilityDetailDto(
            checkin.Id,
            checkin.TeacherId,
            checkin.Teacher?.FullName ?? "",
            checkin.CheckinDate,
            checkin.Status.ToString(),
            checkin.DeclineReason,
            checkin.UnavailableDays,
            checkin.SentAt,
            checkin.RespondedAt,
            coverages);
    }

    // ---------------------------------------------------------------- filtr / saralash / xaritalash

    /// <summary>
    /// Ro'yxat va yig'ma UCHALASI uchun AYNI filtr — ikki joyda takrorlansa
    /// vaqt o'tib ular ajralib ketardi va yig'ma raqamlar ro'yxatga
    /// mos kelmay qolardi.
    /// </summary>
    private IQueryable<TeacherDailyCheckin> Filter(TeacherAvailabilityListQuery query)
    {
        var rows = db.TeacherDailyCheckins.AsNoTracking();

        // ★ UTC O'GIRISH YO'Q: `CheckinDate` allaqachon mahalliy `DateOnly`
        //   (sabab `TeacherAvailabilityListQuery` izohida).
        if (query.From is { } from) rows = rows.Where(c => c.CheckinDate >= from);
        if (query.To is { } to) rows = rows.Where(c => c.CheckinDate <= to);

        if (query.Status is { } status) rows = rows.Where(c => c.Status == status);

        if (query.OnlyUncovered)
        {
            // "Diqqat talab qiladi" = ta'sirlangan darsi bor, lekin kamida
            // bittasiga o'rinbosar HALI topilmagan.
            rows = rows.Where(c => c.AffectedSessions.Any(a =>
                db.SessionCoverageRequests.Any(r =>
                    r.SessionId == a.SessionId && r.Status == CoverageRequestStatus.Open)));
        }

        var term = NormalizeSearch(query.Search);

        if (term is not null)
        {
            // ⚠️ `Contains` EMAS: `lower(col) LIKE '%…%'` ko'rinishi
            //    Postgres'da trigramma indeksidan foydalanadi (loyihadagi
            //    boshqa qidiruvlar bilan AYNI naqsh).
#pragma warning disable CA1304, CA1311
            rows = rows.Where(c =>
                EF.Functions.Like(c.Teacher!.FullName.ToLower(), term)
                || (c.DeclineReason != null && EF.Functions.Like(c.DeclineReason.ToLower(), term)));
#pragma warning restore CA1304, CA1311
        }

        return rows;
    }

    /// <summary>
    /// Saralash — OQ RO'YXAT bo'yicha.
    ///
    /// ★ `ThenBy(Id)` HAR VARIANTDA: bir xil sanali/ismli qatorlarda tartib
    /// so'rovdan so'rovga sakramasin (aks holda 2-sahifada 1-sahifadagi
    /// qator qayta chiqib qolardi).
    /// </summary>
    private static IQueryable<TeacherDailyCheckin> Sort(
        IQueryable<TeacherDailyCheckin> rows, TeacherAvailabilityListQuery query) =>
        (query.Sort, query.Desc) switch
        {
            (TeacherAvailabilitySort.Teacher, false) =>
                rows.OrderBy(c => c.Teacher!.FullName).ThenBy(c => c.Id),
            (TeacherAvailabilitySort.Teacher, true) =>
                rows.OrderByDescending(c => c.Teacher!.FullName).ThenBy(c => c.Id),

            (TeacherAvailabilitySort.Status, false) =>
                rows.OrderBy(c => c.Status).ThenByDescending(c => c.CheckinDate).ThenBy(c => c.Id),
            (TeacherAvailabilitySort.Status, true) =>
                rows.OrderByDescending(c => c.Status).ThenByDescending(c => c.CheckinDate).ThenBy(c => c.Id),

            (_, false) => rows.OrderBy(c => c.CheckinDate).ThenBy(c => c.Teacher!.FullName).ThenBy(c => c.Id),
            (_, true) => rows.OrderByDescending(c => c.CheckinDate).ThenBy(c => c.Teacher!.FullName).ThenBy(c => c.Id),
        };

    /// <summary>Sahifadagi qatorlarni qamrov ma'lumoti bilan to'ldiradi.</summary>
    private async Task<List<TeacherAvailabilityRowDto>> MapRowsAsync(
        List<TeacherDailyCheckin> checkins, CancellationToken ct)
    {
        if (checkins.Count == 0) return [];

        var sessionIds = checkins.SelectMany(c => c.AffectedSessions.Select(a => a.SessionId)).ToList();

        var coverageBySession = sessionIds.Count == 0
            ? new Dictionary<long, SessionCoverageRequest>()
            : (await db.SessionCoverageRequests
                    .Where(r => sessionIds.Contains(r.SessionId))
                    .AsNoTracking()
                    .ToListAsync(ct)
                    .ConfigureAwait(false))
                .GroupBy(r => r.SessionId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());

        var resolverIds = coverageBySession.Values
            .Where(r => r.ResolvedByUserId is not null)
            .Select(r => r.ResolvedByUserId!.Value)
            .Distinct()
            .ToList();

        var resolverNames = resolverIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking()
                .Where(u => resolverIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct)
                .ConfigureAwait(false);

        return
        [
            .. checkins.Select(c => new TeacherAvailabilityRowDto(
                c.Id,
                c.TeacherId,
                c.Teacher?.FullName ?? "",
                c.CheckinDate,
                c.Status.ToString(),
                c.DeclineReason,
                c.UnavailableDays,
                c.SentAt,
                c.RespondedAt,
                [
                    .. c.AffectedSessions
                        .OrderBy(a => a.Session?.ScheduledStart ?? default)
                        .Select(a =>
                        {
                            coverageBySession.TryGetValue(a.SessionId, out var coverage);

                            var substituteName = coverage?.ResolvedByUserId is { } resolverId
                                && resolverNames.TryGetValue(resolverId, out var name)
                                    ? name
                                    : null;

                            return new CoverageStatusDto(
                                a.SessionId,
                                a.Session?.Group?.Name ?? "",
                                a.Session?.ScheduledStart ?? default,
                                coverage?.Status.ToString(),
                                substituteName);
                        }),
                ])),
        ];
    }

    /// <summary>`"  Ism  "` -> `"%ism%"`. Bo'sh bo'lsa `null` (filtrlanmaydi).</summary>
    private static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        return "%" + EscapeLike(trimmed.ToLowerInvariant()) + "%";
    }

    /// <summary>LIKE metabelgilarini zararsizlantiradi (aks holda '%' butun jadvalni tortadi).</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Bir sahifadagi eng ko'p yozuv (loyihadagi boshqa ro'yxatlar bilan AYNI).</summary>
    private const int MaxPageSize = 100;

    // ================================================================ yordamchi

    private async Task SendPlainAsync(
        User recipient, string templateKey, string body, string idempotencyKey, CancellationToken ct,
        string? callbackData = null)
    {
        if (recipient.TelegramId is null) return;

        await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientUserId = recipient.Id,
                RecipientAddress = recipient.TelegramId.Value.ToString(CultureInfo.InvariantCulture),
                TemplateKey = templateKey,
                Body = body,
                CallbackData = callbackData,
                IdempotencyKey = idempotencyKey,
            },
            ct).ConfigureAwait(false);
    }

    private async Task<string?> TelegramAddressAsync(long userId, CancellationToken ct)
    {
        var telegramId = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TelegramId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return telegramId?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Necha kunga — tez tugmalar (1/2/3/5) uchun <c>CallbackData</c>.</summary>
    private static string DaysQuickButtons(long checkinId) =>
        TelegramTemplates.EncodeButtons(
        [
            [
                ("Bugun", $"av:days:{checkinId}:1"),
                ("2 kun", $"av:days:{checkinId}:2"),
                ("3 kun", $"av:days:{checkinId}:3"),
                ("5 kun", $"av:days:{checkinId}:5"),
            ],
        ]);

    private string FormatTime(LiveSession session) =>
        TimeZoneInfo
            .ConvertTime(session.ScheduledStart, timeZoneProvider.TimeZone)
            .ToString("HH:mm", CultureInfo.InvariantCulture);

    private DateOnly LocalToday(DateTimeOffset nowUtc) =>
        LocalWallClock.LocalDate(nowUtc, timeZoneProvider.TimeZone);

    /// <summary>
    /// Mahalliy <paramref name="date"/> dan boshlab <paramref name="days"/>
    /// kunlik oralig'ining UTC chegaralari — yarim ochiq: <c>[start, end)</c>.
    ///
    /// ★ <see cref="LocalWallClock"/> ORQALI (2026-08-17 da to'g'rilandi):
    /// ilgari bu yerda o'z hisobi bor edi va DST o'tishida MAVJUD BO'LMAGAN
    /// soatga tushib qolishi mumkin edi. `LocalWallClock` uni hisobga oladi
    /// va loyihadagi BARCHA sana-oraliq hisoblari (davomat, moliya, jadval)
    /// allaqachon shu yagona manbadan foydalanadi.
    /// </summary>
    private (DateTimeOffset Start, DateTimeOffset End) LocalDayRangeUtc(DateOnly date, int days)
    {
        var tz = timeZoneProvider.TimeZone;

        return (LocalWallClock.StartOfDayUtc(date, tz),
                LocalWallClock.StartOfDayUtc(date.AddDays(days), tz));
    }
}
