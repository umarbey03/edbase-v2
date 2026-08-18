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
        var role = await EnsureCanViewAsync(actorId, ct);

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

        // ═══════════════════════════════════════════════════════════════
        // 🔴 ROLGA QARAB TORAYTIRISH (2026-08-18 da to'g'rilandi)
        //
        // Ilgari BU YERDA HECH QANDAY CHEKLOV YO'Q edi: istalgan ustoz
        // butun markazning kelmaganlar ro'yxatini, ular bilan birga
        // TELEFON RAQAMLARINI ko'ra olardi. Loyihaning qolgan barcha
        // ro'yxatlari (`GroupService.VisibleTo`, `LiveSessionService`,
        // `GlobalSearchService`) rolga qarab toraytiriladi — bu yerda
        // shu qoida tushib qolgan edi.
        //
        // Kurator o'z guruhiga BOG'LANGAN ustoz guruhlarini ham ko'radi
        // (`VisibleTo` bilan AYNI mantiq).
        // ═══════════════════════════════════════════════════════════════
        if (role is UserRole.Teacher or UserRole.Assistant)
        {
            sessions = sessions.Where(s =>
                s.HostId == actorId
                || s.Group!.TeacherId == actorId
                || s.Group.AssistantId == actorId
                || (s.Group.CuratorGroup != null
                    && (s.Group.CuratorGroup.TeacherId == actorId
                        || s.Group.CuratorGroup.AssistantId == actorId)));
        }

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
        var groupIdsForSessions = sessionRows.ConvertAll(s => s.GroupId).Distinct().ToList();

        // ═══════════════════════════════════════════════════════════════
        // 🔴 KELMAGANLAR — A'ZOLARDAN BOSHLANADI, DAVOMATDAN EMAS
        //    (2026-08-18 da to'g'rilandi)
        //
        // Ilgari bu yerda FAQAT `Attendances` jadvali o'qilardi. Lekin
        // davomat qatori o'quvchi darsga KIRGANDA (yoki xodim qo'lda
        // belgilaganda) yaratiladi — umuman kelmagan o'quvchida qator
        // BO'LMAYDI. Natijada panel o'zining ASOSIY holatini ko'rmasdi:
        // 1 faol a'zoli guruhda dars o'tib, o'quvchi umuman kirmasa,
        // hisobot "0 kelmagan" derdi.
        //
        // Loyihaning qolgan qismi buni allaqachon to'g'ri qiladi
        // (`AttendanceSummaryService`, `UserProfileService`,
        // `SessionAutoCloseJob`): "davomat yozuvi YO'QLIGI — kelmagan
        // degani". Endi bu yerda ham AYNI qoida.
        //
        // ★ NOMZODLAR — GURUHNING FAOL A'ZOLARI: kim darsda BO'LISHI
        //   KERAK edi. Chiqib ketgan o'quvchining eski davomat qatori
        //   qolib ketishi mumkin va u qo'ng'iroq ro'yxatiga tushmasligi
        //   kerak (aynan shu sabab "7/5" kabi ma'nosiz nisbat chiqardi).
        // ═══════════════════════════════════════════════════════════════
        // ★ A'ZOLIK DARS VAQTIDAGI holat bo'yicha, HOZIRGISI bo'yicha
        //   EMAS: darsni qoldirib, keyin guruhdan chiqib ketgan o'quvchi
        //   o'sha kunning hisobotida QOLISHI kerak — aks holda "kecha kim
        //   kelmadi?" degan savolga tarix o'zgarib javob berardi.
        //   Chegara `JoinedAt`/`LeftAt` orqali quyida, har dars uchun
        //   alohida tekshiriladi (bitta o'quvchi bir darsda a'zo, boshqa
        //   darsda a'zo bo'lmasligi mumkin).
        var members = db.GroupMembers.AsNoTracking()
            .Where(m => groupIdsForSessions.Contains(m.GroupId)
                && m.Student!.IsActive
                && m.Student.Role == UserRole.Student);

        var term = NormalizeSearch(query.Search);

        if (term is not null)
        {
#pragma warning disable CA1304, CA1311
            members = members.Where(m => EF.Functions.Like(m.Student!.FullName.ToLower(), term));
#pragma warning restore CA1304, CA1311
        }

        var memberRows = await members
            .Select(m => new
            {
                m.GroupId,
                m.StudentId,
                StudentName = m.Student!.FullName,
                m.Student.Phone,
                TelegramLinked = m.Student.TelegramId != null,
                m.JoinedAt,
                m.LeftAt,
                m.Status,
            })
            .ToListAsync(ct);

        // Mavjud davomat yozuvlari — "kelgan/kelmagan" ni aniqlash uchun.
        var attendance = await db.Attendances.AsNoTracking()
            .Where(a => sessionIds.Contains(a.SessionId))
            .Select(a => new { a.SessionId, a.StudentId, a.Status, a.IsExcused })
            .ToListAsync(ct);

        var attendanceByKey = attendance
            .ToDictionary(a => (a.SessionId, a.StudentId), a => a);

        var membersByGroup = memberRows
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<(long SessionId, long StudentId, string StudentName, string? Phone, bool TelegramLinked, AttendanceStatus Status)>();

        // Guruh → darslarda qatnashishi KUTILGAN noyob o'quvchilar.
        // Bu — "kelmaganlar" nisbatining maxraji (sabab DTO izohida).
        var expected = new Dictionary<long, HashSet<long>>();

        foreach (var session in sessionRows)
        {
            if (!membersByGroup.TryGetValue(session.GroupId, out var groupMembers)) continue;

            foreach (var member in groupMembers)
            {
                // ★ A'ZOLIK OYNASI — DARS VAQTIGA nisbatan:
                //   • darsdan KEYIN qo'shilgan o'quvchi uni "qoldirgan"
                //     bo'la olmaydi;
                //   • darsdan OLDIN chiqib ketgan ham shunday.
                //   Darsdan KEYIN chiqib ketgani esa QOLADI — o'sha kuni
                //   u hali guruhda edi.
                if (member.JoinedAt > session.ScheduledStart) continue;
                if (member.LeftAt is { } leftAt && leftAt <= session.ScheduledStart) continue;

                // ★ MUZLATILGAN A'ZO CHIQARILADI, lekin FAQAT hozir
                //   muzlatilgan va o'shanda ham chiqib ketmagan bo'lsa:
                //   muzlatish kelishilgan tanaffus, ya'ni "kelmadi" deb
                //   qo'ng'iroq qilish keraksiz ish bo'lardi.
                if (member.Status == MemberStatus.Paused) continue;

                // Shu darsda qatnashishi kutilgan — kelgan-kelmaganidan
                // QAT'I NAZAR (maxraj shundan yig'iladi).
                if (!expected.TryGetValue(session.GroupId, out var set))
                {
                    set = [];
                    expected[session.GroupId] = set;
                }

                set.Add(member.StudentId);

                attendanceByKey.TryGetValue((session.Id, member.StudentId), out var record);

                // ★ SABABLI QOLDIRISH RO'YXATGA TUSHMAYDI: xodim uni
                //   allaqachon bilib, uzrli deb belgilagan. Qo'ng'iroq
                //   qilish keraksiz ish bo'lardi.
                if (record is { IsExcused: true }) continue;

                // Qator YO'Q — kelmagan (loyihadagi umumiy qoida).
                var status = record?.Status ?? AttendanceStatus.Absent;

                var missed = status == AttendanceStatus.Absent
                    || (query.IncludePartial && status == AttendanceStatus.Partial);

                if (!missed) continue;

                rows.Add((
                    session.Id,
                    member.StudentId,
                    member.StudentName,
                    member.Phone,
                    member.TelegramLinked,
                    status));
            }
        }

        if (rows.Count == 0)
            return new AbsenteeReportDto(from, to, sessionRows.Count, 0, 0, 0, page, pageSize, []);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var groupIds = sessionRows.ConvertAll(s => s.GroupId).Distinct().ToList();

        var streaks = await BuildStreaksAsync(studentIds, groupIds, dayEnd, ct);
        var recent = await BuildRecentMissesAsync(studentIds, dayEnd, zone, ct);

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
                    expected.TryGetValue(head.GroupId, out var candidates) ? candidates.Count : 0,
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
        // ★ DARSLARDAN BOSHLANADI, davomatdan EMAS (2026-08-18 da
        //   to'g'rilandi — asosiy so'rov bilan AYNI sabab): umuman
        //   kelmagan o'quvchida davomat qatori bo'lmaydi va u
        //   "ketma-ket 0 marta qoldirgan" bo'lib chiqardi, ya'ni
        //   eng xavfli o'quvchi xavf ro'yxatiga TUSHMASDI.
        var lessons = await db.LiveSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Ended
                && s.ScheduledStart < before
                && groupIds.Contains(s.GroupId))
            .OrderByDescending(s => s.ScheduledStart)
            // Har guruhdan oxirgi darslar yetarli (chegara sabab
            // maydon izohida) — butun tarixni o'qish shart emas.
            .Take(groupIds.Count * StreakLookback)
            .Select(s => new { s.Id, s.GroupId, s.ScheduledStart })
            .ToListAsync(ct);

        if (lessons.Count == 0) return [];

        var lessonIds = lessons.ConvertAll(s => s.Id);

        var seen = (await db.Attendances.AsNoTracking()
                .Where(a => studentIds.Contains(a.StudentId) && lessonIds.Contains(a.SessionId))
                .Select(a => new { a.SessionId, a.StudentId, a.Status, a.IsExcused })
                .ToListAsync(ct))
            .ToDictionary(a => (a.SessionId, a.StudentId), a => a);

        var byGroup = lessons
            .GroupBy(s => s.GroupId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ScheduledStart).ToList());

        var result = new Dictionary<(long, long), int>();

        foreach (var studentId in studentIds)
        {
            foreach (var (groupId, groupLessons) in byGroup)
            {
                var streak = 0;

                foreach (var lesson in groupLessons.Take(StreakLookback))
                {
                    seen.TryGetValue((lesson.Id, studentId), out var record);

                    // Uzrli qoldirish zanjirni UZMAYDI ham, DAVOM
                    // ETTIRMAYDI ham — u shunchaki hisobga olinmaydi.
                    if (record is { IsExcused: true }) continue;

                    // Qator yo'q — kelmagan; kelgan dars zanjirni uzadi.
                    if (record is not null && record.Status != AttendanceStatus.Absent) break;

                    streak++;
                }

                if (streak > 0) result[(studentId, groupId)] = streak;
            }
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
    /// <returns>
    /// Chaqiruvchi roli — so'rovni ROLGA QARAB toraytirish uchun kerak
    /// (ustoz/kurator faqat o'z guruhlarini ko'radi).
    /// </returns>
    private async Task<UserRole> EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (role is UserRole.Student)
            throw new ForbiddenException("Bu ro'yxatni o'quvchi ko'ra olmaydi.");

        return role;
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
