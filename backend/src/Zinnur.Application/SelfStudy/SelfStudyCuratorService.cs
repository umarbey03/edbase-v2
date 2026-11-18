using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.SelfStudy;

/// <summary>
/// Mustaqil o'quvchiga kurator biriktirish va kuratorning o'z o'quvchilari.
/// Qoida va tarix: <see cref="SelfStudyCuratorAssignment"/>.
/// </summary>
public interface ISelfStudyCuratorService
{
    /// <summary>O'quv bo'limi: o'quvchining joriy kuratori va tarixi.</summary>
    Task<StudentCuratorInfoDto> GetForStudentAsync(long studentId, long actorId, CancellationToken ct = default);

    /// <summary>O'quv bo'limi: kurator biriktirish yoki almashtirish.</summary>
    Task<StudentCuratorInfoDto> AssignAsync(
        long studentId, AssignSelfStudyCuratorRequest request, long actorId, CancellationToken ct = default);

    /// <summary>O'quv bo'limi: kuratorni olib qo'yish (o'quvchi kuratorsiz qoladi).</summary>
    Task<StudentCuratorInfoDto> UnassignAsync(long studentId, long actorId, CancellationToken ct = default);

    /// <summary>O'quv bo'limi: kurator bo'la oladigan xodimlar va ularning yuki.</summary>
    Task<IReadOnlyList<CuratorCandidateDto>> ListCandidatesAsync(long actorId, CancellationToken ct = default);

    /// <summary>O'quv bo'limi: faol obunali, lekin kuratorsiz o'quvchilar.</summary>
    Task<IReadOnlyList<UnassignedSelfStudyStudentDto>> ListUnassignedAsync(long actorId, CancellationToken ct = default);

    /// <summary>Kurator: o'ziga (asosiy yoki zaxira) biriktirilgan o'quvchilar.</summary>
    Task<IReadOnlyList<MySelfStudyStudentDto>> ListMyStudentsAsync(long staffId, CancellationToken ct = default);

    /// <summary>O'quvchi: o'z kuratori (yo'q bo'lsa <c>null</c>).</summary>
    Task<MyCuratorDto?> GetMyCuratorAsync(long studentId, CancellationToken ct = default);

    /// <summary>
    /// O'quvchi guruhga o'tkazilganda faol biriktirishni tugatadi. Tranzaksiya
    /// chaqiruvchida — saqlash ham o'shanda (<c>SaveChanges</c> bu yerda yo'q).
    /// </summary>
    Task EndForGroupTransferAsync(long studentId, long actorId, CancellationToken ct = default);
}

