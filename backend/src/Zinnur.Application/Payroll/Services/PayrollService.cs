using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payroll.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Payroll.Services;

/// <summary>
/// <see cref="IPayrollService"/> ning amalga oshirilishi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ SNAPSHOT — HAR SO'ROVDA QAYTA HISOBLANMAYDI (2026-08-16 dan)
/// ══════════════════════════════════════════════════════════════════════
/// Ilgari natija HAR SO'ROVDA <c>LiveSessions</c> + <c>Attendances</c> +
/// <c>TeacherRates</c> dan qayta hisoblanardi — bu <see cref="Payment"/>/
/// <see cref="Tariff"/> dagi "narx TARIXI saqlanadi" tamoyilidan FARQ
/// QILARDI: stavka tahrirlansa yoki o'chirilsa, O'TGAN OY hisoboti ham
/// jimgina o'zgarib qolardi. Endi bu servis <see cref="SessionPayout"/>
/// jadvalini O'QIYDI — snapshot dars YAKUNLANGANDA (`LessonAccrualService.
/// ReconcilePayoutAsync`) bir marta yoziladi va QOTIB QOLADI.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ 2026-08-16 — BAZA OYLIK + KPI, TASDIQLASH/TO'LOV, QO'LDA TUZATISH
/// ══════════════════════════════════════════════════════════════════════
/// Tadqiqot (Tutorbase/GetCourse/Skyeng/Preply) asosida uchta yangi qism
/// qo'shildi:
///   1) BAZA OYLIK + KPI (asosan kurator uchun, `TeacherRate.BaseSalary`/
///      `ActiveStudentBonusRate`) — SESSIYAGA BOG'LIQ EMAS, shuning uchun
///      bu ikkovi <see cref="SessionPayout"/>dan emas, DAVR OXIRIDAGI holat
///      bo'yicha JONLI hisoblanadi (`BuildRateContextAsync`).
///   2) TASDIQLASH/TO'LOV (<see cref="PayrollApproval"/>) — Draft → Approved
///      → Paid. Yozuv topilmasa davr Draft hisoblanadi.
///   3) QO'LDA TUZATISH (<see cref="PayrollAdjustment"/>) — faqat Draft
///      davrda qo'shiladi/o'chiriladi (`EnsureDraftAsync`).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ RUXSAT — FAQAT ADMIN
/// ══════════════════════════════════════════════════════════════════════
/// <see cref="Zinnur.Application.Payments.Services.PaymentService"/> dan
/// ATAYLAB FARQ QILADI (u yerda Academic HAM kiradi): stavkani boshqarish
/// va xodimlar haqini ko'rish — markazning eng nozik ichki ma'lumoti,
/// faqat Admin.
/// </summary>
public sealed class PayrollService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IPayrollService
{
    private const decimal MaxAmount = 1_000_000_000m;

