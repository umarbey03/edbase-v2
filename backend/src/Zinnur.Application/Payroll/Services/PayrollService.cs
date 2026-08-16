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

        var payouts = await (
            from p in db.SessionPayouts.AsNoTracking()
            join s in db.LiveSessions.AsNoTracking() on p.SessionId equals s.Id
            where s.ScheduledStart >= fromUtc && s.ScheduledStart < toUtc
            select new PayoutRow(
                p.UserId, p.Role, p.AttendedStudents, p.SessionRate, p.BonusAmount,
                p.RateMissing, p.Excluded))
            .ToListAsync(ct);

        if (payouts.Count == 0)
            return new PayrollSummaryDto(billingPeriod.ToString(), [], 0m);

        var userIds = payouts.Select(p => p.UserId).Distinct().ToList();

        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Role })
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = new List<PayrollSummaryRowDto>();

        foreach (var userId in userIds)
        {
            if (!users.TryGetValue(userId, out var user)) continue;

            var userPayouts = payouts.Where(p => p.UserId == userId).ToList();

            var baseAmount = userPayouts.Where(p => !p.Excluded).Sum(p => p.SessionRate);
            var bonusAmount = userPayouts.Where(p => !p.Excluded).Sum(p => p.BonusAmount);
            var missingRate = userPayouts.Count(p => p.RateMissing && !p.Excluded);
            var excludedCount = userPayouts.Count(p => p.Excluded);

            rows.Add(new PayrollSummaryRowDto(
                userId, user.FullName, user.Role, userPayouts.Count,
                userPayouts.Sum(p => p.AttendedStudents),
                baseAmount, bonusAmount, baseAmount + bonusAmount, missingRate, excludedCount));
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
            })
            .ToListAsync(ct);

        var rows = sessionRows.ConvertAll(s => new PayrollSessionRowDto(
            s.SessionId, s.GroupId, s.GroupName, s.ScheduledStart, s.AttendedStudents,
            s.Excluded ? 0m : s.SessionRate, s.Excluded ? 0m : s.BonusAmount,
            s.Excluded ? 0m : s.SessionRate + s.BonusAmount, s.RateMissing, s.Excluded));

        return new PayrollDetailDto(
            user.Id, user.FullName, user.Role, billingPeriod.ToString(), rows, rows.Sum(r => r.Total));
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

    private async Task ValidateRateAsync(TeacherRate rate, CancellationToken ct)
    {
        if (!Enum.IsDefined(rate.Role) || rate.Role is not (UserRole.Teacher or UserRole.Assistant))
            throw Invalid("role", "Stavka faqat ustoz yoki kurator uchun bo'lishi mumkin.");

        if (rate.PerSessionRate < 0 || rate.PerSessionRate > MaxAmount)
            throw Invalid("perSessionRate", "Dars stavkasi 0..1 000 000 000 oralig'ida bo'lishi kerak.");

        if (rate.PerStudentBonusRate < 0 || rate.PerStudentBonusRate > MaxAmount)
            throw Invalid("perStudentBonusRate", "Bonus stavkasi 0..1 000 000 000 oralig'ida bo'lishi kerak.");

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
                "Stavka yozuvi boshqa so'rov bilan to'qnashdi. Sahifani yangilab, qaytadan urinib ko'ring.");
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
