using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Absentees.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Absentees.Services;

/// <inheritdoc cref="IAbsenteeService"/>
public sealed class AbsenteeService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IAbsenteeService
{
    /// <summary>
    /// "Ketma-ket qoldirilgan" hisobida ortga qancha dars ko'riladi.
    ///
    /// Chegara bo'lmasa, o'quvchining butun tarixi o'qilardi. 10 ta dars
    /// — ~1 oylik kesim va "bu o'quvchi ketyapti" degan xulosa uchun
    /// yetarlidan ko'p.
    /// </summary>
    private const int StreakLookback = 10;

    /// <summary>Ketma-ket shu sondan ko'p qoldirgan — "xavf" belgisi.</summary>
    private const int RiskStreak = 3;

    /// <inheritdoc />
    public async Task<AbsenteeReportDto> GetAsync(
        AbsenteeQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var zone = timeZone.TimeZone;

        // ★ STANDART — KECHA (bugun emas): loyiha egasi aynan *"bir kun
        //   avval darsga kirmagan"* larni so'radi, va bugungi darslarning
        //   ko'pi hali o'tmagan bo'ladi.
        var to = query.To ?? LocalWallClock.LocalDate(clock.GetUtcNow(), zone).AddDays(-1);
        var from = query.From ?? to;

        if (from > to)
            throw Invalid("from", "Davr boshi oxiridan keyin bo'lmasligi kerak.");

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        // Yarim ochiq oraliq: chap chegara KIRADI, o'ng chegara `to + 1
        // kun` (KIRMAYDI) — loyihadagi barcha sana oraliqlari bilan AYNI.
        var dayStart = LocalWallClock.StartOfDayUtc(from, zone);
        var dayEnd = LocalWallClock.StartOfDayUtc(to.AddDays(1), zone);

        // ---------------------------------------------------------- o'sha kungi darslar
        // ★ FAQAT YAKUNLANGAN DARS: hali boshlanmagan yoki davom
        //   etayotgan darsda "kelmadi" degan xulosa noto'g'ri bo'lardi —
        //   o'quvchi keyinroq kirishi mumkin.
        var sessions = db.LiveSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Ended
                && s.ScheduledStart >= dayStart
                && s.ScheduledStart < dayEnd);

        if (query.GroupId is { } groupId) sessions = sessions.Where(s => s.GroupId == groupId);
        if (query.TeacherId is { } teacherId) sessions = sessions.Where(s => s.HostId == teacherId);