public sealed class SelfStudyCuratorService(IApplicationDbContext db, TimeProvider clock)
    : ISelfStudyCuratorService
{
    /// <summary>Kurator bo'la oladigan rollar: kurator (Assistant) va ustoz.</summary>
    private static readonly UserRole[] CuratorRoles = [UserRole.Assistant, UserRole.Teacher];

    public async Task<StudentCuratorInfoDto> GetForStudentAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);
        await LoadStudentAsync(studentId, ct);
        return await InfoAsync(studentId, ct);
    }

    public async Task<StudentCuratorInfoDto> AssignAsync(
        long studentId, AssignSelfStudyCuratorRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);
        var student = await LoadStudentAsync(studentId, ct);

        // 🔴 GURUH O'QUVCHISIGA EMAS: uning kuratori guruh o'rindig'idan
        //    keladi (`StaffResponsibility`). Ikki manba bir o'quvchida
        //    "kim tekshiradi" savoliga ikki xil javob berardi.
        var inGroup = await db.GroupMembers.AsNoTracking()
            .AnyAsync(m => m.StudentId == student.Id && m.Status == MemberStatus.Active, ct);
        if (inGroup)
        {
            throw new ConflictException(
                "O'quvchi guruhda — uning kuratori guruh orqali belgilanadi.");
        }

        var hasSubscription = await db.SubscriptionPeriods.AsNoTracking()
            .AnyAsync(p => p.StudentId == student.Id && p.CancelledAt == null, ct);
        if (!hasSubscription)
        {
            throw Invalid(
                "studentId",
                "Kurator faqat Mustaqil o'quvchiga biriktiriladi — avval obuna bering.");
        }

        await EnsureCuratorAsync(request.CuratorId, "curatorId", ct);
        if (request.BackupCuratorId is { } backupId)
        {
            if (backupId == request.CuratorId)
                throw Invalid("backupCuratorId", "Zaxira kurator asosiy kuratordan boshqa odam bo'lishi kerak.");
            await EnsureCuratorAsync(backupId, "backupCuratorId", ct);
        }

        await db.InTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow();
            var current = await db.SelfStudyCuratorAssignments
                .AsTracking()
                .FirstOrDefaultAsync(a => a.StudentId == student.Id && a.EndedAt == null, token);

            // Qayta saqlash — hech narsa o'zgarmaydi, tarixga ham yozilmaydi.
            if (current is not null && current.IsSameAs(request.CuratorId, request.BackupCuratorId))
                return 0;

            if (current is not null)
            {
                current.End(actor.Id, now);
                // ★ Avval eski qator yopiladi, keyin yangisi: qisman unikal
                //   indeks (`EndedAt IS NULL`) bitta SaveChanges ichidagi
                //   tartibga ishonmaydi.
                await db.SaveChangesAsync(token);
            }

            db.SelfStudyCuratorAssignments.Add(SelfStudyCuratorAssignment.Create(
                student.Id, request.CuratorId, request.BackupCuratorId, actor.Id, now));

            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex)
            {
                throw new ConflictException(
                    "Kurator shu paytning o'zida boshqa xodim tomonidan o'zgartirildi — sahifani yangilang.",
                    ex);
            }

            return 0;
        }, ct);

        return await InfoAsync(student.Id, ct);
    }

    public async Task<StudentCuratorInfoDto> UnassignAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        var actor = await EnsureManagerAsync(actorId, ct);
        await LoadStudentAsync(studentId, ct);

        var current = await db.SelfStudyCuratorAssignments
            .AsTracking()
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.EndedAt == null, ct);

        if (current is not null)
        {
            current.End(actor.Id, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }

        return await InfoAsync(studentId, ct);
    }

    public async Task<IReadOnlyList<CuratorCandidateDto>> ListCandidatesAsync(
        long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var rows = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && CuratorRoles.Contains(u.Role))
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Role,
                Active = db.SelfStudyCuratorAssignments
                    .Count(a => a.EndedAt == null && a.CuratorId == u.Id),
            })
            .ToListAsync(ct);

        return rows.ConvertAll(r => new CuratorCandidateDto(r.Id, r.FullName, r.Role.ToString(), r.Active));
    }

    public async Task<IReadOnlyList<UnassignedSelfStudyStudentDto>> ListUnassignedAsync(
        long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);
        var now = clock.GetUtcNow();

        var rows = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.CancelledAt == null && p.StartsAt <= now && now < p.EndsAt)
            .Where(p => !db.SelfStudyCuratorAssignments
                .Any(a => a.StudentId == p.StudentId && a.EndedAt == null))
            .GroupBy(p => new { p.StudentId, p.Student!.FullName })
            .Select(g => new { g.Key.StudentId, g.Key.FullName, EndsAt = g.Max(p => p.EndsAt) })
            .OrderBy(r => r.FullName)
            .ToListAsync(ct);

        return rows.ConvertAll(r => new UnassignedSelfStudyStudentDto(r.StudentId, r.FullName, r.EndsAt));
    }

    public async Task<IReadOnlyList<MySelfStudyStudentDto>> ListMyStudentsAsync(
        long staffId, CancellationToken ct = default)
    {
        var staff = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == staffId, ct)
            ?? throw new NotFoundException(nameof(User), staffId);
        if (!staff.IsActive || staff.Role == UserRole.Student)
            throw new ForbiddenException("Bu ro'yxat faqat xodimlar uchun.");

        var now = clock.GetUtcNow();

        var rows = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(SelfStudyCuratorAssignment.ActiveFor(staffId))
            .Select(a => new
            {
                a.StudentId,
                a.Student!.FullName,
                IsBackup = a.CuratorId != staffId,
                a.AssignedAt,
                EndsAt = db.SubscriptionPeriods
                    .Where(p => p.StudentId == a.StudentId && p.CancelledAt == null)
                    .Max(p => (DateTimeOffset?)p.EndsAt),
                Active = db.SubscriptionPeriods
                    .Any(p => p.StudentId == a.StudentId && p.CancelledAt == null
                           && p.StartsAt <= now && now < p.EndsAt),
                Pending = db.Submissions
                    .Count(s => s.StudentId == a.StudentId && s.Status == SubmissionStatus.Submitted),
            })
            .ToListAsync(ct);

        // Avval asosiy o'quvchilar, keyin zaxiradagilar; ichida — ko'p ish kutayotgani yuqorida.
        return rows
            .OrderBy(r => r.IsBackup)
            .ThenByDescending(r => r.Pending)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .Select(r => new MySelfStudyStudentDto(
                r.StudentId, r.FullName, r.IsBackup, r.AssignedAt, r.EndsAt, r.Active, r.Pending))
            .ToList();
    }

    public async Task<MyCuratorDto?> GetMyCuratorAsync(long studentId, CancellationToken ct = default) =>
        await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.EndedAt == null && a.Curator!.IsActive)
            .Select(a => new MyCuratorDto(a.CuratorId, a.Curator!.FullName))
            .FirstOrDefaultAsync(ct);

    public async Task EndForGroupTransferAsync(long studentId, long actorId, CancellationToken ct = default)
    {
        var current = await db.SelfStudyCuratorAssignments
            .AsTracking()
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.EndedAt == null, ct);
        current?.End(actorId, clock.GetUtcNow());
    }

    // ================================================================ yordamchilar

    private async Task<StudentCuratorInfoDto> InfoAsync(long studentId, CancellationToken ct)
    {
        var history = await db.SelfStudyCuratorAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .OrderByDescending(a => a.AssignedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new SelfStudyCuratorDto(
                a.Id,
                a.CuratorId,
                a.Curator!.FullName,
                a.BackupCuratorId,
                a.BackupCurator == null ? null : a.BackupCurator.FullName,
                a.AssignedAt,
                db.Users.Where(u => u.Id == a.AssignedById).Select(u => u.FullName).FirstOrDefault(),
                a.EndedAt))
            .ToListAsync(ct);

        return new StudentCuratorInfoDto(history.Find(h => h.EndedAt is null), history);
    }

    private async Task<User> LoadStudentAsync(long studentId, CancellationToken ct)
    {
        var student = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);
        if (student.Role != UserRole.Student)
            throw Invalid("studentId", "Kurator faqat o'quvchiga biriktiriladi.");
        return student;
    }

    private async Task EnsureCuratorAsync(long userId, string field, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !user.IsActive || !CuratorRoles.Contains(user.Role))
            throw Invalid(field, "Kurator faol kurator yoki ustoz bo'lishi kerak.");
    }

    private async Task<User> EnsureManagerAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive || actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Kuratorni faqat o'quv bo'limi yoki administrator biriktiradi.");

        return actor;
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
