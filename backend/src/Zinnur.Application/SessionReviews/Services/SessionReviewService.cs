using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.SessionReviews.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.SessionReviews.Services;

/// <summary>
/// <see cref="ISessionReviewService"/> ning amalga oshirilishi.
///
/// ── RUXSAT BITTA JOYDA: <see cref="AuthorizeAsync"/> ─────────────────────
///
/// Uchala use-case ham AYNI metoddan boshlanadi va u ikkita javob
/// qaytaradi: dars (guruh bilan) va chaqiruvchining ROLI. Uchta joyda
/// uchta tekshiruv bo'lsa, bittasida <c>Student</c> darvozasi vaqt o'tib
/// tushib qolishi mumkin edi — bu esa aynan bu talabdagi ENG QIMMAT xato
/// (o'quvchi ustozi haqidagi ichki bahoni o'qib qolishi).
/// </summary>
public sealed class SessionReviewService(
    IApplicationDbContext db,
    TimeProvider clock) : ISessionReviewService
{
    /// <inheritdoc />
    public async Task<SessionReviewDto?> GetAsync(
        long sessionId, long actorId, CancellationToken ct = default)
    {
        var (session, role) = await AuthorizeAsync(sessionId, actorId, canWrite: false, ct)
            .ConfigureAwait(false);

        var review = await db.SessionReviews
            .AsNoTracking()
            .Include(r => r.Author)
            .Include(r => r.Scores)
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, ct)
            .ConfigureAwait(false);

        if (review is null) return null;

        var hostName = await ResolveHostNameAsync(session, ct).ConfigureAwait(false);
        return Map(review, role, session, hostName);
    }

    /// <inheritdoc />
    public async Task<SessionReviewDto> SaveAsync(
        long sessionId, SaveSessionReviewRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (session, role) = await AuthorizeAsync(sessionId, actorId, canWrite: true, ct)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();

        var existing = await db.SessionReviews
            .AsTracking()
            .Include(r => r.Scores)
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // Matn tekshiruvi Domain'da (`SessionReview.Create` -> 409):
            // chegara entity bilan birga turadi va uni ikki joyda yozib,
            // keyin bittasini o'zgartirib qo'yish mumkin emas
            // (`StudentNoteService.CreateAsync` dagi AYNI qoida).
            existing = SessionReview.Create(
                sessionId, actorId, request.Verdict,
                request.Plus, request.Minus, request.Conclusion, now);

            db.SessionReviews.Add(existing);
        }
        else
        {
            // ⚠️ MUALLIF O'ZGARMAYDI. Ikkinchi xodim tahrirlaganda ham
            //    ismi BIRINCHISINIKI bo'lib qoladi — sabab
            //    `SessionReview.Edit` izohida.
            existing.Edit(request.Verdict, request.Plus, request.Minus, request.Conclusion, now);
        }

        // `Scores` NULLABLE (DTO izohi): eski klient uni umuman yubormasligi
        // mumkin — bu holda mezon ballari TEGILMAYDI (faqat erkin matn).
        if (request.Scores is { Count: > 0 } scores)
        {
            var catalog = await CatalogAsync(scores, ct).ConfigureAwait(false);

            existing.SetScores(
                scores.Select(s => (s.CriterionId, s.Score)).ToList(),
                catalog,
                now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Muallif navigatsiyasi yangi qatorda hali yuklanmagan — DTO uchun
        // ism kerak, shuning uchun qayta o'qiymiz. Bu YOZISH yo'li, ya'ni
        // ortiqcha so'rov narxi sezilmaydi.
        var saved = await db.SessionReviews
            .AsNoTracking()
            .Include(r => r.Author)
            .Include(r => r.Scores)
            .FirstAsync(r => r.Id == existing.Id, ct)
            .ConfigureAwait(false);

        var hostName = await ResolveHostNameAsync(session, ct).ConfigureAwait(false);
        return Map(saved, role, session, hostName);
    }

    /// <summary>
    /// So'ralgan mezonlarni katalogdan Id bo'yicha xaritalaydi.
    ///
    /// 🔴 SETSCORES SHU BILAN XAVFSIZ: <c>request.Scores</c> ichidagi nom/
    /// maksimal ball SERVERGA yuborilmaydi ham — faqat <c>CriterionId</c>.
    /// Shuning uchun klient ixtiyoriy shkalada ball "o'ylab topa" olmaydi.
    /// </summary>
    private async Task<IReadOnlyDictionary<long, AnalysisCriterion>> CatalogAsync(
        IReadOnlyList<SaveSessionReviewScoreRequest> scores, CancellationToken ct)
    {
        var ids = scores.Select(s => s.CriterionId).Distinct().ToList();

        return await db.AnalysisCriteria
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long sessionId, long actorId, CancellationToken ct = default)
    {
        await AuthorizeAsync(sessionId, actorId, canWrite: true, ct).ConfigureAwait(false);

        var review = await db.SessionReviews
            .AsTracking()
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, ct)
            .ConfigureAwait(false);

        // IDEMPOTENT: tahlil bo'lmasa jim o'tamiz. 404 qaytarilsa klient
        // ikki marta bosilgan tugmadan keyin xato ko'rsatardi, holat esa
        // aslida AYNAN so'ralganidek bo'lardi.
        if (review is null) return;

        db.SessionReviews.Remove(review);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ================================================================= TAHLILLAR PANELI (2026-08-16)

    /// <inheritdoc />
    public async Task<IReadOnlyList<TeacherReviewOverviewDto>> GetTeachersOverviewAsync(
        long actorId, CancellationToken ct = default)
    {
        await EnsureCanViewOverviewAsync(actorId, ct).ConfigureAwait(false);

        /*
          ★ AVVAL YASSI QATOR, KEYIN GURUHLASH XOTIRADA — `AssignmentService.
          GetGroupsOverviewAsync` dagi AYNI naqsh (izohi o'sha yerda): xodim
          Id'si `Session.Type`ga qarab IKKI YO'LDAN (guruh ustozi YOKI
          kuratori) kelishi mumkin va bu shartni GROUP BY bilan bitta SQL
          so'rovga sig'dirish o'qib bo'lmas ifoda berardi. Tahlillar soni
          cheklangan, ya'ni oxirgi guruhlash xotirada arzon.
        */
        var flat = await db.SessionReviews
            .AsNoTracking()
            .Select(r => new
            {
                r.Verdict,
                r.CreatedAt,

                // ★ AVVAL `HostId`, SO'NG guruh o'rindig'i (2026-08-18 da
                //   to'g'rilandi) — `LiveSessionService.HostUserId` bilan
                //   AYNI qoida va sabab ham o'sha yerda.
                //
                //   Bu yerda u AYNIQSA muhim: tahlil DARSNING QANDAY
                //   O'TGANI haqida. O'rinbosar o'tgan darsni ASL ustozning
                //   ko'rsatkichiga yozish — uni o'zi o'tmagan dars uchun
                //   baholash degani. Ustoz keyin almashgan eski darslarda
                //   ham `HostId` o'sha kuni darsni haqiqatda kim o'tganini
                //   saqlaydi, `Group.TeacherId` esa BUGUNGI ustozni.
                TeacherId = r.Session!.HostId
                    ?? (r.Session.Type == SessionType.Assistant
                        ? r.Session.Group!.AssistantId
                        : r.Session.Group!.TeacherId),
            })
            .Where(x => x.TeacherId != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var grouped = flat
            .GroupBy(x => x.TeacherId!.Value)
            .Select(g => new
            {
                TeacherId = g.Key,
                Total = g.Count(),
                Approved = g.Count(x => x.Verdict == SessionReviewVerdict.Approved),
                HasIssue = g.Count(x => x.Verdict == SessionReviewVerdict.HasIssue),
                NotReviewed = g.Count(x => x.Verdict == SessionReviewVerdict.NotReviewed),
                LastReviewAt = g.Max(x => x.CreatedAt),
            })
            .ToList();

        var teacherIds = grouped.Select(g => g.TeacherId).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => teacherIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct)
            .ConfigureAwait(false);

        return grouped
            .Select(g => new TeacherReviewOverviewDto(
                g.TeacherId,
                names.TryGetValue(g.TeacherId, out var name) ? name : "Noma'lum xodim",
                g.Total,
                g.Approved,
                g.HasIssue,
                g.NotReviewed,
                g.LastReviewAt))
            // ★ ENG YAQINDA TAHLIL QILINGANI BIRINCHI: o'quv bo'limi odatda
            // "kimni SO'NGGI marta ko'rib chiqdim" savolidan boshlaydi.
            .OrderByDescending(x => x.LastReviewAt)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionReviewDto>> ListByTeacherAsync(
        long teacherId, long actorId, CancellationToken ct = default)
    {
        await EnsureCanViewOverviewAsync(actorId, ct).ConfigureAwait(false);

        var teacherName = await db.Users.AsNoTracking()
            .Where(u => u.Id == teacherId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var reviews = await db.SessionReviews
            .AsNoTracking()
            .Include(r => r.Author)
            .Include(r => r.Scores)
            .Include(r => r.Session!)
                .ThenInclude(s => s!.Group)
            // ★ TERNARY EMAS, IKKI SHART OR BILAN: `Session.Type`ga qarab
            // "kim xodim" tekshiruvi ikkita mustaqil taqqoslashga yoyilgan —
            // bitta ifodada `(shart ? a : b) == teacherId` EF Core uchun
            // ko'proq chalkash SQL berardi, bu shakl esa `ResolveHostNameAsync`
            // dagi qoidaning to'g'ridan-to'g'ri tarjimasi.
            //
            // ★ `HostId` USTUN, o'rindiq esa ZAXIRA (2026-08-18) —
            //   yig'ma ko'rinishdagi (`GetTeachersOverviewAsync`) AYNI
            //   qoida. Zaxira shoxi ATAYLAB `HostId == null` bilan
            //   qo'riqlangan: aks holda o'rinbosar o'tgan dars IKKALA
            //   ustozning ro'yxatiga ham tushardi.
            .Where(r =>
                r.Session!.HostId == teacherId
                || (r.Session.HostId == null
                    && ((r.Session.Type == SessionType.Assistant && r.Session.Group!.AssistantId == teacherId)
                        || (r.Session.Type != SessionType.Assistant && r.Session.Group!.TeacherId == teacherId))))
            .OrderByDescending(r => r.Session!.ScheduledStart)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return reviews
            .ConvertAll(r => Map(r, UserRole.Academic, r.Session!, teacherName));
    }

    /// <summary>FAQAT Academic/Admin — sabab `ISessionReviewService` izohida ("Tahlillar paneli").</summary>
    private async Task EnsureCanViewOverviewAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (role is UserRole.Academic or UserRole.Admin) return;

        throw new ForbiddenException("Bu umumiy ko'rinishga faqat o'quv bo'limi va admin kira oladi.");
    }

    // ================================================================= RUXSAT

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 YAGONA RUXSAT DARVOZASI
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Tartib MUHIM va u eng qattiqdan yumshoqqa emas, ENG ARZON VA ENG
    /// XAVFLIDAN boshlanadi:
    ///
    ///  1) O'QUVCHIMI — darhol 403. Bu tekshiruv dars mavjudligidan ham
    ///     OLDIN turadi: aks holda o'quvchi mavjud bo'lmagan dars Id'lari
    ///     bo'yicha 404/403 farqini kuzatib, qaysi darslar borligini
    ///     aniqlab olardi. Bundan tashqari qoida shu tartibda
    ///     "o'chib qolishi" mumkin bo'lgan shoxobcha qoldirmaydi.
    ///  2) DARS BORMI — 404.
    ///  3) YOZISH bo'lsa: faqat <c>Academic</c>/<c>Admin</c>.
    ///  4) O'QISH bo'lsa: <c>Academic</c>/<c>Admin</c> — hammasi; ustoz va
    ///     kurator — FAQAT o'z guruhining darsi.
    /// </summary>
    private async Task<(LiveSession Session, UserRole Role)> AuthorizeAsync(
        long sessionId, long actorId, bool canWrite, CancellationToken ct)
    {
        var role = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // ── 1. O'QUVCHI — HAR YO'LDA VA HAR DOIM ────────────────────────
        //
        // 🔴 BU QATORNI OLIB TASHLASH YOKI SHARTGA O'RASH TAQIQ. Tahlil
        //    matni ustoz haqidagi ichki baho ("tushuntirish sust") va u
        //    o'quvchiga yetib borsa qaytarib bo'lmaydi.
        if (role == UserRole.Student)
        {
            throw new ForbiddenException(
                "Dars tahlili — o'quv bo'limining ichki yozuvi va o'quvchilarga ko'rsatilmaydi.");
        }

        // ── 2. DARS ──────────────────────────────────────────────────────
        var session = await db.LiveSessions
            .AsNoTracking()
            .Include(s => s.Group)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(LiveSession), sessionId);

        var isManage = role is UserRole.Academic or UserRole.Admin;

        // ── 3. YOZISH ────────────────────────────────────────────────────
        //
        // ⚠️ USTOZ O'Z DARSINING TAHLILINI TAHRIRLAY OLMAYDI. U sifat
        //    nazoratining OBYEKTI: aks holda "Muammo bor" xulosasini
        //    o'zi "Tasdiqlandi" ga aylantira olardi.
        if (canWrite)
        {
            if (!isManage)
            {
                throw new ForbiddenException(
                    "Dars tahlilini faqat o'quv bo'limi yozadi va tahrirlaydi.");
            }

            return (session, role);
        }

        // ── 4. O'QISH ────────────────────────────────────────────────────
        if (isManage) return (session, role);

        // R30: ustoz/kurator FAQAT O'Z darsining tahlilini ko'radi.
        //
        // ★ QOIDA `LiveSessionService.IsHost` BILAN AYNI SHAKLDA:
        //   darsning hosti YOKI guruhning ustozi/kuratori. Nusxa emas,
        //   AYNI mezon — u yerda ham `hostId`/`teacherId`/`assistantId`
        //   uchligi tekshiriladi (o'sha metod `private`, shuning uchun
        //   chaqirib bo'lmaydi; ma'nosi esa shu yerda ochiq yozilgan).
        var isOwnSession =
            session.HostId == actorId
            || session.Group?.TeacherId == actorId
            || session.Group?.AssistantId == actorId;

        if (!isOwnSession)
            throw new ForbiddenException("Bu dars tahlilini ko'rish huquqingiz yo'q.");

        return (session, role);
    }

    /// <summary>
    /// Shu darsni olib boradigan xodimning ismi —
    /// <c>LiveSessionService.HostUserId</c> BILAN AYNI qoida (avval
    /// <c>HostId</c>, u bo'sh bo'lsa <c>Type</c>ga qarab guruhning ustozi
    /// yoki kuratori). Ikkalasi mustaqil nusxa: bu servis
    /// <c>LiveSessionService</c>ga bog'lanmaydi (sabab —
    /// <c>ISessionReviewService</c> izohidagi "nega ILiveSessionService
    /// qayta ishlatilmaydi").
    ///
    /// ⚠️ NUSXA BO'LGANI UCHUN BIRGA O'ZGARADI: 2026-08-18 da o'sha
    /// yerdagi qoida `HostId` ga o'tdi va bu yer ham ATAYLAB shu bilan
    /// birga yangilandi — aks holda tahlil oynasi va "Jonli darslar"
    /// paneli bitta dars uchun ikki xil ism ko'rsatardi.
    /// </summary>
    private async Task<string?> ResolveHostNameAsync(LiveSession session, CancellationToken ct)
    {
        var hostUserId = session.HostId
            ?? (session.Type == SessionType.Assistant
                ? session.Group?.AssistantId
                : session.Group?.TeacherId);

        if (hostUserId is null) return null;

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == hostUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    private static SessionReviewDto Map(
        SessionReview review, UserRole role, LiveSession session, string? hostName) => new(
        review.Id,
        review.SessionId,
        review.Verdict.ToString(),
        review.Plus,
        review.Minus,
        review.Conclusion,
        session.ScheduledStart,
        session.Group?.Name ?? string.Empty,
        session.Title,
        hostName,
        review.AuthorId,

        // Muallif `Restrict` bilan bog'langan, ya'ni u DOIM mavjud. `??`
        // shunchaki nullable navigatsiya uchun — jimgina bo'sh satr emas,
        // ko'zga tashlanadigan qiymat.
        review.Author?.FullName ?? "Noma'lum xodim",

        // ★ QULAYLIK BAYROG'I, RUXSAT EMAS (izoh: `SessionReviewDto`).
        CanEdit: role is UserRole.Academic or UserRole.Admin,
        review.CreatedAt,
        review.UpdatedAt,
        review.Scores
            .Select(s => new SessionReviewScoreDto(s.CriterionId, s.CriterionName, s.MaxScore, s.Score))
            .ToList(),
        review.TotalScore,
        review.TotalMaxScore,
        review.ScorePercent);
}
