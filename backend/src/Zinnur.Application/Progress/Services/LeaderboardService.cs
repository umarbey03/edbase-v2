using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Progress.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Finance;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Progress.Services;

/// <summary>
/// ========================================================================
/// OYLIK REYTING — JONLI HISOB + QISQA KESH (SNAPSHOT YO'Q)
/// ========================================================================
///
/// ── QAROR: NEGA SNAPSHOT JADVALI QO'SHILMADI ───────────────────────────
///
/// Eski tizimda `leaderboard_snapshots` jadvali bor edi va oy oxirida
/// scheduler uni to'ldirardi. Uch sabab bilan v2 da TAKRORLANMADI:
///
///  1) ★ ESKI TIZIMNING `overall` DUBLIKAT XATOSI AYNAN SHU JADVALDAN
///     KELIB CHIQQAN. Upsert kaliti `(period, scope, group_id, student_id)`
///     edi, `overall` qatorlarida esa `group_id` — NULL. Postgres'da
///     UNIKAL indeks uchun `NULL <> NULL`, ya'ni `ON CONFLICT` bunday
///     qatorlarni HECH QACHON topmasdi: scheduler har ishga tushganda
///     BIR XIL o'quvchi uchun YANGI qator qo'shardi. Tarix asta-sekin
///     dublikatlarga to'lardi va "necha marta 1-o'rin oldim?" degan
///     sanoq (yutuqlar) ko'payib ketardi.
///
///     ★ QANDAY OLDINI OLINDI: bu yerda saqlanadigan reyting jadvali
///     UMUMAN YO'Q. Saqlanmagan qator dublikat bo'la olmaydi. Bundan
///     tashqari, "guruh" va "umumiy" qamrovlarni ajratadigan NULL bo'ladigan
///     diskriminator ustun ham yo'q — o'rin (`Rank`) hech qayerda
///     saqlanmaydi, u HAR DOIM ballardan hosil qilinadi.
///
///  2) SNAPSHOT IKKINCHI HAQIQAT MANBAI: kurator kechikkan vazifani
///     oy tugagach baholasa, jonli hisob o'zgaradi, snapshot esa qotib
///     qoladi. Ikki javob paydo bo'ladi va qaysi biri to'g'riligini hech
///     kim bilmaydi.
///
///  3) HISOB ARZON. Butun jadval — guruh hajmidan QAT'I NAZAR BESHTA
///     indeksli agregat so'rov (N+1 yo'q). 30 kishilik guruh ham,
///     500 kishilik ham bir xil beshta so'rov.
///
/// ── KESH ────────────────────────────────────────────────────────────────
///
/// Reyting ekrani bosh sahifada ham, "Reyting" bo'limida ham ochiladi va
/// bitta guruhning 30 o'quvchisi bir vaqtda BIR XIL jadvalni so'raydi.
/// Shuning uchun natija Redis'da keshlanadi — kalit GURUH+OY bo'yicha,
/// ya'ni bitta hisob butun guruhga xizmat qiladi.
///
/// ★ KESHDA "MEN" BAYROG'I YO'Q. `IsMe` — ko'ruvchiga bog'liq, keshga
/// tushsa birinchi o'quvchining bayrog'i butun guruhga tarqalardi
/// ("siz" yorlig'i begona qatorda chiqardi). Kesh neytral saqlanadi,
/// bayroq esa o'qilgandan KEYIN qo'yiladi.
///
/// ★ KALIT MAKONSIZ YOZILADI — <see cref="ICacheService"/> prefiksni O'ZI
/// qo'shadi (test/dev/prod bir Redis'da adashmasin).
/// </summary>
public sealed class LeaderboardService(
    IApplicationDbContext db,
    ICacheService cache,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : ILeaderboardService
{
    /// <summary>
    /// JORIY oy uchun TTL — qisqa: dars endi tugadi yoki vazifa endi
    /// baholandi, o'quvchi natijani deyarli darhol ko'rishi kerak.
    /// </summary>
    private static readonly TimeSpan CurrentPeriodTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// O'TGAN oy uchun TTL — uzunroq: u deyarli o'zgarmaydi (faqat kechikkan
    /// baho). Tarixni har daqiqada qayta hisoblash bekorga yuk.
    /// </summary>
    private static readonly TimeSpan PastPeriodTtl = TimeSpan.FromMinutes(10);

    public async Task<GroupLeaderboardDto> GetGroupBoardAsync(
        long groupId, long viewerId, string? period, CancellationToken ct = default)
    {
        var group = await LoadGroupForViewerAsync(groupId, viewerId, ct);
        var resolved = ResolvePeriod(period);

        var board = await GetBoardAsync(group, resolved, ct);

        // "Men" bayrog'i KESHDAN KEYIN — sabab sinf izohida.
        var rows = board.Rows.Count == 0
            ? board.Rows
            : board.Rows.Select(r => r with { IsMe = r.StudentId == viewerId }).ToList();

        return new GroupLeaderboardDto(
            group.Id,
            group.Name,
            resolved.ToString(),
            board.StudentCount,
            rows.FirstOrDefault(r => r.IsMe),
            rows);
    }

    public async Task<MyRankDto> GetMyRankAsync(
        long studentId, string? period, CancellationToken ct = default)
    {
        await LoadUserAsync(studentId, ct);
        var resolved = ResolvePeriod(period);

        var group = await PrimaryGroupAsync(studentId, ct);

        if (group is null)
            return new MyRankDto(null, null, resolved.ToString(), 0, null);

        var board = await GetBoardAsync(group, resolved, ct);

        var me = board.Rows.FirstOrDefault(r => r.StudentId == studentId);

        return new MyRankDto(
            group.Id,
            group.Name,
            resolved.ToString(),
            board.StudentCount,
            me is null ? null : me with { IsMe = true });
    }

    // ================================================================= kesh

    private async Task<CachedLeaderboard> GetBoardAsync(
        Group group, BillingPeriod period, CancellationToken ct)
    {
        var key = CacheKey(group.Id, period);

        var cached = await cache.GetAsync<CachedLeaderboard>(key, ct);
        if (cached is not null) return cached;

        var computed = await ComputeAsync(group.Id, period, ct);

        var ttl = period == CurrentPeriod() ? CurrentPeriodTtl : PastPeriodTtl;
        await cache.SetAsync(key, computed, ttl, ct);

        return computed;
    }

    private static string CacheKey(long groupId, BillingPeriod period) =>
        string.Create(CultureInfo.InvariantCulture, $"leaderboard:g{groupId}:{period}");

    // ================================================================= hisob

    /// <summary>
    /// Butun jadval — BESHTA agregat so'rov, guruh hajmidan qat'i nazar.
    ///
    /// Eski tizim ham shu qoidada ishlardi, lekin har mezonni alohida
    /// funksiyada hisoblab, o'quvchilar ro'yxatini har safar qayta
    /// tortardi. Bu yerda ro'yxat BIR MARTA olinadi va uch mezon unga
    /// bog'lanadi.
    /// </summary>
    private async Task<CachedLeaderboard> ComputeAsync(
        long groupId, BillingPeriod period, CancellationToken ct)
    {
        var (startUtc, endUtc) = period.UtcRange(timeZone.TimeZone);

        // ---------------------------------------------------------- 1) a'zolar
        var members = await db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MemberStatus.Active)
            .Select(m => new MemberRow(m.StudentId, m.Student!.FullName))
            .ToListAsync(ct);

        if (members.Count == 0)
            return new CachedLeaderboard(0, []);

        var studentIds = members.ConvertAll(m => m.StudentId);

        // ---------------------------------------------------------- 2) davomat maxraji
        //
        // Faqat USTOZ darslari va faqat YAKUNLANGANI. Bekor qilingan yoki
        // hali o'tilmagan dars maxrajga kirsa, o'quvchi hali bo'lmagan
        // dars uchun "qoldirgan" deb hisoblanardi.
        var endedSessionIds = await db.LiveSessions.AsNoTracking()
            .Where(s => s.GroupId == groupId
                     && s.Type == SessionType.Teacher
                     && s.Status == SessionStatus.Ended
                     && s.ScheduledStart >= startUtc
                     && s.ScheduledStart < endUtc)
            .Select(s => s.Id)
            .ToListAsync(ct);

        // ---------------------------------------------------------- 3) qatnashganlar
        var attendedByStudent = new Dictionary<long, int>();

        if (endedSessionIds.Count > 0)
        {
            var attendanceRows = await db.Attendances.AsNoTracking()
                .Where(a => endedSessionIds.Contains(a.SessionId)
                         && studentIds.Contains(a.StudentId)
                         && a.Status != AttendanceStatus.Absent)
                .GroupBy(a => a.StudentId)
                .Select(g => new CountRow(g.Key, g.Count()))
                .ToListAsync(ct);

            foreach (var row in attendanceRows)
                attendedByStudent[row.StudentId] = row.Value;
        }

        // ---------------------------------------------------------- 4) vazifalar
        //
        // ★ QAMROV ESKI TIZIMDAN KENGROQ — ATAYLAB. Eski kod faqat
        // `assignment.group_id = <guruh>` vazifalarini hisoblardi. v2 da
        // vazifalarning asosiy qismi KURS darsiga biriktirilgan
        // (`ModuleLessonId`) va ularning `GroupId` si NULL — eski shart
        // ko'chirilganda vazifa mezoni deyarli har doim bo'sh chiqardi va
        // reyting ikki mezonga tushib qolardi.
        //
        // Bir guruhning hamma o'quvchisi bitta kursda bo'lgani uchun kurs
        // vazifalari ham to'liq taqqoslanadi.
        var gradedRatios =
            from submission in db.Submissions.AsNoTracking()
            join assignment in db.Assignments.AsNoTracking()
                on submission.AssignmentId equals assignment.Id
            where studentIds.Contains(submission.StudentId)
                && submission.Status == SubmissionStatus.Graded
                && submission.Score != null
                && assignment.MaxScore > 0
                && (assignment.GroupId == groupId || assignment.ModuleLessonId != null)
                && submission.GradedAt >= startUtc
                && submission.GradedAt < endUtc
            select new { submission.StudentId, Ratio = submission.Score!.Value / assignment.MaxScore };

        var assignmentRows = await gradedRatios
            .GroupBy(x => x.StudentId)
            .Select(g => new RatioRow(g.Key, g.Average(x => x.Ratio)))
            .ToListAsync(ct);

        var assignmentByStudent = assignmentRows.ToDictionary(r => r.StudentId, r => r.Ratio);

        // ---------------------------------------------------------- 5) testlar
        var testRows = await db.TestAttempts.AsNoTracking()
            .Where(t => studentIds.Contains(t.StudentId)
                     && t.Status == AttemptStatus.Submitted
                     && t.Score != null
                     && t.MaxScore > 0
                     && t.SubmittedAt >= startUtc
                     && t.SubmittedAt < endUtc)
            .GroupBy(t => t.StudentId)
            .Select(g => new RatioRow(g.Key, g.Average(t => t.Score!.Value / t.MaxScore!.Value)))
            .ToListAsync(ct);

        var testByStudent = testRows.ToDictionary(r => r.StudentId, r => r.Ratio);

        // ---------------------------------------------------------- yig'ish
        var scores = members.ConvertAll(member => new LeaderboardScore(
            member.StudentId,
            member.FullName,

            // ★ MEZON "BO'SH" BO'LSA `null` — 0 EMAS. Shu oyda umuman dars
            // o'tilmagan bo'lsa davomat mezoni hisobga KIRMAYDI; 0 yozilsa
            // oy boshida hamma 0% davomat bilan turardi.
            AttendancePercent: endedSessionIds.Count == 0
                ? null
                : LeaderboardScore.Percent(
                    attendedByStudent.GetValueOrDefault(member.StudentId),
                    endedSessionIds.Count),

            AssignmentPercent: assignmentByStudent.TryGetValue(member.StudentId, out var asg)
                ? LeaderboardScore.PercentFromRatio(asg)
                : null,

            TestPercent: testByStudent.TryGetValue(member.StudentId, out var test)
                ? LeaderboardScore.PercentFromRatio(test)
                : null));

        var ranked = LeaderboardRanking.Rank(scores);

        var rows = ranked.Select(r => new LeaderboardRowDto(
            r.Score.StudentId,
            r.Score.StudentName,
            r.Rank,
            r.Score.Total,
            r.Score.AttendancePercent,
            r.Score.AssignmentPercent,
            r.Score.TestPercent,
            IsMe: false)).ToList();

        return new CachedLeaderboard(members.Count, rows);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// O'quvchining ASOSIY guruhi: faol a'zolik + faol guruh + oddiy
    /// (ustoz) guruh turi.
    ///
    /// ★ KURATOR GURUHI ATAYLAB CHIQARILGAN: unda o'quvchi to'g'ridan-to'g'ri
    /// a'zo bo'lmaydi (<c>Group.CuratorGroupId</c> orqali bog'lanadi), ya'ni
    /// u yerda reyting jadvali ham bo'lmaydi.
    ///
    /// Bir nechta guruh bo'lsa — ENG ERTA qo'shilgani. Eski tizim `gids[0]`
    /// olardi, ya'ni tartib baza qaytargan tasodifiy ketma-ketlikka
    /// bog'liq edi va bir o'quvchiga har so'rovda boshqa guruh chiqishi
    /// mumkin edi.
    /// </summary>
    private async Task<Group?> PrimaryGroupAsync(long studentId, CancellationToken ct) =>
        await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.IsActive
                     && m.Group.Type != GroupType.Curator)
            .OrderBy(m => m.JoinedAt)
            .ThenBy(m => m.Id)
            .Select(m => m.Group!)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Guruhni yuklaydi va ko'rish huquqini tekshiradi
    /// (eski <c>leaderboard_router._can_view</c> qoidasi bilan bir xil).
    /// </summary>
    private async Task<Group> LoadGroupForViewerAsync(
        long groupId, long viewerId, CancellationToken ct)
    {
        var viewer = await LoadUserAsync(viewerId, ct);

        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new NotFoundException(nameof(Group), groupId);

        // Admin/o'quv bo'limi arxivlangan guruhni ham ko'radi — hisobot va
        // nizolarni tekshirish uchun (eski tizim ham shunday).
        if (viewer.Role is UserRole.Admin or UserRole.Academic)
            return group;

        if (!group.IsActive)
            throw new ForbiddenException("Arxivlangan guruh reytingi ko'rinmaydi.");

        if (group.IsStaff(viewer.Id))
            return group;

        // ★ A'ZOLIK FAOL BO'LISHI SHART. Eski tizimda bu tekshiruv bir
        // vaqtlar yo'q edi va guruhdan chiqarilgan (stopped/paused)
        // o'quvchi hamon reytingni ko'rardi.
        var isMember = await db.GroupMembers.AsNoTracking().AnyAsync(
            m => m.GroupId == groupId
              && m.StudentId == viewerId
              && m.Status == MemberStatus.Active, ct);

        if (!isMember)
            throw new ForbiddenException("Bu guruh reytingiga ruxsatingiz yo'q.");

        return group;
    }

    private async Task<User> LoadUserAsync(long userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return user;
    }

    private BillingPeriod CurrentPeriod() =>
        BillingPeriod.FromDate(LocalWallClockToday());

    private DateOnly LocalWallClockToday() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone.TimeZone).DateTime);

    /// <summary>
    /// <c>?period=</c> ni o'qiydi. Bo'sh bo'lsa — JORIY oy (markaz vaqti).
    ///
    /// ★ NOTO'G'RI FORMAT 400 BERADI, 409 EMAS. <c>BillingPeriod.Parse</c>
    /// <c>DomainException</c> ko'taradi, u esa global xaritada 409 ga
    /// tushadi — "Amal bajarilmadi" so'rov QATORIDAGI xato uchun noto'g'ri
    /// javob va frontend uni qayta urinish kerak deb tushunardi.
    /// </summary>
    private BillingPeriod ResolvePeriod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CurrentPeriod();

        try
        {
            return BillingPeriod.Parse(value);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["period"] = [ex.Message] });
        }
    }

    // ---------------------------------------------------------------- ichki shakllar

    private sealed record MemberRow(long StudentId, string FullName);

    private sealed record CountRow(long StudentId, int Value);

    private sealed record RatioRow(long StudentId, decimal Ratio);
}

/// <summary>
/// Keshda saqlanadigan NEYTRAL jadval — ko'ruvchiga bog'liq maydonsiz.
/// <c>public</c>, chunki <see cref="ICacheService"/> uni JSON'ga
/// serializatsiya qiladi.
/// </summary>
public sealed record CachedLeaderboard(int StudentCount, IReadOnlyList<LeaderboardRowDto> Rows);
