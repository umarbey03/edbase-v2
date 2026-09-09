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
/// <see cref="IPayrollService"/> ning amalga oshirilishi — oylik HISOBOTI.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ IKKI QAMROV — HISOBOT SHU BO'LINISH USTIGA QURILGAN
/// ══════════════════════════════════════════════════════════════════════
///   • DARS QAMROVI (<c>SessionPayout</c> + <c>SessionPayoutLine</c>) —
///     O'QILADI, qayta hisoblanmaydi. Snapshot dars YAKUNLANGANDA
///     (`LessonAccrualService.ReconcilePayoutAsync`) bir marta yoziladi va
///     QOTIB QOLADI. Sabab: stavka tahrirlansa yoki o'chirilsa, O'TGAN OY
///     hisoboti ham jimgina o'zgarib qolardi.
///
///   • DAVR QAMROVI (oklad, tushumdan foiz, oylik o'quvchi bonusi) —
///     davr OXIRIDAGI holat bo'yicha JONLI hisoblanadi
///     (<see cref="PayrollCalculator.ComputePeriod"/>). Bularni darsga
///     bog'lab muzlatib bo'lmaydi: ular darsga BOG'LIQ EMAS.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ 2026-09-04 — QOIDA DVIGATELIGA KO'CHIRILDI
/// ══════════════════════════════════════════════════════════════════════
/// Ilgari hisobot <c>TeacherRate</c> ning beshta ustuniga QATTIQ bog'langan
/// edi (<c>BaseSalaryAmount</c>, <c>KpiBonusAmount</c> alohida ustunlar).
/// Endi davr natijasi QATORLAR ro'yxati (<see cref="PayrollAmountLineDto"/>) —
/// yangi hisoblash turi qo'shilganda bu sinf ham, DTO ham o'zgarmaydi.
///
/// ★ TASDIQLASH/TO'LOV (<see cref="PayrollApproval"/>) va QO'LDA TUZATISH
/// (<see cref="PayrollAdjustment"/>) o'zgarishsiz qoldi.
///
/// ★ RUXSAT — FAQAT ADMIN: izoh <see cref="PayrollGuard"/> da.
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
        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var billingPeriod = ParsePeriodOrCurrent(period);
        var (fromUtc, toUtc) = billingPeriod.UtcRange(timeZone.TimeZone);
        var periodStart = billingPeriod.FirstDay();
        var periodEndDate = billingPeriod.AddMonths(1).FirstDay().AddDays(-1);

        var payouts = await (
            from p in db.SessionPayouts.AsNoTracking()
            join s in db.LiveSessions.AsNoTracking() on p.SessionId equals s.Id
            where s.ScheduledStart >= fromUtc && s.ScheduledStart < toUtc
            select new PayoutRow(
                p.UserId, p.AttendedStudents, p.SessionRate, p.BonusAmount,
                p.RateMissing, p.Excluded, p.IncludedInSalary))
            .ToListAsync(ct);

        // ── DAVR QOIDASI NOMZODLARI: darsi bo'lmasa ham ro'yxatda ko'rinsin ──
        //
        // Masalan yangi qabul qilingan kurator — hali biror darsi yo'q, lekin
        // oklad allaqachon hisoblanishi kerak.
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Teacher || u.Role == UserRole.Assistant))
            .Select(u => new StaffRow(u.Id, u.FullName, u.Role))
            .ToListAsync(ct);

        var payoutUserIds = payouts.Select(p => p.UserId).ToHashSet();

        // Hisobga faqat KERAKLI xodimlar kiradi, lekin davr qoidalarini
        // hisoblash uchun avval hammasi ko'riladi (xodimlar soni o'nlab,
        // yuzlab emas — bu qidiruv arzon).
        var periodLines = await BuildPeriodLinesAsync(
            staff, periodStart, periodEndDate, fromUtc, toUtc, ct);

        var relevantIds = payoutUserIds
            .Union(periodLines.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key))
            .ToList();

        if (relevantIds.Count == 0)
            return new PayrollSummaryDto(billingPeriod.ToString(), [], 0m);

        var adjustmentTotals = await GetAdjustmentTotalsAsync(relevantIds, periodStart, ct);
        var approvals = await GetApprovalsAsync(relevantIds, periodStart, ct);

        var rows = new List<PayrollSummaryRowDto>(relevantIds.Count);

        foreach (var user in staff.Where(s => relevantIds.Contains(s.Id)))
        {
            var userPayouts = payouts.Where(p => p.UserId == user.Id).ToList();

            // Bepul (Excluded) va "oklad ichida" darslar JAMIga qo'shilmaydi,
            // lekin dars SONIGA kiradi — shaffoflik uchun.
            var counted = userPayouts.Where(p => !p.Excluded).ToList();

            var sessionBase = counted.Sum(p => p.SessionRate);
            var sessionBonus = counted.Sum(p => p.BonusAmount);

            var lines = periodLines.TryGetValue(user.Id, out var found) ? found : [];
            var periodAmount = lines.Sum(l => l.Amount);

            adjustmentTotals.TryGetValue(user.Id, out var adjustmentAmount);
            approvals.TryGetValue(user.Id, out var approval);

            var total = sessionBase + sessionBonus + periodAmount + adjustmentAmount;

            rows.Add(new PayrollSummaryRowDto(
                user.Id,
                user.FullName,
                user.Role,
                userPayouts.Count,
                userPayouts.Sum(p => p.AttendedStudents),
                sessionBase,
                sessionBonus,
                periodAmount,
                lines,
                adjustmentAmount,
                total,
                counted.Count(p => p.RateMissing && !p.IncludedInSalary),
                userPayouts.Count(p => p.Excluded),
                userPayouts.Count(p => p.IncludedInSalary),
                approval?.Status ?? PayrollApprovalStatus.Draft,
                approval?.ApprovedAt,
                approval?.PaidAt));
        }

        rows.Sort((a, b) => b.Total.CompareTo(a.Total));

        return new PayrollSummaryDto(billingPeriod.ToString(), rows, rows.Sum(r => r.Total));
    }

    public async Task<PayrollDetailDto> GetDetailAsync(
        long userId, string? period, long actorId, CancellationToken ct = default)
    {
        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new StaffRow(u.Id, u.FullName, u.Role))
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
                p.SessionId,
                s.GroupId,
                GroupName = s.Group!.Name,
                s.ScheduledStart,
                p.AttendedStudents,
                p.SessionRate,
                p.BonusAmount,
                p.RateMissing,
                p.Excluded,
                p.IncludedInSalary,
                p.PremiumMultiplierApplied,
                Lines = p.Lines
                    .OrderBy(l => l.Id)
                    .Select(l => new PayrollAmountLineDto(
                        l.RuleId, l.RuleName, l.Kind, l.Amount, l.Basis))
                    .ToList(),
            })
            .ToListAsync(ct);

        var sessions = sessionRows.ConvertAll(s => new PayrollSessionRowDto(
            s.SessionId,
            s.GroupId,
            s.GroupName,
            s.ScheduledStart,
            s.AttendedStudents,
            s.Excluded ? 0m : s.SessionRate,
            s.Excluded ? 0m : s.BonusAmount,
            s.Excluded ? 0m : s.SessionRate + s.BonusAmount,
            s.RateMissing,
            s.Excluded,
            s.IncludedInSalary,
            s.PremiumMultiplierApplied,
            s.Excluded ? [] : s.Lines));

        var periodLines = await BuildPeriodLinesAsync(
            [user], periodStart, periodEndDate, fromUtc, toUtc, ct);

        var lines = periodLines.TryGetValue(userId, out var found) ? found : [];
        var periodAmount = lines.Sum(l => l.Amount);

        var students = await GetStudentRowsAsync(user, periodStart, ct);
        var weightedUnits = students.Sum(s => s.Percent / 100m);

        var adjustments = await ProjectAdjustments(db.PayrollAdjustments.AsNoTracking()
                .Where(a => a.UserId == userId && a.PeriodStart == periodStart))
            .ToListAsync(ct);

        var approvals = await GetApprovalsAsync([userId], periodStart, ct);
        approvals.TryGetValue(userId, out var approval);

        var grandTotal = sessions.Sum(s => s.Total) + periodAmount + adjustments.Sum(a => a.Amount);

        return new PayrollDetailDto(
            user.Id,
            user.FullName,
            user.Role,
            billingPeriod.ToString(),
            sessions,
            lines,
            periodAmount,
            students.Count,
            weightedUnits,
            students,
            adjustments,
            grandTotal,
            approval?.Status ?? PayrollApprovalStatus.Draft,
            approval?.ApprovedAt,
            approval?.PaidAt);
    }

    // ================================================================= tuzatish

    public async Task<PayrollAdjustmentDto> CreateAdjustmentAsync(
        CreatePayrollAdjustmentRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var billingPeriod = ParsePeriod(request.Period);
        var periodStart = billingPeriod.FirstDay();

        await EnsureDraftAsync(request.UserId, periodStart, ct);

        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == request.UserId, ct))
            throw new NotFoundException(nameof(User), request.UserId);

        if (request.Amount < -MaxAmount || request.Amount > MaxAmount)
            throw PayrollGuard.Invalid("amount", "Tuzatish summasi 1 000 000 000 dan oshmasligi kerak.");

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
        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var adjustment = await db.PayrollAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(PayrollAdjustment), id);

        await EnsureDraftAsync(adjustment.UserId, adjustment.PeriodStart, ct);

        // ═══════════════════════════════════════════════════════════════
        // 🔴 JARIMADAN TUG'ILGAN TUZATMA BU YERDAN O'CHIRILMAYDI
        //    (2026-08-18 da qo'shildi)
        //
        // `Penalties.PayrollAdjustmentId` bu qatorga `Restrict` bilan
        // havola qiladi (jarima — moliyaviy iz, `PenaltyConfiguration`).
        // Tekshiruvsiz `SaveChanges` FK xatosiga uchrardi va u global
        // ushlagichda 500 "Serverda kutilmagan xato" bo'lib chiqardi:
        // admin nima uchun o'chmaganini bilmasdi.
        //
        // ★ TO'G'RI YO'L — "Jarimalar" PANELI: u yerda jarimaning o'zi
        //   bekor qilinadi (hodisa, sabab va kim bekor qilgani ko'rinib
        //   turadi). Bu yerdan o'chirish esa jarimani "tasdiqlangan"
        //   holatida qoldirib, ushlanmani jimgina yo'q qilardi — ikki
        //   panel bir-biriga zid ma'lumot ko'rsatardi.
        // ═══════════════════════════════════════════════════════════════
        if (await db.Penalties.AsNoTracking().AnyAsync(p => p.PayrollAdjustmentId == id, ct))
        {
            throw new ConflictException(
                "Bu tuzatma tasdiqlangan jarimadan kelib chiqqan va bu yerdan o'chirilmaydi. "
                + "Ushlanmani bekor qilish uchun \"Jarimalar\" panelidan jarimaning o'zini bekor qiling.");
        }

        db.PayrollAdjustments.Remove(adjustment);
        await SaveAsync(ct);
    }

    // ================================================================= tasdiqlash/to'lov

    public async Task ApproveAsync(
        PayrollPeriodActionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

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

        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

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

    // ================================================================= davr qamrovi

    /// <summary>
    /// Berilgan xodimlar uchun DAVR qoidalarini hisoblaydi.
    ///
    /// ★ BARCHA MA'LUMOT BITTA MARTA olinadi (xodim boshiga alohida so'rov
    /// emas): kirish so'rovlari 5 ta, xodimlar soni esa o'nlab bo'lishi
    /// mumkin — N+1 bu yerda eng oson kiradigan joy edi.
    /// </summary>
    private async Task<Dictionary<long, List<PayrollAmountLineDto>>> BuildPeriodLinesAsync(
        IReadOnlyList<StaffRow> staff,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct)
    {
        var result = new Dictionary<long, List<PayrollAmountLineDto>>();
        if (staff.Count == 0) return result;

        var ids = staff.Select(s => s.Id).ToList();

        // Davr qamrovidagi qoidalar (oklad, foiz, oylik o'quvchi bonusi).
        // Dars qoidalari bu yerda KERAK EMAS — ular snapshot'da.
        var rules = await db.PayrollRules.AsNoTracking()
            .Where(r => r.IsActive
                     && r.ActiveFrom <= periodEnd
                     && (r.ActiveTo == null || r.ActiveTo >= periodEnd)
                     && r.Kind != PayrollRuleKind.PerSession
                     && r.Kind != PayrollRuleKind.PerAcademicHour
                     && r.Kind != PayrollRuleKind.PerAttendedStudent
                     && r.Kind != PayrollRuleKind.TieredByAttendance
                     && r.Kind != PayrollRuleKind.PerStudentAcademicHour)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            foreach (var s in staff) result[s.Id] = [];
            return result;
        }

        var groups = await GetStaffGroupsAsync(ids, ct);
        var studentUnits = await GetStudentUnitsAsync(staff, groups, periodStart, ct);
        var revenues = await GetGroupRevenuesAsync(ids, groups, fromUtc, toUtc, ct);

        foreach (var member in staff)
        {
            var groupRevenues = revenues.TryGetValue(member.Id, out var r) ? r : [];
            studentUnits.TryGetValue(member.Id, out var units);

            var context = new PayrollPeriodContext(
                member.Id,
                member.Role,
                periodEnd,
                units.Count,
                units.Weighted,
                groupRevenues.Sum(x => x.Amount),
                groupRevenues);

            result[member.Id] = PayrollCalculator.ComputePeriod(rules, context)
                .Select(l => new PayrollAmountLineDto(l.RuleId, l.RuleName, l.Kind, l.Amount, l.Basis))
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// Xodim HOST bo'lgan guruhlar. Ustoz uchun <c>TeacherId</c>, kurator
    /// uchun <c>AssistantId</c> — <c>Group.HostId</c> bilan AYNI qoida.
    /// </summary>
    private async Task<List<StaffGroupRow>> GetStaffGroupsAsync(
        List<long> ids, CancellationToken ct) =>
        await db.Groups.AsNoTracking()
            .Where(g => g.IsActive
                     && ((g.TeacherId != null && ids.Contains(g.TeacherId.Value))
                      || (g.AssistantId != null && ids.Contains(g.AssistantId.Value))))
            .Select(g => new StaffGroupRow(
                g.Id, g.TeacherId, g.AssistantId, g.CourseId, g.CategoryId, g.Type))
            .ToListAsync(ct);

    /// <summary>
    /// Har xodim uchun faol o'quvchilar soni va KOEFFITSIENT bilan
    /// o'lchangan ulushlar yig'indisi.
    /// </summary>
    /// <remarks>
    /// ★ O'QUVCHI BIR MARTA sanaladi (<c>Distinct</c>): bitta xodimning ikki
    /// guruhida bo'lgan o'quvchi ikki barobar bonus keltirmasligi kerak.
    /// Eski kod a'zolik QATORLARINI sanardi va kurator guruhida bu muammo
    /// yuzaga chiqmasdi — ustozga kengaytirilgandan keyin esa chiqardi.
    /// </remarks>
    private async Task<Dictionary<long, StudentUnits>> GetStudentUnitsAsync(
        IReadOnlyList<StaffRow> staff,
        List<StaffGroupRow> groups,
        DateOnly periodStart,
        CancellationToken ct)
    {
        var result = new Dictionary<long, StudentUnits>();
        if (staff.Count == 0) return result;

        var groupIds = groups.ConvertAll(g => g.GroupId);
        var ids = staff.Select(s => s.Id).ToList();

        var members = groupIds.Count == 0
            ? []
            : await db.GroupMembers.AsNoTracking()
                .Where(m => m.Status == MemberStatus.Active && groupIds.Contains(m.GroupId))
                .Select(m => new { m.GroupId, m.StudentId })
                .ToListAsync(ct);

        var coefficients = await db.PayrollStudentCoefficients.AsNoTracking()
            .Where(c => ids.Contains(c.UserId) && c.PeriodStart == periodStart)
            .Select(c => new { c.UserId, c.StudentId, c.Percent })
            .ToListAsync(ct);

        var coefficientMap = coefficients.ToDictionary(c => (c.UserId, c.StudentId), c => c.Percent);

        foreach (var member in staff)
        {
            var ownGroupIds = groups
                .Where(g => member.Role == UserRole.Teacher
                    ? g.TeacherId == member.Id
                    : g.AssistantId == member.Id)
                .Select(g => g.GroupId)
                .ToHashSet();

            var studentIds = members
                .Where(m => ownGroupIds.Contains(m.GroupId))
                .Select(m => m.StudentId)
                .Distinct()
                .ToList();

            var weighted = studentIds.Sum(studentId =>
                coefficientMap.TryGetValue((member.Id, studentId), out var percent)
                    ? percent / 100m
                    : 1m);

            result[member.Id] = new StudentUnits(studentIds.Count, weighted);
        }

        return result;
    }

    /// <summary>
    /// Tafsilot ko'rinishi uchun: xodimning faol o'quvchilari va ularning
    /// koeffitsientlari (izoh: <see cref="PayrollStudentUnitDto"/>).
    ///
    /// ★ KOEFFITSIENT YOZUVI YO'Q = 100%: jadval faqat ISTISNONI saqlaydi
    /// (<c>PayrollStudentCoefficient</c> izohi), shuning uchun to'ldirish
    /// shu yerda — bir joyda — bajariladi.
    /// </summary>
    private async Task<List<PayrollStudentUnitDto>> GetStudentRowsAsync(
        StaffRow staff, DateOnly periodStart, CancellationToken ct)
    {
        var groups = await GetStaffGroupsAsync([staff.Id], ct);

        var ownGroupIds = groups
            .Where(g => staff.Role == UserRole.Teacher
                ? g.TeacherId == staff.Id
                : g.AssistantId == staff.Id)
            .Select(g => g.GroupId)
            .ToList();

        if (ownGroupIds.Count == 0) return [];

        // `Distinct` — o'quvchi xodimning ikki guruhida bo'lsa ham BIR
        // MARTA ko'rinadi (`GetStudentUnitsAsync` dagi AYNI qoida).
        var students = await db.GroupMembers.AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active && ownGroupIds.Contains(m.GroupId))
            .Select(m => new { m.StudentId, Name = m.Student!.FullName })
            .Distinct()
            .ToListAsync(ct);

        var coefficients = await db.PayrollStudentCoefficients.AsNoTracking()
            .Where(c => c.UserId == staff.Id && c.PeriodStart == periodStart)
            .Select(c => new { c.StudentId, c.Percent, c.Note })
            .ToListAsync(ct);

        var map = coefficients.ToDictionary(c => c.StudentId);

        return students
            .Select(s => map.TryGetValue(s.StudentId, out var found)
                ? new PayrollStudentUnitDto(s.StudentId, s.Name, found.Percent, found.Note)
                : new PayrollStudentUnitDto(s.StudentId, s.Name, 100m, null))
            .OrderBy(s => s.StudentName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Xodimning shu davrda HISOBLANGAN o'quv haqi, guruhma-guruh —
    /// "tushumdan foiz" qoidasining asosi.
    /// </summary>
    /// <remarks>
    /// ★ <c>NetAmount</c> — chegirmadan KEYINGI, HAQIQATDA hisoblangan summa
    /// (<c>LessonCharge.NetAmount</c> izohi). <c>Amount</c> (stiker narx)
    /// olinsa, chegirmali oilalar bo'lgan guruhda markaz olmagan puldan
    /// foiz to'lanardi.
    ///
    /// ★ Dars HOST'i bo'yicha bog'lanadi (<c>LiveSession.HostId</c>) —
    /// guruhning bugungi ustozi bo'yicha emas: o'rinbosar o'tgan dars
    /// tushumi o'rinbosarga tegishli.
    /// </remarks>
    private async Task<Dictionary<long, List<PayrollGroupRevenue>>> GetGroupRevenuesAsync(
        List<long> ids,
        List<StaffGroupRow> groups,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct)
    {
        var raw = await (
            from c in db.LessonCharges.AsNoTracking()
            join s in db.LiveSessions.AsNoTracking() on c.SessionId equals s.Id
            where s.ScheduledStart >= fromUtc
               && s.ScheduledStart < toUtc
               && s.HostId != null
               && ids.Contains(s.HostId.Value)
            group c by new { UserId = s.HostId!.Value, c.GroupId } into g
            select new
            {
                g.Key.UserId,
                g.Key.GroupId,
                Amount = g.Sum(x => x.NetAmount),
            })
            .ToListAsync(ct);

        // Guruh xossalari (kurs/kategoriya/tur) — foiz qoidasi shular
        // bo'yicha moslanadi. `GetStaffGroupsAsync` faqat FAOL guruhlarni
        // qaytaradi, shuning uchun arxivlangan guruh tushumi uchun
        // xossalar alohida olinadi.
        var missingIds = raw
            .Select(x => x.GroupId)
            .Where(id => !groups.Exists(g => g.GroupId == id))
            .Distinct()
            .ToList();

        var lookup = groups.ToDictionary(g => g.GroupId);

        if (missingIds.Count > 0)
        {
            var extra = await db.Groups.AsNoTracking()
                .Where(g => missingIds.Contains(g.Id))
                .Select(g => new StaffGroupRow(
                    g.Id, g.TeacherId, g.AssistantId, g.CourseId, g.CategoryId, g.Type))
                .ToListAsync(ct);

            foreach (var g in extra) lookup[g.GroupId] = g;
        }

        var result = new Dictionary<long, List<PayrollGroupRevenue>>();

        foreach (var row in raw)
        {
            if (!lookup.TryGetValue(row.GroupId, out var group)) continue;

            if (!result.TryGetValue(row.UserId, out var list))
            {
                list = [];
                result[row.UserId] = list;
            }

            list.Add(new PayrollGroupRevenue(
                group.GroupId, group.CourseId, group.CategoryId, group.Type, row.Amount));
        }

        return result;
    }

    // ================================================================= yordamchi

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

    private IQueryable<PayrollAdjustmentDto> ProjectAdjustments(IQueryable<PayrollAdjustment> rows) =>
        rows.OrderByDescending(a => a.CreatedAt)
            .Select(a => new PayrollAdjustmentDto(
                a.Id, a.UserId, a.PeriodStart, a.Amount, a.Reason, a.CreatedById,
                a.CreatedBy == null ? null : a.CreatedBy.FullName, a.CreatedAt,

                // ★ Korrelyatsiyalangan `EXISTS` — AYNI `SELECT` ichida
                //   (loyihadagi umumiy naqsh): qator boshiga alohida
                //   so'rov N+1 bo'lardi. Sabab `FromPenalty` izohida.
                db.Penalties.Any(p => p.PayrollAdjustmentId == a.Id)));

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
            throw PayrollGuard.Invalid("period", ex.Message);
        }
    }

    private sealed record PayoutRow(
        long UserId, int AttendedStudents, decimal SessionRate, decimal BonusAmount,
        bool RateMissing, bool Excluded, bool IncludedInSalary);

    private sealed record StaffRow(long Id, string FullName, UserRole Role);

    private sealed record StaffGroupRow(
        long GroupId, long? TeacherId, long? AssistantId,
        long? CourseId, long? CategoryId, GroupType Type);

    private readonly record struct StudentUnits(int Count, decimal Weighted);
}
