using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Penalties.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Penalties.Services;

/// <inheritdoc cref="IPenaltyService"/>
public sealed class PenaltyService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IPenaltyService
{
    /// <summary>
    /// Kechikish uchun CHIDAM (daqiqa) — frontenddagi bilan AYNI.
    ///
    /// Soatlar bir necha soniyaga farq qilishi odatiy hol. Chegara
    /// bo'lmasa deyarli har dars "1 daqiqa kechikdi" bo'lib jarimaga
    /// tushardi va butun ko'rsatkich ishonchini yo'qotardi.
    /// </summary>
    private const int LateToleranceMinutes = 1;

    private const int MaxPageSize = 100;

    // ================================================================= o'qish

    /// <inheritdoc />
    public async Task<PagedResult<PenaltyRowDto>> ListAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = Filter(query);
        var total = await rows.CountAsync(ct);

        var items = await rows
            // Yangidan eskiga; `Id` — teng vaqtli yozuvlarda tartib
            // so'rovdan so'rovga sakramasin.
            .OrderByDescending(p => p.OccurredAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(Projection)
            .ToListAsync(ct);

        return new PagedResult<PenaltyRowDto>(items.ConvertAll(ToDto), page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<PenaltySummaryDto> GetSummaryAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var flat = await Filter(query)
            .Select(p => new { p.Status, p.Amount })
            .ToListAsync(ct);

        int CountOf(PenaltyStatus status) => flat.Count(x => x.Status == status);
        decimal SumOf(PenaltyStatus status) => flat.Where(x => x.Status == status).Sum(x => x.Amount);

        return new PenaltySummaryDto(
            Total: flat.Count,
            PendingCount: CountOf(PenaltyStatus.Pending),
            ApprovedCount: CountOf(PenaltyStatus.Approved),
            CancelledCount: CountOf(PenaltyStatus.Cancelled),
            PendingAmount: SumOf(PenaltyStatus.Pending),
            ApprovedAmount: SumOf(PenaltyStatus.Approved));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PenaltyByUserDto>> GetByUserAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCanViewAsync(actorId, ct);

        var flat = await Filter(query)
            .Select(p => new
            {
                p.UserId,
                UserName = p.User!.FullName,
                UserRole = p.User.Role,
                p.Status,
                p.Amount,
                p.LateMinutes,
            })
            .ToListAsync(ct);

        return flat
            .GroupBy(x => x.UserId)
            .Select(g =>
            {
                var first = g.First();

                return new PenaltyByUserDto(
                    g.Key,
                    first.UserName,
                    first.UserRole.ToString(),
                    g.Count(x => x.Status == PenaltyStatus.Pending),
                    g.Count(x => x.Status == PenaltyStatus.Approved),
                    g.Where(x => x.Status == PenaltyStatus.Approved).Sum(x => x.Amount),
                    g.Sum(x => x.LateMinutes ?? 0));
            })
            // Eng ko'p ushlab qolingani birinchi — diqqat talab qiladigan
            // xodim tepada tursin.
            .OrderByDescending(x => x.ApprovedAmount)
            .ThenByDescending(x => x.PendingCount)
            .ThenBy(x => x.UserName, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PenaltyReportDto> GetReportAsync(
        string period, long actorId, CancellationToken ct = default)
    {
        await EnsureCanViewAsync(actorId, ct);

        var billingPeriod = BillingPeriod.Parse(period);
        var firstDay = billingPeriod.FirstDay();

        // ★ FAQAT TASDIQLANGAN VA KUTILAYOTGAN: bekor qilingan jarima
        //   pul EMAS — uni hisobotga qo'shsak, xodimga ko'rsatiladigan
        //   "jami" oylikdagi ushlanmadan katta chiqib, bahsga sabab
        //   bo'lardi.
        var flat = await db.Penalties.AsNoTracking()
            .Where(p => p.PeriodStart == firstDay && p.Status != PenaltyStatus.Cancelled)
            .Select(p => new
            {
                p.UserId,
                UserName = p.User!.FullName,
                UserRole = p.User.Role,
                Label = p.Category == null ? null : p.Category.Label,
                p.Kind,
                p.Amount,
            })
            .ToListAsync(ct);

        var users = flat
            .GroupBy(x => x.UserId)
            .Select(g =>
            {
                var first = g.First();

                var lines = g
                    // Kategoriyasiz eski jarimalar tur nomi ostida
                    // yig'iladi — hisobotda "nomsiz" qator qolmasin.
                    .GroupBy(x => x.Label ?? KindLabel(x.Kind))
                    .Select(l => new PenaltyReportLineDto(l.Key, l.Count(), l.Sum(x => x.Amount)))
                    .OrderByDescending(l => l.Amount)
                    .ThenBy(l => l.Label, StringComparer.Ordinal)
                    .ToList();

                return new PenaltyReportUserDto(
                    g.Key,
                    first.UserName,
                    first.UserRole.ToString(),
                    g.Sum(x => x.Amount),
                    lines);
            })
            .OrderByDescending(u => u.Total)
            .ThenBy(u => u.UserName, StringComparer.Ordinal)
            .ToList();

        return new PenaltyReportDto(
            billingPeriod.ToString(),
            users.Sum(u => u.Total),
            users);
    }

    /// <summary>Kategoriyasiz jarima uchun o'qiladigan nom.</summary>
    private static string KindLabel(PenaltyKind kind) => kind switch
    {
        PenaltyKind.LateStart => "Darsga kechikish",
        PenaltyKind.MissedLesson => "Dars o'tilmadi",
        _ => "Boshqa",
    };

    // ================================================================= yozish

    /// <inheritdoc />
    public async Task<PenaltyRowDto> CreateManualAsync(
        CreateManualPenaltyRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ★ KIRITISH — O'QUV BO'LIMIGA HAM OCHIQ (tasdiqlashdan FARQLI):
        //   qo'lda kiritilgan jarima ham `Pending` bo'lib tug'iladi va
        //   oylikka TEGMAYDI. Pulga aylanadigan qadam — tasdiqlash, va
        //   u faqat adminda (`EnsureCanManageAsync`).
        await EnsureCanViewAsync(actorId, ct);

        var target = await db.Users.AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.Id, u.Role })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        // 🔴 FAQAT USTOZ VA KURATOR: jarima — dars o'tish majburiyatining
        //    natijasi. O'quvchiga yoki adminga yozish ma'nosiz va u
        //    oylik tuzatmasiga aylanganda tushunarsiz yozuv qoldirardi.
        if (target.Role is not (UserRole.Teacher or UserRole.Assistant))
            throw new ConflictException("Jarima faqat ustoz yoki kuratorga yoziladi.");

        PenaltyCategory? category = null;

        if (request.CategoryId is { } categoryId)
        {
            category = await db.PenaltyCategories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoryId, ct)
                ?? throw new NotFoundException(nameof(PenaltyCategory), categoryId);

            // Arxivlangan tarif YANGI jarimada tanlanmaydi (eski
            // jarimalarda esa ko'rinib turaveradi).
            if (!category.IsActive)
                throw new ConflictException($"\"{category.Label}\" tarifi arxivlangan.");
        }

        var now = clock.GetUtcNow();
        var occurredAt = request.OccurredAt ?? now;

        var penalty = Penalty.Manual(
            target.Id,
            category,
            request.Quantity,
            request.Amount,
            request.Reason,
            actorId,
            occurredAt,
            PeriodOf(occurredAt));

        db.Penalties.Add(penalty);
        await db.SaveChangesAsync(ct);

        return await GetRowAsync(penalty.Id, ct);
    }

