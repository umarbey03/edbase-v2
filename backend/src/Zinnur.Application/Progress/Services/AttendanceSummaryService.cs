using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Progress.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Progress.Services;

/// <summary>
/// ========================================================================
/// DAVOMAT XULOSASI — BITTA SO'ROV
/// ========================================================================
///
/// ★ N+1 YO'Q: darslar va o'quvchining har darsdagi holati BITTA
/// <c>SELECT</c> da (ichki korrelyatsion so'rov bilan) olinadi. Eski
/// tizim buni to'rt alohida agregat so'rov bilan qilardi va "streak"
/// uchun BESHINCHISINI yuborardi.
///
/// ★ NIMA UCHUN XOTIRADA HISOBLANADI: bitta o'quvchining butun kursdagi
/// darslari ~200 qator (8 oy × haftada 2 ustoz + 3 kurator darsi). Bu
/// bitta indeksli o'qish va xotirada bir marta aylanish — SQL'da uchta
/// alohida `GROUP BY` yuborishdan arzon, ustiga "streak" ni SQL'da
/// hisoblash oyna funksiyalari talab qilardi va o'qilmaydigan bo'lardi.
///
/// ★ "YOZUV YO'Q" = "KELMAGAN". Davomat qatori faqat xonaga KIRGAN
/// o'quvchi uchun yaratiladi (`LiveSessionService.RegisterJoinAsync`).
/// Shuning uchun <c>LEFT JOIN</c> natijasidagi <c>null</c> — qoldirilgan
/// dars. Eski tizim ham aynan shunday hisoblardi.
/// </summary>
public sealed class AttendanceSummaryService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone) : IAttendanceSummaryService
{
    /// <summary>
    /// "Streak" uchun ko'riladigan eng yangi darslar soni.
    /// Undan uzun seriya amalda bo'lmaydi (bir o'quv yili ~70 ustoz darsi),
    /// chegara esa hisobni bir qatorda ushlab turadi.
    /// </summary>
    private const int StreakWindow = 120;

    public async Task<AttendanceSummaryDto> GetMySummaryAsync(
        long studentId,
        long? groupId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct = default)
    {
        await LoadStudentAsync(studentId, ct);

        if (fromDate is { } start && toDate is { } end && start > end)
            throw Invalid("fromDate", "Boshlanish sanasi tugash sanasidan keyin bo'lishi mumkin emas.");

        var groupIds = await ResolveGroupIdsAsync(studentId, groupId, ct);

        if (groupIds.Count == 0)
        {
            return new AttendanceSummaryDto(
                groupIds, fromDate, toDate,
                Empty(), Empty(), Empty(), Streak: 0);
        }

        var zone = timeZone.TimeZone;

        // Mahalliy sana -> UTC oralig'i. `to` KIRADI, shuning uchun
        // keyingi kunning boshi olinadi (kun oxirini `23:59:59` deb yozish
        // o'sha oxirgi soniyani yo'qotardi).
        var fromUtc = fromDate is { } f ? LocalWallClock.StartOfDayUtc(f, zone) : (DateTimeOffset?)null;
        var toUtc = toDate is { } t ? LocalWallClock.StartOfDayUtc(t.AddDays(1), zone) : (DateTimeOffset?)null;

        var rows = await db.LiveSessions.AsNoTracking()
            .Where(s => groupIds.Contains(s.GroupId)
                     && s.Status == SessionStatus.Ended
                     && (fromUtc == null || s.ScheduledStart >= fromUtc)
                     && (toUtc == null || s.ScheduledStart < toUtc))
            .OrderByDescending(s => s.ScheduledStart)
            .ThenByDescending(s => s.Id)
            .Select(s => new SessionRow(
                s.Type,
                db.Attendances
                    .Where(a => a.SessionId == s.Id && a.StudentId == studentId)
                    .Select(a => (AttendanceStatus?)a.Status)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        var overall = AttendanceTally.Empty;
        var teacher = AttendanceTally.Empty;
        var assistant = AttendanceTally.Empty;

        foreach (var row in rows)
        {
            var attended = row.Status is { } status && AttendanceMath.IsAttended(status);

            overall = overall.Add(attended);

            if (row.Type == SessionType.Teacher)
                teacher = teacher.Add(attended);
            else
                assistant = assistant.Add(attended);
        }

        // `rows` allaqachon YANGIDAN ESKIGA tartiblangan — streak shu tartibni kutadi.
        var streak = AttendanceMath.Streak(rows.Take(StreakWindow).Select(r => r.Status));

        return new AttendanceSummaryDto(
            groupIds, fromDate, toDate,
            Map(overall), Map(teacher), Map(assistant), streak);
    }

    // ---------------------------------------------------------------- yordamchi

    /// <summary>
    /// Qaysi guruhlar hisobga olinadi.
    ///
    /// ★ RUXSAT SHU YERDA: `groupId` berilgan bo'lsa u o'quvchining FAOL
    /// a'zoligi bo'lgan guruhlar ro'yxatidan tanlanadi — ya'ni begona
    /// guruh Id'si yuborilsa natija bo'sh emas, 403 bo'ladi.
    /// </summary>
    private async Task<IReadOnlyList<long>> ResolveGroupIdsAsync(
        long studentId, long? groupId, CancellationToken ct)
    {
        var mine = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.IsActive)
            .Select(m => m.GroupId)
            .Distinct()
            .ToListAsync(ct);

        if (groupId is not { } wanted)
            return mine;

        if (!mine.Contains(wanted))
            throw new ForbiddenException("Bu guruh ma'lumotiga ruxsatingiz yo'q.");

        return [wanted];
    }

    private async Task<User> LoadStudentAsync(long studentId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        if (!user.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return user;
    }

    private static AttendanceBucketDto Map(AttendanceTally tally) =>
        new(tally.Total, tally.Attended, tally.Missed, tally.Percent);

    private static AttendanceBucketDto Empty() => Map(AttendanceTally.Empty);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private sealed record SessionRow(SessionType Type, AttendanceStatus? Status);
}
