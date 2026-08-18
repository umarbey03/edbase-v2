using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Scope;
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
///
/// ── KESH KALITLARI (IKKI QAMROV) ────────────────────────────────────────
///
///   guruh  :  <c>leaderboard:g{groupId}:{period}</c>          — o'zgarmadi
///   markaz :  <c>leaderboard:center:{markaz}:{period}</c>     — YANGI
///
/// 🔴 KALIT DIZAYNI SHUNCHAKI DID EMAS: <see cref="ICacheService"/> da
///    PREFIKS BO'YICHA O'CHIRISH YO'Q — faqat aniq kalit bo'yicha
///    <c>RemoveAsync</c> va TTL. Ya'ni noto'g'ri kalit bilan yozilgan
///    jadvalni "hammasini tozalab yuborish" bilan tuzatib bo'lmaydi, u
///    TTL tugagunicha noto'g'ri odamga ko'rinib turadi. Shundan uchta
///    qoida kelib chiqadi:
///
///     1) MARKAZ BELGISI KALIT ICHIDA. Bugun u <c>"solo"</c>, ertaga —
///        markaz Id'si. Ko'p-markazli o'zgarishdan keyin eski
///        <c>...:center:solo:...</c> kalitlari yangi
///        <c>...:center:7:...</c> bilan HECH QACHON to'qnashmaydi.
///
///     2) QAMROVLAR ALOHIDA MAKONDA (<c>:g</c> va <c>:center:</c>) —
///        guruh Id'si 7 va markaz Id'si 7 bir kalitga tushib qolmasin.
///
///     3) SAQLANADIGAN SHAKL (<see cref="CachedLeaderboard"/>) O'ZGARSA,
///        KALIT MAKONI HAM O'ZGARISHI SHART (masalan <c>:center2:</c>).
///        Aks holda eski JSON yangi shaklga deserializatsiya qilinib,
///        maydonlar jimgina standart qiymat oladi. Shu sabab markaz
///        jadvali uchun YANGI shakl kiritilmadi — guruh bilan AYNI
///        <see cref="CachedLeaderboard"/> ishlatiladi.
///
/// ★ MARKAZ JADVALI KESHDA TO'LIQ SAQLANADI (TOP-N EMAS). Sabab: kesh
///   NEYTRAL bo'lishi kerak, kesish esa KO'RUVCHIGA bog'liq — 847-o'rindagi
///   o'quvchiga uning o'z qatorini ko'rsatish uchun to'liq ro'yxat kerak.
///   Kesish ham, <c>IsMe</c> ham keshdan KEYIN qo'llanadi.
///   🔴 Bu Redis'dagi hajmni o'quvchilar soniga chiziqli qiladi
///      (~120 bayt × o'quvchi). Yuzlab o'quvchida bu arzimas; o'n minglab
///      o'quvchida snapshot jadvali kerak bo'ladi.
///
/// ★ KESH "STAMPEDE" DAN HIMOYALANMAGAN — ATAYLAB. TTL tugagan onda 30 ta
///   o'quvchi bir vaqtda so'rasa, hisob 30 marta bajariladi (qulf yo'q).
///   Guruh jadvalida bu arzon edi; markaz jadvalida u qimmatroq, lekin
///   baribir OLTITA indeksli agregat so'rov — taqsimlangan qulf qo'shish
///   esa yangi turdagi nosozlik (qulf qotib qolishi) olib kelardi.
/// </summary>
public sealed class LeaderboardService(
    IApplicationDbContext db,
    ICacheService cache,
    IScheduleTimeZoneProvider timeZone,
    ILearningCenterScope center,
    TimeProvider clock) : ILeaderboardService
{
    /// <summary>
    /// MARKAZ jadvalida yuboriladigan qatorlar chegarasi.
    ///
    /// ── NIMA UCHUN TOP-N, VA NIMA UCHUN `MaxRows` KO'TARILMADI ──────────
    ///
    /// <c>LeaderboardRanking.MaxRows</c> (500) markazda 409 xatosi berardi:
    /// 500 dan ko'p faol o'quvchili markaz — mutlaqo normal holat. Uchta
    /// yo'l bor edi va uchinchisi tanlandi:
    ///
    ///   (a) chegarani ko'tarish — muammoni SURADI, hal qilmaydi: 5000
    ///       qatorli JSON telefon internetida ~600 KB va Mini App uni
    ///       chizishga ham ulgurmaydi;
    ///
    ///   (b) chegarani qamrovga bog'lash — baribir (a) ning o'zi, faqat
    ///       ikki xil son bilan;
    ///
    ///   (c) ★ TANLANDI: TOP-N + KO'RUVCHINING O'Z QATORI. Javob hajmi
    ///       markaz kattaligidan QAT'I NAZAR barqaror, o'quvchi esa
    ///       o'ziga kerak bo'lgan ikkala narsani ham oladi — cho'qqi
    ///       kim ekani va O'ZI qayerda turgani ("847 / 3000").
    ///
    ///       Bu qo'shimcha ravishda BEKOR QILINGAN MAXFIYLIK e'tiroziga
    ///       qisman javob beradi: markaz jadvali barcha o'quvchilarning
    ///       ismini yoymaydi, faqat yuqori yuzlikni ko'rsatadi.
    ///
    /// 100 — o'quvchi haqiqatda skroll qiladigan uzunlik. 500 qatorni
    /// hech kim oxirigacha ko'rmaydi, 20 esa markaz miqyosi hissini
    /// bermasdi.
    ///
    /// ★ GURUH JADVALI TO'LIQ QOLADI: o'quvchi o'z guruhidagi HAMMANI
    ///   ko'rishi kerak, u yerda kesish ma'nosiz.
    /// </summary>
    public const int CenterTopRows = 100;

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

    public async Task<CenterLeaderboardDto> GetCenterBoardAsync(
        long viewerId, string? period, CancellationToken ct = default)
    {
        // ★ AVVAL `period`, KEYIN QAMROV — guruh yo'lidagidan farqli tartib.
        // Sabab: `ResolvePeriod` sof funksiya (bazaga bormaydi), qamrov esa
        // butun markaz auditoriyasini tortadi. Noto'g'ri yozilgan
        // `?period=may` uchun uni tortish bekorga yuk bo'lardi.
        var resolved = ResolvePeriod(period);

        // Ruxsat SHU YERDA hal bo'ladi — servis qoidani takrorlamaydi.
        var audience = await center.ResolveForViewerAsync(viewerId, ct);

        var board = await CenterBoardAsync(audience, resolved, ct);

        // ★ KESISH KESHDAN KEYIN: keshdagi ro'yxat NEYTRAL va TO'LIQ.
        var rows = board.Rows.Count <= CenterTopRows
            ? board.Rows
            : board.Rows.Take(CenterTopRows).ToList();

        var top = rows.Select(r => r with { IsMe = r.StudentId == viewerId }).ToList();

        // "Men" qatori: avval yuqori yuzlikdan, topilmasa TO'LIQ ro'yxatdan.
        // Ikkinchi holatda qator `Rows` ICHIDA BO'LMAYDI — frontend uni
        // jadvaldan tashqarida, alohida ko'rsatadi.
        var me = top.Find(r => r.IsMe);

        if (me is null && board.Rows.FirstOrDefault(r => r.StudentId == viewerId) is { } outside)
            me = outside with { IsMe = true };

        return new CenterLeaderboardDto(
            resolved.ToString(),
            board.StudentCount,
            CenterTopRows,
            me,
            top);
    }

    public Task<MyRankDto> GetMyRankAsync(
        long studentId, LeaderboardScope scope, string? period, CancellationToken ct = default) =>
        scope == LeaderboardScope.Center
            ? CenterRankAsync(studentId, period, ct)
            : GroupRankAsync(studentId, period, ct);

    private async Task<MyRankDto> GroupRankAsync(
        long studentId, string? period, CancellationToken ct)
    {
        await LoadUserAsync(studentId, ct);
        var resolved = ResolvePeriod(period);

        var group = await PrimaryGroupAsync(studentId, ct);

        if (group is null)
            return new MyRankDto(LeaderboardScope.Group, null, null, resolved.ToString(), 0, null);

        var board = await GetBoardAsync(group, resolved, ct);

        var me = board.Rows.FirstOrDefault(r => r.StudentId == studentId);

        return new MyRankDto(
            LeaderboardScope.Group,
            group.Id,
            group.Name,
            resolved.ToString(),
            board.StudentCount,
            me is null ? null : me with { IsMe = true });
    }

    /// <summary>
    /// Markazdagi o'rin — jadvalsiz.
    ///
    /// ★ BU YERDA TOP-N KESISH YO'Q: savol "men qayerdaman?", ya'ni javob
    /// o'quvchi 847-o'rinda bo'lsa ham berilishi kerak.
    /// </summary>
    private async Task<MyRankDto> CenterRankAsync(
        long studentId, string? period, CancellationToken ct)
    {
        var resolved = ResolvePeriod(period);

        var audience = await center.ResolveForViewerAsync(studentId, ct);
        var board = await CenterBoardAsync(audience, resolved, ct);

        var me = board.Rows.FirstOrDefault(r => r.StudentId == studentId);

        return new MyRankDto(
            LeaderboardScope.Center,
            // Markaz jadvalining guruhi YO'Q — "guruh topilmadi" bilan
            // aralashmasin uchun qamrov diskriminatori javobda turadi.
            GroupId: null,
            GroupName: null,
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

    private async Task<CachedLeaderboard> CenterBoardAsync(
        LearningCenterAudience audience, BillingPeriod period, CancellationToken ct)
    {
        var key = CenterCacheKey(audience.CacheDiscriminator, period);

        var cached = await cache.GetAsync<CachedLeaderboard>(key, ct);
        if (cached is not null) return cached;

        var computed = await ComputeCenterAsync(audience, period, ct);

        var ttl = period == CurrentPeriod() ? CurrentPeriodTtl : PastPeriodTtl;
        await cache.SetAsync(key, computed, ttl, ct);

        return computed;
    }

    private static string CacheKey(long groupId, BillingPeriod period) =>
        string.Create(CultureInfo.InvariantCulture, $"leaderboard:g{groupId}:{period}");

    /// <summary>
    /// Markaz kaliti. Markaz belgisi kalit ICHIDA — sinf izohidagi uch
    /// qoidaning birinchisi.
    /// </summary>
    private static string CenterCacheKey(string discriminator, BillingPeriod period) =>
        string.Create(CultureInfo.InvariantCulture, $"leaderboard:center:{discriminator}:{period}");

    // ================================================================= hisob

    /// <summary>
    /// Butun jadval — OLTITA agregat so'rov, guruh hajmidan qat'i nazar
    /// (R24 dan keyin: oldin beshta edi).
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
            // ★ KURATOR GURUHI HISOBGA OLINADI (2026-08-18) — qoida
            //   `GroupMembershipScope` da. Ilgari kurator guruhining
            //   reytingi bo'sh chiqardi.
            .Where(GroupMembershipScope.ActiveIn(groupId))
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

        // ---------------------------------------------------------- 6) dars baholari (R24)
        //
        // ★ QAMROV DAVOMAT MEZONINIKIGA O'XSHASH, LEKIN AYNI EMAS —
        //   farqlar ataylab:
        //
        //     • OY `Session.ScheduledStart` BO'YICHA, `GradedAt` bo'yicha
        //       EMAS (vazifa mezonidan FARQI). Baho DARSGA tegishli, ya'ni
        //       iyul darsining bahosi avgustda qo'yilsa ham IYULGA kiradi.
        //       Aks holda ustoz oy oxirida bir yo'la baholaganda butun
        //       guruhning bali keyingi oyga sakrab o'tardi.
        //
        //     • DARS TURI CHEKLANMAGAN (davomat maxrajidan FARQI): kurator
        //       darsiga qo'yilgan baho ham ustozning bahosi kabi haqiqiy.
        //       Davomatda `Teacher` filtri MAXRAJ uchun kerak edi ("nechta
        //       dars o'tildi"), bu yerda esa maxraj yo'q — o'rtacha faqat
        //       MAVJUD baholardan olinadi.
        //
        //     • BEKOR QILINGAN dars CHIQARILADI: u o'tilmagan, ya'ni
        //       bahosi (agar bekor qilishdan oldin qo'yilgan bo'lsa)
        //       endi ma'nosiz.
        //
        // 🔴 "BAHOSI YO'Q DARS" 0 EMAS — u o'rtachaga UMUMAN KIRMAYDI.
        //    Aks holda ustoz faqat ba'zi darslarni baholaganda (odatiy
        //    holat) qolgan hammasi nol bo'lib, dars mezoni butun guruhni
        //    pastga tortardi.
        var lessonGradeRows = await db.LessonGrades.AsNoTracking()
            .Where(g => studentIds.Contains(g.StudentId)
                     && g.Session!.GroupId == groupId
                     && g.Session.Status != SessionStatus.Cancelled
                     && g.Session.ScheduledStart >= startUtc
                     && g.Session.ScheduledStart < endUtc)
            .GroupBy(g => g.StudentId)
            .Select(g => new RatioRow(
                g.Key,
                g.Average(x => x.Score / (x.MaxScore ?? LessonGrade.DefaultMaxScore))))
            .ToListAsync(ct);

        var lessonByStudent = lessonGradeRows.ToDictionary(r => r.StudentId, r => r.Ratio);

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
                : null,

            LessonPercent: lessonByStudent.TryGetValue(member.StudentId, out var lesson)
                ? LeaderboardScore.PercentFromRatio(lesson)
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
            IsMe: false,
            r.Score.LessonPercent)).ToList();

        return new CachedLeaderboard(members.Count, rows);
    }

    /// <summary>
    /// ====================================================================
    /// MARKAZ JADVALI — BESHTA AGREGAT SO'ROV, GURUHLAR SONIDAN QAT'I NAZAR
    /// ====================================================================
    ///
    /// MURAKKABLIK: qamrov 2 so'rov (o'quvchilar + a'zoliklar) + shu yerda
    /// 5 so'rov = JAMI 7 ta borish-kelish (R24 dan keyin; oldin 6 edi).
    /// O'quvchi soniga ham, GURUH soniga ham bog'liq EMAS.
    ///
    /// ── 🔴 NIMA UCHUN GURUH JADVALLARINI QO'SHIB YUBORISH TANLANMADI ─────
    ///
    /// Ikkinchi yo'l bor edi: har guruh uchun mavjud (va allaqachon
    /// keshlangan) jadvalni olib, natijalarni qo'shib qayta tartiblash.
    /// U ILIQ keshda arzon ko'rinadi, lekin SOVUQ keshda O(guruhlar soni)
    /// FAN-OUT beradi: 40 guruhli markazda 40 × 5 = 200 ta so'rov, va ular
    /// oyning birinchi so'rovida BIR VAQTDA bajarilardi. Bundan tashqari
    /// ikki guruhda turgan o'quvchi ikki marta chiqib, dublikatni qo'lda
    /// tozalash kerak bo'lardi.
    ///
    /// Shu yerdagi yo'l esa maxrajni SO'ROV ICHIDA guruhlarga bo'ladi
    /// (<c>GROUP BY group_id</c>) — fan-out umuman yo'q.
    ///
    /// ── ★ DAVOMAT MEZONI: MAXRAJ HAR O'QUVCHIDA O'ZINIKI ────────────────
    ///
    /// Guruh jadvalida maxraj bitta son edi — "shu guruhda o'tilgan ustoz
    /// darslari". Markazda bu UMUMLASHMAYDI: A guruhi 8 dars o'tgan,
    /// B guruhi 4 dars. Umumiy maxraj (12) olinsa, B guruhining hamma
    /// darsiga qatnashgan o'quvchi 33% davomat bilan chiqardi — u
    /// bormagan darslar uchun jazolangan bo'lardi. Markaz maxraji esa
    /// har o'quvchining ASOSIY GURUHINIKI, ya'ni "shu o'quvchi uchun
    /// o'tilgan darslar".
    ///
    /// ★ NATIJADA GURUH VA MARKAZ JADVALLARI BIR XIL FOIZ KO'RSATADI —
    ///   bitta guruhdagi o'quvchi uchun ikkala hisob AYNAN bir xil
    ///   kasrni beradi. Ikki guruhda turgan o'quvchida esa markaz
    ///   jadvali ASOSIY guruhni oladi (eng erta qo'shilgani) — bu
    ///   "mening o'rnim" kartochkasidagi qoidaning aynan o'zi, ya'ni
    ///   o'quvchiga ikki xil raqam ko'rsatilmaydi.
    ///
    /// ★ GURUHSIZ O'QUVCHI (<c>PrimaryGroupId == null</c>) jadvalda
    ///   QOLADI, lekin davomat mezonisiz: uning vazifa va test ballari
    ///   haqiqiy va ularni o'chirib tashlash ma'lumotni yo'qotish bo'lardi.
    /// </summary>
    private async Task<CachedLeaderboard> ComputeCenterAsync(
        LearningCenterAudience audience, BillingPeriod period, CancellationToken ct)
    {
        var (startUtc, endUtc) = period.UtcRange(timeZone.TimeZone);

        if (audience.Students.Count == 0)
            return new CachedLeaderboard(0, []);

        List<long> studentIds = [.. audience.Students.Select(s => s.StudentId)];
        List<long> groupIds = [.. audience.GroupIds];

        // ---------------------------------------------------- 1) maxraj, GURUHLAB
        //
        // Guruh jadvalidagi shartning aynan o'zi (faqat USTOZ darslari va
        // faqat YAKUNLANGANI), lekin natija bitta son emas, guruh -> son
        // jadvali.
        var sessionRows = await db.LiveSessions.AsNoTracking()
            .Where(s => groupIds.Contains(s.GroupId)
                     && s.Type == SessionType.Teacher
                     && s.Status == SessionStatus.Ended
                     && s.ScheduledStart >= startUtc
                     && s.ScheduledStart < endUtc)
            .GroupBy(s => s.GroupId)
            .Select(g => new GroupCountRow(g.Key, g.Count()))
            .ToListAsync(ct);

        var sessionsByGroup = sessionRows.ToDictionary(r => r.GroupId, r => r.Value);

        // ---------------------------------------------------- 2) qatnashganlar
        //
        // ★ KALIT — (GURUH, O'QUVCHI) JUFTLIGI, faqat o'quvchi emas.
        // Aks holda ikki guruhda o'qiydigan o'quvchining ikkala guruhdagi
        // qatnashuvi qo'shilib, ASOSIY guruh maxrajiga bo'linardi va
        // davomat 100% dan oshib ketishi mumkin edi.
        var attendanceRows = await db.Attendances.AsNoTracking()
            .Where(a => studentIds.Contains(a.StudentId)
                     && a.Status != AttendanceStatus.Absent
                     && a.Session!.Type == SessionType.Teacher
                     && a.Session.Status == SessionStatus.Ended
                     && a.Session.ScheduledStart >= startUtc
                     && a.Session.ScheduledStart < endUtc)
            .GroupBy(a => new { a.Session!.GroupId, a.StudentId })
            .Select(g => new GroupStudentCountRow(g.Key.GroupId, g.Key.StudentId, g.Count()))
            .ToListAsync(ct);

        var attendedByPair = attendanceRows.ToDictionary(
            r => (r.GroupId, r.StudentId), r => r.Value);

        // ---------------------------------------------------- 3) vazifalar
        //
        // ★ QAMROV QAYTA CHIQARILDI, GURUH SHARTIDAN KO'CHIRILMADI.
        //   Guruhda shart `assignment.GroupId == groupId || ModuleLessonId != null`
        //   edi. Markazda uning to'g'ri tarjimasi:
        //
        //     "shu MARKAZNING guruh vazifasi" YOKI "kurs darsi vazifasi".
        //
        //   KURS VAZIFALARI (GroupId — NULL) ATAYLAB QOLDIRILDI. Ikki sabab:
        //
        //     1) v2 da vazifalarning asosiy qismi kurs darsiga biriktirilgan.
        //        Ularni chiqarib tashlansa markaz jadvalida vazifa mezoni
        //        deyarli har doim bo'sh chiqardi va yakuniy ball IKKI
        //        mezondan hisoblanardi — ya'ni o'quvchi bir xil oyda guruh
        //        jadvalida 63, markaz jadvalida 55 ball ko'rardi va bu
        //        farqni hech kim tushuntira olmasdi.
        //
        //     2) Ball MUTLAQ emas, FOIZ (baho / maksimal baho). Turli
        //        kursdagi ikki o'quvchining 80% i bir xil ma'noni
        //        anglatadi — "berilgan ishning 80% ini bajardi". Adolat
        //        e'tirozi aynan shu tufayli ishlamaydi.
        //
        //   🔴 KELAJAK: kurs vazifasi markazlararo bo'lishi mumkin (bitta
        //      kurs bir necha markazga sotiladi). O'shanda shu shartga
        //      kursning markazi bo'yicha filtr kerak bo'ladi — SUBMISSION
        //      esa allaqachon markaz o'quvchilari bilan chegaralangan,
        //      ya'ni sizib chiqish xavfi yo'q, faqat qamrov kengroq bo'ladi.
        var gradedRatios =
            from submission in db.Submissions.AsNoTracking()
            join assignment in db.Assignments.AsNoTracking()
                on submission.AssignmentId equals assignment.Id
            where studentIds.Contains(submission.StudentId)
                && submission.Status == SubmissionStatus.Graded
                && submission.Score != null
                && assignment.MaxScore > 0
                && (assignment.ModuleLessonId != null
                    || (assignment.GroupId != null
                        && groupIds.Contains(assignment.GroupId.Value)))
                && submission.GradedAt >= startUtc
                && submission.GradedAt < endUtc
            select new { submission.StudentId, Ratio = submission.Score!.Value / assignment.MaxScore };

        var assignmentRows = await gradedRatios
            .GroupBy(x => x.StudentId)
            .Select(g => new RatioRow(g.Key, g.Average(x => x.Ratio)))
            .ToListAsync(ct);

        var assignmentByStudent = assignmentRows.ToDictionary(r => r.StudentId, r => r.Ratio);

        // ---------------------------------------------------- 4) testlar
        //
        // Test urinishi guruhga BOG'LANMAGAN — shart guruh jadvalidagi
        // bilan bir xil, faqat o'quvchilar ro'yxati kengroq.
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

        // ---------------------------------------------------- 5) dars baholari (R24)
        //
        // ★ GURUH JADVALIDAGI SHARTNING AYNAN TARJIMASI, faqat "shu guruh"
        //   o'rniga "shu markazning guruhlari".
        //
        // ★ KALIT — FAQAT O'QUVCHI, (guruh, o'quvchi) JUFTLIGI EMAS
        //   (davomatdan FARQI). Sabab: bu mezonda MAXRAJ YO'Q — u
        //   o'rtacha FOIZ, ya'ni ikki guruhda o'qiydigan o'quvchining
        //   ikkala guruhdagi baholari birga o'rtachalanadi va bu to'g'ri.
        //   Davomatda esa maxraj ASOSIY guruhniki edi, shuning uchun u
        //   yerda juftlik shart edi.
        var lessonGradeRows = await db.LessonGrades.AsNoTracking()
            .Where(g => studentIds.Contains(g.StudentId)
                     && groupIds.Contains(g.Session!.GroupId)
                     && g.Session.Status != SessionStatus.Cancelled
                     && g.Session.ScheduledStart >= startUtc
                     && g.Session.ScheduledStart < endUtc)
            .GroupBy(g => g.StudentId)
            .Select(g => new RatioRow(
                g.Key,
                g.Average(x => x.Score / (x.MaxScore ?? LessonGrade.DefaultMaxScore))))
            .ToListAsync(ct);

        var lessonByStudent = lessonGradeRows.ToDictionary(r => r.StudentId, r => r.Ratio);

        // ---------------------------------------------------- yig'ish
        var scores = new List<LeaderboardScore>(audience.Students.Count);

        foreach (var student in audience.Students)
        {
            decimal? attendance = null;

            // Maxraj 0 bo'lsa mezon `null` — 0 EMAS (guruh jadvalidagi
            // qoidaning aynan o'zi: markaz hali dars o'tmagan bo'lsa
            // o'quvchi buning uchun jazolanmaydi).
            if (student.PrimaryGroupId is { } groupId
                && sessionsByGroup.TryGetValue(groupId, out var ended)
                && ended > 0)
            {
                attendance = LeaderboardScore.Percent(
                    attendedByPair.GetValueOrDefault((groupId, student.StudentId)), ended);
            }

            scores.Add(new LeaderboardScore(
                student.StudentId,
                student.FullName,
                attendance,
                AssignmentPercent: assignmentByStudent.TryGetValue(student.StudentId, out var asg)
                    ? LeaderboardScore.PercentFromRatio(asg)
                    : null,
                TestPercent: testByStudent.TryGetValue(student.StudentId, out var test)
                    ? LeaderboardScore.PercentFromRatio(test)
                    : null,
                LessonPercent: lessonByStudent.TryGetValue(student.StudentId, out var lesson)
                    ? LeaderboardScore.PercentFromRatio(lesson)
                    : null));
        }

        // ★ CHEGARASIZ TARTIBLASH: `Rank` (500 chegarasi) GURUH invarianti,
        // markazda esa 500+ o'quvchi normal holat. Kesish keyinroq,
        // KESHDAN CHIQQANDAN SO'NG bo'ladi.
        var ranked = LeaderboardRanking.RankAll(scores);

        var rows = ranked.Select(r => new LeaderboardRowDto(
            r.Score.StudentId,
            r.Score.StudentName,
            r.Rank,
            r.Score.Total,
            r.Score.AttendancePercent,
            r.Score.AssignmentPercent,
            r.Score.TestPercent,
            IsMe: false,
            r.Score.LessonPercent)).ToList();

        return new CachedLeaderboard(audience.Students.Count, rows);
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

    /// <summary>Guruhda o'tilgan darslar soni (markaz davomat maxraji).</summary>
    private sealed record GroupCountRow(long GroupId, int Value);

    /// <summary>Bitta o'quvchining BITTA guruhdagi qatnashuvlari soni.</summary>
    private sealed record GroupStudentCountRow(long GroupId, long StudentId, int Value);

    private sealed record RatioRow(long StudentId, decimal Ratio);
}

/// <summary>
/// Keshda saqlanadigan NEYTRAL jadval — ko'ruvchiga bog'liq maydonsiz.
/// <c>public</c>, chunki <see cref="ICacheService"/> uni JSON'ga
/// serializatsiya qiladi.
/// </summary>
public sealed record CachedLeaderboard(int StudentCount, IReadOnlyList<LeaderboardRowDto> Rows);
