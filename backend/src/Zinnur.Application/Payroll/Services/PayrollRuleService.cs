using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payroll.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Payroll.Services;

/// <summary>
/// <see cref="IPayrollRuleService"/> ning amalga oshirilishi — oylik
/// dvigatelining SOZLAMA tomoni (2026-09-04).
///
/// ★ TARIX SAQLANADI: qoida qatori tahrirlanishi MUMKIN, lekin bu O'TGAN
/// OYGA TA'SIR QILMAYDI — dars qamrovidagi natija <c>SessionPayout</c> +
/// <c>SessionPayoutLine</c> ichida allaqachon muzlatilgan
/// (<c>SessionPayout</c> sinf izohi). Stavkani "shu sanadan" o'zgartirish
/// uchun to'g'ri yo'l — eskisiga <c>ActiveTo</c> qo'yib, YANGI qator
/// kiritish; UI ham shuni taklif qiladi.
/// </summary>
public sealed class PayrollRuleService(
    IApplicationDbContext db,
    TimeProvider clock) : IPayrollRuleService
{
    public async Task<IReadOnlyList<PayrollRuleDto>> ListAsync(
        long actorId, CancellationToken ct = default)
    {
        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        return await ProjectRules(db.PayrollRules.AsNoTracking()
                .OrderByDescending(r => r.IsActive)
                .ThenBy(r => r.Kind)
                .ThenByDescending(r => r.ActiveFrom)
                .ThenByDescending(r => r.Id))
            .ToListAsync(ct);
    }

    public async Task<PayrollRuleDto> CreateAsync(
        PayrollRuleRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var rule = new PayrollRule { Name = string.Empty };
        Apply(rule, request);

        await ValidateAsync(rule, request, ct);

        db.PayrollRules.Add(rule);
        await SaveAsync(ct);

        return await GetAsync(rule.Id, ct);
    }

    public async Task<PayrollRuleDto> UpdateAsync(
        long id, PayrollRuleRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var rule = await db.PayrollRules
            .Include(r => r.Tiers)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(PayrollRule), id);

        Apply(rule, request);
        rule.UpdatedAt = clock.GetUtcNow();

        // ★ BOSQICHLAR TO'LIQ ALMASHADI: eskilarini o'chirib, yangilarini
        //   qo'shamiz. Moslashtirib yangilash (Id bo'yicha) frontenddan
        //   bosqich Id'larini qaytarib yuborishni talab qilardi va bitta
        //   yo'qolgan Id jimgina "bosqich yo'qoldi" ga aylanardi.
        db.PayrollRuleTiers.RemoveRange(rule.Tiers);
        rule.Tiers.Clear();
        AddTiers(rule, request);

        await ValidateAsync(rule, request, ct);
        await SaveAsync(ct);

        return await GetAsync(rule.Id, ct);
    }

    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var rule = await db.PayrollRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(PayrollRule), id);

        // ★ BOG'LANGAN GURUHLARNI AVVAL BO'SHATAMIZ: guruh "aniq shu qoida"
        //   rejimida qolib, qoidasi yo'qolsa — u jimgina "qoidasiz majburlash"
        //   holatiga tushardi va darslari haqsiz hisoblanardi. Bazadagi
        //   `SetNull` faqat ustunni tozalaydi, REJIMNI tiklay olmaydi.
        var pinned = await db.Groups
            .Where(g => g.PayrollRuleId == id)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();

        foreach (var group in pinned)
        {
            group.PayrollMode = GroupPayrollMode.Auto;
            group.PayrollRuleId = null;
            group.UpdatedAt = now;
        }

        db.PayrollRules.Remove(rule);
        await SaveAsync(ct);
    }

    // ================================================================= guruh tayinlash

    public async Task<IReadOnlyList<GroupPayrollAssignmentDto>> ListGroupAssignmentsAsync(
        long actorId, CancellationToken ct = default)
    {
        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        // ATAYLAB faqat ISTISNOLAR: `Auto` — standart holat va guruhlar
        // soni yuzlab bo'lishi mumkin. Ro'yxat "nima odatdagidan farq
        // qiladi" degan savolga javob berishi kerak.
        return await db.Groups.AsNoTracking()
            .Where(g => g.PayrollMode != GroupPayrollMode.Auto)
            .OrderBy(g => g.Name)
            .Select(g => new GroupPayrollAssignmentDto(
                g.Id,
                g.Name,
                g.PayrollMode,
                g.PayrollRuleId,
                g.PayrollRule == null ? null : g.PayrollRule.Name))
            .ToListAsync(ct);
    }

    public async Task<GroupPayrollAssignmentDto> SetGroupAssignmentAsync(
        long groupId, SetGroupPayrollAssignmentRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        if (!Enum.IsDefined(request.Mode))
            throw PayrollGuard.Invalid("mode", "Oylik rejimi noto'g'ri.");

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new NotFoundException(nameof(Group), groupId);

        if (request.Mode == GroupPayrollMode.FixedRule)
        {
            if (request.RuleId is not { } ruleId)
                throw PayrollGuard.Invalid("ruleId", "Qoida tanlanishi shart.");

            var kind = await db.PayrollRules.AsNoTracking()
                .Where(r => r.Id == ruleId)
                .Select(r => (PayrollRuleKind?)r.Kind)
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException(nameof(PayrollRule), ruleId);

            // ★ FAQAT DARS QAMROVIDAGI qoida majburlanadi: oklad guruhga
            //   bog'lanmaydi (`PayrollRuleKinds.SupportsGroupTargeting`
            //   izohi). Bu tekshiruvsiz admin guruhga oklad qoidasini
            //   tayinlardi va guruh darslari umuman haqsiz qolardi —
            //   hech qanday xato xabarisiz.
            if (!PayrollRuleKinds.IsSessionScoped(kind))
            {
                throw PayrollGuard.Invalid("ruleId",
                    "Guruhga faqat dars uchun hisoblanadigan qoida tayinlanadi "
                    + "(oklad va oylik o'quvchi bonusi butun oyga tegishli).");
            }

            group.PayrollRuleId = ruleId;
        }
        else
        {
            group.PayrollRuleId = null;
        }

        group.PayrollMode = request.Mode;
        group.UpdatedAt = clock.GetUtcNow();

        await SaveAsync(ct);

        return await db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new GroupPayrollAssignmentDto(
                g.Id,
                g.Name,
                g.PayrollMode,
                g.PayrollRuleId,
                g.PayrollRule == null ? null : g.PayrollRule.Name))
            .FirstAsync(ct);
    }

    // ================================================================= koeffitsient

    public async Task<PayrollStudentCoefficientDto?> SetStudentCoefficientAsync(
        SetPayrollStudentCoefficientRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await PayrollGuard.EnsureAdminAsync(db, actorId, ct);

        var periodStart = ParsePeriod(request.Period).FirstDay();

        if (request.Percent is < 0 or > 100)
            throw PayrollGuard.Invalid("percent", "Koeffitsient 0..100 oralig'ida bo'lishi kerak.");

        await EnsureDraftAsync(request.UserId, periodStart, ct);

        var existing = await db.PayrollStudentCoefficients
            .FirstOrDefaultAsync(
                c => c.UserId == request.UserId
                  && c.StudentId == request.StudentId
                  && c.PeriodStart == periodStart,
                ct);

        // ★ 100% = ISTISNO YO'Q → qator O'CHIRILADI (sabab
        //   `PayrollStudentCoefficient` sinf izohida). Aks holda jadval
        //   har oy har o'quvchi uchun ma'nosiz "100" qatorlari bilan
        //   to'lib borardi.
        if (request.Percent == 100m)
        {
            if (existing is not null)
            {
                db.PayrollStudentCoefficients.Remove(existing);
                await SaveAsync(ct);
            }

            return null;
        }

        if (existing is null)
        {
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == request.UserId, ct))
                throw new NotFoundException(nameof(User), request.UserId);

            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == request.StudentId, ct))
                throw new NotFoundException(nameof(User), request.StudentId);

            existing = new PayrollStudentCoefficient
            {
                UserId = request.UserId,
                StudentId = request.StudentId,
                PeriodStart = periodStart,
                CreatedById = actorId,
            };

            db.PayrollStudentCoefficients.Add(existing);
        }
        else
        {
            existing.UpdatedAt = clock.GetUtcNow();
        }

        existing.Percent = request.Percent;
        existing.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        existing.Validate();

        await SaveAsync(ct);

        return await db.PayrollStudentCoefficients.AsNoTracking()
            .Where(c => c.Id == existing.Id)
            .Select(c => new PayrollStudentCoefficientDto(
                c.Id, c.UserId, c.StudentId,
                c.Student == null ? string.Empty : c.Student.FullName,
                c.PeriodStart, c.Percent, c.Note))
            .FirstAsync(ct);
    }

    // ================================================================= yordamchi

    private static void Apply(PayrollRule rule, PayrollRuleRequest request)
    {
        rule.Name = (request.Name ?? string.Empty).Trim();
        rule.Kind = request.Kind;
        rule.Role = request.Role;
        rule.UserId = request.UserId;
        rule.Amount = request.Amount;
        rule.AcademicHourMinutes = request.AcademicHourMinutes;
        rule.Basis = request.Basis;
        rule.MinStudents = request.MinStudents;
        rule.MaxStudents = request.MaxStudents;
        rule.MinDurationMinutes = request.MinDurationMinutes;
        rule.PlanAmount = request.PlanAmount;
        rule.PlanReachedPercent = request.PlanReachedPercent;
        rule.WeekendHolidayMultiplier = request.WeekendHolidayMultiplier;
        rule.ActiveFrom = request.ActiveFrom;
        rule.ActiveTo = request.ActiveTo;
        rule.IsActive = request.IsActive;

        // ★ O'QUV MAQSADI turga bog'liq: qo'llab-quvvatlamaydigan turda
        //   ular JIMGINA TOZALANADI, xato ko'tarilmaydi. Sabab — UI da
        //   admin turni almashtirganda oldin to'ldirgan maydonlari
        //   qolib ketishi mumkin; ularni saqlab, keyin `Validate` bilan
        //   rad etish "nega saqlanmayapti?" degan tushunarsiz to'siq
        //   bo'lardi. Tozalash esa ONGLI va kutilgan natija beradi.
        var supportsGroups = PayrollRuleKinds.SupportsGroupTargeting(request.Kind);

        rule.CourseId = supportsGroups ? request.CourseId : null;
        rule.GroupId = supportsGroups ? request.GroupId : null;
        rule.CategoryId = supportsGroups ? request.CategoryId : null;
        rule.GroupType = supportsGroups ? request.GroupType : null;

        // Reja faqat foiz turida ma'noli — AYNI mulohaza.
        if (request.Kind != PayrollRuleKind.PercentOfRevenue)
        {
            rule.PlanAmount = null;
            rule.PlanReachedPercent = null;
        }

        if (rule.Id == 0) AddTiers(rule, request);
    }

    private static void AddTiers(PayrollRule rule, PayrollRuleRequest request)
    {
        if (request.Kind != PayrollRuleKind.TieredByAttendance) return;
        if (request.Tiers is null) return;

        foreach (var tier in request.Tiers)
        {
            rule.Tiers.Add(new PayrollRuleTier
            {
                StudentCount = tier.StudentCount,
                Amount = tier.Amount,
            });
        }
    }

    private async Task ValidateAsync(
        PayrollRule rule, PayrollRuleRequest request, CancellationToken ct)
    {
        rule.Validate();

        if (!Enum.IsDefined(rule.Basis))
            throw PayrollGuard.Invalid("basis", "Hisob asosi noto'g'ri.");

        if (rule.GroupType is { } groupType && !Enum.IsDefined(groupType))
            throw PayrollGuard.Invalid("groupType", "Guruh turi noto'g'ri.");

        if (rule.Kind == PayrollRuleKind.TieredByAttendance)
        {
            if (rule.Tiers.Count == 0)
            {
                throw PayrollGuard.Invalid("tiers",
                    "Bosqichli stavkada kamida bitta bosqich bo'lishi kerak.");
            }

            if (rule.Tiers.Select(t => t.StudentCount).Distinct().Count() != rule.Tiers.Count)
                throw PayrollGuard.Invalid("tiers", "Bir xil o'quvchi soni ikki marta kiritilgan.");

            foreach (var tier in rule.Tiers) tier.Validate();
        }
        else if (request.Tiers is { Count: > 0 })
        {
            throw PayrollGuard.Invalid("tiers",
                "Bosqichlar faqat \"o'quvchi soniga bosqichli\" qoidada ishlatiladi.");
        }

        if (rule.UserId is { } userId)
        {
            var role = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (UserRole?)u.Role)
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException(nameof(User), userId);

            if (role != rule.Role)
            {
                throw PayrollGuard.Invalid("userId",
                    "Tanlangan xodimning haqiqiy roli qoidadagi rol bilan mos emas.");
            }
        }

        if (rule.CourseId is { } courseId
            && !await db.Courses.AsNoTracking().AnyAsync(c => c.Id == courseId, ct))
        {
            throw new NotFoundException(nameof(Course), courseId);
        }

        if (rule.CategoryId is { } categoryId
            && !await db.GroupCategories.AsNoTracking().AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new NotFoundException(nameof(GroupCategory), categoryId);
        }

        if (rule.GroupId is { } groupId
            && !await db.Groups.AsNoTracking().AnyAsync(g => g.Id == groupId, ct))
        {
            throw new NotFoundException(nameof(Group), groupId);
        }
    }

    /// <summary>Faqat Draft davrda o'zgartirish mumkin — tasdiqlangandan keyin summa "muzlaydi".</summary>
    private async Task EnsureDraftAsync(long userId, DateOnly periodStart, CancellationToken ct)
    {
        var status = await db.PayrollApprovals.AsNoTracking()
            .Where(a => a.UserId == userId && a.PeriodStart == periodStart)
            .Select(a => (PayrollApprovalStatus?)a.Status)
            .FirstOrDefaultAsync(ct);

        if (status is not (null or PayrollApprovalStatus.Draft))
        {
            throw new ConflictException(
                "Bu davr allaqachon tasdiqlangan/to'langan — koeffitsientni o'zgartirib bo'lmaydi.");
        }
    }

    private async Task<PayrollRuleDto> GetAsync(long id, CancellationToken ct) =>
        await ProjectRules(db.PayrollRules.AsNoTracking().Where(r => r.Id == id))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(PayrollRule), id);

    private IQueryable<PayrollRuleDto> ProjectRules(IQueryable<PayrollRule> rows) =>
        rows.Select(r => new PayrollRuleDto(
            r.Id,
            r.Name,
            r.Kind,
            r.UserId,
            r.User == null ? null : r.User.FullName,
            r.Role,
            r.CourseId,
            r.Course == null ? null : r.Course.Name,
            r.GroupId,
            r.Group == null ? null : r.Group.Name,
            r.CategoryId,
            r.Category == null ? null : r.Category.Name,
            r.GroupType,
            r.Amount,
            r.AcademicHourMinutes,
            r.Basis,
            r.MinStudents,
            r.MaxStudents,
            r.MinDurationMinutes,
            r.PlanAmount,
            r.PlanReachedPercent,
            r.WeekendHolidayMultiplier,
            r.ActiveFrom,
            r.ActiveTo,
            r.IsActive,

            // Hisoblanuvchi qiymatlar SQL'ga tarjima qilinadi (property emas —
            // `Ignore` qilingan, ya'ni ustun yo'q).
            (r.GroupId != null ? 8 : 0)
                + (r.UserId != null ? 4 : 0)
                + (r.CourseId != null ? 2 : 0)
                + (r.CategoryId != null ? 1 : 0)
                + (r.GroupType != null ? 1 : 0),

            r.Kind == PayrollRuleKind.PerSession
                || r.Kind == PayrollRuleKind.PerAcademicHour
                || r.Kind == PayrollRuleKind.PerAttendedStudent
                || r.Kind == PayrollRuleKind.TieredByAttendance,

            r.Tiers
                .OrderBy(t => t.StudentCount)
                .Select(t => new PayrollRuleTierDto(t.Id, t.StudentCount, t.Amount))
                .ToList(),

            // ★ Korrelyatsiyalangan sanoq — AYNI `SELECT` ichida (loyihadagi
            //   umumiy naqsh, `PayrollService.ProjectAdjustments` bilan bir xil):
            //   qator boshiga alohida so'rov N+1 bo'lardi.
            db.Groups.Count(g => g.PayrollRuleId == r.Id),

            r.CreatedAt,
            r.UpdatedAt));

    private static BillingPeriod ParsePeriod(string value)
    {
        try
        {
            return BillingPeriod.Parse((value ?? string.Empty).Trim());
        }
        catch (Zinnur.Domain.Exceptions.DomainException ex)
        {
            throw PayrollGuard.Invalid("period", ex.Message);
        }
    }

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
}
