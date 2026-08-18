using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Penalties.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Penalties.Services;

/// <inheritdoc cref="IPenaltyService"/>
public sealed class PenaltyService(
    IApplicationDbContext db,
    ISettingsResolver settings,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IPenaltyService
{
    /// <summary>
    /// Kechikish uchun CHIDAM (daqiqa) — frontenddagi bilan AYNI.
    ///
    /// Soatlar bir necha soniyaga farq qilishi odatiy hol. Chegara
    /// bo'lmasa deyarli har dars "1 daqiqa kechikdi" bo'lib jarimaga
    /// tushardi va butun ko'rsatkich ishonchini yo'qotardi.
    /// </summary>
    private const int LateToleranceMinutes = 1;

    private const int MaxPageSize = 100;

    // ================================================================= o'qish

    /// <inheritdoc />
    public async Task<PagedResult<PenaltyRowDto>> ListAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = Filter(query);
        var total = await rows.CountAsync(ct);

        var items = await rows
            // Yangidan eskiga; `Id` — teng vaqtli yozuvlarda tartib
            // so'rovdan so'rovga sakramasin.
            .OrderByDescending(p => p.OccurredAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.UserId,
                UserName = p.User!.FullName,
                UserRole = p.User.Role,
                p.SessionId,
                GroupName = p.Session == null ? null : p.Session.Group!.Name,
                SessionScheduledStart = p.Session == null ? (DateTimeOffset?)null : p.Session.ScheduledStart,
                SessionActualStart = p.Session == null ? null : p.Session.ActualStart,
                p.Kind,
                p.Status,
                p.LateMinutes,
                p.Amount,
                p.Reason,
                p.OccurredAt,
                p.PeriodStart,
                CreatedByName = p.CreatedBy == null ? null : p.CreatedBy.FullName,
                ReviewedByName = p.ReviewedBy == null ? null : p.ReviewedBy.FullName,
                p.ReviewedAt,
            })
            .ToListAsync(ct);

        // `enum.ToString()` XOTIRADA — so'rov ichida yozilsa SQL'ga
        // tarjima qilishga majburlardi (loyihadagi AYNI qoida).
        var mapped = items.ConvertAll(x => new PenaltyRowDto(
            x.Id, x.UserId, x.UserName, x.UserRole.ToString(),
            x.SessionId, x.GroupName, x.SessionScheduledStart, x.SessionActualStart,
            x.Kind.ToString(), x.Status.ToString(), x.LateMinutes, x.Amount, x.Reason,
            x.OccurredAt, x.PeriodStart, x.CreatedByName, x.ReviewedByName, x.ReviewedAt));

        return new PagedResult<PenaltyRowDto>(mapped, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<PenaltySummaryDto> GetSummaryAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var flat = await Filter(query)
            .Select(p => new { p.Status, p.Amount })
            .ToListAsync(ct);

        int CountOf(PenaltyStatus status) => flat.Count(x => x.Status == status);
        decimal SumOf(PenaltyStatus status) => flat.Where(x => x.Status == status).Sum(x => x.Amount);

        return new PenaltySummaryDto(
            Total: flat.Count,
            PendingCount: CountOf(PenaltyStatus.Pending),
            ApprovedCount: CountOf(PenaltyStatus.Approved),
            CancelledCount: CountOf(PenaltyStatus.Cancelled),
            PendingAmount: SumOf(PenaltyStatus.Pending),
            ApprovedAmount: SumOf(PenaltyStatus.Approved));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PenaltyByUserDto>> GetByUserAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var flat = await Filter(query)
            .Select(p => new
            {
                p.UserId,
                UserName = p.User!.FullName,
                UserRole = p.User.Role,
                p.Status,
                p.Amount,
                p.LateMinutes,
            })
            .ToListAsync(ct);

        return flat
            .GroupBy(x => x.UserId)
            .Select(g =>
            {
                var first = g.First();

                return new PenaltyByUserDto(
                    g.Key,
                    first.UserName,
                    first.UserRole.ToString(),
                    g.Count(x => x.Status == PenaltyStatus.Pending),
                    g.Count(x => x.Status == PenaltyStatus.Approved),
                    g.Where(x => x.Status == PenaltyStatus.Approved).Sum(x => x.Amount),
                    g.Sum(x => x.LateMinutes ?? 0));
            })
            // Eng ko'p ushlab qolingani birinchi — diqqat talab qiladigan
            // xodim tepada tursin.
            .OrderByDescending(x => x.ApprovedAmount)
            .ThenByDescending(x => x.PendingCount)
            .ThenBy(x => x.UserName, StringComparer.Ordinal)
            .ToList();
    }

    // ================================================================= yozish

    /// <inheritdoc />
    public async Task<PenaltyRowDto> CreateManualAsync(
        CreateManualPenaltyRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ★ KIRITISH — O'QUV BO'LIMIGA HAM OCHIQ (tasdiqlashdan FARQLI):
        //   qo'lda kiritilgan jarima ham `Pending` bo'lib tug'iladi va
        //   oylikka TEGMAYDI. Pulga aylanadigan qadam — tasdiqlash, va
        //   u faqat adminda (`EnsureCanManageAsync`).
        await EnsureCanViewAsync(actorId, ct);

        var target = await db.Users.AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.Id, u.Role })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        // 🔴 FAQAT USTOZ VA KURATOR: jarima — dars o'tish majburiyatining
        //    natijasi. O'quvchiga yoki adminga yozish ma'nosiz va u
        //    oylik tuzatmasiga aylanganda tushunarsiz yozuv qoldirardi.
        if (target.Role is not (UserRole.Teacher or UserRole.Assistant))
            throw new ConflictException("Jarima faqat ustoz yoki kuratorga yoziladi.");

        var now = clock.GetUtcNow();
        var occurredAt = request.OccurredAt ?? now;

        var penalty = Penalty.Manual(
            target.Id,
            request.Amount,
            request.Reason,
            actorId,
            occurredAt,
            PeriodOf(occurredAt));

        db.Penalties.Add(penalty);
        await db.SaveChangesAsync(ct);

        return await GetRowAsync(penalty.Id, ct);
    }

    /// <inheritdoc />
    public async Task<PenaltyRowDto> ApproveAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        await EnsureCanManageAsync(actorId, ct);

        var penalty = await db.Penalties.AsTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(Penalty), id);

        var now = clock.GetUtcNow();
        penalty.Approve(actorId, now);

        // ═══════════════════════════════════════════════════════════════
        // OYLIKKA MANFIY TUZATMA — TASDIQLASHNING BUTUN MA'NOSI.
        //
        // ★ AYNI TRANZAKSIYA: jarimaning holati va oylik tuzatmasi BIRGA
        //   saqlanadi. Alohida bo'lsa "tasdiqlangan, lekin oylikda yo'q"
        //   (yoki teskarisi) yarim holati yuzaga kelardi.
        //
        // ★ MANFIY SUMMA: `PayrollAdjustment.Amount` kelishuvi bo'yicha
        //   musbat — bonus, manfiy — ushlab qolish.
        // ═══════════════════════════════════════════════════════════════
        var adjustment = new PayrollAdjustment
        {
            UserId = penalty.UserId,
            PeriodStart = penalty.PeriodStart,
            Amount = -penalty.Amount,
            Reason = $"Jarima: {penalty.Reason}",
            CreatedById = actorId,
        };

        adjustment.Validate();
        db.PayrollAdjustments.Add(adjustment);

        await db.SaveChangesAsync(ct);

        // Havola ikkinchi saqlashda yoziladi: `adjustment.Id` faqat
        // birinchi `SaveChanges` dan keyin ma'lum bo'ladi.
        penalty.PayrollAdjustmentId = adjustment.Id;
        await db.SaveChangesAsync(ct);

        return await GetRowAsync(penalty.Id, ct);
    }

    /// <inheritdoc />
    public async Task<PenaltyRowDto> CancelAsync(
        long id, CancelPenaltyRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureCanManageAsync(actorId, ct);

        var penalty = await db.Penalties.AsTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(Penalty), id);

        penalty.Cancel(actorId, request.Reason, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        return await GetRowAsync(penalty.Id, ct);
    }

    // ================================================================= avtomatik aniqlash

    /// <inheritdoc />
    public async Task DetectLateStartAsync(LiveSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.HostId is not { } hostId) return;
        if (session.ActualStart is not { } actualStart) return;

        var lateMinutes = (int)Math.Floor((actualStart - session.ScheduledStart).TotalMinutes);
        if (lateMinutes <= LateToleranceMinutes) return;

        var perMinute = await MoneySettingAsync(SettingsRegistry.Keys.PenaltyLatePerMinute, ct);
        if (perMinute <= 0) return;

        // Bu dars uchun kechikish jarimasi allaqachon bormi. Baza
        // darajasidagi unikal indeks ham bor — bu tekshiruv faqat
        // ortiqcha istisnodan saqlaydi.
        var exists = await db.Penalties
            .AnyAsync(p => p.SessionId == session.Id && p.Kind == PenaltyKind.LateStart, ct);

        if (exists) return;

        db.Penalties.Add(Penalty.ForLateStart(
            hostId, session.Id, lateMinutes, perMinute, actualStart, PeriodOf(actualStart)));
    }

    /// <inheritdoc />
    public async Task<int> ScanMissedLessonsAsync(CancellationToken ct = default)
    {
        var amount = await MoneySettingAsync(SettingsRegistry.Keys.PenaltyMissedLesson, ct);
        if (amount <= 0) return 0;

        var now = clock.GetUtcNow();

        // ★ "O'TILMAGAN" TA'RIFI: rejadagi tugash vaqti O'TGAN, lekin
        //   dars hamon `Scheduled` — ya'ni hech qachon boshlanmagan.
        //   Bekor qilingani sanalmaydi (bayram yoki o'quv bo'limi qarori).
        var candidates = await db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Status == SessionStatus.Scheduled
                && s.ScheduledEnd < now
                && s.HostId != null
                && !db.Penalties.Any(p => p.SessionId == s.Id && p.Kind == PenaltyKind.MissedLesson))
            .Select(s => new { s.Id, s.HostId, s.ScheduledStart })
            .Take(200)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        foreach (var session in candidates)
        {
            db.Penalties.Add(Penalty.ForMissedLesson(
                session.HostId!.Value,
                session.Id,
                amount,
                session.ScheduledStart,
                PeriodOf(session.ScheduledStart)));
        }

        await db.SaveChangesAsync(ct);

        return candidates.Count;
    }

    // ================================================================= yordamchi

    private IQueryable<Penalty> Filter(PenaltyListQuery query)
    {
        var rows = db.Penalties.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Period))
        {
            var period = BillingPeriod.Parse(query.Period).FirstDay();
            rows = rows.Where(p => p.PeriodStart == period);
        }

        if (query.UserId is { } userId) rows = rows.Where(p => p.UserId == userId);
        if (query.Kind is { } kind) rows = rows.Where(p => p.Kind == kind);
        if (query.Status is { } status) rows = rows.Where(p => p.Status == status);

        var term = NormalizeSearch(query.Search);

        if (term is not null)
        {
#pragma warning disable CA1304, CA1311
            rows = rows.Where(p =>
                EF.Functions.Like(p.User!.FullName.ToLower(), term)
                || EF.Functions.Like(p.Reason.ToLower(), term));
#pragma warning restore CA1304, CA1311
        }

        return rows;
    }

    /// <summary>
    /// Bitta yozuvni qaytaradi (yaratish/tasdiqlash/bekor qilishdan keyin).
    ///
    /// ★ RUXSAT QAYTA TEKSHIRILMAYDI: bu metod FAQAT tekshiruvdan
    /// allaqachon o'tgan amallardan chaqiriladi.
    /// </summary>
    private async Task<PenaltyRowDto> GetRowAsync(long id, CancellationToken ct)
    {
        var row = await db.Penalties.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.UserId,
                UserName = p.User!.FullName,
                UserRole = p.User.Role,
                p.SessionId,
                GroupName = p.Session == null ? null : p.Session.Group!.Name,
                SessionScheduledStart = p.Session == null ? (DateTimeOffset?)null : p.Session.ScheduledStart,
                SessionActualStart = p.Session == null ? null : p.Session.ActualStart,
                p.Kind,
                p.Status,
                p.LateMinutes,
                p.Amount,
                p.Reason,
                p.OccurredAt,
                p.PeriodStart,
                CreatedByName = p.CreatedBy == null ? null : p.CreatedBy.FullName,
                ReviewedByName = p.ReviewedBy == null ? null : p.ReviewedBy.FullName,
                p.ReviewedAt,
            })
            .FirstAsync(ct);

        return new PenaltyRowDto(
            row.Id, row.UserId, row.UserName, row.UserRole.ToString(),
            row.SessionId, row.GroupName, row.SessionScheduledStart, row.SessionActualStart,
            row.Kind.ToString(), row.Status.ToString(), row.LateMinutes, row.Amount, row.Reason,
            row.OccurredAt, row.PeriodStart, row.CreatedByName, row.ReviewedByName, row.ReviewedAt);
    }

    /// <summary>Hodisa sanasidan oylik davrini (oyning 1-kuni) chiqaradi.</summary>
    private DateOnly PeriodOf(DateTimeOffset instant) =>
        BillingPeriod.FromDate(LocalWallClock.LocalDate(instant, timeZone.TimeZone)).FirstDay();

    /// <summary>Pul sozlamasini o'qiydi; bo'sh yoki buzuq bo'lsa 0.</summary>
    private async Task<decimal> MoneySettingAsync(string key, CancellationToken ct)
    {
        var raw = await settings.GetValueAsync(key, ct);

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    private async Task EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        var role = await RoleOfAsync(actorId, ct);

        if (role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Jarimalar panelini faqat o'quv bo'limi va administrator ko'radi.");
    }

    /// <summary>
    /// 🔴 TASDIQLASH/BEKOR QILISH — FAQAT ADMIN: bu amal PULGA aylanadi
    /// (oylikdan ushlab qolinadi). O'quv bo'limi jarimani ko'radi va
    /// qo'lda kirita oladi, lekin oylikka ta'sir qiladigan qarorni
    /// administrator qabul qiladi.
    /// </summary>
    private async Task EnsureCanManageAsync(long actorId, CancellationToken ct)
    {
        var role = await RoleOfAsync(actorId, ct);

        if (role is not UserRole.Admin)
            throw new ForbiddenException("Jarimani tasdiqlash yoki bekor qilishni faqat administrator bajaradi.");
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
}
