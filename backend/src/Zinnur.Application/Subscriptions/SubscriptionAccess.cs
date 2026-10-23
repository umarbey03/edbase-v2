using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Subscriptions;

/// <summary>Mustaqil o'quvchining obuna holati.</summary>
/// <param name="CourseId">Obuna qilingan kurs.</param>
/// <param name="EndsAt">Kirish huquqi tugaydigan vaqt (ketma-ket davrlar qo'shilgan).</param>
/// <param name="IsActive">Hozir kirish huquqi bormi; <c>false</c> — muddati tugagan.</param>
public sealed record SubscriptionState(long CourseId, DateTimeOffset EndsAt, bool IsActive);

/// <summary>
/// "Bu o'quvchi MUSTAQIL o'quvchimi va obunasi amaldami?" — YAGONA javob
/// beruvchi (2026-10-23). Gating, kurs, reyting va guruhga qo'shish shu
/// yerdan so'raydi — qoida bir joyda yashaydi.
///
/// ★ GURUH USTUN: faol guruh a'zoligi bo'lgan o'quvchi uchun obuna
///   hisobga olinmaydi. Amalda ikkalasi bir vaqtda bo'lmaydi — guruhga
///   qo'shish va obuna berish bir-birini taqiqlaydi
///   (<c>GroupService.AddMemberAsync</c>, <c>SubscriptionService.GrantAsync</c>).
/// </summary>
public interface ISubscriptionAccess
{
    /// <returns>Obuna hech qachon bo'lmagan bo'lsa <c>null</c>.</returns>
    Task<SubscriptionState?> GetStateAsync(long studentId, CancellationToken ct = default);

    /// <summary>Hozir faol obunasi bormi (guruh a'zoligiga qaramaydi).</summary>
    Task<bool> HasActiveAsync(long studentId, CancellationToken ct = default);

    /// <summary>Hozir faol obunasi bor o'quvchilar (mustaqillar reytingi auditoriyasi).</summary>
    IQueryable<long> ActiveStudentIds();
}

public sealed class SubscriptionAccess(IApplicationDbContext db, TimeProvider clock) : ISubscriptionAccess
{
    public async Task<SubscriptionState?> GetStateAsync(long studentId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var periods = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.CancelledAt == null)
            .Select(p => new { p.CourseId, p.StartsAt, p.EndsAt })
            .ToListAsync(ct);

        if (periods.Count == 0) return null;

        var current = periods
            .Where(p => p.StartsAt <= now && now < p.EndsAt)
            .OrderByDescending(p => p.EndsAt)
            .FirstOrDefault();

        if (current is not null)
        {
            // Oldindan to'langan keyingi davrlar ham tugash sanasiga qo'shiladi.
            var endsAt = periods
                .Where(p => p.CourseId == current.CourseId && p.EndsAt > now)
                .Max(p => p.EndsAt);
            return new SubscriptionState(current.CourseId, endsAt, IsActive: true);
        }

        var last = periods.OrderByDescending(p => p.EndsAt).First();
        return new SubscriptionState(last.CourseId, last.EndsAt, IsActive: false);
    }

    public async Task<bool> HasActiveAsync(long studentId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return await db.SubscriptionPeriods.AsNoTracking()
            .AnyAsync(p => p.StudentId == studentId
                        && p.CancelledAt == null
                        && p.StartsAt <= now
                        && p.EndsAt > now, ct);
    }

    public IQueryable<long> ActiveStudentIds()
    {
        var now = clock.GetUtcNow();
        return db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.CancelledAt == null
                     && p.StartsAt <= now
                     && p.EndsAt > now
                     && p.Student!.Role == UserRole.Student
                     && p.Student.IsActive)
            .Select(p => p.StudentId)
            .Distinct();
    }
}
