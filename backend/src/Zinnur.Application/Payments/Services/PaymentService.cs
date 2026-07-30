using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Payments.Services;

/// <summary>
/// ========================================================================
/// MOLIYA SERVISI — PUL BILAN ISHLAYDIGAN YAGONA JOY
/// ========================================================================
///
/// ★ BU SERVIS O'Z HISOBINI YURITMAYDI. Taqsimlash, balansdan yopish,
/// qaytarish va bloklash qoidalari Domain'da
/// (<see cref="PaymentAllocator"/>, <see cref="Payment"/>,
/// <see cref="StudentAccount"/>, <see cref="PaymentBlockPolicy"/>) va
/// 50 ta unit test bilan qoplangan. Bu yerda faqat uchta ish bor:
///
///   1) FAKTLARNI topish (kim, qaysi oy, qancha qarz, qaysi tarif);
///   2) Domain qoidasini CHAQIRISH;
///   3) natijani BITTA tranzaksiyada yozish (jurnal + audit bilan birga).
///
/// ── TRANZAKSIYA CHEGARALARI ────────────────────────────────────────────
///
/// EF Core'da bitta <c>SaveChangesAsync</c> — bitta baza tranzaksiyasi.
/// Shuning uchun "yarim holat" bo'lmasligi kerak bo'lgan hamma narsa
/// BITTA chaqiruvga yig'ilgan:
///
///   • to'lov:      oylarning yangi holati + jurnal + balans + audit  -> 1 ta
///   • kechirim:    oy holati + jurnal + audit                        -> 1 ta
///   • qaytarish:   oylar + balans + jurnal + audit                   -> 1 ta
///   • oy ochish:   (a) yozuvlarni yaratish -> 1 ta,
///                  (b) balansdan yopish    -> 1 ta
///
/// Oy ochishda IKKITA tranzaksiya ATAYLAB: audit va jurnal yozuvlariga
/// yangi yozuvlarning <c>Id</c> lari kerak, ular esa faqat saqlangandan
/// keyin ma'lum bo'ladi. Ikkinchi qadam yiqilsa ham ma'lumot BUZILMAYDI:
/// oylar ochilgan, balans esa hamon o'z joyida turadi — amal IDEMPOTENT
/// bo'lgani uchun qayta chaqirish yetarli (birinchi qadam jimgina o'tadi,
/// ikkinchisi ishini oxiriga yetkazadi).
///
/// ── ESKI TIZIMDAN OLINGAN SABOQ ────────────────────────────────────────
///
/// To'lov kiritishning IKKI xil yo'li bor edi: biri <c>paid_amount</c> ni
/// yangilardi, ikkinchisi jurnalga yozardi. Ikki manba bir-biriga mos
/// kelmay qolardi va qaysi biri to'g'riligini hech kim bilmasdi. Bu yerda
/// yo'l BITTA: <see cref="RecordPaymentAsync"/>.
/// </summary>
public sealed class PaymentService(
    IApplicationDbContext db,
    IFinanceSettingsStore settings,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IPaymentService
{
    // ================================================================= 1) OY OCHISH

    /// <summary>
    /// ========================================================================
    /// OYLIK YOZUVLARNI OCHISH — IDEMPOTENT
    /// ========================================================================
    ///
    /// Har FAOL a'zolikka bitta yozuv: tarif tanlanadi -> chegirma qo'llanadi
    /// -> summa qotiriladi. Summa yozuvga KO'CHIRILADI (havola emas): tarif
    /// keyin ko'tarilsa o'tmishdagi oy o'zgarmasligi kerak.
    ///
    /// ★ TAKROR CHAQIRUV — XATO EMAS. Bazada <c>(StudentId, GroupId, Period)</c>
    /// unikal indeksi bor, lekin unga TAYANMAYMIZ: mavjud yozuvlar avval
    /// o'qib olinadi va jimgina o'tkazib yuboriladi. Indeks — oxirgi himoya
    /// (ikki jarayon bir vaqtda ishga tushsa).
    ///
    /// ★ TARIF TOPILMASA butun amal YIQILMAYDI — o'sha a'zolik o'tkazib
    /// yuboriladi va sababi javobda qaytadi. Aks holda bitta sozlanmagan
    /// guruh butun markazning oyini ochilmay qoldirardi.
    ///
    /// ★ OCHILGANDAN KEYIN BALANSDAN AVTOMATIK YOPISH: oldindan to'lagan
    /// o'quvchi yangi oy ochilishi bilan "qarzdor" bo'lib chiqmasin va
    /// bloklanmasin.
    /// </summary>
    public async Task<OpenPeriodResult> OpenPeriodAsync(
        OpenPeriodRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var period = ParsePeriodOrCurrent(request.Period);
        var periodText = period.ToString();

        // Tarif va chegirma OYNING BIRINCHI KUNIGA qarab tanlanadi: 15-iyulda
        // kiritilgan yangi narx iyul oyiga EMAS, avgustdan tushadi. Aks holda
        // oy o'rtasida narx o'zgarishi allaqachon to'langan oyni qayta
        // hisoblab, qarz "yo'qdan bor" bo'lardi.
        var on = period.FirstDay();
        var now = clock.GetUtcNow();

        var members = await ActiveMembershipsAsync(request.GroupId, ct);

        var existing = await db.Payments.AsNoTracking()
            .Where(p => p.Period == periodText
                     && (request.GroupId == null || p.GroupId == request.GroupId))
            .Select(p => new { p.StudentId, p.GroupId })
            .ToListAsync(ct);

        var already = new HashSet<(long StudentId, long GroupId)>(
            existing.Select(e => (e.StudentId, e.GroupId)));

        var studentIds = members.ConvertAll(m => m.StudentId).Distinct().ToList();

        var tariffs = await db.Tariffs.AsNoTracking()
            .Where(t => t.IsActive && t.ActiveFrom <= on)
            .ToListAsync(ct);

        var discounts = await db.StudentDiscounts.AsNoTracking()
            .Where(d => d.IsActive
                     && studentIds.Contains(d.StudentId)
                     && d.ValidFrom <= on
                     && (d.ValidTo == null || d.ValidTo >= on))
            .ToListAsync(ct);

        var created = new List<Payment>();
        var warnings = new List<string>();
        var skippedNoTariff = 0;
        var alreadyOpen = 0;

        foreach (var member in members)
        {
            if (already.Contains((member.StudentId, member.GroupId)))
            {
                alreadyOpen++;
                continue;
            }

            var tariff = BillingSelection.PickTariff(tariffs, member.GroupId, member.CourseId, on);

            if (tariff is null)
            {
                skippedNoTariff++;

                var warning = string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{member.GroupName}' guruhi uchun {periodText} oyiga amaldagi tarif topilmadi — yozuv ochilmadi.");

                if (!warnings.Contains(warning, StringComparer.Ordinal))
                    warnings.Add(warning);

                continue;
            }

            var discount = BillingSelection.PickDiscount(
                discounts.Where(d => d.StudentId == member.StudentId), member.GroupId, on);

            var (final, cut) = StudentDiscount.ApplyOrNone(discount, tariff.Amount);

            var payment = new Payment
            {
                StudentId = member.StudentId,
                GroupId = member.GroupId,
                Period = periodText,
                BaseAmount = tariff.Amount,
                DiscountAmount = cut,
                Amount = final,
                PaidAmount = 0m,
                Status = PaymentStatus.Due,
                MarkedById = actorId,
                CreatedAt = now,
            };

            // Domain invariantlari (Amount = BaseAmount − DiscountAmount va h.k.)
            // BAZAGA BORISHDAN OLDIN tekshiriladi — xato xabari tushunarli
            // bo'lsin, `CHECK` buzilishi emas.
            payment.Validate();

            db.Payments.Add(payment);
            created.Add(payment);
        }

        // ---- 1-tranzaksiya: yozuvlarni yaratish ---------------------------
        if (created.Count > 0)
            await SaveMoneyAsync(ct);

        // ---- 2-tranzaksiya: audit izi + balansdan avtomatik yopish --------
        //
        // Audit ATAYLAB shu yerda: `EntityId` uchun yangi yozuvning haqiqiy
        // `Id` si kerak, u esa faqat saqlangandan keyin ma'lum bo'ladi.
        // "Oy ochildi" da pul harakati YO'Q, shuning uchun jurnalga emas,
        // faqat auditga tushadi.
        foreach (var payment in created)
        {
            db.PaymentAudits.Add(PaymentAudit.Money(
                PaymentEntity, "create", payment.Id, payment.StudentId,
                0m, payment.Amount, now, actorId, periodText));
        }

        var (balanceApplied, monthsClosed) = await ConsumeBalancesAsync(studentIds, actorId, now, ct);

        if (created.Count > 0 || balanceApplied > 0)
            await SaveMoneyAsync(ct);

        var ids = created.ConvertAll(p => p.Id);

        // ★ TARTIB PROYEKSIYADAN OLDIN: EF DTO konstruktoriga yasalgan
        // obyektning maydoni bo'yicha `ORDER BY` ni TARJIMA QILA OLMAYDI
        // (so'rov butunlay yiqiladi). Shuning uchun tartib DOIM entity
        // ifodalari bo'yicha quriladi.
        IReadOnlyList<PaymentDto> payments = ids.Count == 0
            ? []
            : await ProjectPayments(db.Payments.AsNoTracking()
                    .Where(p => ids.Contains(p.Id))
                    .OrderBy(p => p.Student!.FullName)
                    .ThenBy(p => p.Group!.Name))
                .ToListAsync(ct);

        return new OpenPeriodResult(
            periodText,
            created.Count,
            alreadyOpen,
            skippedNoTariff,
            balanceApplied,
            monthsClosed,
            payments,
            warnings);
    }

    /// <summary>
    /// Balansdagi pulni ochiq qarzlarga sarflaydi (eng eskidan).
    /// Qoida Domain'da — bu yerda faqat yozuvlarni yig'ish, jurnal va audit.
    ///
    /// SAQLAMAYDI: chaqiruvchi audit yozuvlari bilan BIRGA saqlaydi, ya'ni
    /// audit va u tasvirlayotgan o'zgarish bitta tranzaksiyada bo'ladi.
    /// </summary>
    private async Task<(decimal Applied, int MonthsClosed)> ConsumeBalancesAsync(
        List<long> studentIds, long actorId, DateTimeOffset now, CancellationToken ct)
    {
        if (studentIds.Count == 0) return (0m, 0);

        var accounts = await db.StudentAccounts
            .Where(a => studentIds.Contains(a.StudentId) && a.Balance > 0)
            .ToListAsync(ct);

        if (accounts.Count == 0) return (0m, 0);

        var owners = accounts.ConvertAll(a => a.StudentId);

        var open = await db.Payments
            .Where(p => owners.Contains(p.StudentId)
                     && (p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial))
            .ToListAsync(ct);

        var applied = 0m;
        var closed = 0;

        foreach (var account in accounts)
        {
            var mine = open.FindAll(p => p.StudentId == account.StudentId);
            if (mine.Count == 0) continue;

            var before = Snapshot(mine);
            var balanceBefore = account.Balance;

            var result = PaymentAllocator.ConsumeBalance(account, mine, now);
            if (result.Applied <= 0) continue;

            applied += result.Applied;
            closed += result.MonthsClosed;

            // Balansdan yopishda kassaga pul TUSHMAYDI — shuning uchun
            // `Method` va `ReceiptNo` yo'q. Jurnalda alohida `Kind` bilan
            // turadi, aks holda kunlik tushum hisoboti soxta bo'lardi.
            db.PaymentTransactions.Add(NewTransaction(
                account.StudentId, null, PaymentTransactionKind.BalanceUse,
                result.Applied, null, null, "Balansdan avtomatik yopildi", actorId, now));

            db.PaymentAudits.Add(PaymentAudit.Money(
                BalanceEntity, "consume", account.Id, account.StudentId,
                balanceBefore, account.Balance, now, actorId, "Oy ochilgandan keyin"));

            WriteAllocationAudits(mine, before, result.TouchedIds, "allocate", now, actorId, "Balansdan");
        }

        return (applied, closed);
    }

    // ================================================================= 2) TO'LOV

    /// <summary>
    /// ========================================================================
    /// ★ PUL QABUL QILISHNING YAGONA YO'LI
    /// ========================================================================
    ///
    /// Ketma-ketlik (hammasi BITTA <c>SaveChanges</c> da):
    ///   1) eng eski qarzdan boshlab oylar yopiladi (Domain: `Allocate`);
    ///   2) ortib qolgan pul BALANSGA tushadi — yo'qolmaydi;
    ///   3) jurnalga bitta yozuv (kvitansiya raqami bilan);
    ///   4) har o'zgargan oy va balans uchun audit izi.
    ///
    /// ★ "Kamida bitta oy" yaxlitlash YO'Q: 100 000 so'm 540 000 lik oyni
    /// yopmaydi, u <c>Partial</c> bo'lib qoladi va qolgani hamon qarz.
    ///
    /// ★ JURNALGA KELGAN SUMMA yoziladi (taqsimlangan qismi emas): kunlik
    /// kassa hisoboti aynan shu ustunning yig'indisi bo'lishi kerak.
    /// </summary>
    public async Task<PaymentReceiptDto> RecordPaymentAsync(
        RecordPaymentRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        RequirePositive(request.Amount, nameof(request.Amount));
        RequireKnownMethod(request.Method);

        var student = await LoadStudentAsync(request.StudentId, ct);

        if (request.GroupId is { } groupId)
            await EnsureGroupExistsAsync(groupId, ct);

        var now = clock.GetUtcNow();

        var open = await db.Payments
            .Where(p => p.StudentId == student.Id
                     && (p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial)
                     && (request.GroupId == null || p.GroupId == request.GroupId))
            .ToListAsync(ct);

        var before = Snapshot(open);

        // ★ QOIDA DOMAIN'DA: pul qancha bo'lsa, shuncha qarz yopiladi.
        var result = PaymentAllocator.Allocate(open, request.Amount, now);

        foreach (var payment in open.Where(p => result.TouchedIds.Contains(p.Id)))
        {
            payment.Method = request.Method;
            payment.MarkedById = actorId;
            payment.Validate();
        }

        var account = await GetOrCreateAccountAsync(student.Id, now, ct);
        var balanceBefore = account.Balance;

        if (result.Leftover > 0)
        {
            // ★ ORTIQCHA PUL BALANSGA. Eski tizimda u JIM YO'QOLARDI:
            // "3 oyga oldindan to'ladim" degan ota-onaning bolasi keyingi oy
            // qarzdor bo'lib chiqardi.
            account.Deposit(result.Leftover, now);

            db.PaymentAudits.Add(PaymentAudit.Money(
                BalanceEntity, "deposit", account.Id, student.Id,
                balanceBefore, account.Balance, now, actorId, "Ortiqcha to'lov"));
        }

        var receipt = await NextReceiptAsync(now, ct);

        var transaction = NewTransaction(
            student.Id, request.GroupId, PaymentTransactionKind.Payment,
            request.Amount, receipt.ToString(), request.Method, request.Note, actorId, now);

        db.PaymentTransactions.Add(transaction);

        WriteAllocationAudits(open, before, result.TouchedIds, "allocate", now, actorId, receipt.ToString());

        await SaveMoneyAsync(ct);

        var affected = await LoadPaymentsAsync(result.TouchedIds, ct);
        var debt = await PaymentBlockService.DebtOfAsync(db, student.Id, ct);

        return new PaymentReceiptDto(
            transaction.Id,
            transaction.ReceiptNo!,
            student.Id,
            student.FullName,
            request.Amount,
            result.Applied,
            result.Leftover,
            result.MonthsClosed,
            result.MonthsPartial,
            account.Balance,
            debt,
            request.Method,
            affected,
            transaction.CreatedAt);
    }

    // ================================================================= 3) KECHIRIM / QAYTARISH

    /// <summary>
    /// Kechirim: pul olinmaydi, lekin oy qarz bo'lib qolmaydi.
    /// <c>PaidAt</c> QO'YILMAYDI (Domain qoidasi) — kassaga pul tushmagan.
    /// Jurnalda alohida <c>Kind</c> bilan turadi, ya'ni kunlik tushumga
    /// aralashmaydi, lekin "qayerga ketdi" savoliga javob bor.
    /// </summary>
    public async Task<PaymentDto> WaiveAsync(
        long paymentId, WaiveRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException(nameof(Payment), paymentId);

        var now = clock.GetUtcNow();
        var outstanding = payment.Outstanding;
        var statusBefore = payment.Status;

        // To'langan oyni kechirib bo'lmaydi -> DomainException -> 409.
        payment.Waive(now, actorId);

        if (!string.IsNullOrWhiteSpace(request.Reason))
            payment.Note = request.Reason.Trim();

        // Summasi 0 bo'lgan oy (100% chegirma) uchun jurnal yozuvi YOZILMAYDI:
        // jurnal summasi musbat bo'lishi shart (Domain), va nol summali
        // "pul harakati" jurnalni chalg'itardi. Audit izi baribir qoladi.
        if (outstanding > 0)
        {
            db.PaymentTransactions.Add(NewTransaction(
                payment.StudentId, payment.GroupId, PaymentTransactionKind.Waiver,
                outstanding, null, null, request.Reason, actorId, now));
        }

        db.PaymentAudits.Add(new PaymentAudit
        {
            Entity = PaymentEntity,
            Action = "waive",
            EntityId = payment.Id,
            StudentId = payment.StudentId,
            Field = "status",
            OldValue = statusBefore.ToString(),
            NewValue = payment.Status.ToString(),
            Note = request.Reason,
            ActorId = actorId,
            CreatedAt = now,
        });

        await SaveMoneyAsync(ct);

        return await GetPaymentAsync(payment.Id, ct);
    }

    /// <summary>
    /// Pulni ORQAGA qaytaradi. Tartib Domain'da: avval BALANSdan, so'ng eng
    /// YANGI to'langan oylardan (eski oylar yopiq qolsin — aks holda o'quvchi
    /// bir necha oy oldingi "bloklangan" holatiga qaytardi).
    ///
    /// ★ Eski tizimda qaytarish faqat jurnalga yozuv qo'shardi va
    /// <c>payments</c> qatori hamon "to'langan" turardi — tizim pul
    /// qaytarilgandan keyin ham o'quvchini qarzsiz deb bilardi.
    /// </summary>
    public async Task<ReversalDto> ReverseAsync(
        ReversePaymentRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        RequirePositive(request.Amount, nameof(request.Amount));

        var student = await LoadStudentAsync(request.StudentId, ct);
        var now = clock.GetUtcNow();

        var account = await db.StudentAccounts
            .FirstOrDefaultAsync(a => a.StudentId == student.Id, ct);

        var payments = await db.Payments
            .Where(p => p.StudentId == student.Id
                     && p.PaidAmount > 0
                     && p.Status != PaymentStatus.Waived)
            .ToListAsync(ct);

        var before = Snapshot(payments);
        var balanceBefore = account?.Balance ?? 0m;

        var result = PaymentAllocator.Reverse(account, payments, request.Amount, now);

        // HECH NARSA qaytarilmasa — bu amal emas, yanglishish. 409 va aniq
        // sabab: jimgina 200 qaytarish "qaytardim" degan yolg'on tasavvur
        // hosil qilardi. Qisman qaytarish esa XATO EMAS (Domain qarori) —
        // qoldiq `Unreturned` da ko'rsatiladi.
        if (result.Returned <= 0)
        {
            throw new ConflictException(
                "Qaytarish uchun pul topilmadi: bu o'quvchining balansi bo'sh va "
                + "to'langan oyi yo'q. Avval to'lovlar jurnalini tekshiring.");
        }

        foreach (var payment in payments.Where(p => result.TouchedIds.Contains(p.Id)))
        {
            payment.MarkedById = actorId;
            payment.Validate();
        }

        if (result.FromBalance > 0 && account is not null)
        {
            db.PaymentAudits.Add(PaymentAudit.Money(
                BalanceEntity, "withdraw", account.Id, student.Id,
                balanceBefore, account.Balance, now, actorId, request.Reason));
        }

        db.PaymentTransactions.Add(NewTransaction(
            student.Id, null, PaymentTransactionKind.Refund,
            result.Returned, null, null, request.Reason, actorId, now));

        WriteAllocationAudits(payments, before, result.TouchedIds, "reverse", now, actorId, request.Reason);

        await SaveMoneyAsync(ct);

        var affected = await LoadPaymentsAsync(result.TouchedIds, ct);
        var debt = await PaymentBlockService.DebtOfAsync(db, student.Id, ct);

        return new ReversalDto(
            student.Id,
            request.Amount,
            result.Returned,
            result.FromBalance,
            result.FromPayments,
            result.Unreturned,
            account?.Balance ?? 0m,
            debt,
            affected);
    }

    // ================================================================= 4) O'QISH

    public async Task<StudentAccountDto> GetStudentAccountAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanViewStudent(actor, studentId);

        var student = await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId)
            .Select(u => new { u.Id, u.FullName, Exempt = EF.Property<bool>(u, PaymentFields.Exempt) })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        var months = await ProjectPayments(db.Payments.AsNoTracking()
                .Where(p => p.StudentId == studentId)
                .OrderByDescending(p => p.Period)
                .ThenBy(p => p.Group!.Name))
            .ToListAsync(ct);

        var transactions = await ProjectTransactions(db.PaymentTransactions.AsNoTracking()
                .Where(t => t.StudentId == studentId)
                .OrderByDescending(t => t.Id)
                .Take(RecentTransactionCount))
            .ToListAsync(ct);

        var balance = await db.StudentAccounts.AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .Select(a => (decimal?)a.Balance)
            .FirstOrDefaultAsync(ct) ?? 0m;

        return new StudentAccountDto(
            student.Id,
            student.FullName,
            months.Where(m => m.Status is PaymentStatus.Due or PaymentStatus.Partial).Sum(m => m.Outstanding),
            balance,
            student.Exempt,
            months.Count(m => m.Status is PaymentStatus.Due or PaymentStatus.Partial),
            months.Sum(m => m.PaidAmount),
            months,
            transactions);
    }

    public async Task EnsureCanViewStudentAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanViewStudent(actor, studentId);
    }

    public async Task<PagedResult<PaymentTransactionDto>> ListTransactionsAsync(
        long studentId, int page, int pageSize, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanViewStudent(actor, studentId);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var rows = db.PaymentTransactions.AsNoTracking().Where(t => t.StudentId == studentId);

        // Ikkita so'rov (COUNT + sahifa): `Total` bo'lmasa frontend paginator
        // sahifalar sonini bila olmaydi.
        var total = await rows.CountAsync(ct);

        var items = await ProjectTransactions(rows
                .OrderByDescending(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<PaymentTransactionDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<PaymentDto>> ListPaymentsAsync(
        PaymentListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = db.Payments.AsNoTracking();

        // Davr SO'ROVDAN OLDIN normallashtiriladi: "2026-7" ham "2026-07" ga
        // aylanadi, aks holda filtr jimgina hech nima topmasdi.
        if (query.Period is not null)
        {
            var periodText = ParsePeriod(query.Period).ToString();
            rows = rows.Where(p => p.Period == periodText);
        }

        if (query.GroupId is { } groupId)
            rows = rows.Where(p => p.GroupId == groupId);

        if (query.StudentId is { } studentId)
            rows = rows.Where(p => p.StudentId == studentId);

        if (query.Status is { } status)
            rows = rows.Where(p => p.Status == status);

        // Qarzdorlar hisoboti: qisman to'langan oy ham QARZ.
        if (query.OnlyDebt)
        {
            rows = rows.Where(p => (p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial)
                                && p.Amount > p.PaidAmount);
        }

        var total = await rows.CountAsync(ct);

        var items = await ProjectPayments(rows
                .OrderByDescending(p => p.Period)
                .ThenBy(p => p.StudentId)
                .ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<PaymentDto>(items, page, pageSize, total);
    }

    // ================================================================= 5) TARIF

    public async Task<IReadOnlyList<TariffDto>> ListTariffsAsync(
        bool? isActive, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var rows = db.Tariffs.AsNoTracking();

        if (isActive is { } active)
            rows = rows.Where(t => t.IsActive == active);

        // ANIQLIKDAN UMUMIYGA — ro'yxat ham AYNAN tanlov tartibida keladi,
        // shunda xodim "qaysi tarif tushadi" degan savolga ro'yxatga qarab
        // javob bera oladi. Aniqlik ifodasi SQL'da `CASE` ga aylanadi
        // (`Specificity` — hisoblanadigan property, uni EF tarjima qila olmaydi).
        return await ProjectTariffs(rows
                .OrderByDescending(t => t.GroupId != null ? 2 : t.CourseId != null ? 1 : 0)
                .ThenByDescending(t => t.ActiveFrom)
                .ThenByDescending(t => t.Id))
            .ToListAsync(ct);
    }

    public async Task<TariffDto?> ResolveTariffAsync(
        long groupId, DateOnly? onDate, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var group = await db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.CourseId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Group), groupId);

        var date = onDate ?? LocalToday();

        var candidates = await db.Tariffs.AsNoTracking()
            .Where(t => t.IsActive && t.ActiveFrom <= date)
            .ToListAsync(ct);

        var picked = BillingSelection.PickTariff(candidates, group.Id, group.CourseId, date);

        return picked is null ? null : await GetTariffAsync(picked.Id, ct);
    }

    public async Task<TariffDto> CreateTariffAsync(
        CreateTariffRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var tariff = new Tariff
        {
            Name = RequireName(request.Name),
            Amount = request.Amount,
            LessonsCount = request.LessonsCount,
            CourseId = request.CourseId,
            GroupId = request.GroupId,
            ActiveFrom = RequireDate(request.ActiveFrom, nameof(request.ActiveFrom)),
            IsActive = request.IsActive,
        };

        await ValidateTariffAsync(tariff, ct);

        db.Tariffs.Add(tariff);

        db.PaymentAudits.Add(PaymentAudit.Money(
            TariffEntity, "create", null, null, 0m, tariff.Amount,
            clock.GetUtcNow(), actorId, tariff.Name));

        await SaveMoneyAsync(ct);

        return await GetTariffAsync(tariff.Id, ct);
    }

    /// <summary>
    /// ★ <c>PUT</c> — TO'LIQ ALMASHTIRISH. Yuborilmagan maydon standart
    /// qiymatga tushadi (masalan <c>courseId</c> -> <c>null</c>), shuning
    /// uchun klient DOIM to'liq holatni yuboradi. Sana va son maydonlari
    /// alohida tekshiriladi: JSON'da yuborilmasa ular <c>0001-01-01</c> va
    /// <c>0</c> bo'lib kelardi va jimgina yaroqsiz tarif hosil bo'lardi.
    /// </summary>
    public async Task<TariffDto> UpdateTariffAsync(
        long id, UpdateTariffRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var tariff = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Tariff), id);

        var amountBefore = tariff.Amount;

        tariff.Name = RequireName(request.Name);
        tariff.Amount = request.Amount;
        tariff.LessonsCount = request.LessonsCount;
        tariff.CourseId = request.CourseId;
        tariff.GroupId = request.GroupId;
        tariff.ActiveFrom = RequireDate(request.ActiveFrom, nameof(request.ActiveFrom));
        tariff.IsActive = request.IsActive;

        await ValidateTariffAsync(tariff, ct);

        db.PaymentAudits.Add(PaymentAudit.Money(
            TariffEntity, "update", tariff.Id, null, amountBefore, tariff.Amount,
            clock.GetUtcNow(), actorId, tariff.Name));

        await SaveMoneyAsync(ct);

        return await GetTariffAsync(tariff.Id, ct);
    }

    /// <summary>
    /// Tarifni o'chiradi.
    ///
    /// O'CHIRISH XAVFSIZ: oylik yozuvlar tarifga HAVOLA saqlamaydi — summa
    /// yaratilganda `Payment` ga KO'CHIRILGAN. Ya'ni o'tmishdagi hisobotlar
    /// o'zgarmaydi. Narx tarixini saqlab qolish uchun o'chirish o'rniga
    /// <c>isActive = false</c> tavsiya etiladi (bu `PUT` orqali).
    /// </summary>
    public async Task DeleteTariffAsync(long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var tariff = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Tariff), id);

        db.Tariffs.Remove(tariff);

        db.PaymentAudits.Add(PaymentAudit.Money(
            TariffEntity, "delete", tariff.Id, null, tariff.Amount, 0m,
            clock.GetUtcNow(), actorId, tariff.Name));

        await SaveMoneyAsync(ct);
    }

    // ================================================================= 6) CHEGIRMA

    public async Task<IReadOnlyList<StudentDiscountDto>> ListDiscountsAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        await LoadStudentAsync(studentId, ct);

        return await ProjectDiscounts(db.StudentDiscounts.AsNoTracking()
                .Where(d => d.StudentId == studentId)
                .OrderByDescending(d => d.GroupId != null ? 1 : 0)
                .ThenByDescending(d => d.ValidFrom)
                .ThenByDescending(d => d.Id))
            .ToListAsync(ct);
    }

    public async Task<StudentDiscountDto> CreateDiscountAsync(
        long studentId, CreateDiscountRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var student = await LoadStudentAsync(studentId, ct);

        var discount = new StudentDiscount
        {
            StudentId = student.Id,
            GroupId = request.GroupId,
            Kind = request.Kind,
            Value = request.Value,
            ValidFrom = RequireDate(request.ValidFrom, nameof(request.ValidFrom)),
            ValidTo = request.ValidTo,
            IsActive = request.IsActive,
            Reason = request.Reason,
        };

        await ValidateDiscountAsync(discount, ct);

        db.StudentDiscounts.Add(discount);

        db.PaymentAudits.Add(PaymentAudit.Money(
            DiscountEntity, "create", null, student.Id, 0m, discount.Value,
            clock.GetUtcNow(), actorId, discount.Reason));

        await SaveMoneyAsync(ct);

        return await GetDiscountAsync(discount.Id, ct);
    }

    /// <summary>★ <c>PUT</c> — TO'LIQ ALMASHTIRISH (izoh <see cref="UpdateTariffAsync"/> da).</summary>
    public async Task<StudentDiscountDto> UpdateDiscountAsync(
        long studentId, long id, UpdateDiscountRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var discount = await LoadDiscountForManageAsync(studentId, id, ct);
        var valueBefore = discount.Value;

        discount.Kind = request.Kind;
        discount.Value = request.Value;
        discount.ValidFrom = RequireDate(request.ValidFrom, nameof(request.ValidFrom));
        discount.ValidTo = request.ValidTo;
        discount.GroupId = request.GroupId;
        discount.IsActive = request.IsActive;
        discount.Reason = request.Reason;

        await ValidateDiscountAsync(discount, ct);

        db.PaymentAudits.Add(PaymentAudit.Money(
            DiscountEntity, "update", discount.Id, studentId, valueBefore, discount.Value,
            clock.GetUtcNow(), actorId, discount.Reason));

        await SaveMoneyAsync(ct);

        return await GetDiscountAsync(discount.Id, ct);
    }

    public async Task DeleteDiscountAsync(
        long studentId, long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var discount = await LoadDiscountForManageAsync(studentId, id, ct);

        db.StudentDiscounts.Remove(discount);

        db.PaymentAudits.Add(PaymentAudit.Money(
            DiscountEntity, "delete", discount.Id, studentId, discount.Value, 0m,
            clock.GetUtcNow(), actorId, discount.Reason));

        await SaveMoneyAsync(ct);
    }

    // ================================================================= 7) SOZLAMA

    public async Task<FinanceSettingsDto> GetSettingsAsync(
        long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var current = await settings.GetAsync(ct);

        return new FinanceSettingsDto(current.BlockThreshold, current.BlockScope, current.Enforce);
    }

    public async Task<FinanceSettingsDto> UpdateSettingsAsync(
        UpdateFinanceSettingsRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        if (request.BlockThreshold < 0)
            throw Invalid(nameof(request.BlockThreshold), "Chegara manfiy bo'lmaydi.");

        if (!Enum.IsDefined(request.BlockScope))
            throw Invalid(nameof(request.BlockScope), "Qamrov noto'g'ri (None, Video, Live, Platform).");

        var before = await settings.GetAsync(ct);
        var saved = await settings.SaveAsync(request.BlockThreshold, request.BlockScope, actorId, ct);

        db.PaymentAudits.Add(PaymentAudit.Money(
            SettingsEntity, "update", null, null, before.BlockThreshold, saved.BlockThreshold,
            clock.GetUtcNow(), actorId,
            "scope: " + before.BlockScope.ToString() + " -> " + saved.BlockScope.ToString()));

        await SaveMoneyAsync(ct);

        return new FinanceSettingsDto(saved.BlockThreshold, saved.BlockScope, saved.Enforce);
    }

    /// <summary>
    /// Bloklashdan istisno. Maydon SOYA ustunda saqlanadi
    /// (<see cref="PaymentFields.Exempt"/>) — sababi o'sha faylda.
    /// </summary>
    public async Task<PaymentBlockDto> SetExemptAsync(
        long studentId, SetExemptRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var student = await db.Users.FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        if (student.Role != UserRole.Student)
            throw Invalid(nameof(studentId), "Istisno faqat o'quvchiga qo'yiladi.");

        var entry = db.Users.Entry(student).Property<bool>(PaymentFields.Exempt);
        var before = entry.CurrentValue;

        entry.CurrentValue = request.Exempt;

        db.PaymentAudits.Add(new PaymentAudit
        {
            Entity = StudentEntity,
            Action = "exempt",
            EntityId = student.Id,
            StudentId = student.Id,
            Field = PaymentFields.Exempt,
            OldValue = before.ToString(CultureInfo.InvariantCulture),
            NewValue = request.Exempt.ToString(CultureInfo.InvariantCulture),
            Note = request.Reason,
            ActorId = actorId,
            CreatedAt = clock.GetUtcNow(),
        });

        await SaveMoneyAsync(ct);

        var current = await settings.GetAsync(ct);
        var debt = await PaymentBlockService.DebtOfAsync(db, studentId, ct);

        return new PaymentBlockDto(
            studentId,
            Blocked: false,
            debt,
            current.BlockThreshold,
            current.BlockScope,
            RequestedScope: PaymentBlockScope.None,
            request.Exempt,
            current.Enforce,
            Reason: null);
    }

    // ================================================================= RUXSAT

    /// <summary>
    /// ================================================================
    /// MOLIYANING YAGONA RUXSAT QOIDASI
    /// ================================================================
    /// Pulga tegadigan HAR BIR metod shu tekshiruvdan o'tadi.
    ///
    /// USTOZ VA KURATOR MOLIYAGA UMUMAN KIRMAYDI — bu ataylab: dars beruvchi
    /// odam o'z o'quvchisining qarzini "kechirib" yuborishi yoki chegirma
    /// qo'yishi mumkin bo'lmasligi kerak (manfaatlar to'qnashuvi).
    ///
    /// Kontrollerdagi <c>[Authorize(Roles=...)]</c> faqat DARVOZA; haqiqiy
    /// qoida shu yerda, chunki servis fon vazifasidan ham chaqiriladi
    /// (oylik yozuvlarni avtomatik ochish — FAZA 5.5).
    /// </summary>
    private static void EnsureCanManage(User actor)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "Moliya bo'limiga faqat o'quv bo'limi xodimi va administrator kira oladi. "
                + "Ustoz va kurator to'lov ma'lumotlarini ko'ra ham, o'zgartira ham olmaydi.");
        }
    }

    /// <summary>O'quvchi FAQAT o'z hisobini ko'radi; ustoz/kurator umuman ko'rmaydi.</summary>
    private static void EnsureCanViewStudent(User actor, long studentId)
    {
        if (actor.Role is UserRole.Admin or UserRole.Academic) return;

        if (actor.Role is UserRole.Student)
        {
            if (actor.Id == studentId) return;

            throw new ForbiddenException(
                "Siz faqat O'Z to'lov hisobingizni ko'ra olasiz.");
        }

        throw new ForbiddenException(
            "Ustoz va kurator o'quvchilarning to'lov ma'lumotlariga kira olmaydi.");
    }

    // ================================================================= ICHKI YORDAMCHI

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN: kirish tokeni 15 daqiqa yashaydi,
        // ya'ni endi o'chirilgan yoki roli pasaytirilgan xodim eski token
        // bilan pul harakatini bajara olmasligi kerak.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    private async Task<User> LoadStudentAsync(long studentId, CancellationToken ct)
    {
        var student = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        // Moliya faqat O'QUVCHIGA tegishli: ustozga "to'lov" kiritish
        // hisobotlarni buzardi va bu deyarli har doim xato tanlov natijasi.
        if (student.Role != UserRole.Student)
            throw Invalid("studentId", "To'lov faqat 'Student' rolidagi foydalanuvchi uchun kiritiladi.");

        return student;
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

    /// <summary>
    /// Faol a'zoliklar. KURATOR guruhlari ATAYLAB chetda: ularda bevosita
    /// a'zo yo'q (o'quvchilar bog'langan ustoz guruhlaridan keladi), aks
    /// holda bitta o'quvchiga bir oy uchun IKKI marta hisob chiqarilardi.
    /// </summary>
    private async Task<List<Membership>> ActiveMembershipsAsync(long? groupId, CancellationToken ct) =>
        await db.GroupMembers.AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active
                     && m.Group!.IsActive
                     && m.Student!.IsActive
                     && m.Student.Role == UserRole.Student
                     && (groupId == null || m.GroupId == groupId))
            .Select(m => new Membership(
                m.StudentId, m.GroupId, m.Group!.Name, m.Group.CourseId))
            .ToListAsync(ct);

    /// <summary>
    /// Kvitansiya raqami: <c>ZN-2026-07-000123</c>, oy ichida ketma-ket.
    ///
    /// OY — pul TUSHGAN oy (hisob oyi emas): kvitansiya kassa hujjati va u
    /// qaysi kunda berilgani bo'yicha qidiriladi.
    ///
    /// Tartib raqami nol bilan to'ldirilgani uchun satr bo'yicha
    /// <c>ORDER BY ... DESC</c> SON tartibiga teng — "oxirgi raqam" ishonchli
    /// topiladi. Ikki kassir bir vaqtda urinsa unikal indeks ushlaydi va
    /// 409 qaytadi (pul yo'qolmaydi).
    /// </summary>
    private async Task<ReceiptNumber> NextReceiptAsync(DateTimeOffset now, CancellationToken ct)
    {
        var period = BillingPeriod.FromDate(LocalDate(now));
        var prefix = "ZN-" + period.ToString() + "-";

        var last = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.ReceiptNo != null && EF.Functions.Like(t.ReceiptNo!, prefix + "%"))
            .OrderByDescending(t => t.ReceiptNo)
            .Select(t => t.ReceiptNo)
            .FirstOrDefaultAsync(ct);

        return ReceiptNumber.Next(period, last is null ? null : ReceiptNumber.Parse(last));
    }

    private static PaymentTransaction NewTransaction(
        long studentId,
        long? groupId,
        PaymentTransactionKind kind,
        decimal amount,
        string? receiptNo,
        PaymentMethod? method,
        string? note,
        long actorId,
        DateTimeOffset now)
    {
        var transaction = new PaymentTransaction
        {
            StudentId = studentId,
            GroupId = groupId,
            Kind = kind,
            Amount = amount,
            ReceiptNo = receiptNo,
            Method = method,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorId = actorId,
            CreatedAt = now,
        };

        transaction.Validate();
        return transaction;
    }

    /// <summary>O'zgarishdan OLDINGI to'langan summalar — audit uchun.</summary>
    private static Dictionary<long, decimal> Snapshot(IEnumerable<Payment> payments) =>
        payments.ToDictionary(p => p.Id, p => p.PaidAmount);

    /// <summary>Har o'zgargan oy uchun "nimadan-nimaga" izi.</summary>
    private void WriteAllocationAudits(
        IEnumerable<Payment> candidates,
        Dictionary<long, decimal> before,
        IReadOnlyList<long> touched,
        string action,
        DateTimeOffset now,
        long actorId,
        string? note)
    {
        foreach (var payment in candidates.Where(p => touched.Contains(p.Id)))
        {
            db.PaymentAudits.Add(PaymentAudit.Money(
                PaymentEntity,
                action,
                payment.Id,
                payment.StudentId,
                before.TryGetValue(payment.Id, out var old) ? old : 0m,
                payment.PaidAmount,
                now,
                actorId,
                note is null ? payment.Period : payment.Period + " · " + note));
        }
    }

    /// <summary>
    /// ★ PUL YOZILADIGAN YAGONA SAQLASH NUQTASI.
    ///
    ///  • <c>DbUpdateConcurrencyException</c> -> 409. `Payment` va
    ///    `StudentAccount` da Postgres'ning `xmin` tizim ustuni optimistik
    ///    qulf sifatida sozlangan: ikki kassir bir vaqtda bir oyni yopsa,
    ///    ikkinchisining <c>UPDATE</c> i 0 qator o'zgartiradi va shu istisno
    ///    ko'tariladi. "Oxirgi yozgan yutadi" bo'lsa bitta to'lov jimgina
    ///    yo'qolardi.
    ///
    ///  • Boshqa <c>DbUpdateException</c> (unikal indeks: kvitansiya raqami
    ///    yoki <c>(StudentId, GroupId, Period)</c>) -> ham 409, chunki bu
    ///    ham "boshqa so'rov ulgurdi" holati.
    ///
    /// TARTIB MUHIM: <c>DbUpdateConcurrencyException</c> —
    /// <c>DbUpdateException</c> ning avlodi, shuning uchun avval u tutiladi.
    /// </summary>
    private async Task SaveMoneyAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "Bu o'quvchining to'lov yozuvi ayni paytda boshqa xodim tomonidan o'zgartirildi. "
                + "HECH NARSA saqlanmadi — sahifani yangilang va summani qaytadan kiriting.");
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(
                "Yozuv boshqa so'rov bilan to'qnashdi: shu oy uchun yozuv yoki kvitansiya raqami "
                + "allaqachon band. Sahifani yangilab, qaytadan urinib ko'ring.");
        }
    }

    // ---------------------------------------------------------------- tekshiruvlar

    private static void RequirePositive(decimal amount, string field)
    {
        // Domain ham buni tekshiradi, lekin u `DomainException` (409) beradi.
        // Kiruvchi ma'lumot xatosi esa 400 bo'lishi kerak — `errors` ichida
        // maydon nomi bilan, frontend uni maydon tagida ko'rsatadi.
        if (amount <= 0)
            throw Invalid(field, "Summa musbat bo'lishi kerak.");

        if (amount > MaxAmount)
            throw Invalid(field, "Summa juda katta — kiritishda xatolik bo'lgan bo'lishi mumkin.");
    }

    private static void RequireKnownMethod(PaymentMethod method)
    {
        // `JsonStringEnumConverter` RAQAMni ham qabul qiladi va TEKSHIRMAYDI:
        // `method: 7` yuborilsa bazaga ma'nosiz qiymat tushardi va kunlik
        // kassa hisoboti usul bo'yicha bo'linmay qolardi.
        if (!Enum.IsDefined(method))
            throw Invalid("method", "To'lov usuli noto'g'ri (Cash yoki Card).");
    }

    private static string RequireName(string? name)
    {
        var value = name?.Trim();

        if (string.IsNullOrEmpty(value))
            throw Invalid("name", "Nom kiritilishi shart.");

        if (value.Length > MaxNameLength)
            throw Invalid("name", "Nom juda uzun.");

        return value;
    }

    /// <summary>
    /// Sana YUBORILGANIGA ishonch. <c>PUT</c> to'liq almashtirish bo'lgani
    /// uchun yuborilmagan sana <c>0001-01-01</c> bo'lib kelardi va tarif
    /// "har doim amalda" bo'lib qolardi.
    /// </summary>
    private static DateOnly RequireDate(DateOnly value, string field)
    {
        if (value.Year is < MinYear or > MaxYear)
            throw Invalid(field, "Sana kiritilishi shart (masalan 2026-07-01).");

        return value;
    }

    private async Task ValidateTariffAsync(Tariff tariff, CancellationToken ct)
    {
        if (tariff.Amount < 0)
            throw Invalid("amount", "Tarif summasi manfiy bo'lmaydi.");

        if (tariff.Amount > MaxAmount)
            throw Invalid("amount", "Tarif summasi juda katta.");

        if (tariff.LessonsCount is < 1 or > 60)
            throw Invalid("lessonsCount", "Darslar soni 1..60 oralig'ida bo'lishi kerak.");

        if (tariff.CourseId is { } courseId
            && !await db.Courses.AsNoTracking().AnyAsync(c => c.Id == courseId, ct))
        {
            throw new NotFoundException(nameof(Course), courseId);
        }

        if (tariff.GroupId is { } groupId)
            await EnsureGroupExistsAsync(groupId, ct);

        // Domain — oxirgi himoya (yuqoridagi tekshiruvlar bilan bir xil qoida,
        // lekin bu yerdan o'tib ketgan holat uchun).
        tariff.Validate();
    }

    private async Task ValidateDiscountAsync(StudentDiscount discount, CancellationToken ct)
    {
        if (!Enum.IsDefined(discount.Kind))
            throw Invalid("kind", "Chegirma turi noto'g'ri (Percent yoki Amount).");

        if (discount.Value <= 0)
            throw Invalid("value", "Chegirma qiymati musbat bo'lishi kerak.");

        if (discount.Kind == DiscountKind.Percent && discount.Value > 100)
            throw Invalid("value", "Foizli chegirma 100 dan oshmaydi.");

        if (discount.Kind == DiscountKind.Amount && discount.Value > MaxAmount)
            throw Invalid("value", "Chegirma summasi juda katta.");

        if (discount.ValidTo is { } to && to < discount.ValidFrom)
            throw Invalid("validTo", "Tugash sanasi boshlanish sanasidan oldin bo'lmaydi.");

        if (discount.GroupId is { } groupId)
            await EnsureGroupExistsAsync(groupId, ct);

        discount.Validate();
    }

    private async Task EnsureGroupExistsAsync(long groupId, CancellationToken ct)
    {
        if (!await db.Groups.AsNoTracking().AnyAsync(g => g.Id == groupId, ct))
            throw new NotFoundException(nameof(Group), groupId);
    }

    private static BillingPeriod ParsePeriod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid("period", "Davr kiritilishi shart (YYYY-MM).");

        try
        {
            return BillingPeriod.Parse(value.Trim());
        }
        catch (Zinnur.Domain.Exceptions.DomainException ex)
        {
            // Format xatosi — bu KIRUVCHI ma'lumot muammosi (400), biznes
            // qoidasi buzilishi (409) emas.
            throw Invalid("period", ex.Message);
        }
    }

    /// <summary>
    /// <c>null</c> bo'lsa markaz vaqt zonasidagi JORIY oy.
    /// UTC'da 1-avgust 00:30 Toshkentda hali 31-iyul — server UTC'da
    /// ishlagani uchun bu farq oy chegarasida hisobni bir oyga surib
    /// yuborardi.
    /// </summary>
    private BillingPeriod ParsePeriodOrCurrent(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? BillingPeriod.FromDate(LocalToday())
            : ParsePeriod(value);

    private DateOnly LocalToday() => LocalDate(clock.GetUtcNow());

    private DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone.TimeZone).DateTime);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ---------------------------------------------------------------- proyeksiyalar

    private async Task<PaymentDto> GetPaymentAsync(long id, CancellationToken ct) =>
        await ProjectPayments(db.Payments.AsNoTracking().Where(p => p.Id == id))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Payment), id);

    private async Task<IReadOnlyList<PaymentDto>> LoadPaymentsAsync(
        IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        return await ProjectPayments(db.Payments.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .OrderBy(p => p.Period))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Nomlar BAZADA qo'shiladi (JOIN) — aks holda ro'yxatning har qatori
    /// uchun alohida so'rov ketardi (N+1).
    /// </summary>
    private static IQueryable<PaymentDto> ProjectPayments(IQueryable<Payment> rows) =>
        rows.Select(p => new PaymentDto(
            p.Id,
            p.StudentId,
            p.Student!.FullName,
            p.GroupId,
            p.Group!.Name,
            p.Period,
            p.BaseAmount,
            p.DiscountAmount,
            p.Amount,
            p.PaidAmount,
            p.Amount - p.PaidAmount,
            p.Status,
            p.PaidAt,
            p.Method,
            p.Note,
            p.CreatedAt,
            p.UpdatedAt));

    /// <summary>
    /// Jurnal qatori. Xodim ismi ichki so'rov bilan olinadi: `ActorId`
    /// navigatsiyasiz FK (EF konfiguratsiyasidagi ongli tanlov), shuning
    /// uchun `Include` mumkin emas. Ichki so'rov BITTA `SELECT` ichida
    /// qoladi — N+1 hosil bo'lmaydi.
    /// </summary>
    private IQueryable<PaymentTransactionDto> ProjectTransactions(
        IQueryable<PaymentTransaction> rows) =>
        rows.Select(t => new PaymentTransactionDto(
            t.Id,
            t.StudentId,
            t.GroupId,
            t.Group == null ? null : t.Group.Name,
            t.Kind,
            t.Amount,
            t.ReceiptNo,
            t.Method,
            t.Note,
            t.ActorId,
            db.Users.Where(u => u.Id == t.ActorId).Select(u => u.FullName).FirstOrDefault(),
            t.CreatedAt));

    private async Task<TariffDto> GetTariffAsync(long id, CancellationToken ct) =>
        await ProjectTariffs(db.Tariffs.AsNoTracking().Where(t => t.Id == id))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Tariff), id);

    private static IQueryable<TariffDto> ProjectTariffs(IQueryable<Tariff> rows) =>
        rows.Select(t => new TariffDto(
            t.Id,
            t.Name,
            t.Amount,
            t.LessonsCount,
            t.CourseId,
            t.Course == null ? null : t.Course.Name,
            t.GroupId,
            t.Group == null ? null : t.Group.Name,
            t.ActiveFrom,
            t.IsActive,
            t.GroupId != null ? 2 : t.CourseId != null ? 1 : 0,
            t.CreatedAt,
            t.UpdatedAt));

    private async Task<StudentDiscount> LoadDiscountForManageAsync(
        long studentId, long id, CancellationToken ct)
    {
        var discount = await db.StudentDiscounts.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException(nameof(StudentDiscount), id);

        // Chegirma boshqa o'quvchiniki bo'lsa 404 (403 EMAS): manzil noto'g'ri
        // yig'ilgan, resurs shu yo'lda MAVJUD EMAS.
        if (discount.StudentId != studentId)
            throw new NotFoundException(nameof(StudentDiscount), id);

        return discount;
    }

    private async Task<StudentDiscountDto> GetDiscountAsync(long id, CancellationToken ct) =>
        await ProjectDiscounts(db.StudentDiscounts.AsNoTracking().Where(d => d.Id == id))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(StudentDiscount), id);

    private static IQueryable<StudentDiscountDto> ProjectDiscounts(IQueryable<StudentDiscount> rows) =>
        rows.Select(d => new StudentDiscountDto(
            d.Id,
            d.StudentId,
            d.Student!.FullName,
            d.GroupId,
            d.Group == null ? null : d.Group.Name,
            d.Kind,
            d.Value,
            d.ValidFrom,
            d.ValidTo,
            d.IsActive,
            d.Reason,
            d.GroupId != null ? 1 : 0,
            d.CreatedAt,
            d.UpdatedAt));

    // ---------------------------------------------------------------- doimiylar

    private const int MaxPageSize = 100;
    private const int MaxNameLength = 150;
    private const int RecentTransactionCount = 50;
    private const int MinYear = 2000;
    private const int MaxYear = 2200;

    /// <summary>Bir amaldagi eng katta summa — nol qo'shib yuborishdan himoya.</summary>
    private const decimal MaxAmount = 1_000_000_000m;

    // Audit `Entity` qiymatlari — SATR sifatida bir joyda (imlo xatosi bo'lsa
    // audit hisobotida qator yo'qolardi).
    private const string PaymentEntity = "payment";
    private const string BalanceEntity = "balance";
    private const string TariffEntity = "tariff";
    private const string DiscountEntity = "discount";
    private const string SettingsEntity = "settings";
    private const string StudentEntity = "student";

    /// <summary>Oy ochish uchun kerakli a'zolik faktlari.</summary>
    private sealed record Membership(long StudentId, long GroupId, string GroupName, long? CourseId);
}
