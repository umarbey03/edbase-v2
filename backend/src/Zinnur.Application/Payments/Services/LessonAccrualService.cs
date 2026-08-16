using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payroll;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Payments.Services;

/// <summary>
/// <see cref="ILessonAccrualService"/> ning amalga oshirilishi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NEGA <see cref="PaymentService"/> DAN ALOHIDA
/// ══════════════════════════════════════════════════════════════════════
/// <c>PaymentService</c> allaqachon 1500+ qatordan oshgan va uning hayot
/// sikli boshqa: xodim so'rovlariga javob beradi. Bu servis esa DARS
/// YAKUNLASH oqimining bir qismi — chaqiruvchi <c>LiveSessionService</c>,
/// aktyor esa ko'pincha ustoz (moliyaga umuman ruxsati yo'q xodim!). Shu
/// sabab bu yerda <c>PaymentService.EnsureCanManage</c> QO'LLANMAYDI —
/// bu YOZISH huquqi TEKSHIRUVI emas, DARS HODISASINING oqibati.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ 2026-08-16 — "MUVOFIQLASHTIRISH" (RECONCILE), ENDI FAQAT "BIR MARTA" EMAS
/// ══════════════════════════════════════════════════════════════════════
/// Ilgari bu servis "shu darsga allaqachon yozuv bormi — bo'lsa chiqish"
/// qulfi bilan ISHLAR edi. Amalda buning kamchiligi topildi: agar dars
/// YAKUNLANIB pul YECHILGANDAN keyin xodim uni "bepul" yoki o'quvchini
/// "sababli" deb belgilasa — ESKI kod HECH NARSA qilmasdi (dars allaqachon
/// hisoblangan edi), ya'ni tugma bossa ham pul qaytmasdi.
///
/// Endi bu METOD IDEMPOTENT emas, balki MUVOFIQLASHTIRUVCHI: har chaqiruvda
/// (dars yakunlanganda YOKI keyinroq bepul/sababli bayrog'i o'zgarganda) har
/// o'quvchi uchun "qanday bo'lishi KERAK" (SkipReason) bilan "hozir QANDAY"
/// (bazadagi <see cref="LessonCharge"/>) taqqoslanadi:
///   • FARQ YO'Q -> tegilmaydi (haqiqiy idempotentlik, aynan shu qatorda);
///   • YANGI qarz (avval yechilmagan, endi yechilishi kerak) -> `Payment.
///     Accrue` bilan QO'SHILADI (o'sish — hech qanday cheklovga tegmaydi);
///   • BEKOR QILINADI (avval yechilgan, endi bepul/sababli) -> `Payment.
///     Accrue` bilan AYIRILADI; agar oila ALLAQACHON shuncha (yoki ko'proq)
///     to'lagan bo'lsa, ORTIQCHA qism <see cref="StudentAccount"/> ga
///     QAYTARILADI (izoh: `Payment.Accrue` ning yangi qaytish qiymati).
///
/// ★ STIKER NARX (`LessonCharge.Amount`) BIRINCHI marta yaratilganda
/// QOTIB QOLADI — keyingi reconcile'larda tarifdan QAYTA olinmaydi. Aks
/// holda tarifni keyinroq TAHRIRLASH (yangi qator emas, joyida) o'tgan
/// darslarning narxini ham jimgina o'zgartirib qo'yardi — bu SessionPayout
/// uchun ham AYNI sabab bilan qo'llanadi (pastga qarang).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NEGA XATO ICHKARIDA YUTILADI
/// ══════════════════════════════════════════════════════════════════════
/// <c>Payment</c> jadvali <c>xmin</c> optimistik qulf bilan himoyalangan
/// (`PaymentConfiguration` izohi) — kassa xodimi AYNI paytda shu
/// o'quvchining to'lovini yozsa, bu yerdagi <c>SaveChangesAsync</c>
/// <c>DbUpdateConcurrencyException</c> bilan yiqilishi MUMKIN. Bu METOD
/// dars YAKUNLASH oqimining bir qismi (`LiveSessionService.EndAsync`) va
/// pul bilan bog'liq nozik poyga holati "dars yakunlanmadi" degan
/// NOTO'G'RI taassurot berishi mutlaqo mumkin emas — dars HAQIQATDA
/// yakunlangan. Shu sabab xato bu yerda ICHKARIDA ushlanadi va logga
/// yoziladi.
/// </summary>
public sealed class LessonAccrualService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock,
    ILogger<LessonAccrualService> logger) : ILessonAccrualService
{
    public async Task AccrueForSessionAsync(long sessionId, long actorId, CancellationToken ct = default)
    {
        try
        {
            await AccrueCoreAsync(sessionId, actorId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or Domain.Exceptions.DomainException)
        {
            // ★ FAQAT KUTILGAN xato turlari yutiladi (poyga holati, invariant
            // buzilishi) — dasturlash xatosi (NullReferenceException va h.k.)
            // BEXATO tarqaladi, aks holda haqiqiy bug jimgina "hisoblanmadi"
            // bo'lib qolardi va uni hech kim sezmasdi.
            AccrualLog.Failed(logger, sessionId, ex);
        }
    }

    private async Task AccrueCoreAsync(long sessionId, long actorId, CancellationToken ct)
    {
        var session = await db.LiveSessions.AsNoTracking()
            .Include(s => s.Group)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null || session.Group is not { } group) return;

        // FAQAT YAKUNLANGAN darsda pul harakati bor. Bu METOD ikkita
        // yo'ldan chaqiriladi: `LiveSessionService.EndAsync` (dars ENDI
        // Ended bo'ldi) va bepul/sababli TOGGLE'lari (dars hali `Scheduled`/
        // `Live` bo'lsa — bu yerda umuman ish yo'q, `EndAsync` o'zi
        // yakunlanganda hisoblaydi).
        if (session.Status != SessionStatus.Ended) return;

        var zone = timeZone.TimeZone;
        var lessonDate = LocalWallClock.LocalDate(session.ScheduledStart, zone);
        var periodText = BillingPeriod.FromDate(lessonDate).ToString();
        var now = clock.GetUtcNow();

        var studentOutcome = group.Type == GroupType.Curator
            // KURATOR GURUHIDA TO'G'RIDAN-TO'G'RI A'ZO YO'Q — o'quvchilar
            // bog'langan ustoz guruhlaridan keladi, aks holda ikki marta
            // hisoblanardi. Bu FAQAT o'quvchi hisobiga tegishli — kurator
            // HAQI (pastda) baribir hisoblanadi.
            ? null
            : await ReconcileStudentChargesAsync(session, group, lessonDate, periodText, now, ct);

        var payoutChanged = await ReconcilePayoutAsync(session, lessonDate, now, ct);

        if (studentOutcome is { Changed: true } || payoutChanged)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (studentOutcome is { Changed: true } outcome)
            await WriteStudentAuditsAndCreditsAsync(outcome, sessionId, now, actorId, ct);
    }

    // ================================================================= o'quvchi ulushi

    private async Task<StudentReconcileOutcome> ReconcileStudentChargesAsync(
        LiveSession session, Group group, DateOnly lessonDate, string periodText,
        DateTimeOffset now, CancellationToken ct)
    {
        // `JoinedAt <= ScheduledStart`: oy o'rtasida qo'shilgan o'quvchi
        // OLDINGI darslar uchun to'lamaydi (talab: reja hujjati, Bosqich 3).
        var memberIds = await db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == group.Id
                     && m.Status == MemberStatus.Active
                     && m.JoinedAt <= session.ScheduledStart
                     && m.Student!.IsActive
                     && m.Student.Role == UserRole.Student)
            .Select(m => m.StudentId)
            .ToListAsync(ct);

        if (memberIds.Count == 0) return StudentReconcileOutcome.Empty;

        var excusedIds = await db.Attendances.AsNoTracking()
            .Where(a => a.SessionId == session.Id && memberIds.Contains(a.StudentId) && a.IsExcused)
            .Select(a => a.StudentId)
            .ToListAsync(ct);

        var existingCharges = await db.LessonCharges
            .Where(c => c.SessionId == session.Id)
            .ToDictionaryAsync(c => c.StudentId, ct);

        var tariffs = await db.Tariffs.AsNoTracking()
            .Where(t => t.IsActive && t.ActiveFrom <= lessonDate)
            .ToListAsync(ct);

        var discounts = await db.StudentDiscounts.AsNoTracking()
            .Where(d => d.IsActive
                     && memberIds.Contains(d.StudentId)
                     && d.ValidFrom <= lessonDate
                     && (d.ValidTo == null || d.ValidTo >= lessonDate))
            .ToListAsync(ct);

        var existingPayments = await db.Payments
            .Where(p => p.GroupId == group.Id && p.Period == periodText && memberIds.Contains(p.StudentId))
            .ToListAsync(ct);

        var paymentsByStudent = existingPayments.ToDictionary(p => p.StudentId);

        var audits = new List<StudentAuditEntry>();
        var credits = new List<BalanceCredit>();
        var changed = false;

        foreach (var studentId in memberIds)
        {
            existingCharges.TryGetValue(studentId, out var existingCharge);

            var targetSkip = session.IsFreeLesson
                ? LessonChargeSkipReason.Free
                : excusedIds.Contains(studentId)
                    ? LessonChargeSkipReason.Excused
                    : (LessonChargeSkipReason?)null;

            decimal stickerPrice;
            if (existingCharge is not null)
            {
                // ★ QOTIB QOLGAN — tarifdan QAYTA olinmaydi (sinf izohi).
                stickerPrice = existingCharge.Amount;

                if (existingCharge.SkipReason == targetSkip) continue; // haqiqiy no-op
            }
            else
            {
                var tariff = BillingSelection.PickTariff(tariffs, group.Id, group.CourseId, lessonDate);

                // Tarif topilmasa — butun amal yiqilmaydi, shu o'quvchi
                // jimgina o'tkazib yuboriladi (`OpenPeriodAsync` bilan AYNI qoida).
                if (tariff is null) continue;

                stickerPrice = Math.Round(
                    tariff.Amount / tariff.LessonsCount, 2, MidpointRounding.AwayFromZero);
            }

            if (!paymentsByStudent.TryGetValue(studentId, out var payment))
            {
                payment = new Payment
                {
                    StudentId = studentId,
                    GroupId = group.Id,
                    Period = periodText,
                    BaseAmount = 0m,
                    DiscountAmount = 0m,
                    Amount = 0m,
                    PaidAmount = 0m,
                    Status = PaymentStatus.Due,
                    CreatedAt = now,
                };
                db.Payments.Add(payment);
                paymentsByStudent[studentId] = payment;
            }

            // Kechirilgan oyga tegilmaydi — xodimning qarori qat'iy
            // (`Payment.Accrue` domain qoidasi baribir shu bilan yiqilardi,
            // lekin bu yerda oldindan aniq o'tkazib yuboramiz).
            if (payment.Status == PaymentStatus.Waived) continue;

            var existingGross = existingCharge is { SkipReason: null } ? existingCharge.Amount : 0m;
            var targetGross = targetSkip is null ? stickerPrice : 0m;

            var newBase = payment.BaseAmount - existingGross + targetGross;

            var discount = BillingSelection.PickDiscount(
                discounts.Where(d => d.StudentId == studentId), group.Id, lessonDate);
            var (_, cut) = StudentDiscount.ApplyOrNone(discount, newBase);

            var oldAmount = payment.Amount;
            var excess = payment.Accrue(newBase, cut, now);
            payment.Validate();

            var netContribution = targetSkip is null ? Math.Max(0m, payment.Amount - oldAmount) : 0m;

            if (existingCharge is null)
            {
                var charge = new LessonCharge
                {
                    SessionId = session.Id,
                    StudentId = studentId,
                    GroupId = group.Id,
                    Payment = payment,
                    Amount = stickerPrice,
                    NetAmount = netContribution,
                    SkipReason = targetSkip,
                };
                db.LessonCharges.Add(charge);
            }
            else
            {
                existingCharge.SkipReason = targetSkip;
                existingCharge.NetAmount = netContribution;
                existingCharge.UpdatedAt = now;
            }

            changed = true;
            var action = targetSkip is null ? "accrue" : "reverse";
            audits.Add(new StudentAuditEntry(payment, oldAmount, action));

            if (excess > 0m)
                credits.Add(new BalanceCredit(studentId, group.Id, excess));
        }

        return new StudentReconcileOutcome(changed, audits, credits);
    }

    private async Task WriteStudentAuditsAndCreditsAsync(
        StudentReconcileOutcome outcome, long sessionId, DateTimeOffset now, long actorId, CancellationToken ct)
    {
        // ---- balansga qaytarish (mavjud bo'lsa) — Id'lar endi ma'lum ----
        foreach (var credit in outcome.Credits)
        {
            var account = await GetOrCreateAccountAsync(credit.StudentId, now, ct);
            account.Deposit(credit.Amount, now);

            db.PaymentTransactions.Add(new PaymentTransaction
            {
                StudentId = credit.StudentId,
                GroupId = credit.GroupId,
                Kind = PaymentTransactionKind.LessonReversal,
                Amount = credit.Amount,
                Note = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Dars bekor qilindi (bepul/sababli), sessionId={sessionId}"),
                ActorId = actorId,
                CreatedAt = now,
            });
        }

        // ---- audit izi (2-tranzaksiya — `payment.Id` endi ma'lum) ----
        foreach (var entry in outcome.Audits)
        {
            db.PaymentAudits.Add(PaymentAudit.Money(
                "payment", entry.Action, entry.Payment.Id, entry.Payment.StudentId,
                entry.OldAmount, entry.Payment.Amount, now, actorId,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Dars muvofiqlashtirildi (sessionId={sessionId})")));
        }

        if (outcome.Credits.Count > 0 || outcome.Audits.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<StudentAccount> GetOrCreateAccountAsync(
        long studentId, DateTimeOffset now, CancellationToken ct)
    {
        var account = await db.StudentAccounts.FirstOrDefaultAsync(a => a.StudentId == studentId, ct);
        if (account is not null) return account;

        account = new StudentAccount { StudentId = studentId, Balance = 0m, CreatedAt = now };
        db.StudentAccounts.Add(account);
        return account;
    }

    // ================================================================= ustoz/kurator haqi

    /// <summary>
    /// Host haqini muvofiqlashtiradi. <see cref="SessionPayout.SessionRate"/>/
    /// <see cref="SessionPayout.BonusAmount"/>/<see cref="SessionPayout.
    /// AttendedStudents"/>/<see cref="SessionPayout.RateMissing"/> — FAQAT
    /// BIRINCHI yaratilganda hisoblanadi va keyin QOTIB QOLADI (sinf
    /// izohi). Keyingi chaqiruvlarda FAQAT <see cref="SessionPayout.
    /// Excluded"/> yangilanadi — bepul dars + "ustoz ham haq olmasin"
    /// bayrog'i o'zgarsa.
    /// </summary>
    private async Task<bool> ReconcilePayoutAsync(
        LiveSession session, DateOnly lessonDate, DateTimeOffset now, CancellationToken ct)
    {
        if (session.HostId is not { } hostId) return false;

        var targetExcluded = session.IsFreeLesson && session.PayrollExcluded;

        var existing = await db.SessionPayouts.FirstOrDefaultAsync(p => p.SessionId == session.Id, ct);

        if (existing is not null)
        {
            if (existing.Excluded == targetExcluded) return false;
            existing.Excluded = targetExcluded;
            existing.UpdatedAt = now;
            return true;
        }

        var role = session.Type == SessionType.Teacher ? UserRole.Teacher : UserRole.Assistant;

        var rates = await db.TeacherRates.AsNoTracking()
            .Where(r => r.IsActive && r.ActiveFrom <= lessonDate)
            .ToListAsync(ct);

        var rate = TeacherRateSelection.PickRate(rates, hostId, role, lessonDate);

        var attended = await db.Attendances.AsNoTracking()
            .CountAsync(a => a.SessionId == session.Id && a.Status != AttendanceStatus.Absent, ct);

        db.SessionPayouts.Add(new SessionPayout
        {
            SessionId = session.Id,
            UserId = hostId,
            Role = role,
            AttendedStudents = attended,
            SessionRate = rate?.PerSessionRate ?? 0m,
            BonusAmount = attended * (rate?.PerStudentBonusRate ?? 0m),
            RateMissing = rate is null,
            Excluded = targetExcluded,
        });

        return true;
    }

    // ================================================================= yordamchi turlar

    private readonly record struct StudentAuditEntry(Payment Payment, decimal OldAmount, string Action);

    private readonly record struct BalanceCredit(long StudentId, long GroupId, decimal Amount);

    private sealed class StudentReconcileOutcome(
        bool changed, List<StudentAuditEntry> audits, List<BalanceCredit> credits)
    {
        public static readonly StudentReconcileOutcome Empty = new(false, [], []);

        public bool Changed { get; } = changed;
        public List<StudentAuditEntry> Audits { get; } = audits;
        public List<BalanceCredit> Credits { get; } = credits;
    }
}

/// <summary>Manba-generatsiyali log metodlari (CA1848).</summary>
internal static partial class AccrualLog
{
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Error,
        Message = "Dars ulushini hisoblash muvaffaqiyatsiz: sessionId={SessionId}. "
                + "Dars YAKUNLANGAN, lekin bu dars uchun to'lov hisoblanmagan bo'lishi mumkin.")]
    internal static partial void Failed(ILogger logger, long sessionId, Exception ex);
}
