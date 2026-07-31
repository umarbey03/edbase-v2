using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Export;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Payments.Services;

/// <summary>
/// ========================================================================
/// MOLIYA YIG'MA HISOBOTI — FAQAT O'QISH
/// ========================================================================
///
/// ★ BUTUN MA'NOSI — ISHLASH. Bu endpoint borligining yagona sababi: mijoz
/// bu raqamlarni O'ZI hisoblay olmaydi. Buning uchun u minglab to'lov
/// qatorini yuklab olishi kerak bo'lardi. Shuning uchun bu yerdagi HAR
/// SO'ROV quyidagi qoidaga bo'ysunadi:
///
///   ★ AGREGATSIYA SQL TOMONDA. C# ga faqat AGREGAT natija keladi
///     (o'nlab qator: oylar, guruhlar, usullar), xom to'lov qatorlari EMAS.
///
/// EF Core'ning tuzog'i: <c>GroupBy</c> ning proyeksiyasi FAQAT agregat
/// funksiyalardan iborat bo'lmasa (masalan <c>g.ToList()</c>), EF butun
/// jadvalni yuklab, guruhlashni XOTIRADA bajaradi — VA BUNI JIMGINA
/// QILADI. Shu sababli bu fayldagi har <c>GroupBy</c> ning `Select` i
/// faqat <c>Sum</c> / <c>Count</c> dan iborat, "guruh obyekti" hech qayerga
/// uzatilmaydi. Yozilgan SQL integratsiya testida tekshirilgan.
///
/// ── QAYSI INDEKSLAR ISHLAYDI ───────────────────────────────────────────
///
///   • `IX_Payments_Period`               — oy kesimi va davr oylari filtri
///   • `IX_Payments_GroupId_Period`       — guruh kesimi
///   • `IX_PaymentTransactions_CreatedAt` — naqd oqim (sana oralig'i)
///
/// Qarz so'rovlari (`Status IN (Due, Partial)`) uchun mos indeks HOZIRCHA
/// YO'Q: mavjud `IX_Payments_StudentId_Status` `StudentId` dan boshlangani
/// uchun butun markaz kesimiga yaramaydi. Tavsiya: `(Status, Period)`
/// bo'yicha indeks. Migratsiya BU YERDA yaratilmaydi (koordinator batch
/// oxirida bitta migratsiya qiladi).
///
/// ── VAQT ZONASI ────────────────────────────────────────────────────────
///
/// Server UTC'da ishlaydi, hisobot esa MARKAZ kalendari bo'yicha o'qiladi.
/// Kun chegarasi shuning uchun <see cref="LocalWallClock"/> orqali UTC'ga
/// o'giriladi: Toshkentda 1-avgust 00:00 — UTC'da 31-iyul 19:00. To'g'ridan
/// to'g'ri UTC olinsa, 31-iyul kechqurungi to'lov avgust hisobotiga tushib
/// ketardi.
/// </summary>
public sealed class PaymentSummaryService(
    IApplicationDbContext db,
    IPaymentService payments,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IPaymentSummaryService
{
    // ================================================================= 1) HISOBOT

    public async Task<PaymentSummaryDto> GetSummaryAsync(
        PaymentSummaryQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ★ RUXSAT — MOLIYANING YAGONA QOIDASI (ustoz va kurator 403 oladi).
        // Bu yerda ikkinchi nusxa YOZILMAYDI, mavjud qoida chaqiriladi.
        await payments.EnsureCanManageFinanceAsync(actorId, ct);

        var window = ResolveWindow(query);

        // --- 1. NAQD OQIM: jurnal turlari bo'yicha bitta GROUP BY --------
        var cash = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= window.StartUtc && t.CreatedAt < window.EndUtc)
            .GroupBy(t => t.Kind)
            .Select(g => new KindTotal(g.Key, g.Sum(t => t.Amount), g.Count()))
            .ToListAsync(ct);

        var payingStudents = await CashRows(window)
            .Select(t => t.StudentId)
            .Distinct()
            .CountAsync(ct);

        // --- 2. HISOB: davrga tegishli oylar bo'yicha bitta yig'indi ------
        //
        // `GroupBy(_ => 1)` — EF Core'da "butun natijaga bitta agregat qator"
        // ning standart usuli. Bo'sh jadvalda qator umuman qaytmaydi,
        // shuning uchun natija `?? Empty` bilan NOLGA tushiriladi (UI'da
        // `null` "NaN" bo'lib chiqmasin).
        var accrual = await PeriodRows(window.Periods)
            .GroupBy(_ => 1)
            .Select(g => new AccrualTotal(
                g.Sum(p => p.Amount),
                g.Sum(p => p.PaidAmount),
                g.Sum(p => p.DiscountAmount),
                g.Count()))
            .FirstOrDefaultAsync(ct) ?? AccrualTotal.Empty;

        // --- 3. HOLAT: qarz yoshi (u bilan birga JORIY UMUMIY QARZ) -------
        var aging = await BuildAgingAsync(window.AsOf, ct);

        var debtorStudents = await db.Payments.AsNoTracking()
            .Where(Unpaid)
            .Select(p => p.StudentId)
            .Distinct()
            .CountAsync(ct);

        // Balans: `SumAsync` bo'sh to'plamda 0 qaytaradi (EF `COALESCE`
        // qo'shadi), ya'ni `null` xavfi yo'q.
        var studentBalance = await db.StudentAccounts.AsNoTracking()
            .Where(a => a.Balance > 0)
            .SumAsync(a => a.Balance, ct);

        // --- 4. OXIRGI 12 OY va KESIMLAR ---------------------------------
        var months = await BuildMonthsAsync(window.ToPeriod, ct);
        var groups = await BuildGroupsAsync(window.Periods, ct);
        var methods = BuildMethods(await MethodRowsAsync(window, ct));

        var collected = AmountOf(cash, PaymentTransactionKind.Payment);
        var refunded = AmountOf(cash, PaymentTransactionKind.Refund);
        var outstanding = aging.Sum(b => b.Amount);

        var kpi = new PaymentSummaryKpiDto(
            collected,
            refunded,
            collected - refunded,
            AmountOf(cash, PaymentTransactionKind.BalanceUse),
            AmountOf(cash, PaymentTransactionKind.Waiver),
            accrual.Billed,
            accrual.Discounts,
            accrual.Collected,
            Rate(accrual.Collected, accrual.Billed),
            outstanding,
            studentBalance,
            payingStudents,
            debtorStudents,
            CountOf(cash, PaymentTransactionKind.Payment));

        return new PaymentSummaryDto(
            window.From,
            window.To,
            window.FromPeriod,
            window.ToPeriod,
            window.AsOf,
            kpi,
            aging,
            months,
            groups,
            methods);
    }

    // ================================================================= 2) EKSPORT

    /// <summary>
    /// Hisobotni CSV qilib beradi. Bo'limlar (KPI, qarz yoshi, oylar,
    /// guruhlar, usullar) BITTA faylda, bo'sh qator bilan ajratilgan —
    /// eski .xlsx dagi beshta varaqning CSV'dagi muqobili.
    /// </summary>
    public async Task<CsvExport> ExportSummaryCsvAsync(
        PaymentSummaryQuery query, long actorId, CancellationToken ct = default)
    {
        // ★ Ruxsat va hisoblash — AYNI yo'ldan. Ikkinchi hisoblash mantig'i
        // yozilmaydi, aks holda ekrandagi va fayldagi raqam ajralib ketardi.
        var summary = await GetSummaryAsync(query, actorId, ct);

        // `WithExcelHint()` — `sep=,` qatori: Excel faylni lokal "ro'yxat
        // ajratgichi" bo'yicha bo'ladi va uz-UZ/ru-RU da u `;`. Bu qatorsiz
        // butun hisobot BITTA ustunga tushib qolardi.
        var csv = new CsvBuilder(ExportCapacity).WithExcelHint();

        csv.Row("ZIN-NUR — MOLIYA HISOBOTI");
        csv.Row("Davr", Iso(summary.From) + " … " + Iso(summary.To));
        csv.Row("Qarz holati sanasi", Iso(summary.AsOf));
        csv.Blank();

        csv.Row("UMUMIY", "Qiymat");
        csv.Row("Kassaga tushgan (davrda)", CsvBuilder.Money(summary.Kpi.Collected));
        csv.Row("Qaytarilgan", CsvBuilder.Money(summary.Kpi.Refunded));
        csv.Row("Sof tushum", CsvBuilder.Money(summary.Kpi.NetCollected));
        csv.Row("Balansdan yopilgan", CsvBuilder.Money(summary.Kpi.BalanceUsed));
        csv.Row("Kechirilgan", CsvBuilder.Money(summary.Kpi.Waived));
        csv.Row("Rejadagi tushum (davr oylari)", CsvBuilder.Money(summary.Kpi.Billed));
        csv.Row("Chegirmalar", CsvBuilder.Money(summary.Kpi.Discounts));
        csv.Row("Davr oylariga tushgan", CsvBuilder.Money(summary.Kpi.PeriodCollected));
        csv.Row("Yig'ilish foizi, %", CsvBuilder.Percent(summary.Kpi.CollectionRate));
        csv.Row("Joriy umumiy qarz", CsvBuilder.Money(summary.Kpi.Outstanding));
        csv.Row("O'quvchilar balansi", CsvBuilder.Money(summary.Kpi.StudentBalance));
        csv.Row("To'lagan o'quvchilar", CsvBuilder.Count(summary.Kpi.PayingStudents));
        csv.Row("Qarzdor o'quvchilar", CsvBuilder.Count(summary.Kpi.DebtorStudents));
        csv.Row("To'lovlar soni", CsvBuilder.Count(summary.Kpi.PaymentCount));
        csv.Blank();

        csv.Row("QARZ YOSHI (kun)", "Summa", "O'quvchi", "Oylar");
        foreach (var bucket in summary.Aging)
        {
            csv.Row(
                bucket.Bucket,
                CsvBuilder.Money(bucket.Amount),
                CsvBuilder.Count(bucket.Students),
                CsvBuilder.Count(bucket.Months));
        }

        csv.Blank();

        csv.Row("OY", "Reja", "Yig'ilgan", "Qarz", "Kechirilgan", "Chegirma", "Foiz");
        foreach (var month in summary.Months)
        {
            csv.Row(
                month.Period,
                CsvBuilder.Money(month.Billed),
                CsvBuilder.Money(month.Collected),
                CsvBuilder.Money(month.Outstanding),
                CsvBuilder.Money(month.Waived),
                CsvBuilder.Money(month.Discounts),
                CsvBuilder.Percent(month.CollectionRate));
        }

        csv.Blank();

        csv.Row("GURUH", "Reja", "Yig'ilgan", "Qarz", "Kechirilgan", "Foiz", "O'quvchi");
        foreach (var group in summary.Groups)
        {
            csv.Row(
                group.GroupName,
                CsvBuilder.Money(group.Billed),
                CsvBuilder.Money(group.Collected),
                CsvBuilder.Money(group.Outstanding),
                CsvBuilder.Money(group.Waived),
                CsvBuilder.Percent(group.CollectionRate),
                CsvBuilder.Count(group.Students));
        }

        csv.Blank();

        csv.Row("TO'LOV USULI", "Summa", "Soni", "Ulush, %");
        foreach (var method in summary.Methods)
        {
            csv.Row(
                method.MethodName,
                CsvBuilder.Money(method.Amount),
                CsvBuilder.Count(method.Count),
                CsvBuilder.Percent(method.Share));
        }

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"zinnur-moliya-{Iso(summary.From)}_{Iso(summary.To)}.csv");

        return csv.ToExport(fileName);
    }

    // ================================================================= QARZ TA'RIFI

    /// <summary>
    /// ★★ QARZ TA'RIFI — BUTUN HISOBOTDA YAGONA IFODA.
    ///
    /// QARZGA KIRADI:
    ///   • <see cref="PaymentStatus.Due"/>      — pul umuman tushmagan oy;
    ///   • <see cref="PaymentStatus.Partial"/>  — QISMAN to'langan oy,
    ///     QOLGAN QISMI bo'yicha (eski tizim qisman to'lovni "to'langan"
    ///     deb bilardi va markaz jimgina pul yo'qotardi).
    ///
    /// QARZGA KIRMAYDI:
    ///   • <see cref="PaymentStatus.Paid"/>   — yopilgan;
    ///   • <see cref="PaymentStatus.Waived"/> — ★ KECHIRILGAN.
    ///
    /// ★ NIMA UCHUN `Waived` ALOHIDA TA'KIDLANADI: aynan shu yerda topilgan
    /// xato bor edi. Kechirilgan oy hisobot jadvalida "qarz 540 000" bo'lib
    /// turardi, o'quvchining shaxsiy hisobida esa "qarz 0" — natijada kassir
    /// markaz ALLAQACHON kechirgan oy uchun ota-onadan yana pul so'ragan.
    /// Kechirim — bu markazning ONGLI qarori, uni qarz sifatida qayta
    /// ko'rsatish ma'nosiz va uyat.
    ///
    /// `Amount > PaidAmount` sharti qo'shimcha: summasi 0 bo'lgan oy
    /// (100% chegirma) "qarzdor" ro'yxatiga tushib qolmasin.
    ///
    /// ★ NIMA UCHUN <c>Expression</c> MAYDONI: bitta ifoda bir necha
    /// so'rovda QAYTA ISHLATILADI va SQL'ga tarjima bo'ladi. Oddiy metod
    /// bo'lganda EF uni tarjima qila olmasdi; har so'rovda qo'lda
    /// takrorlansa esa bir kuni bir joyda `Waived` unutilardi — ya'ni
    /// tuzatilgan xato jimgina qaytib kelardi.
    /// </summary>
    private static readonly Expression<Func<Payment, bool>> Unpaid = p =>
        (p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial)
        && p.Amount > p.PaidAmount;

    // ================================================================= QARZ YOSHI

    /// <summary>
    /// Qarz yoshi guruhlari. Chegaralar: 0-30 / 31-60 / 61-90 / 90+ kun,
    /// yosh HISOB OYINING BIRINCHI KUNIDAN sanaladi.
    ///
    /// ★ IKKI SO'ROV, IKKALASI HAM SQL TOMONDA:
    ///
    ///   1) HISOB OYI bo'yicha <c>GROUP BY</c> — summa va yozuvlar soni.
    ///      Natija OYLAR soni qadar qator (o'nlab), o'quvchilar soni qadar
    ///      EMAS. Yosh guruhlariga taqsimlash shu KICHIK natija ustida
    ///      bajariladi (bir necha o'nlab element).
    ///
    ///   2) yosh guruhidagi NOYOB o'quvchilar — `COUNT(DISTINCT)` bazada.
    ///      Buni birinchi so'rovdan chiqarib bo'lmaydi: bitta o'quvchining
    ///      AYNI yosh guruhiga tushadigan bir necha oyi bo'lishi mumkin va
    ///      oylar bo'yicha qo'shilsa u bir necha marta sanalardi (eski
    ///      tizimning aynan shu xatosi bor edi). Yosh guruhi bo'sh bo'lsa
    ///      so'rov umuman YUBORILMAYDI.
    /// </summary>
    private async Task<List<DebtAgingBucketDto>> BuildAgingAsync(
        DateOnly asOf, CancellationToken ct)
    {
        var rows = await db.Payments.AsNoTracking()
            .Where(Unpaid)
            .GroupBy(p => p.Period)
            .Select(g => new PeriodDebt(g.Key, g.Sum(p => p.Amount - p.PaidAmount), g.Count()))
            .ToListAsync(ct);

        var amounts = new decimal[DebtAging.Buckets.Count];
        var monthCounts = new int[DebtAging.Buckets.Count];
        var periodsByBucket = new List<string>[DebtAging.Buckets.Count];

        for (var i = 0; i < DebtAging.Buckets.Count; i++)
            periodsByBucket[i] = [];

        foreach (var row in rows)
        {
            // ★ Guruh chegarasi QOIDA — u `DebtAging` da va unit testlar bilan
            // qotirilgan (30/31, 60/61, 90/91 kunlar).
            var index = DebtAging.IndexOf(asOf, row.Period);

            amounts[index] += row.Outstanding;
            monthCounts[index] += row.Months;
            periodsByBucket[index].Add(row.Period);
        }

        var result = new List<DebtAgingBucketDto>(DebtAging.Buckets.Count);

        for (var i = 0; i < DebtAging.Buckets.Count; i++)
        {
            var bucket = DebtAging.Buckets[i];

            var students = periodsByBucket[i].Count == 0
                ? 0
                : await db.Payments.AsNoTracking()
                    .Where(Unpaid)
                    .Where(p => periodsByBucket[i].Contains(p.Period))
                    .Select(p => p.StudentId)
                    .Distinct()
                    .CountAsync(ct);

            result.Add(new DebtAgingBucketDto(
                bucket.Key, bucket.MinDays, bucket.MaxDays, amounts[i], students, monthCounts[i]));
        }

        return result;
    }

    // ================================================================= OYLAR va KESIMLAR

    /// <summary>
    /// Oxirgi 12 hisob oyi, ESKIDAN YANGIGA.
    ///
    /// ★ MA'LUMOTI YO'Q OY HAM QAYTADI (nol qiymatlar bilan): grafikda oy
    /// tushib qolsa dinamika egri chizig'i uzilib, "shu oyda hech nima
    /// bo'lmagan" o'rniga "shu oy yo'q" ko'rinardi.
    /// </summary>
    private async Task<List<PaymentMonthPointDto>> BuildMonthsAsync(
        string toPeriod, CancellationToken ct)
    {
        var last = BillingPeriod.Parse(toPeriod);

        var periods = new List<string>(TrendMonths);
        for (var i = TrendMonths - 1; i >= 0; i--)
            periods.Add(last.AddMonths(-i).ToString());

        var rows = await PeriodRows(periods)
            .GroupBy(p => p.Period)
            .Select(g => new MonthTotal(
                g.Key,
                g.Sum(p => p.Amount),
                g.Sum(p => p.PaidAmount),
                g.Sum(p => (p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial)
                        && p.Amount > p.PaidAmount
                    ? p.Amount - p.PaidAmount
                    : 0m),
                g.Sum(p => p.Status == PaymentStatus.Waived ? p.Amount - p.PaidAmount : 0m),
                g.Sum(p => p.DiscountAmount),
                g.Count()))
            .ToListAsync(ct);

        var byPeriod = rows.ToDictionary(r => r.Period, StringComparer.Ordinal);

        return periods.ConvertAll(period =>
            byPeriod.TryGetValue(period, out var row)
                ? new PaymentMonthPointDto(
                    period,
                    row.Billed,
                    row.Collected,
                    row.Outstanding,
                    row.Waived,
                    row.Discounts,
                    Rate(row.Collected, row.Billed),
                    row.Records)
                : new PaymentMonthPointDto(period, 0m, 0m, 0m, 0m, 0m, 0m, 0));
    }

    /// <summary>
    /// Guruh kesimi — davrga tegishli hisob oylari bo'yicha, qarzi
    /// kattasidan boshlab.
    ///
    /// Noyob o'quvchilar soni ALOHIDA so'rovda: bir o'quvchi guruhning bir
    /// necha oyida turadi, ya'ni oddiy <c>COUNT</c> uni bir necha marta
    /// sanardi. <c>Distinct().GroupBy()</c> ikkalasini ham SQL'da qoldiradi
    /// (<c>SELECT DISTINCT</c> ustidan <c>GROUP BY</c>).
    /// </summary>
    private async Task<List<PaymentGroupSliceDto>> BuildGroupsAsync(
        IReadOnlyList<string> periods, CancellationToken ct)
    {
        var rows = await PeriodRows(periods)
            .GroupBy(p => new { p.GroupId, GroupName = p.Group!.Name })
            .Select(g => new GroupTotal(
                g.Key.GroupId,
                g.Key.GroupName,
                g.Sum(p => p.Amount),
                g.Sum(p => p.PaidAmount),
                g.Sum(p => (p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial)
                        && p.Amount > p.PaidAmount
                    ? p.Amount - p.PaidAmount
                    : 0m),
                g.Sum(p => p.Status == PaymentStatus.Waived ? p.Amount - p.PaidAmount : 0m)))
            .ToListAsync(ct);

        var studentRows = await PeriodRows(periods)
            .Select(p => new { p.GroupId, p.StudentId })
            .Distinct()
            .GroupBy(x => x.GroupId)
            .Select(g => new GroupStudents(g.Key, g.Count()))
            .ToListAsync(ct);

        var students = studentRows.ToDictionary(r => r.GroupId, r => r.Students);

        var slices = rows.ConvertAll(row => new PaymentGroupSliceDto(
            row.GroupId,
            row.GroupName,
            row.Billed,
            row.Collected,
            row.Outstanding,
            row.Waived,
            Rate(row.Collected, row.Billed),
            students.TryGetValue(row.GroupId, out var count) ? count : 0));

        // Tartiblash C# da: qatorlar soni — GURUHLAR soni (o'nlab), shuning
        // uchun bu yerda `ORDER BY` foydasi yo'q, kod esa soddaroq.
        slices.Sort((left, right) =>
        {
            var byDebt = right.Outstanding.CompareTo(left.Outstanding);
            return byDebt != 0 ? byDebt : string.CompareOrdinal(left.GroupName, right.GroupName);
        });

        return slices;
    }

    /// <summary>
    /// To'lov usuli kesimi — davrda kassaga tushgan pul bo'yicha.
    ///
    /// ★ MANBA — JURNAL, `Payments.Method` EMAS: oylik yozuvdagi usul
    /// "oxirgi to'lovniki" bo'lib, bir oy ikki xil usulda yopilsa
    /// birinchisi yo'qolardi. Kassa hisoboti pul harakatiga tayanishi kerak.
    /// </summary>
    private async Task<List<MethodTotal>> MethodRowsAsync(
        SummaryWindow window, CancellationToken ct) =>
        await CashRows(window)
            .GroupBy(t => t.Method)
            .Select(g => new MethodTotal(g.Key, g.Sum(t => t.Amount), g.Count()))
            .ToListAsync(ct);

    private static List<PaymentMethodSliceDto> BuildMethods(
        List<MethodTotal> rows)
    {
        var total = rows.Sum(r => r.Amount);

        var slices = rows.ConvertAll(row => new PaymentMethodSliceDto(
            row.Method,
            MethodName(row.Method),
            row.Amount,
            row.Count,
            Rate(row.Amount, total)));

        slices.Sort((left, right) => right.Amount.CompareTo(left.Amount));

        return slices;
    }

    private static string MethodName(PaymentMethod? method) => method switch
    {
        PaymentMethod.Cash => "Naqd",
        PaymentMethod.Card => "Karta",
        _ => "Ko'rsatilmagan",
    };

    // ================================================================= DAVR

    /// <summary>
    /// So'rovdagi sanalarni hisobot oynasiga aylantiradi.
    ///
    /// ★ O'NG CHEGARA "KIRADI": foydalanuvchi <c>to=2026-07-31</c> deganda
    /// 31-iyulning O'ZI ham kirishini kutadi. Shuning uchun UTC oralig'i
    /// <c>[from 00:00, to+1 kun 00:00)</c> — ya'ni chap chegara kiradi, o'ng
    /// chegara kirmaydi. <c>23:59:59</c> yozilsa o'sha oxirgi soniya ichida
    /// kelgan to'lov IKKI kunning HECH BIRIGA tushmay yo'qolardi.
    /// </summary>
    private SummaryWindow ResolveWindow(PaymentSummaryQuery query)
    {
        var zone = timeZone.TimeZone;
        var today = LocalWallClock.LocalDate(clock.GetUtcNow(), zone);

        var to = query.To ?? today;

        // Standart davr — JORIY OY BOSHIDAN: kassirning kundalik savoli
        // "shu oy qancha yig'ildi".
        var from = query.From ?? BillingPeriod.FromDate(to).FirstDay();

        if (from > to)
        {
            throw Invalid(
                "from",
                "Davr boshi oxiridan keyin bo'lmasligi kerak (from <= to).");
        }

        // ★ CHEGARA: oraliq `Period` satrlari ro'yxatiga aylantiriladi va u
        // SQL'ga `IN (...)` bo'lib tushadi. Chegarasiz "2000-2200" so'rovi
        // ikki ming elementli ro'yxat yasab, so'rovni rejalashtirishning
        // o'zini og'irlashtirardi.
        if (to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            throw Invalid(
                "to",
                "Hisobot oralig'i 5 yildan oshmasligi kerak. Kichikroq davr tanlang.");
        }

        var fromPeriod = BillingPeriod.FromDate(from);
        var toPeriod = BillingPeriod.FromDate(to);

        var periods = new List<string>();
        for (var period = fromPeriod; period <= toPeriod; period = period.AddMonths(1))
            periods.Add(period.ToString());

        return new SummaryWindow(
            from,
            to,
            fromPeriod.ToString(),
            toPeriod.ToString(),
            today,
            periods,
            LocalWallClock.StartOfDayUtc(from, zone),
            LocalWallClock.StartOfDayUtc(to.AddDays(1), zone));
    }

    // ================================================================= ICHKI YORDAMCHI

    /// <summary>Davr oylariga tegishli to'lov yozuvlari (kuzatuvsiz).</summary>
    private IQueryable<Payment> PeriodRows(IReadOnlyList<string> periods) =>
        db.Payments.AsNoTracking().Where(p => periods.Contains(p.Period));

    /// <summary>Davrda kassaga tushgan pul yozuvlari (naqd oqim).</summary>
    private IQueryable<PaymentTransaction> CashRows(SummaryWindow window) =>
        db.PaymentTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= window.StartUtc
                     && t.CreatedAt < window.EndUtc
                     && t.Kind == PaymentTransactionKind.Payment);

    private static decimal AmountOf(IReadOnlyList<KindTotal> rows, PaymentTransactionKind kind) =>
        rows.FirstOrDefault(r => r.Kind == kind)?.Amount ?? 0m;

    private static int CountOf(IReadOnlyList<KindTotal> rows, PaymentTransactionKind kind) =>
        rows.FirstOrDefault(r => r.Kind == kind)?.Count ?? 0;

    /// <summary>
    /// Foiz 0..100. ★ Maxraj nol bo'lsa <c>0</c> qaytadi, <c>null</c> EMAS:
    /// UI'da `null` arifmetikaga tushib "NaN" ko'rsatardi.
    /// </summary>
    private static decimal Rate(decimal part, decimal whole) =>
        whole <= 0m ? 0m : Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero);

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ---------------------------------------------------------------- doimiylar

    /// <summary>Dinamika grafigi uzunligi — eski tizimdagi bilan bir xil.</summary>
    private const int TrendMonths = 12;

    /// <summary>Eng uzun oraliq (kun) — 5 yil. Sabab <see cref="ResolveWindow"/> da.</summary>
    private const int MaxRangeDays = 5 * 366;

    /// <summary>CSV uchun taxminiy hajm (KPI + 4 bo'lim).</summary>
    private const int ExportCapacity = 4096;

    // ---------------------------------------------------------------- so'rov natijalari
    //
    // Bu turlar SQL PROYEKSIYASI: EF ular bo'yicha `SUM`/`COUNT` yozadi.
    // Anonim tur o'rniga nomlangan record — natija metodlar orasida
    // uzatiladi, anonim tur esa buni imkonsiz qilardi.

    private sealed record KindTotal(PaymentTransactionKind Kind, decimal Amount, int Count);

    private sealed record MethodTotal(PaymentMethod? Method, decimal Amount, int Count);

    private sealed record PeriodDebt(string Period, decimal Outstanding, int Months);

    private sealed record GroupStudents(long GroupId, int Students);

    private sealed record AccrualTotal(
        decimal Billed, decimal Collected, decimal Discounts, int Records)
    {
        /// <summary>Bo'sh natija — barcha raqam 0 (UI'da "NaN" chiqmasin).</summary>
        public static AccrualTotal Empty { get; } = new(0m, 0m, 0m, 0);
    }

    private sealed record MonthTotal(
        string Period,
        decimal Billed,
        decimal Collected,
        decimal Outstanding,
        decimal Waived,
        decimal Discounts,
        int Records);

    private sealed record GroupTotal(
        long GroupId,
        string GroupName,
        decimal Billed,
        decimal Collected,
        decimal Outstanding,
        decimal Waived);

    /// <summary>Hisoblab qo'yilgan davr chegaralari — har so'rovda qayta hisoblanmasin.</summary>
    private sealed record SummaryWindow(
        DateOnly From,
        DateOnly To,
        string FromPeriod,
        string ToPeriod,
        DateOnly AsOf,
        IReadOnlyList<string> Periods,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);
}
