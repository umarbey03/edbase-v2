using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Application.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(long actorId, CancellationToken ct = default);

    Task<SubscriptionPlanDto> CreatePlanAsync(SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default);

    Task<SubscriptionPlanDto> UpdatePlanAsync(
        long planId, SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Obuna beradi yoki uzaytiradi — yangi davr avvalgisining oxiridan boshlanadi.</summary>
    Task<SubscriptionPeriodDto> GrantAsync(GrantSubscriptionRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Onlayn buyurtma tasdiqlanganda obuna ochadi — narx BUYURTMADAN
    /// (o'quvchi ko'rgan summa), reja keyin o'chirilgan bo'lsa ham. Davr Id'si.
    /// </summary>
    Task<long> GrantForOrderAsync(
        long studentId, long planId, decimal amount, string note, long actorId, CancellationToken ct = default);

    Task<SubscriptionPeriodDto> CancelAsync(
        long periodId, CancelSubscriptionRequest request, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionPeriodDto>> ListForStudentAsync(
        long studentId, long actorId, CancellationToken ct = default);

    Task<MySubscriptionDto> GetMineAsync(long studentId, CancellationToken ct = default);
}

/// <summary>
/// MUSTAQIL O'QISH OBUNASI (2026-10-23).
///
/// ★ FAQAT O'QUV BO'LIMI VA ADMINISTRATOR boshqaradi: obuna — pul, ya'ni
///   to'lov qabul qilishdagi kabi rol.
/// ★ GURUH VA OBUNA BIR-BIRINI TAQIQLAYDI: guruhdagi o'quvchiga obuna
///   berilmaydi (va aksincha, <c>GroupService.AddMemberAsync</c>). Sabab
///   <see cref="ISubscriptionAccess"/> izohida.
/// ★ Har o'zgarishdan keyin gating keshi tozalanadi — darslar darhol
///   ochiladi yoki yopiladi.
/// </summary>
public sealed class SubscriptionService(
    IApplicationDbContext db,
    ISubscriptionAccess access,
    IGatingService gating,
    TimeProvider clock) : ISubscriptionService
{
    // ================================================================ rejalar

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        return await db.SubscriptionPlans.AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.Course!.Name)
            .ThenBy(p => p.Months)
            .Select(p => new SubscriptionPlanDto(
                p.Id, p.Name, p.CourseId, p.Course!.Name, p.Months, p.Price, p.IsActive,
                p.Kind, p.Description, p.IsPublic, p.IncludesInteractiveBook))
            .ToListAsync(ct);
    }

    public async Task<SubscriptionPlanDto> CreatePlanAsync(
        SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureManagerAsync(actorId, ct);

        var plan = new SubscriptionPlan { Name = request.Name.Trim() };
        await ApplyAsync(plan, request, ct);

        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync(ct);

        return await PlanDtoAsync(plan.Id, ct);
    }

    public async Task<SubscriptionPlanDto> UpdatePlanAsync(
        long planId, SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureManagerAsync(actorId, ct);

        var plan = await db.SubscriptionPlans.AsTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), planId);

        plan.Name = request.Name.Trim();
        await ApplyAsync(plan, request, ct);
        plan.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return await PlanDtoAsync(plan.Id, ct);
    }

    private async Task ApplyAsync(SubscriptionPlan plan, SaveSubscriptionPlanRequest request, CancellationToken ct)
    {
        if (!await db.Courses.AsNoTracking().AnyAsync(c => c.Id == request.CourseId && c.IsActive, ct))
            throw Invalid(nameof(request.CourseId), "Kurs topilmadi yoki faol emas.");

        plan.CourseId = request.CourseId;
        plan.Months = request.Months;
        plan.Price = request.Price;
        plan.IsActive = request.IsActive;
        plan.Kind = request.Kind;
        plan.IsPublic = request.IsPublic;
        plan.IncludesInteractiveBook = request.IncludesInteractiveBook;

        var description = request.Description?.Trim();
        plan.Description = string.IsNullOrEmpty(description) ? null : description;

        try
        {
            plan.Validate();
        }
        catch (DomainException ex)
        {
            throw Invalid("plan", ex.Message);
        }
    }

    private Task<SubscriptionPlanDto> PlanDtoAsync(long planId, CancellationToken ct) =>
        db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.Id == planId)
            .Select(p => new SubscriptionPlanDto(
                p.Id, p.Name, p.CourseId, p.Course!.Name, p.Months, p.Price, p.IsActive,
                p.Kind, p.Description, p.IsPublic, p.IncludesInteractiveBook))
            .FirstAsync(ct);

    // ================================================================ obuna

    public async Task<SubscriptionPeriodDto> GrantAsync(
        GrantSubscriptionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);

        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PlanId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), request.PlanId);

        if (!plan.IsActive)
            throw new ConflictException("Bu reja o'chirilgan — yangi obuna berilmaydi.");

        var periodId = await GrantCoreAsync(
            request.StudentId, plan, plan.Price, request.Method, Clip(request.Note), actor.Id, ct);

        return await PeriodDtoAsync(periodId, ct);
    }

    public async Task<long> GrantForOrderAsync(
        long studentId, long planId, decimal amount, string note, long actorId, CancellationToken ct = default)
    {
        var actor = await EnsureManagerAsync(actorId, ct);

        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), planId);

        return await GrantCoreAsync(studentId, plan, amount, PaymentMethod.Card, Clip(note), actor.Id, ct);
    }

    private async Task<long> GrantCoreAsync(
        long studentId, SubscriptionPlan plan, decimal amount, PaymentMethod method, string? note,
        long actorId, CancellationToken ct)
    {
        // ★ GURUH TARIFI OBUNA EMAS: u jonli kursning oylik narxi va pul balansga tushadi.
        if (plan.Kind != PlanKind.SelfStudy)
            throw Invalid("planId", "Bu guruh kursi tarifi — mustaqil o'qish obunasi berilmaydi.");

        var student = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        if (student.Role != UserRole.Student)
            throw Invalid("studentId", "Obuna faqat o'quvchiga beriladi.");
        if (!student.IsActive)
            throw new ConflictException("Foydalanuvchi profili faol emas.");

        var inGroup = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == student.Id
                     && (m.Status == MemberStatus.Active || m.Status == MemberStatus.Paused)
                     && m.Group!.Type != GroupType.Curator)
            .Select(m => m.Group!.Name)
            .FirstOrDefaultAsync(ct);

        if (inGroup is not null)
        {
            throw new ConflictException(
                $"O'quvchi \"{inGroup}\" guruhida. Mustaqil o'qish obunasi faqat guruhsiz "
                + "o'quvchiga beriladi — avval guruhdan chiqaring.");
        }

        var now = clock.GetUtcNow();
        var upcoming = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.StudentId == student.Id && p.CancelledAt == null && p.EndsAt > now)
            .Select(p => new { p.CourseId, p.EndsAt })
            .ToListAsync(ct);

        if (upcoming.Exists(p => p.CourseId != plan.CourseId))
        {
            throw new ConflictException(
                "O'quvchining boshqa kursga faol obunasi bor. Bir vaqtda bitta kurs — "
                + "avval o'sha obunani bekor qiling.");
        }

        // Uzaytirish: yangi davr mavjudining OXIRIDAN boshlanadi — to'langan kun yo'qolmaydi.
        var startsAt = upcoming.Count == 0 ? now : upcoming.Max(p => p.EndsAt);

        var period = new SubscriptionPeriod
        {
            StudentId = student.Id,
            PlanId = plan.Id,
            CourseId = plan.CourseId,
            StartsAt = startsAt,
            EndsAt = startsAt.AddMonths(plan.Months),
            Amount = amount,
            Method = method,
            Note = note,
            CreatedById = actorId,
            CreatedAt = now,
        };

        db.SubscriptionPeriods.Add(period);
        await db.SaveChangesAsync(ct);
        await gating.InvalidateAsync(student.Id, ct);

        return period.Id;
    }

    public async Task<SubscriptionPeriodDto> CancelAsync(
        long periodId, CancelSubscriptionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);

        var period = await db.SubscriptionPeriods.AsTracking().FirstOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPeriod), periodId);

        if (period.IsCancelled)
            throw new ConflictException("Bu obuna allaqachon bekor qilingan.");

        var now = clock.GetUtcNow();
        period.CancelledAt = now;
        period.CancelledById = actor.Id;
        period.CancelReason = Clip(request.Reason);
        period.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await gating.InvalidateAsync(period.StudentId, ct);

        return await PeriodDtoAsync(period.Id, ct);
    }

    public async Task<IReadOnlyList<SubscriptionPeriodDto>> ListForStudentAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        if (studentId != actorId)
            await EnsureManagerAsync(actorId, ct);

        return await PeriodsQuery(p => p.StudentId == studentId).ToListAsync(ct);
    }

    public async Task<MySubscriptionDto> GetMineAsync(long studentId, CancellationToken ct = default)
    {
        var history = await PeriodsQuery(p => p.StudentId == studentId).ToListAsync(ct);

        var state = await access.GetStateAsync(studentId, ct);
        if (state is null)
            return new MySubscriptionDto(false, false, null, null, null, null, history);

        var courseName = await db.Courses.AsNoTracking()
            .Where(c => c.Id == state.CourseId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        int? daysLeft = state.IsActive
            ? Math.Max(0, (int)Math.Floor((state.EndsAt - clock.GetUtcNow()).TotalDays))
            : null;

        return new MySubscriptionDto(true, state.IsActive, state.CourseId, courseName, state.EndsAt, daysLeft, history);
    }

    // ================================================================ yordamchi

    /// <summary>Filtr va tartib PROYEKSIYADAN OLDIN — aks holda EF uni SQL'ga o'gira olmaydi.</summary>
    private IQueryable<SubscriptionPeriodDto> PeriodsQuery(Expression<Func<SubscriptionPeriod, bool>> filter) =>
        db.SubscriptionPeriods.AsNoTracking()
            .Where(filter)
            .OrderByDescending(p => p.StartsAt)
            .Select(p => new SubscriptionPeriodDto(
            p.Id,
            p.StudentId,
            p.Plan != null ? p.Plan.Name : null,
            p.CourseId,
            p.Course!.Name,
            p.StartsAt,
            p.EndsAt,
            p.Amount,
            p.Method,
            p.Note,
            p.CreatedAt,
            p.CancelledAt,
            p.CancelReason));

    private Task<SubscriptionPeriodDto> PeriodDtoAsync(long periodId, CancellationToken ct) =>
        PeriodsQuery(p => p.Id == periodId).FirstAsync(ct);

    private async Task<User> EnsureManagerAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive || actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Obunalarni faqat o'quv bo'limi yoki administrator boshqaradi.");

        return actor;
    }

    private static string? Clip(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= SubscriptionPeriod.MaxNoteLength
            ? trimmed
            : trimmed[..SubscriptionPeriod.MaxNoteLength];
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
