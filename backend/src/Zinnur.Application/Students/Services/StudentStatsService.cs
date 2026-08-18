using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Students.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Students.Services;

/// <inheritdoc cref="IStudentStatsService"/>
public sealed class StudentStatsService(
    IApplicationDbContext db,
    IStudiedLessonCounter studiedLessons) : IStudentStatsService
{
    /// <inheritdoc />
    public async Task<StudentStatsDto> GetAsync(long actorId, CancellationToken ct = default)
    {
        await EnsureCanViewAsync(actorId, ct);

        var studentIds = await db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Student && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (studentIds.Count == 0)
            return new StudentStatsDto(0, 0, 0, 0, 0, 0);

        // ★ KURATOR GURUHLARI CHIQARIB TASHLANADI: o'quvchilar u yerga
        //   to'g'ridan-to'g'ri a'zo BO'LMAYDI (ular bog'langan ustoz
        //   guruhlaridan keladi). Namunaviy ma'lumotda bunday qator
        //   uchragan, shuning uchun filtr ATAYLAB qo'yilgan —
        //   `StudentClassroomService` dagi AYNI sabab.
        var memberships = await db.GroupMembers
            .AsNoTracking()
            .Where(m => m.Group!.Type != GroupType.Curator)
            .Select(m => new { m.StudentId, m.Status })
            .ToListAsync(ct);

        // O'quvchi bo'yicha ENG KUCHLI holat: faol → pauza → to'xtagan.
        // Sabab `StudentStatsDto` izohida (odam sanaladi, a'zolik emas).
        var statusByStudent = memberships
            .GroupBy(m => m.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.Any(m => m.Status == MemberStatus.Active)
                    ? MemberStatus.Active
                    : g.Any(m => m.Status == MemberStatus.Paused)
                        ? MemberStatus.Paused
                        : MemberStatus.Stopped);

        // Faol o'quvchilarni "probniy" va "aktiv" ga ajratish uchun har
        // biri nechta darsni HAQIQATAN o'tagani kerak.
        //
        // ★ QOIDA `IStudiedLessonCounter` DA — `GroupService` (a'zolik
        //   hodisasini yozayotganda) AYNI portdan foydalanadi. Ikki
        //   nusxada yozilsa, bu karta bilan to'kilishlar hisoboti bir-biriga
        //   mos kelmay qolardi.
        var activeIds = statusByStudent
            .Where(pair => pair.Value == MemberStatus.Active)
            .Select(pair => pair.Key)
            .ToList();

        var lessonsByStudent = await studiedLessons.CountManyAsync(activeIds, ct);

        var active = 0;
        var trial = 0;

        foreach (var studentId in activeIds)
        {
            var lessons = lessonsByStudent.TryGetValue(studentId, out var count) ? count : 0;

            if (lessons >= GroupMembershipEvent.TrialLessonCount) active++;
            else trial++;
        }

        var paused = statusByStudent.Count(pair => pair.Value == MemberStatus.Paused);
        var stopped = statusByStudent.Count(pair => pair.Value == MemberStatus.Stopped);

        // ★ MANBASI BOSHQA (hodisa jurnali) — sabab DTO izohida.
        //   ODAM sanaladi: bir o'quvchi ikki guruhdan ketgan bo'lsa ham bitta.
        var activeLosses = await db.GroupMembershipEvents
            .AsNoTracking()
            .Where(e => e.Kind == MembershipEventKind.Stopped
                && e.LessonsCompleted >= GroupMembershipEvent.TrialLessonCount)
            .Select(e => e.StudentId)
            .Distinct()
            .CountAsync(ct);

        var withoutGroup = studentIds.Count(id => !statusByStudent.ContainsKey(id));

        return new StudentStatsDto(
            Active: active,
            Trial: trial,
            Paused: paused,
            Stopped: stopped,
            ActiveLosses: activeLosses,
            WithoutGroup: withoutGroup);
    }

    /// <summary>
    /// Faqat o'quv bo'limi va admin. Ruxsat SERVISDA ham tekshiriladi —
    /// loyihadagi qoida (controller atributiga qo'shimcha).
    /// </summary>
    private async Task EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct);

        if (role is null)
            throw new NotFoundException(nameof(User), actorId);

        if (role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "Bu ko'rsatkichlarni faqat o'quv bo'limi xodimi yoki administrator ko'radi.");
        }
    }
}