        var sessionRows = await sessions
            .Select(s => new
            {
                s.Id,
                s.GroupId,
                s.ScheduledStart,
                GroupName = s.Group!.Name,
                TeacherName = s.Group.TeacherId == null
                    ? null
                    : db.Users.Where(u => u.Id == s.Group.TeacherId).Select(u => u.FullName).FirstOrDefault(),
                AssistantName = s.Group.AssistantId == null
                    ? null
                    : db.Users.Where(u => u.Id == s.Group.AssistantId).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        if (sessionRows.Count == 0)
            return new AbsenteeReportDto(from, to, 0, 0, 0, 0, page, pageSize, []);

        var sessionIds = sessionRows.ConvertAll(s => s.Id);

        // ---------------------------------------------------------- kelmaganlar
        var wanted = query.IncludePartial
            ? new[] { AttendanceStatus.Absent, AttendanceStatus.Partial }
            : [AttendanceStatus.Absent];

        var absences = db.Attendances.AsNoTracking()
            .Where(a => sessionIds.Contains(a.SessionId) && wanted.Contains(a.Status));

        var term = NormalizeSearch(query.Search);

        if (term is not null)
        {
#pragma warning disable CA1304, CA1311
            absences = absences.Where(a => EF.Functions.Like(a.Student!.FullName.ToLower(), term));
#pragma warning restore CA1304, CA1311
        }

        var rows = await absences
            .Select(a => new
            {
                a.SessionId,
                a.StudentId,
                StudentName = a.Student!.FullName,
                a.Student.Phone,
                TelegramLinked = a.Student.TelegramId != null,
                a.Status,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new AbsenteeReportDto(from, to, sessionRows.Count, 0, 0, 0, page, pageSize, []);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var groupIds = sessionRows.ConvertAll(s => s.GroupId).Distinct().ToList();

        var streaks = await BuildStreaksAsync(studentIds, groupIds, dayEnd, ct);
        var recent = await BuildRecentMissesAsync(studentIds, dayEnd, zone, ct);

        var activeCounts = await db.GroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId) && m.Status == MemberStatus.Active)
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        var sessionById = sessionRows.ToDictionary(s => s.Id);

        // ---------------------------------------------------------- yig'ish
        var groups = rows
            .GroupBy(r => sessionById[r.SessionId].GroupId)
            .Select(g =>
            {
                var head = sessionById[g.First().SessionId];

                var students = g
                    // ★ HAR O'QUVCHIGA BITTA QATOR: davr bir kundan uzun
                    //   bo'lsa, bitta o'quvchi bir necha darsni qoldirgan
                    //   bo'lishi mumkin. Har biri alohida qator bo'lsa,
                    //   ro'yxat takrorlarga to'lib, "nechta odamga
                    //   qo'ng'iroq qilaman?" degan savolga javob bermay
                    //   qolardi.
                    .GroupBy(r => r.StudentId)
                    .Select(byStudent =>
                    {
                        // Davrdagi ENG SO'NGGI qoldirilgan dars.
                        var last = byStudent
                            .OrderByDescending(r => sessionById[r.SessionId].ScheduledStart)
                            .First();

                        var session = sessionById[last.SessionId];
                        streaks.TryGetValue((last.StudentId, session.GroupId), out var streak);
                        recent.TryGetValue(last.StudentId, out var missed);

                        return new AbsenteeStudentDto(
                            last.StudentId,
                            last.StudentName,
                            last.Phone,
                            last.TelegramLinked,
                            last.SessionId,
                            session.ScheduledStart,
                            last.Status.ToString(),
                            streak,
                            missed,
                            byStudent.Count());
                    })
                    .Where(s => s.ConsecutiveMisses >= query.MinStreak)
                    // ★ ENG XAVFLISI TEPADA: kurator ro'yxatni yuqoridan
                    //   pastga qo'ng'iroq qiladi va vaqti tugasa,
                    //   qolganlari eng kam xavflilari bo'lishi kerak.
                    .OrderByDescending(s => s.ConsecutiveMisses)
                    .ThenByDescending(s => s.MissedInRange)
                    .ThenBy(s => s.StudentName, StringComparer.Ordinal)
                    .ToList();

                return new AbsenteeGroupDto(
                    head.GroupId,
                    head.GroupName,
                    head.TeacherName,
                    head.AssistantName,
                    students.Count,
                    activeCounts.TryGetValue(head.GroupId, out var active) ? active : 0,
                    students);
            })
            .Where(g => g.Students.Count > 0)
            .OrderByDescending(g => g.AbsentCount)
            .ThenBy(g => g.GroupName, StringComparer.Ordinal)
            .ToList();

        // ★ YIG'MA SAHIFALASHDAN OLDIN hisoblanadi: kartalardagi raqamlar
        //   BUTUN davrni ko'rsatishi kerak, joriy sahifani emas
        //   (loyihadagi barcha yig'malar bilan AYNI qoida).
        var all = groups.SelectMany(g => g.Students).ToList();

        return new AbsenteeReportDto(
            from,
            to,
            sessionRows.Count,
            // ★ NOYOB O'QUVCHI: ikki guruhda darsi bo'lgan o'quvchi ikki
            //   marta sanalsa, "kecha 14 kishi kelmadi" raqami
            //   haqiqatdan katta chiqardi.
            all.Select(s => s.StudentId).Distinct().Count(),
            all.Where(s => s.ConsecutiveMisses >= RiskStreak)
                .Select(s => s.StudentId).Distinct().Count(),
            groups.Count,
            page,
            pageSize,
            groups.Skip((page - 1) * pageSize).Take(pageSize).ToList());
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private const int MaxPageSize = 100;

    // ================================================================= yordamchi

    /// <summary>
    /// Har (o'quvchi, guruh) juftligi uchun KETMA-KET qoldirilgan darslar
    /// soni — tanlangan kundan ortga qarab.
    ///
    /// ★ NEGA XOTIRADA: har juftlik uchun alohida SQL yozilsa, 50 ta
    /// o'quvchida 50 ta so'rov bo'lardi. Bu yerda bitta so'rov bilan
    /// oxirgi <see cref="StreakLookback"/> ta darsning davomati olinadi
    /// va sanoq xotirada bajariladi.
    /// </summary>
    private async Task<Dictionary<(long StudentId, long GroupId), int>> BuildStreaksAsync(
        List<long> studentIds, List<long> groupIds, DateTimeOffset before, CancellationToken ct)
    {
        var history = await db.Attendances.AsNoTracking()
            .Where(a => studentIds.Contains(a.StudentId)
                && a.Session!.Status == SessionStatus.Ended
                && a.Session.ScheduledStart < before
                && groupIds.Contains(a.Session.GroupId))
            .Select(a => new
            {
                a.StudentId,
                a.Session!.GroupId,
                a.Session.ScheduledStart,
                a.Status,
            })
            .ToListAsync(ct);

        var result = new Dictionary<(long, long), int>();

        foreach (var pair in history.GroupBy(x => (x.StudentId, x.GroupId)))
        {
            var streak = 0;

            foreach (var item in pair.OrderByDescending(x => x.ScheduledStart).Take(StreakLookback))
            {
                // Kelgan dars zanjirni UZADI — "ketma-ket" ta'rifi shu.
                if (item.Status != AttendanceStatus.Absent) break;

                streak++;
            }

            result[pair.Key] = streak;
        }

        return result;
    }

    /// <summary>Oxirgi 30 kunda jami nechta dars qoldirgan (barcha guruhlar bo'yicha).</summary>
    private async Task<Dictionary<long, int>> BuildRecentMissesAsync(
        List<long> studentIds, DateTimeOffset before, TimeZoneInfo zone, CancellationToken ct)
    {
        var from = LocalWallClock.StartOfDayUtc(
            LocalWallClock.LocalDate(before, zone).AddDays(-30), zone);

        var flat = await db.Attendances.AsNoTracking()
            .Where(a => studentIds.Contains(a.StudentId)
                && a.Status == AttendanceStatus.Absent
                && a.Session!.Status == SessionStatus.Ended
                && a.Session.ScheduledStart >= from
                && a.Session.ScheduledStart < before)
            .GroupBy(a => a.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return flat.ToDictionary(x => x.StudentId, x => x.Count);
    }

    /// <summary>
    /// Xaritani o'quv bo'limi, administrator VA kurator/ustoz ko'radi.
    ///
    /// ★ NEGA USTOZ HAM: qo'ng'iroqlarni amalda ko'pincha guruh kuratori
    /// qiladi. Ular ro'yxatni ko'ra olmasa, panel asosiy foydalanuvchisiz
    /// qolardi. O'quvchiga esa YOPIQ — bu boshqalarning ma'lumoti.
    /// </summary>
    private async Task EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (role is UserRole.Student)
            throw new ForbiddenException("Bu ro'yxatni o'quvchi ko'ra olmaydi.");
    }

    private static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        return "%" + trimmed.ToLowerInvariant()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
    }
}
