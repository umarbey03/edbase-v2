using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Absentees.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Absentees.Services;

/// <inheritdoc cref="IAbsenceNoticeService"/>
public sealed class AbsenceNoticeService(
    IApplicationDbContext db,
    INotificationOutbox outbox,
    IOutboxStatusReader outboxStatus,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IAbsenceNoticeService
{
    /// <summary>Telegram xabarining shablon kaliti — klaviatura tanlashda ishlatiladi.</summary>
    public const string TemplateKey = TelegramTemplates.AbsenceNotice;

    private const int MaxPageSize = 100;

    /// <summary>
    /// Bir marta yuborishda eng ko'p oluvchi.
    ///
    /// ★ SABAB: har oluvchi uchun alohida yozuv va navbat qatori
    /// yaratiladi. Chegara bo'lmasa, tasodifan butun markazga
    /// yuborilishi va Telegram tezlik chegarasiga urilishi mumkin edi.
    /// </summary>
    private const int MaxTargets = 200;

    // ================================================================= yuborish

    /// <inheritdoc />
    public async Task<SendAbsenceNoticeResultDto> SendAsync(
        SendAbsenceNoticeRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureCanSendAsync(actorId, ct);

        var body = (request.Body ?? string.Empty).Trim();

        if (body.Length == 0)
            throw Invalid(nameof(request.Body), "Xabar matnini kiriting.");

        if (request.Targets is null || request.Targets.Count == 0)
            throw Invalid(nameof(request.Targets), "Kamida bitta o'quvchini tanlang.");

        if (request.Targets.Count > MaxTargets)
            throw Invalid(nameof(request.Targets), $"Bir marta {MaxTargets} tadan ko'p yuborib bo'lmaydi.");

        if (request.TemplateId is { } templateId)
        {
            var exists = await db.MessageTemplates.AnyAsync(t => t.Id == templateId, ct);

            if (!exists) throw new NotFoundException(nameof(MessageTemplate), templateId);
        }

        var sessionIds = request.Targets.Select(t => t.SessionId).Distinct().ToList();
        var studentIds = request.Targets.Select(t => t.StudentId).Distinct().ToList();

        var sessions = await db.LiveSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                s.GroupId,
                s.ScheduledStart,
                GroupName = s.Group!.Name,
                TeacherName = s.Group.TeacherId == null
                    ? null
                    : db.Users.Where(u => u.Id == s.Group.TeacherId).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToDictionaryAsync(s => s.Id, ct);

        var students = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id) && u.Role == UserRole.Student)
            .Select(u => new { u.Id, u.FullName, u.TelegramId })
            .ToDictionaryAsync(u => u.Id, ct);

        var zone = timeZone.TimeZone;
        var now = clock.GetUtcNow();

        var notices = new List<(AbsenceNotice Notice, long? TelegramId)>();
        var skipped = 0;

        foreach (var target in request.Targets.Distinct())
        {
            if (!sessions.TryGetValue(target.SessionId, out var session)
                || !students.TryGetValue(target.StudentId, out var student))
            {
                skipped++;
                continue;
            }

            var rendered = AbsenceNoticePlaceholders.Apply(
                body, student.FullName, session.GroupName, session.ScheduledStart, zone, session.TeacherName);

            var notice = AbsenceNotice.Create(
                student.Id,
                session.GroupId,
                session.Id,
                session.ScheduledStart,
                rendered,
                actorId,
                toTelegram: student.TelegramId is not null,
                now);

            db.AbsenceNotices.Add(notice);
            notices.Add((notice, student.TelegramId));
        }

        if (notices.Count == 0)
            return new SendAbsenceNoticeResultDto(0, 0, 0, skipped);

        // ★ AVVAL SAQLANADI: navbat kaliti yozuv `Id` siga tayanadi
        //   (`GroupBroadcastService` dagi AYNI naqsh) — u faqat birinchi
        //   `SaveChanges` dan keyin ma'lum bo'ladi.
        await db.SaveChangesAsync(ct);

        var queued = 0;

        foreach (var (notice, telegramId) in notices)
        {
            if (telegramId is not { } chatId) continue;

            var key = $"absence_notice:{notice.Id}";
            notice.OutboxKey = key;

            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = notice.StudentId,
                    RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TemplateKey,
                    Body = NotificationText.Escape(notice.Body),
                    IdempotencyKey = key,
                },
                ct);

            queued++;
        }

        // Kalitlar va navbat yozuvlari BITTA tranzaksiyada (commit-then-send).
        await db.SaveChangesAsync(ct);

        return new SendAbsenceNoticeResultDto(
            notices.Count,
            queued,
            notices.Count(x => x.TelegramId is null),
            skipped);
    }

    // ================================================================= o'qish

    /// <inheritdoc />
    public async Task<PagedResult<AbsenceNoticeRowDto>> ListAsync(
        AbsenceNoticeListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = Filter(query);
        var total = await rows.CountAsync(ct);

        var items = await rows
            .OrderByDescending(n => n.SentAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                n.Id,
                n.StudentId,
                StudentName = n.Student!.FullName,
                StudentPhone = n.Student.Phone,
                n.GroupId,
                GroupName = n.Group!.Name,
                n.SessionId,
                n.SessionStart,
                n.Body,
                SentByName = n.SentBy!.FullName,
                n.SentAt,
                n.ToTelegram,
                n.OutboxKey,
            })
            .ToListAsync(ct);

        var statuses = await outboxStatus.GetStatusesAsync(
            items.Where(x => x.OutboxKey is not null).Select(x => x.OutboxKey!).ToList(), ct);

        var mapped = items.ConvertAll(x =>
        {
            var (status, deliveredAt, error) = Resolve(x.ToTelegram, x.OutboxKey, statuses);

            return new AbsenceNoticeRowDto(
                x.Id, x.StudentId, x.StudentName, x.StudentPhone,
                x.GroupId, x.GroupName, x.SessionId, x.SessionStart,
                x.Body, x.SentByName, x.SentAt, x.ToTelegram,
                status, deliveredAt, error);
        });

        // ★ YETKAZILISH FILTRI XOTIRADA: holat navbat jadvalida, ro'yxat
        //   esa boshqa jadvalda. Ularni SQL'da qo'shish uchun navbatni
        //   Application qatlamiga ochish kerak bo'lardi — bu esa ataylab
        //   qilinmagan (`IOutboxStatusReader` izohi). Sahifa kichik
        //   (20-100 qator), shuning uchun narxi sezilmaydi.
        if (!string.IsNullOrWhiteSpace(query.Delivery))
        {
            mapped = mapped
                .Where(r => string.Equals(r.DeliveryStatus, query.Delivery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new PagedResult<AbsenceNoticeRowDto>(mapped, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<AbsenceNoticeSummaryDto> GetSummaryAsync(
        AbsenceNoticeListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var flat = await Filter(query)
            .Select(n => new { n.ToTelegram, n.OutboxKey })
            .ToListAsync(ct);

        if (flat.Count == 0) return new AbsenceNoticeSummaryDto(0, 0, 0, 0, 0);

        var statuses = await outboxStatus.GetStatusesAsync(
            flat.Where(x => x.OutboxKey is not null).Select(x => x.OutboxKey!).ToList(), ct);

        var resolved = flat.ConvertAll(x => Resolve(x.ToTelegram, x.OutboxKey, statuses).Status);

        return new AbsenceNoticeSummaryDto(
            flat.Count,
            resolved.Count(s => s == "Sent"),
            resolved.Count(s => s == "Pending"),
            resolved.Count(s => s == "Failed"),
            resolved.Count(s => s == "NoTelegram"));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AbsenceNoticeTarget>> GetSentTargetsAsync(
        IReadOnlyCollection<long> sessionIds, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        await EnsureCanViewAsync(actorId, ct);

        if (sessionIds.Count == 0) return [];

        var ids = sessionIds.Distinct().ToList();

        return await db.AbsenceNotices.AsNoTracking()
            .Where(n => ids.Contains(n.SessionId))
            .Select(n => new AbsenceNoticeTarget(n.StudentId, n.SessionId))
            .Distinct()
            .ToListAsync(ct);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// Yozuv holatini navbat holatidan chiqaradi.
    ///
    /// ★ `NoTelegram` — XATO EMAS, HOLAT: o'quvchida Telegram ulanmagan.
    /// Uni "yuborilmadi" deb ko'rsatish kuratorni chalg'itardi — bu
    /// texnik nosozlik emas, shunchaki boshqa kanal (qo'ng'iroq) kerak.
    /// </summary>
    private static (string Status, DateTimeOffset? DeliveredAt, string? Error) Resolve(
        bool toTelegram, string? key, IReadOnlyDictionary<string, OutboxStatusDto> statuses)
    {
        if (!toTelegram || key is null) return ("NoTelegram", null, null);

        // Navbatda topilmasa — hali yozilmagan (yoki tozalangan): eng
        // xavfsiz talqin "kutilmoqda".
        if (!statuses.TryGetValue(key, out var status)) return ("Pending", null, null);

        return (status.Status, status.SentAt, status.LastError);
    }

    private IQueryable<AbsenceNotice> Filter(AbsenceNoticeListQuery query)
    {
        var rows = db.AbsenceNotices.AsNoTracking();
        var zone = timeZone.TimeZone;

        // Yarim ochiq oraliq mahalliy kun chegarasida — loyihadagi AYNI qoida.
        if (query.From is { } from)
        {
            var fromUtc = LocalWallClock.StartOfDayUtc(from, zone);
            rows = rows.Where(n => n.SentAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = LocalWallClock.StartOfDayUtc(to.AddDays(1), zone);
            rows = rows.Where(n => n.SentAt < toUtc);
        }

        if (query.GroupId is { } groupId) rows = rows.Where(n => n.GroupId == groupId);
        if (query.StudentId is { } studentId) rows = rows.Where(n => n.StudentId == studentId);

        var term = NormalizeSearch(query.Search);

        if (term is not null)
        {
#pragma warning disable CA1304, CA1311
            rows = rows.Where(n =>
                EF.Functions.Like(n.Student!.FullName.ToLower(), term)
                || EF.Functions.Like(n.Group!.Name.ToLower(), term)
                || EF.Functions.Like(n.Body.ToLower(), term));
#pragma warning restore CA1304, CA1311
        }

        return rows;
    }

    /// <summary>
    /// Xabar YUBORISH — o'quv bo'limi va admin.
    ///
    /// ★ NEGA USTOZ/KURATOR EMAS: bu o'quvchiga markaz nomidan ketadigan
    /// rasmiy xabar. Ro'yxatni ular ham ko'radi (qo'ng'iroq qilish
    /// uchun), lekin nom bilan xabar yuborish qarorini o'quv bo'limi
    /// qabul qiladi — `GroupBroadcastService` dagi AYNI qoida.
    /// </summary>
    private async Task EnsureCanSendAsync(long actorId, CancellationToken ct)
    {
        if (await RoleOfAsync(actorId, ct) is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Xabar yuborishni o'quv bo'limi va administrator bajaradi.");
    }

    /// <summary>Tarixni o'quvchidan boshqa hamma ko'radi (kurator ham ish yuritadi).</summary>
    private async Task EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        if (await RoleOfAsync(actorId, ct) is UserRole.Student)
            throw new ForbiddenException("Bu ro'yxatni o'quvchi ko'ra olmaydi.");
    }

    private async Task<UserRole> RoleOfAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct);

        return role ?? throw new NotFoundException(nameof(User), actorId);
    }

    private static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        return "%" + trimmed.ToLowerInvariant()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