    public async Task<PayrollSummaryDto> GetSummaryAsync(
        string? period, long actorId, CancellationToken ct = default)
    {
        await EnsureAdminAsync(actorId, ct);

        var billingPeriod = ParsePeriodOrCurrent(period);
        var (fromUtc, toUtc) = billingPeriod.UtcRange(timeZone.TimeZone);
        var periodStart = billingPeriod.FirstDay();
        var periodEndDate = billingPeriod.AddMonths(1).FirstDay().AddDays(-1);

        var payouts = await (
            from p in db.SessionPayouts.AsNoTracking()
            join s in db.LiveSessions.AsNoTracking() on p.SessionId equals s.Id
            where s.ScheduledStart >= fromUtc && s.ScheduledStart < toUtc
            select new PayoutRow(
                p.UserId, p.Role, p.AttendedStudents, p.SessionRate, p.BonusAmount,
                p.RateMissing, p.Excluded))
            .ToListAsync(ct);

        // ── BAZA OYLIK/KPI NOMZODLARI: darsi bo'lmasa ham ro'yxatda ko'rinsin ──
        //
        // Masalan yangi qabul qilingan kurator — hali biror darsi yo'q, lekin
        // baza oylik + KPI bonusi allaqachon hisoblanishi kerak.
        var staffUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Teacher || u.Role == UserRole.Assistant))
            .Select(u => new { u.Id, u.Role })
            .ToListAsync(ct);

        var rates = await db.TeacherRates.AsNoTracking()
            .Where(r => r.IsActive && r.ActiveFrom <= periodEndDate)
            .ToListAsync(ct);

        var ratesByUser = staffUsers.ToDictionary(
            u => u.Id, u => TeacherRateSelection.PickRate(rates, u.Id, u.Role, periodEndDate));

        var payoutUserIds = payouts.Select(p => p.UserId).Distinct();
        var salaryUserIds = ratesByUser
            .Where(kv => kv.Value is { BaseSalary: > 0 } or { ActiveStudentBonusRate: > 0 })
            .Select(kv => kv.Key);
        var relevantUserIds = payoutUserIds.Union(salaryUserIds).ToList();

        if (relevantUserIds.Count == 0)
            return new PayrollSummaryDto(billingPeriod.ToString(), [], 0m);

        var users = await db.Users.AsNoTracking()
            .Where(u => relevantUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Role })
            .ToDictionaryAsync(u => u.Id, ct);

        var activeStudentCounts = await GetActiveStudentCountsAsync(relevantUserIds, ct);
        var adjustmentTotals = await GetAdjustmentTotalsAsync(relevantUserIds, periodStart, ct);
        var approvals = await GetApprovalsAsync(relevantUserIds, periodStart, ct);

        var rows = new List<PayrollSummaryRowDto>();

        foreach (var userId in relevantUserIds)
        {
            if (!users.TryGetValue(userId, out var user)) continue;

            var userPayouts = payouts.Where(p => p.UserId == userId).ToList();

            var baseAmount = userPayouts.Where(p => !p.Excluded).Sum(p => p.SessionRate);
            var bonusAmount = userPayouts.Where(p => !p.Excluded).Sum(p => p.BonusAmount);
            var missingRate = userPayouts.Count(p => p.RateMissing && !p.Excluded);
            var excludedCount = userPayouts.Count(p => p.Excluded);

            ratesByUser.TryGetValue(userId, out var rate);
            var baseSalaryAmount = rate?.BaseSalary ?? 0m;
            activeStudentCounts.TryGetValue(userId, out var activeStudents);
            var kpiBonusAmount = activeStudents * (rate?.ActiveStudentBonusRate ?? 0m);

            adjustmentTotals.TryGetValue(userId, out var adjustmentAmount);
            approvals.TryGetValue(userId, out var approval);

            var total = baseAmount + bonusAmount + baseSalaryAmount + kpiBonusAmount + adjustmentAmount;

            rows.Add(new PayrollSummaryRowDto(
                userId, user.FullName, user.Role, userPayouts.Count,
                userPayouts.Sum(p => p.AttendedStudents),
                baseAmount, bonusAmount, baseSalaryAmount, activeStudents, kpiBonusAmount,
                adjustmentAmount, total, missingRate, excludedCount,
                approval?.Status ?? PayrollApprovalStatus.Draft, approval?.ApprovedAt, approval?.PaidAt));
        }

        rows.Sort((a, b) => b.Total.CompareTo(a.Total));

        return new PayrollSummaryDto(billingPeriod.ToString(), rows, rows.Sum(r => r.Total));
    }

    public async Task<PayrollDetailDto> GetDetailAsync(
        long userId, string? period, long actorId, CancellationToken ct = default)
    {
        await EnsureAdminAsync(actorId, ct);

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FullName, u.Role })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), userId);

        var billingPeriod = ParsePeriodOrCurrent(period);
        var (fromUtc, toUtc) = billingPeriod.UtcRange(timeZone.TimeZone);
        var periodStart = billingPeriod.FirstDay();
        var periodEndDate = billingPeriod.AddMonths(1).FirstDay().AddDays(-1);

        var sessionRows = await (
            from p in db.SessionPayouts.AsNoTracking()
            join s in db.LiveSessions.AsNoTracking() on p.SessionId equals s.Id
            where p.UserId == userId && s.ScheduledStart >= fromUtc && s.ScheduledStart < toUtc
            orderby s.ScheduledStart
            select new
            {
                SessionId = p.SessionId,
                s.GroupId,
                GroupName = s.Group!.Name,
                s.ScheduledStart,
                p.AttendedStudents,
                p.SessionRate,
                p.BonusAmount,
                p.RateMissing,
                p.Excluded,
                p.PremiumMultiplierApplied,
            })
            .ToListAsync(ct);

        var sessions = sessionRows.ConvertAll(s => new PayrollSessionRowDto(
            s.SessionId, s.GroupId, s.GroupName, s.ScheduledStart, s.AttendedStudents,
            s.Excluded ? 0m : s.SessionRate, s.Excluded ? 0m : s.BonusAmount,
            s.Excluded ? 0m : s.SessionRate + s.BonusAmount, s.RateMissing, s.Excluded,
            s.PremiumMultiplierApplied));

        var rates = await db.TeacherRates.AsNoTracking()
            .Where(r => r.IsActive && r.ActiveFrom <= periodEndDate)
            .ToListAsync(ct);
        var rate = TeacherRateSelection.PickRate(rates, userId, user.Role, periodEndDate);

        var baseSalaryAmount = rate?.BaseSalary ?? 0m;
        var activeStudentCounts = await GetActiveStudentCountsAsync([userId], ct);
        activeStudentCounts.TryGetValue(userId, out var activeStudentCount);
        var kpiBonusAmount = activeStudentCount * (rate?.ActiveStudentBonusRate ?? 0m);

        var adjustments = await ProjectAdjustments(db.PayrollAdjustments.AsNoTracking()
                .Where(a => a.UserId == userId && a.PeriodStart == periodStart))
            .ToListAsync(ct);

        var approvals = await GetApprovalsAsync([userId], periodStart, ct);
        approvals.TryGetValue(userId, out var approval);

        var grandTotal = sessions.Sum(s => s.Total) + baseSalaryAmount + kpiBonusAmount
            + adjustments.Sum(a => a.Amount);

        return new PayrollDetailDto(
            user.Id, user.FullName, user.Role, billingPeriod.ToString(), sessions,
            baseSalaryAmount, activeStudentCount, kpiBonusAmount, adjustments, grandTotal,
            approval?.Status ?? PayrollApprovalStatus.Draft, approval?.ApprovedAt, approval?.PaidAt);
    }

    // ================================================================= tuzatish

    public async Task<PayrollAdjustmentDto> CreateAdjustmentAsync(
        CreatePayrollAdjustmentRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureAdminAsync(actorId, ct);

        var billingPeriod = ParsePeriod(request.Period);
        var periodStart = billingPeriod.FirstDay();

        await EnsureDraftAsync(request.UserId, periodStart, ct);

        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == request.UserId, ct))
            throw new NotFoundException(nameof(User), request.UserId);

        if (request.Amount < -MaxAmount || request.Amount > MaxAmount)
            throw Invalid("amount", "Tuzatish summasi 1 000 000 000 dan oshmasligi kerak.");

        var adjustment = new PayrollAdjustment
        {
            UserId = request.UserId,
            PeriodStart = periodStart,
            Amount = request.Amount,
            Reason = (request.Reason ?? string.Empty).Trim(),
            CreatedById = actorId,
        };
        adjustment.Validate();

        db.PayrollAdjustments.Add(adjustment);
        await SaveAsync(ct);

        return await ProjectAdjustments(db.PayrollAdjustments.AsNoTracking().Where(a => a.Id == adjustment.Id))
            .FirstAsync(ct);
    }

    public async Task DeleteAdjustmentAsync(long id, long actorId, CancellationToken ct = default)
    {
        await EnsureAdminAsync(actorId, ct);

        var adjustment = await db.PayrollAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(PayrollAdjustment), id);

        await EnsureDraftAsync(adjustment.UserId, adjustment.PeriodStart, ct);

        db.PayrollAdjustments.Remove(adjustment);
        await SaveAsync(ct);
    }

    // ================================================================= tasdiqlash/to'lov

    public async Task ApproveAsync(
        PayrollPeriodActionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureAdminAsync(actorId, ct);

        var detail = await GetDetailAsync(request.UserId, request.Period, actorId, ct);

        if (detail.ApprovalStatus != PayrollApprovalStatus.Draft)
            throw new ConflictException("Bu davr allaqachon tasdiqlangan yoki to'langan.");

        var periodStart = ParsePeriod(request.Period).FirstDay();
        var now = clock.GetUtcNow();

        var approval = await db.PayrollApprovals
            .FirstOrDefaultAsync(a => a.UserId == request.UserId && a.PeriodStart == periodStart, ct);

        if (approval is null)
        {
            approval = new PayrollApproval { UserId = request.UserId, PeriodStart = periodStart };
            db.PayrollApprovals.Add(approval);
        }

        approval.Status = PayrollApprovalStatus.Approved;
        approval.SnapshotTotalAmount = detail.GrandTotal;
        approval.ApprovedById = actorId;
        approval.ApprovedAt = now;
        approval.UpdatedAt = now;

        await SaveAsync(ct);
    }

    public async Task MarkPaidAsync(
        PayrollPeriodActionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureAdminAsync(actorId, ct);

        var periodStart = ParsePeriod(request.Period).FirstDay();

        var approval = await db.PayrollApprovals
            .FirstOrDefaultAsync(a => a.UserId == request.UserId && a.PeriodStart == periodStart, ct)
            ?? throw new ConflictException("Bu davr hali tasdiqlanmagan — avval tasdiqlang.");

        if (approval.Status != PayrollApprovalStatus.Approved)
            throw new ConflictException("Faqat tasdiqlangan davrni to'landi deb belgilash mumkin.");

        var now = clock.GetUtcNow();
        approval.Status = PayrollApprovalStatus.Paid;
        approval.PaidById = actorId;
        approval.PaidAt = now;
        approval.UpdatedAt = now;

        await SaveAsync(ct);
    }

    /// <summary>Faqat Draft davrda o'zgartirish mumkin — tasdiqlangandan keyin summa "muzlaydi".</summary>
    private async Task EnsureDraftAsync(long userId, DateOnly periodStart, CancellationToken ct)
    {
        var status = await db.PayrollApprovals.AsNoTracking()
            .Where(a => a.UserId == userId && a.PeriodStart == periodStart)
            .Select(a => (PayrollApprovalStatus?)a.Status)
            .FirstOrDefaultAsync(ct);

        if (status is not (null or PayrollApprovalStatus.Draft))
            throw new ConflictException("Bu davr allaqachon tasdiqlangan/to'langan — tuzatish qo'shib/o'chirib bo'lmaydi.");
    }

    // ================================================================= stavka

    public async Task<IReadOnlyList<TeacherRateDto>> ListRatesAsync(
        long actorId, CancellationToken ct = default)
    {
        await EnsureAdminAsync(actorId, ct);

        return await ProjectRates(db.TeacherRates.AsNoTracking()
                .OrderByDescending(r => r.UserId != null ? 1 : 0)
                .ThenByDescending(r => r.ActiveFrom)
                .ThenByDescending(r => r.Id))
            .ToListAsync(ct);
    }

    public async Task<TeacherRateDto> CreateRateAsync(
        CreateTeacherRateRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureAdminAsync(actorId, ct);

        var rate = new TeacherRate
        {
            UserId = request.UserId,
            Role = request.Role,
            PerSessionRate = request.PerSessionRate,
            PerStudentBonusRate = request.PerStudentBonusRate,
            BaseSalary = request.BaseSalary,
            ActiveStudentBonusRate = request.ActiveStudentBonusRate,
            WeekendHolidayMultiplier = request.WeekendHolidayMultiplier,
            ActiveFrom = RequireDate(request.ActiveFrom, nameof(request.ActiveFrom)),
            IsActive = request.IsActive,
        };

        await ValidateRateAsync(rate, ct);

        db.TeacherRates.Add(rate);
        await SaveAsync(ct);

        return await GetRateAsync(rate.Id, ct);
    }

    public async Task<TeacherRateDto> UpdateRateAsync(
        long id, UpdateTeacherRateRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureAdminAsync(actorId, ct);

        var rate = await db.TeacherRates.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(TeacherRate), id);

        rate.UserId = request.UserId;
        rate.Role = request.Role;
        rate.PerSessionRate = request.PerSessionRate;
        rate.PerStudentBonusRate = request.PerStudentBonusRate;
        rate.BaseSalary = request.BaseSalary;
        rate.ActiveStudentBonusRate = request.ActiveStudentBonusRate;
        rate.WeekendHolidayMultiplier = request.WeekendHolidayMultiplier;
        rate.ActiveFrom = RequireDate(request.ActiveFrom, nameof(request.ActiveFrom));
        rate.IsActive = request.IsActive;

        await ValidateRateAsync(rate, ct);

        await SaveAsync(ct);

        return await GetRateAsync(rate.Id, ct);
    }

    public async Task DeleteRateAsync(long id, long actorId, CancellationToken ct = default)
    {
        await EnsureAdminAsync(actorId, ct);

        var rate = await db.TeacherRates.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(TeacherRate), id);

        db.TeacherRates.Remove(rate);
        await SaveAsync(ct);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// Kurator/xodimning DAVR OXIRIDAGI faol o'quvchilari — KPI hisob asosi
    /// (`TeacherRate.ActiveStudentBonusRate` izohi). <c>Group.AssistantId</c>
    /// bo'lgan HAR QANDAY guruhdagi (oddiy YOKI kurator turi) faol a'zolar
    /// yig'indisi — kurator guruhida to'g'ridan-to'g'ri a'zo bo'lmagani
    /// uchun (`GroupService` dagi bilan AYNI qoida) bu yig'indi ikki marta
    /// sanamaydi.
    /// </summary>
    private async Task<Dictionary<long, int>> GetActiveStudentCountsAsync(
        IEnumerable<long> userIds, CancellationToken ct)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0) return [];

        return await db.GroupMembers.AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active
                     && m.Group!.AssistantId != null
                     && ids.Contains(m.Group!.AssistantId!.Value))
            .GroupBy(m => m.Group!.AssistantId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
    }

    private async Task<Dictionary<long, decimal>> GetAdjustmentTotalsAsync(
        IEnumerable<long> userIds, DateOnly periodStart, CancellationToken ct)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0) return [];

        return await db.PayrollAdjustments.AsNoTracking()
            .Where(a => ids.Contains(a.UserId) && a.PeriodStart == periodStart)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Sum = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.UserId, x => x.Sum, ct);
    }

    private async Task<Dictionary<long, PayrollApproval>> GetApprovalsAsync(
        IEnumerable<long> userIds, DateOnly periodStart, CancellationToken ct)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0) return [];

        return await db.PayrollApprovals.AsNoTracking()
            .Where(a => ids.Contains(a.UserId) && a.PeriodStart == periodStart)
            .ToDictionaryAsync(a => a.UserId, ct);
    }

    private static IQueryable<PayrollAdjustmentDto> ProjectAdjustments(IQueryable<PayrollAdjustment> rows) =>
        rows.OrderByDescending(a => a.CreatedAt)
            .Select(a => new PayrollAdjustmentDto(
                a.Id, a.UserId, a.PeriodStart, a.Amount, a.Reason, a.CreatedById,
                a.CreatedBy == null ? null : a.CreatedBy.FullName, a.CreatedAt));

    private async Task ValidateRateAsync(TeacherRate rate, CancellationToken ct)
    {
        if (!Enum.IsDefined(rate.Role) || rate.Role is not (UserRole.Teacher or UserRole.Assistant))
            throw Invalid("role", "Stavka faqat ustoz yoki kurator uchun bo'lishi mumkin.");

        if (rate.PerSessionRate < 0 || rate.PerSessionRate > MaxAmount)
            throw Invalid("perSessionRate", "Dars stavkasi 0..1 000 000 000 oralig'ida bo'lishi kerak.");

        if (rate.PerStudentBonusRate < 0 || rate.PerStudentBonusRate > MaxAmount)
            throw Invalid("perStudentBonusRate", "Bonus stavkasi 0..1 000 000 000 oralig'ida bo'lishi kerak.");

        if (rate.BaseSalary < 0 || rate.BaseSalary > MaxAmount)
            throw Invalid("baseSalary", "Baza oylik 0..1 000 000 000 oralig'ida bo'lishi kerak.");

        if (rate.ActiveStudentBonusRate < 0 || rate.ActiveStudentBonusRate > MaxAmount)
            throw Invalid("activeStudentBonusRate", "KPI bonusi 0..1 000 000 000 oralig'ida bo'lishi kerak.");

        if (rate.WeekendHolidayMultiplier is { } multiplier && (multiplier < 1 || multiplier > 10))
            throw Invalid("weekendHolidayMultiplier", "Ko'paytiruvchi 1..10 oralig'ida bo'lishi kerak.");

        if (rate.UserId is { } userId)
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Role })
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException(nameof(User), userId);

            if (user.Role != rate.Role)
            {
                throw Invalid("userId",
                    "Tanlangan xodimning haqiqiy roli stavkadagi rol bilan mos emas.");
            }
        }

        rate.Validate();
    }

    private async Task EnsureAdminAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN — `PaymentService.LoadActorAsync`
        // bilan AYNI sabab: eski token bilan pasaytirilgan rol ishlamasin.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        if (actor.Role != UserRole.Admin)
        {
            throw new ForbiddenException(
                "Oylik hisoblash paneliga faqat administrator kira oladi.");
        }
    }

    private async Task<TeacherRateDto> GetRateAsync(long id, CancellationToken ct) =>
        await ProjectRates(db.TeacherRates.AsNoTracking().Where(r => r.Id == id))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(TeacherRate), id);

    private static IQueryable<TeacherRateDto> ProjectRates(IQueryable<TeacherRate> rows) =>
        rows.Select(r => new TeacherRateDto(
            r.Id,
            r.UserId,
            r.User == null ? null : r.User.FullName,
            r.Role,
            r.PerSessionRate,
            r.PerStudentBonusRate,
            r.BaseSalary,
            r.ActiveStudentBonusRate,
            r.WeekendHolidayMultiplier,
            r.ActiveFrom,
            r.IsActive,
            r.UserId != null ? 1 : 0,
            r.CreatedAt,
            r.UpdatedAt));

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(
                "Yozuv boshqa so'rov bilan to'qnashdi. Sahifani yangilab, qaytadan urinib ko'ring.");
        }
    }

    private static DateOnly RequireDate(DateOnly value, string field)
    {
        if (value.Year is < 2000 or > 2200)
            throw Invalid(field, "Sana kiritilishi shart (masalan 2026-07-01).");

        return value;
    }

    /// <summary>
    /// <c>null</c> bo'lsa markaz vaqt zonasidagi JORIY oy — server UTC'da
    /// ishlagani uchun oddiy <c>DateTime.UtcNow</c> oy chegarasida bir kunlik
    /// farq berardi (`PaymentService.ParsePeriodOrCurrent` bilan AYNI sabab).
    /// </summary>
    private BillingPeriod ParsePeriodOrCurrent(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? BillingPeriod.FromDate(LocalWallClock.LocalDate(clock.GetUtcNow(), timeZone.TimeZone))
            : ParsePeriod(value);

    private static BillingPeriod ParsePeriod(string value)
    {
        try
        {
            return BillingPeriod.Parse(value.Trim());
        }
        catch (Zinnur.Domain.Exceptions.DomainException ex)
        {
            throw Invalid("period", ex.Message);
        }
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private sealed record PayoutRow(
        long UserId, UserRole Role, int AttendedStudents,
        decimal SessionRate, decimal BonusAmount, bool RateMissing, bool Excluded);
}