    /// <inheritdoc />
    public async Task<PenaltyRowDto> ApproveAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        var penalty = await db.Penalties.AsTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(Penalty), id);

        await EnsureCanReviewAsync(penalty, actorId, ct);

        var now = clock.GetUtcNow();
        penalty.Approve(actorId, now);

        // ═══════════════════════════════════════════════════════════════
        // OYLIKKA MANFIY TUZATMA — TASDIQLASHNING BUTUN MA'NOSI.
        //
        // ★ AYNI TRANZAKSIYA: jarimaning holati va oylik tuzatmasi BIRGA
        //   saqlanadi. Alohida bo'lsa "tasdiqlangan, lekin oylikda yo'q"
        //   (yoki teskarisi) yarim holati yuzaga kelardi.
        //
        // ★ MANFIY SUMMA: `PayrollAdjustment.Amount` kelishuvi bo'yicha
        //   musbat — bonus, manfiy — ushlab qolish.
        // ═══════════════════════════════════════════════════════════════
        var adjustment = new PayrollAdjustment
        {
            UserId = penalty.UserId,
            PeriodStart = penalty.PeriodStart,
            Amount = -penalty.Amount,
            Reason = $"Jarima: {penalty.Reason}",
            CreatedById = actorId,
        };

        adjustment.Validate();
        db.PayrollAdjustments.Add(adjustment);

        await db.SaveChangesAsync(ct);

        // Havola ikkinchi saqlashda yoziladi: `adjustment.Id` faqat
        // birinchi `SaveChanges` dan keyin ma'lum bo'ladi.
        penalty.PayrollAdjustmentId = adjustment.Id;
        await db.SaveChangesAsync(ct);

        return await GetRowAsync(penalty.Id, ct);
    }

    /// <inheritdoc />
    public async Task<PenaltyRowDto> CancelAsync(
        long id, CancelPenaltyRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var penalty = await db.Penalties.AsTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(Penalty), id);

        await EnsureCanReviewAsync(penalty, actorId, ct);

        penalty.Cancel(actorId, request.Reason, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        return await GetRowAsync(penalty.Id, ct);
    }

    // ================================================================= avtomatik aniqlash

    /// <inheritdoc />
    public async Task DetectLateStartAsync(LiveSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.HostId is not { } hostId) return;
        if (session.ActualStart is not { } actualStart) return;

        var lateMinutes = (int)Math.Floor((actualStart - session.ScheduledStart).TotalMinutes);
        if (lateMinutes <= LateToleranceMinutes) return;

        var category = await SystemCategoryAsync(PenaltyCategory.LateStartKey, ct);
        if (category is null) return;

        // Bu dars uchun kechikish jarimasi allaqachon bormi. Baza
        // darajasidagi unikal indeks ham bor — bu tekshiruv faqat
        // ortiqcha istisnodan saqlaydi.
        var exists = await db.Penalties
            .AnyAsync(p => p.SessionId == session.Id && p.Kind == PenaltyKind.LateStart, ct);

        if (exists) return;

        db.Penalties.Add(Penalty.ForLateStart(
            hostId, session.Id, lateMinutes, category, actualStart, PeriodOf(actualStart)));
    }

    /// <inheritdoc />
    public async Task<int> ScanMissedLessonsAsync(CancellationToken ct = default)
    {
        var category = await SystemCategoryAsync(PenaltyCategory.MissedLessonKey, ct);
        if (category is null) return 0;

        var now = clock.GetUtcNow();

        // ★ "O'TILMAGAN" TA'RIFI: rejadagi tugash vaqti O'TGAN, lekin
        //   dars hamon `Scheduled` — ya'ni hech qachon boshlanmagan.
        //   Bekor qilingani sanalmaydi (bayram yoki o'quv bo'limi qarori).
        var candidates = await db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Status == SessionStatus.Scheduled
                && s.ScheduledEnd < now
                && s.HostId != null
                && !db.Penalties.Any(p => p.SessionId == s.Id && p.Kind == PenaltyKind.MissedLesson))
            .Select(s => new { s.Id, s.HostId, s.ScheduledStart })
            .Take(200)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        foreach (var session in candidates)
        {
            db.Penalties.Add(Penalty.ForMissedLesson(
                session.HostId!.Value,
                session.Id,
                category,
                session.ScheduledStart,
                PeriodOf(session.ScheduledStart)));
        }

        await db.SaveChangesAsync(ct);

        return candidates.Count;
    }

    // ================================================================= yordamchi

    private IQueryable<Penalty> Filter(PenaltyListQuery query)
    {
        var rows = db.Penalties.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Period))
        {
            var period = BillingPeriod.Parse(query.Period).FirstDay();
            rows = rows.Where(p => p.PeriodStart == period);
        }

        if (query.OccurredOn is { } day)
        {
            // ★ YARIM OCHIQ ORALIQ mahalliy kun chegarasida: `OccurredAt`
            //   UTC saqlanadi, foydalanuvchi esa Toshkent kunini
            //   nazarda tutadi (loyihadagi AYNI qoida).
            var from = LocalWallClock.StartOfDayUtc(day, timeZone.TimeZone);
            var to = LocalWallClock.StartOfDayUtc(day.AddDays(1), timeZone.TimeZone);

            rows = rows.Where(p => p.OccurredAt >= from && p.OccurredAt < to);
        }

        if (query.UserId is { } userId) rows = rows.Where(p => p.UserId == userId);
        if (query.CategoryId is { } categoryId) rows = rows.Where(p => p.CategoryId == categoryId);
        if (query.Kind is { } kind) rows = rows.Where(p => p.Kind == kind);
        if (query.Status is { } status) rows = rows.Where(p => p.Status == status);

        var term = NormalizeSearch(query.Search);

        if (term is not null)
        {
#pragma warning disable CA1304, CA1311
            rows = rows.Where(p =>
                EF.Functions.Like(p.User!.FullName.ToLower(), term)
                || EF.Functions.Like(p.Reason.ToLower(), term)
                || (p.Category != null && EF.Functions.Like(p.Category.Label.ToLower(), term)));
#pragma warning restore CA1304, CA1311
        }

        return rows;
    }

    /// <summary>
    /// Bitta yozuvni qaytaradi (yaratish/tasdiqlash/bekor qilishdan keyin).
    ///
    /// ★ RUXSAT QAYTA TEKSHIRILMAYDI: bu metod FAQAT tekshiruvdan
    /// allaqachon o'tgan amallardan chaqiriladi.
    /// </summary>
    private async Task<PenaltyRowDto> GetRowAsync(long id, CancellationToken ct) =>
        ToDto(await db.Penalties.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(Projection)
            .FirstAsync(ct));

    /// <summary>
    /// Oraliq shakl: enum'lar HALI matnga aylantirilmagan.
    ///
    /// ★ NEGA TO'G'RIDAN <see cref="PenaltyRowDto"/> GA EMAS:
    /// <c>enum.ToString()</c> ni so'rov ichida yozsak EF uni SQL'ga
    /// tarjima qilishga urinardi (loyihadagi AYNI qoida) — shuning
    /// uchun matnga o'girish XOTIRADA, <see cref="ToDto"/> da.
    /// </summary>
    private sealed record Row(
        long Id,
        long UserId,
        string UserName,
        UserRole UserRole,
        long? SessionId,
        string? GroupName,
        DateTimeOffset? SessionScheduledStart,
        DateTimeOffset? SessionActualStart,
        PenaltyKind Kind,
        PenaltyStatus Status,
        long? CategoryId,
        string? CategoryLabel,
        decimal? Quantity,
        string? UnitLabel,
        int? LateMinutes,
        decimal Amount,
        string Reason,
        DateTimeOffset OccurredAt,
        DateOnly PeriodStart,
        string? CreatedByName,
        DateTimeOffset CreatedAt,
        string? ReviewedByName,
        DateTimeOffset? ReviewedAt);

    /// <summary>
    /// Jadval va bitta yozuv AYNI proyeksiyadan o'qiydi — ustun
    /// qo'shilganda ikki joyni yangilash unutilmasin.
    /// </summary>
    private static readonly Expression<Func<Penalty, Row>> Projection =
        p => new Row(
            p.Id,
            p.UserId,
            p.User!.FullName,
            p.User.Role,
            p.SessionId,
            p.Session == null ? null : p.Session.Group!.Name,
            p.Session == null ? (DateTimeOffset?)null : p.Session.ScheduledStart,
            p.Session == null ? null : p.Session.ActualStart,
            p.Kind,
            p.Status,
            p.CategoryId,
            p.Category == null ? null : p.Category.Label,
            p.Quantity,
            p.Category == null ? null : p.Category.UnitLabel,
            p.LateMinutes,
            p.Amount,
            p.Reason,
            p.OccurredAt,
            p.PeriodStart,
            p.CreatedBy == null ? null : p.CreatedBy.FullName,
            p.CreatedAt,
            p.ReviewedBy == null ? null : p.ReviewedBy.FullName,
            p.ReviewedAt);

    private static PenaltyRowDto ToDto(Row r) => new(
        r.Id, r.UserId, r.UserName, r.UserRole.ToString(),
        r.SessionId, r.GroupName, r.SessionScheduledStart, r.SessionActualStart,
        r.Kind.ToString(), r.Status.ToString(),
        r.CategoryId, r.CategoryLabel, r.Quantity, r.UnitLabel,
        r.LateMinutes, r.Amount, r.Reason, r.OccurredAt, r.PeriodStart,
        r.CreatedByName, r.CreatedAt, r.ReviewedByName, r.ReviewedAt);

    /// <summary>Hodisa sanasidan oylik davrini (oyning 1-kuni) chiqaradi.</summary>
    private DateOnly PeriodOf(DateTimeOffset instant) =>
        BillingPeriod.FromDate(LocalWallClock.LocalDate(instant, timeZone.TimeZone)).FirstDay();

    /// <summary>
    /// Avtomatik jarima tarifini kalit bo'yicha topadi.
    ///
    /// ★ <c>null</c> QAYTISHI — XATO EMAS, O'CHIRGICH: tarif topilmasa
    /// yoki summasi `0` bo'lsa, jarima shunchaki yozilmaydi. Shu tarzda
    /// administrator summani `0` qilib avtomatik jarimani vaqtincha
    /// to'xtata oladi (ilgari bu sozlamadagi `0` bilan qilinardi).
    /// </summary>
    private async Task<PenaltyCategory?> SystemCategoryAsync(string systemKey, CancellationToken ct)
    {
        var category = await db.PenaltyCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SystemKey == systemKey, ct);

        return category is { Amount: > 0 } ? category : null;
    }

    private async Task EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        var role = await RoleOfAsync(actorId, ct);

        if (role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Jarimalar panelini faqat o'quv bo'limi va administrator ko'radi.");
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// KIM TASDIQLAY OLADI (loyiha egasi qarori, 2026-08-18)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// • TIZIM YOZGAN jarima (kechikish, o'tilmagan dars) — o'quv bo'limi
    ///   HAM tasdiqlaydi. Sabab: bu jarimalarni HECH KIM yozmagan, ularni
    ///   dastur o'zi aniqlagan. Ustoz kechikkanmi yoki yo'qmi — buni
    ///   kundalik ish oqimida aynan o'quv bo'limi biladi, va har kuni
    ///   administratorni kutish jarimalarni "kutilmoqda" holatida
    ///   uyib qo'yardi.
    ///
    /// • QO'LDA yozilgan jarima — FAQAT ADMIN. Sabab: uni o'quv bo'limi
    ///   xodimining O'ZI kiritadi. Tasdiqlashga ham ruxsat berilsa, bitta
    ///   odam ham jarima yozib, ham uni pulga aylantirib yuborardi —
    ///   ya'ni oylikdan ushlab qolish nazoratsiz qolardi.
    ///
    /// ★ BEKOR QILISH HAM AYNI QOIDA BO'YICHA: tasdiqlay oladigan odam
    ///   bekor ham qila olishi SHART. Aks holda noto'g'ri aniqlangan
    ///   kechikishni faqat tasdiqlash mumkin bo'lib, rad etib bo'lmasdi.
    /// </summary>
    private async Task EnsureCanReviewAsync(Penalty penalty, long actorId, CancellationToken ct)
    {
        var role = await RoleOfAsync(actorId, ct);

        if (role is UserRole.Admin) return;

        if (role is UserRole.Academic && penalty.Kind is not PenaltyKind.Manual) return;

        throw new ForbiddenException(
            role is UserRole.Academic
                ? "Qo'lda yozilgan jarimani faqat administrator tasdiqlaydi."
                : "Jarimani tasdiqlash yoki bekor qilishni o'quv bo'limi va administrator bajaradi.");
    }

    private async Task<UserRole> RoleOfAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct);

        return role ?? throw new NotFoundException(nameof(User), actorId);
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
