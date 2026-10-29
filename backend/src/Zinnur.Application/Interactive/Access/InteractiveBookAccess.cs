using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payments.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Interactive.Access;

/// <summary>Darslikka kirish holati.</summary>
public enum BookAccessStatus
{
    /// <summary>Kirish yo'q va taklif ham ko'rsatilmaydi (muddati o'tgan, to'lov bloki, kurs yo'q).</summary>
    None = 0,

    /// <summary>Oddiy Mustaqil: darslik ko'rinmaydi, o'rniga "Mustaqil+ da" kartasi.</summary>
    Upsell = 1,

    /// <summary>Mustaqil+ yoki guruh (Milliy) o'quvchisi.</summary>
    Granted = 2,
}

/// <param name="Status">Natija.</param>
/// <param name="CourseId">Huquq qaysi kurs uchun (guruh kursi yoki obuna kursi).</param>
/// <param name="Source">"group" / "subscription" / <c>null</c> — hisobot va test uchun.</param>
public sealed record BookAccess(BookAccessStatus Status, long? CourseId, string? Source)
{
    public bool Granted => Status == BookAccessStatus.Granted;
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARSLIKKA KIRISH HUQUQI — YAGONA JOY (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
/// | Holat                                              | Natija   |
/// |----------------------------------------------------|----------|
/// | To'lov bloki (`PaymentBlockScope.Video`) faol      | None     |
/// | Faol guruh a'zosi (Milliy) — egasi qarori 1        | Granted  |
/// | Faol obuna davri, tarifda darslik bor (Mustaqil+)  | Granted  |
/// | Faol obuna davri, tarifda darslik yo'q (Mustaqil)  | Upsell   |
/// | Obuna muddati tugagan / bekor qilingan / yo'q      | None     |
///
/// ★ Har o'quvchi endpointi shu yerdan so'raydi; frontend faqat ko'rsatadi.
/// ★ Kurs mosligi, darsning gating holati va kitob nashri —
///   `InteractiveUnitService.EnsureStudentAsync` da (birlikka bog'liq).
/// </summary>
public interface IInteractiveBookAccess
{
    Task<BookAccess> GetAsync(long studentId, CancellationToken ct = default);
}

public sealed class InteractiveBookAccess(
    IApplicationDbContext db,
    IPaymentBlockService paymentBlock,
    TimeProvider clock) : IInteractiveBookAccess
{
    public async Task<BookAccess> GetAsync(long studentId, CancellationToken ct = default)
    {
        // Video bilan AYNI darvoza: qarzi bor o'quvchiga darslik ham yopiq.
        var block = await paymentBlock.EvaluateAsync(studentId, PaymentBlockScope.Video, ct);
        if (block.Blocked) return new BookAccess(BookAccessStatus.None, null, null);

        var groupCourse = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.IsActive
                     && m.Group.CourseId != null)
            .OrderBy(m => m.GroupId)
            .Select(m => m.Group!.CourseId)
            .FirstOrDefaultAsync(ct);

        if (groupCourse is { } courseId)
            return new BookAccess(BookAccessStatus.Granted, courseId, "group");

        var now = clock.GetUtcNow();
        var periods = await CurrentPeriodFlags(studentId, now).ToListAsync(ct);

        if (periods.Count == 0) return new BookAccess(BookAccessStatus.None, null, null);

        var withBook = periods.FirstOrDefault(p => p.IncludesInteractiveBook);
        return withBook is not null
            ? new BookAccess(BookAccessStatus.Granted, withBook.CourseId, "subscription")
            : new BookAccess(BookAccessStatus.Upsell, periods[0].CourseId, "subscription");
    }

    /// <summary>
    /// Hozir amaldagi (bekor qilinmagan) davrlar va ularning "darslik kiradimi" belgisi.
    ///
    /// ★ DAVRNING O'Z NUSXASIDAN O'QILADI (<c>SubscriptionPeriod.IncludesInteractiveBook</c>,
    ///   davr berilgan paytdagi tarif belgisi): tarif belgisi keyin o'zgartirilsa,
    ///   allaqachon to'langan davr huquqi jimgina o'zgarmaydi. (2026-10-29)
    /// </summary>
    private IQueryable<PeriodFlag> CurrentPeriodFlags(long studentId, DateTimeOffset now) =>
        db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.StudentId == studentId
                     && p.CancelledAt == null
                     && p.StartsAt <= now
                     && p.EndsAt > now)
            .OrderBy(p => p.StartsAt)
            .Select(p => new PeriodFlag(p.CourseId, p.IncludesInteractiveBook));

    private sealed record PeriodFlag(long CourseId, bool IncludesInteractiveBook);
}
