using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Payments.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Application.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(long actorId, CancellationToken ct = default);

    Task<SubscriptionPlanDto> CreatePlanAsync(SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default);

    Task<SubscriptionPlanDto> UpdatePlanAsync(
        long planId, SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Obuna beradi yoki uzaytiradi — yangi davr avvalgisining oxiridan boshlanadi.</summary>
    Task<SubscriptionPeriodDto> GrantAsync(GrantSubscriptionRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Onlayn buyurtma tasdiqlanganda obuna ochadi — narx BUYURTMADAN
    /// (o'quvchi ko'rgan summa), reja keyin o'chirilgan bo'lsa ham.
    /// </summary>
    Task<SubscriptionGrantResult> GrantForOrderAsync(
        long studentId, long planId, decimal amount, string note, long actorId, CancellationToken ct = default);

    /// <summary>
    /// O'quvchi tarifni BALANSIDAN to'laydi (2026-10-29, Q-4) — xodim tasdig'isiz.
    /// ⚠️ Ruxsatni chaqiruvchi (do'kon servisi) tekshiradi: amalni o'quvchining O'ZI bajaradi.
    /// </summary>
    Task<SubscriptionGrantResult> GrantFromBalanceAsync(
        long studentId, long planId, decimal amount, string note, CancellationToken ct = default);

    Task<SubscriptionPeriodDto> CancelAsync(
        long periodId, CancelSubscriptionRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Guruhga o'tkazilsa qancha kun va pul balansga o'tadi — hech narsa o'zgartirmaydi.</summary>
    Task<GroupConversionDto> PreviewGroupConversionAsync(long studentId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Obunani guruhga o'tkazish (2026-10-29, Q-2): joriy davr HOZIR yopiladi,
    /// kelgusi davrlar bekor qilinadi, ishlatilmagan pul balansga tushadi.
    /// </summary>
    Task<GroupConversionDto> ConvertToGroupAsync(long studentId, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionPeriodDto>> ListForStudentAsync(
        long studentId, long actorId, CancellationToken ct = default);

    Task<MySubscriptionDto> GetMineAsync(long studentId, CancellationToken ct = default);
}

/// <summary>
/// MUSTAQIL O'QISH OBUNASI (2026-10-23).
///
/// ★ FAQAT O'QUV BO'LIMI VA ADMINISTRATOR boshqaradi: obuna — pul, ya'ni
///   to'lov qabul qilishdagi kabi rol.
/// ★ GURUH VA OBUNA BIR-BIRINI TAQIQLAYDI: guruhdagi o'quvchiga obuna
///   berilmaydi (va aksincha, <c>GroupService.AddMemberAsync</c>). Sabab
///   <see cref="ISubscriptionAccess"/> izohida.
/// ★ Har o'zgarishdan keyin gating keshi tozalanadi — darslar darhol
///   ochiladi yoki yopiladi.
///
/// ════════════════════════════════════════════════════════════════════════
///  PUL QOIDALARI (2026-10-29, loyiha egasi qarorlari)
/// ════════════════════════════════════════════════════════════════════════
///  • Q-1 TARIF ALMASHTIRISH DARHOL: joriy davr boshqa tarifda bo'lsa, u
///    HOZIR yopiladi (bekor qilinmaydi), yangi davr hozirdan boshlanadi va
///    eski davrning ishlatilmagan qiymati (<see cref="SubscriptionPeriod.UnusedValue"/>)
///    balansga tushadi. Ayni tarifni uzaytirish — avvalgidek oxiridan.
///  • Q-2 GURUHGA O'TISH: qolgan kunlar pulga aylanib balansga tushadi.
///  • Q-4 BALANSDAN TO'LASH: xodim tasdig'isiz, bitta tranzaksiyada.
///  • Q-5 JURNAL: har so'm jurnalga O'Z turida kiradi va chiqadi
///    (<see cref="IPaymentService.RecordSubscriptionMoneyAsync"/>).
///  ★ Har amal BITTA baza tranzaksiyasida: davr ham, pul ham — yoki
///    ikkalasi yoziladi, yoki hech biri.
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SubscriptionService(
    IApplicationDbContext db,
    ISubscriptionAccess access,
    IGatingService gating,
    IPaymentService payments,
    TimeProvider clock) : ISubscriptionService
{
    // ================================================================ rejalar

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        return await db.SubscriptionPlans.AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.Course!.Name)
            .ThenBy(p => p.Months)
            .Select(p => new SubscriptionPlanDto(
                p.Id, p.Name, p.CourseId, p.Course!.Name, p.Months, p.Price, p.IsActive,
                p.Kind, p.Description, p.IsPublic, p.IncludesInteractiveBook))
            .ToListAsync(ct);
    }

    public async Task<SubscriptionPlanDto> CreatePlanAsync(
        SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureManagerAsync(actorId, ct);

        var plan = new SubscriptionPlan { Name = request.Name.Trim() };
        await ApplyAsync(plan, request, ct);

        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync(ct);

        return await PlanDtoAsync(plan.Id, ct);
    }

    public async Task<SubscriptionPlanDto> UpdatePlanAsync(
        long planId, SaveSubscriptionPlanRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureManagerAsync(actorId, ct);

        var plan = await db.SubscriptionPlans.AsTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), planId);

        // 🔴 SOTILGAN TARIFNING TURI VA KURSI O'ZGARMAYDI (audit P0-1, 2026-10-29):
        //    lokal bazada «Milliy» (guruh, 540 000) `SelfStudy` bo'lib qolgan edi.
        //    Tur sotuvdan keyin almashsa, kutilayotgan buyurtma tasdiqlanganda
        //    540 000 evaziga 1 oylik obuna ochilardi (pul balansga tushmasdi)
        //    yoki aksincha — o'quvchi to'lagan narsasini olmasdi. Tarixdagi
        //    buyurtma va davrlar ham boshqa ma'noga ega bo'lib qolardi.
        //    Xato tur bilan yaratilgan tarifni o'chirib, yangisini ochish kerak.
        if (plan.Kind != request.Kind || plan.CourseId != request.CourseId)
        {
            var sold = await db.PurchaseOrders.AsNoTracking().AnyAsync(o => o.PlanId == plan.Id, ct)
                || await db.SubscriptionPeriods.AsNoTracking().AnyAsync(p => p.PlanId == plan.Id, ct);

            if (sold)
            {
                throw Invalid(
                    plan.Kind != request.Kind ? nameof(request.Kind) : nameof(request.CourseId),
                    "Bu tarif bo'yicha buyurtma yoki obuna bor — turi va kursi o'zgarmaydi. "
                    + "Yangi tarif yarating va eskisini o'chiring.");
            }
        }

        plan.Name = request.Name.Trim();
        await ApplyAsync(plan, request, ct);
        plan.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return await PlanDtoAsync(plan.Id, ct);
    }

    private async Task ApplyAsync(SubscriptionPlan plan, SaveSubscriptionPlanRequest request, CancellationToken ct)
    {
        if (!await db.Courses.AsNoTracking().AnyAsync(c => c.Id == request.CourseId && c.IsActive, ct))
            throw Invalid(nameof(request.CourseId), "Kurs topilmadi yoki faol emas.");

        plan.CourseId = request.CourseId;
        plan.Months = request.Months;
        plan.Price = request.Price;
        plan.IsActive = request.IsActive;
        plan.Kind = request.Kind;
        plan.IsPublic = request.IsPublic;
        plan.IncludesInteractiveBook = request.IncludesInteractiveBook;

        var description = request.Description?.Trim();
        plan.Description = string.IsNullOrEmpty(description) ? null : description;

        try
        {
            plan.Validate();
        }
        catch (DomainException ex)
        {
            throw Invalid("plan", ex.Message);
        }
    }

    private Task<SubscriptionPlanDto> PlanDtoAsync(long planId, CancellationToken ct) =>
        db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.Id == planId)
            .Select(p => new SubscriptionPlanDto(
                p.Id, p.Name, p.CourseId, p.Course!.Name, p.Months, p.Price, p.IsActive,
                p.Kind, p.Description, p.IsPublic, p.IncludesInteractiveBook))
            .FirstAsync(ct);

    // ================================================================ obuna

    public async Task<SubscriptionPeriodDto> GrantAsync(
        GrantSubscriptionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);

        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PlanId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), request.PlanId);

        if (!plan.IsActive)
            throw new ConflictException("Bu reja o'chirilgan — yangi obuna berilmaydi.");

        if (!Enum.IsDefined(request.Method))
            throw Invalid(nameof(request.Method), "To'lov usuli noto'g'ri (Cash yoki Card).");

        var result = await GrantAndInvalidateAsync(
            request.StudentId, plan, plan.Price, request.Method, Clip(request.Note), actor.Id, ct);

        return await PeriodDtoAsync(result.PeriodId, ct);
    }

    public async Task<SubscriptionGrantResult> GrantForOrderAsync(
        long studentId, long planId, decimal amount, string note, long actorId, CancellationToken ct = default)
    {
        var actor = await EnsureManagerAsync(actorId, ct);

        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), planId);

        // Payme/Click — karta to'lovi.
        return await GrantAndInvalidateAsync(studentId, plan, amount, PaymentMethod.Card, Clip(note), actor.Id, ct);
    }

    public async Task<SubscriptionGrantResult> GrantFromBalanceAsync(
        long studentId, long planId, decimal amount, string note, CancellationToken ct = default)
    {
        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), planId);

        // Usul `null` — balansdan. Amalni o'quvchining o'zi bajaradi, ya'ni jurnalda "kim" ham u.
        return await GrantAndInvalidateAsync(studentId, plan, amount, method: null, Clip(note), studentId, ct);
    }

    private async Task<SubscriptionGrantResult> GrantAndInvalidateAsync(
        long studentId, SubscriptionPlan plan, decimal amount, PaymentMethod? method, string? note,
        long actorId, CancellationToken ct)
    {
        var result = await db.InTransactionAsync(
            token => GrantCoreAsync(studentId, plan, amount, method, note, actorId, token), ct);

        // Commit'dan KEYIN (ichma-ich chaqiruvda chaqiruvchi ham tozalaydi):
        // aks holda parallel so'rov eski holatni qayta keshlab qo'yardi.
        await gating.InvalidateAsync(studentId, ct);
        return result;
    }

    /// <summary>
    /// Davr ochish yadrosi. ⚠️ Tranzaksiya ichida chaqiriladi va hamma narsani
    /// o'zi o'qiydi (qayta urinishda ham to'g'ri ishlasin).
    /// </summary>
    /// <param name="method"><c>null</c> — balansdan to'lanmoqda.</param>
    private async Task<SubscriptionGrantResult> GrantCoreAsync(
        long studentId, SubscriptionPlan plan, decimal amount, PaymentMethod? method, string? note,
        long actorId, CancellationToken ct)
    {
        // ★ GURUH TARIFI OBUNA EMAS: u jonli kursning oylik narxi va pul balansga tushadi.
        if (plan.Kind != PlanKind.SelfStudy)
            throw Invalid("planId", "Bu guruh kursi tarifi — mustaqil o'qish obunasi berilmaydi.");

        var student = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        if (student.Role != UserRole.Student)
            throw Invalid("studentId", "Obuna faqat o'quvchiga beriladi.");
        if (!student.IsActive)
            throw new ConflictException("Foydalanuvchi profili faol emas.");

        var inGroup = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == student.Id
                     && (m.Status == MemberStatus.Active || m.Status == MemberStatus.Paused)
                     && m.Group!.Type != GroupType.Curator)
            .Select(m => m.Group!.Name)
            .FirstOrDefaultAsync(ct);

        if (inGroup is not null)
        {
            throw new ConflictException(
                $"O'quvchi \"{inGroup}\" guruhida. Mustaqil o'qish obunasi faqat guruhsiz "
                + "o'quvchiga beriladi — avval guruhdan chiqaring.");
        }

        var now = clock.GetUtcNow();
        var live = await LivePeriods(student.Id, now).AsTracking().ToListAsync(ct);

        if (live.Exists(p => p.CourseId != plan.CourseId))
        {
            throw new ConflictException(
                "O'quvchining boshqa kursga faol obunasi bor. Bir vaqtda bitta kurs — "
                + "avval o'sha obunani bekor qiling.");
        }

        // 1) PUL AVVAL: balansdan to'lashda mablag' yetmasa — hech narsa o'zgarmasin.
        long? transactionId = null;
        if (amount > 0)
        {
            transactionId = await payments.RecordSubscriptionMoneyAsync(
                new SubscriptionMoneyMove(
                    student.Id,
                    method is null ? PaymentTransactionKind.SubscriptionBalanceUse : PaymentTransactionKind.Subscription,
                    amount,
                    method,
                    LedgerNote(plan.Name, note)),
                actorId, ct);
        }

        // 2) TARIF ALMASHTIRISH (Q-1): hozir BOSHQA tarifdagi davr yurib turibdi.
        var current = live.Find(p => p.StartsAt < now);
        var switching = current is not null && current.PlanId != plan.Id;

        DateTimeOffset startsAt;
        decimal credit = 0m;

        if (switching)
        {
            credit = current!.UnusedValue(now);
            current.CloseAt(now);
            startsAt = now;
        }
        else
        {
            // Uzaytirish: yangi davr mavjudining OXIRIDAN boshlanadi — to'langan kun yo'qolmaydi.
            startsAt = live.Count == 0 ? now : live.Max(p => p.EndsAt);
        }

        var period = new SubscriptionPeriod
        {
            StudentId = student.Id,
            PlanId = plan.Id,
            CourseId = plan.CourseId,
            StartsAt = startsAt,
            EndsAt = startsAt.AddMonths(plan.Months),
            Amount = amount,
            Method = method,
            IncludesInteractiveBook = plan.IncludesInteractiveBook,
            Note = note,
            CreatedById = actorId,
            CreatedAt = now,
        };

        if (switching)
        {
            // ⚠️ OLDINDAN TO'LANGAN KELGUSI DAVRLAR (kam uchraydi) yo'qolmaydi va
            //    pulga ham aylanmaydi: yangi davr OXIRIGA o'z uzunligi bilan suriladi.
            //    Konservativ tanlov — o'quvchi to'lagan vaqtini baribir oladi.
            var cursor = period.EndsAt;
            foreach (var next in live.Where(p => !ReferenceEquals(p, current)))
            {
                var length = next.EndsAt - next.StartsAt;
                next.StartsAt = cursor;
                next.EndsAt = cursor + length;
                next.UpdatedAt = now;
                cursor = next.EndsAt;
            }
        }

        db.SubscriptionPeriods.Add(period);
        await db.SaveChangesAsync(ct);

        // 3) ESKI DAVRNING ISHLATILMAGAN QISMI BALANSGA (Q-1). Buyurtma summasi
        //    o'zgarmaydi — o'quvchi to'laganini to'lagan, farq balansda turadi.
        if (credit > 0)
        {
            await payments.RecordSubscriptionMoneyAsync(
                new SubscriptionMoneyMove(
                    student.Id,
                    PaymentTransactionKind.SubscriptionCredit,
                    credit,
                    null,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Tarif almashtirildi → «{plan.Name}»: obuna #{current!.Id} ning ishlatilmagan qismi")),
                actorId, ct);
        }

        return new SubscriptionGrantResult(period.Id, transactionId, period.StartsAt, period.EndsAt, credit);
    }

    public async Task<SubscriptionPeriodDto> CancelAsync(
        long periodId, CancelSubscriptionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);

        if (!Enum.IsDefined(request.RefundMode))
            throw Invalid(nameof(request.RefundMode), "Qaytarish usuli noto'g'ri (None, ToBalance yoki Cash).");

        var studentId = await db.InTransactionAsync(async token =>
        {
            var period = await db.SubscriptionPeriods.AsTracking().FirstOrDefaultAsync(p => p.Id == periodId, token)
                ?? throw new NotFoundException(nameof(SubscriptionPeriod), periodId);

            if (period.IsCancelled)
                throw new ConflictException("Bu obuna allaqachon bekor qilingan.");

            var now = clock.GetUtcNow();
            var refund = ValidateRefund(period, request, now);

            period.CancelledAt = now;
            period.CancelledById = actor.Id;
            period.CancelReason = Clip(request.Reason);
            period.UpdatedAt = now;
            await db.SaveChangesAsync(token);

            // ★ QAYTARILGAN PUL JURNALDA (Q-5): "bekor qilindi, pul qayerga ketdi?"
            //   savoliga endi javob bor — balansga yoki kassadan naqd.
            if (refund > 0)
            {
                var toBalance = request.RefundMode == SubscriptionRefundMode.ToBalance;
                var note = string.Create(CultureInfo.InvariantCulture, $"Obuna #{period.Id} bekor qilindi")
                    + (period.CancelReason is null ? string.Empty : ": " + period.CancelReason);

                await payments.RecordSubscriptionMoneyAsync(
                    new SubscriptionMoneyMove(
                        period.StudentId,
                        toBalance ? PaymentTransactionKind.SubscriptionCredit : PaymentTransactionKind.SubscriptionRefund,
                        refund,
                        toBalance ? null : PaymentMethod.Cash,
                        note),
                    actor.Id, token);
            }

            return period.StudentId;
        }, ct);

        await gating.InvalidateAsync(studentId, ct);
        return await PeriodDtoAsync(periodId, ct);
    }

    /// <summary>
    /// ⚠️ QAYTARISH CHEGARALARI — pul IKKI MARTA chiqmasin:
    ///   • davr summasidan ko'p qaytarilmaydi;
    ///   • TUGAGAN davr uchun qaytarilmaydi — u to'liq ishlatilgan, yoki tarif
    ///     almashtirishda yopilib, qolgan qismi ALLAQACHON balansga o'tgan.
    /// </summary>
    private static decimal ValidateRefund(SubscriptionPeriod period, CancelSubscriptionRequest request, DateTimeOffset now)
    {
        if (request.RefundMode == SubscriptionRefundMode.None)
            return 0m;

        if (request.RefundAmount is not { } amount || amount <= 0)
            throw Invalid(nameof(request.RefundAmount), "Qaytariladigan summani kiriting.");

        if (amount > period.Amount)
        {
            throw Invalid(
                nameof(request.RefundAmount),
                string.Create(CultureInfo.InvariantCulture,
                    $"Qaytariladigan summa obuna summasidan ({period.Amount:0.##} so'm) oshmasin."));
        }

        if (period.EndsAt <= now)
            throw new ConflictException("Tugagan obuna uchun pul qaytarilmaydi — u ishlatib bo'lingan.");

        return amount;
    }

    // ================================================================ guruhga o'tish

    public async Task<GroupConversionDto> PreviewGroupConversionAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var now = clock.GetUtcNow();
        var live = await LivePeriods(studentId, now).AsNoTracking().ToListAsync(ct);
        return Summarize(live, now);
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// OBUNANI GURUHGA O'TKAZISH (2026-10-29, loyiha egasi qarori Q-2)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 ILGARI (audit P0-3): guruhga qo'shish obuna bor bo'lsa 409 berardi,
    ///   xodimning yagona chorasi — davrni TO'LIQ bekor qilish edi. 20 kun
    ///   (~166 000 so'm) va oldindan to'langan keyingi oy jimgina yo'qolardi.
    ///
    /// ★ Joriy davr HOZIR yopiladi (tarixda "ishlatilgan" qismi qoladi),
    ///   kelgusi davrlar bekor qilinadi; ishlatilmagan qiymat (joriy —
    ///   pro-rata, kelgusi — to'liq) balansga tushadi va har davr uchun
    ///   jurnalda alohida <c>SubscriptionCredit</c> yozuvi qoladi.
    /// ★ Tranzaksiya: chaqiruvchi (guruhga qo'shish) o'z tranzaksiyasini
    ///   ochgan bo'lsa — o'sha ichida, aks holda o'zinikida.
    /// </summary>
    public async Task<GroupConversionDto> ConvertToGroupAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        var actor = await EnsureManagerAsync(actorId, ct);

        var result = await db.InTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow();
            var live = await LivePeriods(studentId, now).AsTracking().ToListAsync(token);
            var summary = Summarize(live, now);

            var credits = new List<(long PeriodId, decimal Amount)>(live.Count);
            foreach (var period in live)
            {
                credits.Add((period.Id, period.UnusedValue(now)));

                if (period.StartsAt < now)
                {
                    period.CloseAt(now);
                }
                else
                {
                    period.CancelledAt = now;
                    period.CancelledById = actor.Id;
                    period.CancelReason = "Guruhga o'tkazildi — pul balansga";
                    period.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync(token);

            foreach (var (periodId, amount) in credits.Where(c => c.Amount > 0))
            {
                await payments.RecordSubscriptionMoneyAsync(
                    new SubscriptionMoneyMove(
                        studentId,
                        PaymentTransactionKind.SubscriptionCredit,
                        amount,
                        null,
                        string.Create(CultureInfo.InvariantCulture,
                            $"Guruhga o'tish: obuna #{periodId} ning ishlatilmagan qismi")),
                    actor.Id, token);
            }

            return summary;
        }, ct);

        await gating.InvalidateAsync(studentId, ct);
        return result;
    }

    /// <summary>Bekor qilinmagan va hali tugamagan davrlar (joriy + oldindan to'langan).</summary>
    private IQueryable<SubscriptionPeriod> LivePeriods(long studentId, DateTimeOffset now) =>
        db.SubscriptionPeriods
            .Where(p => p.StudentId == studentId && p.CancelledAt == null && p.EndsAt > now)
            .OrderBy(p => p.StartsAt);

    private static GroupConversionDto Summarize(List<SubscriptionPeriod> live, DateTimeOffset now)
    {
        if (live.Count == 0)
            return new GroupConversionDto(false, 0, 0m);

        // Kunlar — har davrning QOLGAN qismi yig'indisi (oradagi bo'shliq sanalmaydi).
        var remaining = live.Aggregate(
            TimeSpan.Zero, (sum, p) => sum + (p.EndsAt - (p.StartsAt > now ? p.StartsAt : now)));

        return new GroupConversionDto(
            true,
            (int)Math.Floor(remaining.TotalDays),
            live.Sum(p => p.UnusedValue(now)));
    }

    // ================================================================ o'qish

    public async Task<IReadOnlyList<SubscriptionPeriodDto>> ListForStudentAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        if (studentId != actorId)
            await EnsureManagerAsync(actorId, ct);

        return await PeriodsWithUnusedAsync(PeriodsQuery(p => p.StudentId == studentId), ct);
    }

    public async Task<MySubscriptionDto> GetMineAsync(long studentId, CancellationToken ct = default)
    {
        var history = await PeriodsQuery(p => p.StudentId == studentId).ToListAsync(ct);

        var state = await access.GetStateAsync(studentId, ct);
        if (state is null)
            return new MySubscriptionDto(false, false, null, null, null, null, history);

        // 🔴 GURUH OBUNADAN USTUN (audit, 2026-10-29): Mustaqil muddati tugab,
        //    o'quvchi Milliy'ga o'tib guruhga qo'shilsa, eski (tugagan) davr
        //    tufayli `HasSubscription = true` qaytardik — frontend uni mustaqil
        //    o'quvchi deb ko'rib, Kalendar, chat va jonli dars kartasini
        //    yashirardi. To'lagan guruh o'quvchisi jonli darsni topa olmasdi.
        //    Guruhda bo'lsa — u guruh o'quvchisi; tarix baribir qaytadi.
        var inGroup = await db.GroupMembers.AsNoTracking().AnyAsync(m => m.StudentId == studentId
            && (m.Status == MemberStatus.Active || m.Status == MemberStatus.Paused)
            && m.Group!.IsActive
            && m.Group.Type != GroupType.Curator, ct);
        if (inGroup)
            return new MySubscriptionDto(false, false, null, null, null, null, history);

        var courseName = await db.Courses.AsNoTracking()
            .Where(c => c.Id == state.CourseId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        int? daysLeft = state.IsActive
            ? Math.Max(0, (int)Math.Floor((state.EndsAt - clock.GetUtcNow()).TotalDays))
            : null;

        // ★ TARIF NOMI (audit, 2026-10-29): «Mustaqil» yoki «Mustaqil+» — kartada
        //   ko'rinsin. Hozirgi (bo'lmasa — eng so'nggi) bekor qilinmagan davr.
        var now = clock.GetUtcNow();
        var live = history.Where(p => p.CancelledAt is null).ToList();
        var planName = (live.FirstOrDefault(p => p.StartsAt <= now && now < p.EndsAt)
                        ?? live.MaxBy(p => p.EndsAt))?.PlanName;

        return new MySubscriptionDto(
            true, state.IsActive, state.CourseId, courseName, state.EndsAt, daysLeft, history, planName);
    }

    // ================================================================ yordamchi

    /// <summary>Filtr va tartib PROYEKSIYADAN OLDIN — aks holda EF uni SQL'ga o'gira olmaydi.</summary>
    private IQueryable<SubscriptionPeriodDto> PeriodsQuery(Expression<Func<SubscriptionPeriod, bool>> filter) =>
        db.SubscriptionPeriods.AsNoTracking()
            .Where(filter)
            .OrderByDescending(p => p.StartsAt)
            .Select(p => new SubscriptionPeriodDto(
            p.Id,
            p.StudentId,
            p.Plan != null ? p.Plan.Name : null,
            p.CourseId,
            p.Course!.Name,
            p.StartsAt,
            p.EndsAt,
            p.Amount,
            p.Method,
            p.Note,
            p.CreatedAt,
            p.CancelledAt,
            p.CancelReason,
            p.PlanId,
            p.IncludesInteractiveBook,
            0m));

    /// <summary>
    /// Xodim ro'yxati: har davrning HOZIRGI ishlatilmagan qiymati — bekor qilish
    /// formasidagi "qaytariladigan summa" taklifi. Hisob Domain'dagi AYNI formula.
    /// </summary>
    private async Task<List<SubscriptionPeriodDto>> PeriodsWithUnusedAsync(
        IQueryable<SubscriptionPeriodDto> query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await query.ToListAsync(ct);
        return rows.ConvertAll(r => r with { UnusedValue = UnusedOf(r, now) });
    }

    private static decimal UnusedOf(SubscriptionPeriodDto row, DateTimeOffset now) =>
        new SubscriptionPeriod
        {
            StartsAt = row.StartsAt,
            EndsAt = row.EndsAt,
            Amount = row.Amount,
            CancelledAt = row.CancelledAt,
        }.UnusedValue(now);

    private async Task<SubscriptionPeriodDto> PeriodDtoAsync(long periodId, CancellationToken ct) =>
        (await PeriodsWithUnusedAsync(PeriodsQuery(p => p.Id == periodId), ct))[0];

    private static string LedgerNote(string planName, string? note) =>
        note is null ? $"Obuna: «{planName}»" : $"Obuna: «{planName}» · {note}";

    private async Task<User> EnsureManagerAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive || actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Obunalarni faqat o'quv bo'limi yoki administrator boshqaradi.");

        return actor;
    }

    private static string? Clip(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= SubscriptionPeriod.MaxNoteLength
            ? trimmed
            : trimmed[..SubscriptionPeriod.MaxNoteLength];
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
