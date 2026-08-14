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
        var (_, role) = await AuthorizeAsync(sessionId, actorId, canWrite: false, ct)
            .ConfigureAwait(false);

        var review = await db.SessionReviews
            .AsNoTracking()
            .Include(r => r.Author)
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, ct)
            .ConfigureAwait(false);

        return review is null ? null : Map(review, role);
    }

    /// <inheritdoc />
    public async Task<SessionReviewDto> SaveAsync(
        long sessionId, SaveSessionReviewRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (_, role) = await AuthorizeAsync(sessionId, actorId, canWrite: true, ct)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();

        var existing = await db.SessionReviews
            .AsTracking()
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // Matn tekshiruvi Domain'da (`SessionReview.Create` -> 409):
            // chegara entity bilan birga turadi va uni ikki joyda yozib,
            // keyin bittasini o'zgartirib qo'yish mumkin emas
            // (`StudentNoteService.CreateAsync` dagi AYNI qoida).
            existing = SessionReview.Create(
                sessionId, actorId, request.Verdict, request.Body, now);

            db.SessionReviews.Add(existing);
        }
        else
        {
            // ⚠️ MUALLIF O'ZGARMAYDI. Ikkinchi xodim tahrirlaganda ham
            //    ismi BIRINCHISINIKI bo'lib qoladi — sabab
            //    `SessionReview.Edit` izohida.
            existing.Edit(request.Verdict, request.Body, now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Muallif navigatsiyasi yangi qatorda hali yuklanmagan — DTO uchun
        // ism kerak, shuning uchun qayta o'qiymiz. Bu YOZISH yo'li, ya'ni
        // ortiqcha so'rov narxi sezilmaydi.
        var saved = await db.SessionReviews
            .AsNoTracking()
            .Include(r => r.Author)
            .FirstAsync(r => r.Id == existing.Id, ct)
            .ConfigureAwait(false);

        return Map(saved, role);
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

    private static SessionReviewDto Map(SessionReview review, UserRole role) => new(
        review.Id,
        review.SessionId,
        review.Verdict.ToString(),
        review.Body,
        review.AuthorId,

        // Muallif `Restrict` bilan bog'langan, ya'ni u DOIM mavjud. `??`
        // shunchaki nullable navigatsiya uchun — jimgina bo'sh satr emas,
        // ko'zga tashlanadigan qiymat.
        review.Author?.FullName ?? "Noma'lum xodim",

        // ★ QULAYLIK BAYROG'I, RUXSAT EMAS (izoh: `SessionReviewDto`).
        CanEdit: role is UserRole.Academic or UserRole.Admin,
        review.CreatedAt,
        review.UpdatedAt);
}
