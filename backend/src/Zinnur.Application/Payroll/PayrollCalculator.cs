using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payroll;

/// <summary>
/// ============================================================================
///  OYLIK DVIGATELI — SOF HISOB (2026-09-04)
/// ============================================================================
///
/// Bu sinf BAZAGA ham, VAQTGA ham, so'rov konteksti ham TEGMAYDI: kirish —
/// qoidalar ro'yxati va tayyor kontekst, chiqish — summalar. Sabab
/// <c>PaymentAllocator</c>/<c>BillingSelection</c> dagi bilan AYNI: pul
/// hisobi eng ko'p sinaladigan qism, uni EF so'rovlari bilan aralashtirish
/// har bir holatni sinash uchun butun bazani ko'tarishni talab qilardi.
///
/// Chaqiruvchilar:
///   • <c>LessonAccrualService.ReconcilePayoutAsync</c> — dars yakunlanganda
///     <see cref="ComputeSession"/> natijasini <c>SessionPayout</c> ga MUZLATADI.
///   • <c>PayrollService.GetSummaryAsync/GetDetailAsync</c> —
///     <see cref="ComputePeriod"/> ni davr oxiridagi holat bo'yicha JONLI
///     chaqiradi (oklad/foiz/KPI darsga bog'liq emas).
/// </summary>
public static class PayrollCalculator
{
    /// <summary>Pul DOIM 2 xonagacha, yarmi yuqoriga — loyihadagi umumiy qoida.</summary>
    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // ================================================================= DARS

    /// <summary>
    /// Bitta dars uchun haqni hisoblaydi.
    /// </summary>
    /// <param name="rules">
    /// NOMZODLAR — chaqiruvchi sanaga ko'ra oldindan toraytirishi mumkin,
    /// lekin shart emas: bu yerda baribir <c>AppliesToSession</c> tekshiriladi.
    /// </param>
    public static PayrollSessionResult ComputeSession(
        IEnumerable<PayrollRule> rules, PayrollSessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(ctx);

        // ★ "Oklad ichida" — dars qamrovida HECH NARSA hisoblanmaydi.
        //   `RuleMissing` ATAYLAB `false`: qoida yo'qligi XATO belgisi
        //   (admin sozlamagan), bu esa ONGLI QAROR. Ikkovini bir bayroqqa
        //   yig'ish hisobotda "stavka yo'q" ogohlantirishini soxta chiqarardi.
        if (ctx.Mode == GroupPayrollMode.IncludedInSalary)
            return PayrollSessionResult.Salaried;

        var sessionRules = rules.Where(r => PayrollRuleKinds.IsSessionScoped(r.Kind));

        var candidates = ctx.Mode == GroupPayrollMode.FixedRule && ctx.ForcedRuleId is { } forcedId

            // ★ QO'LDA MAJBURLASH: maqsad tekshirilmaydi (admin ataylab shuni
            //   tanlagan), lekin muddat va shartlar kuchda — `MeetsConditions` izohi.
            ? sessionRules.Where(r =>
                r.Id == forcedId
                && r.IsEffectiveOn(ctx.Date)
                && r.MeetsConditions(ctx.DurationMinutes, ctx.CountFor(r.Basis)))

            : sessionRules.Where(r => r.AppliesToSession(
                ctx.UserId, ctx.Role, ctx.GroupId, ctx.CourseId, ctx.CategoryId,
                ctx.GroupType, ctx.DurationMinutes, ctx.CountFor(r.Basis), ctx.Date));

        var winners = PayrollRuleSelection.PickPerKind(candidates);

        if (winners.Count == 0) return PayrollSessionResult.Missing;

        var lines = new List<PayrollLine>(winners.Count);
        var baseAmount = 0m;
        var bonusAmount = 0m;
        var multiplierApplied = 1m;

        foreach (var rule in winners)
        {
            var count = ctx.CountFor(rule.Basis);

            // ★ USTAMA FAQAT ASOSIY STAVKAGA: dam olish kuni ustozning
            //   MEHNATI qimmatroq, o'quvchi soni emas. Eski
            //   `LessonAccrualService` dagi qoida shu — o'zgartirilmadi.
            var multiplier = ctx.IsWeekendOrHoliday
                ? rule.WeekendHolidayMultiplier ?? 1m
                : 1m;

            switch (rule.Kind)
            {
                case PayrollRuleKind.PerSession:
                {
                    var amount = Money(rule.Amount * multiplier);
                    lines.Add(new PayrollLine(rule.Id, rule.Name, rule.Kind, amount, null));
                    baseAmount += amount;
                    multiplierApplied = Math.Max(multiplierApplied, multiplier);
                    break;
                }

                case PayrollRuleKind.PerAcademicHour:
                {
                    // Bo'luvchi domainda 10..240 bilan cheklangan (`Validate`),
                    // shuning uchun nolga bo'lish MUMKIN EMAS — lekin qoida
                    // bazadan kelgani uchun himoya qoldirildi.
                    var divisor = rule.AcademicHourMinutes > 0 ? rule.AcademicHourMinutes : 45;
                    var hours = Math.Round((decimal)ctx.DurationMinutes / divisor, 2, MidpointRounding.AwayFromZero);
                    var amount = Money(rule.Amount * hours * multiplier);

                    lines.Add(new PayrollLine(
                        rule.Id, rule.Name, rule.Kind, amount,
                        $"{hours:0.##} akademik soat ({ctx.DurationMinutes} daq / {divisor} daq)"));

                    baseAmount += amount;
                    multiplierApplied = Math.Max(multiplierApplied, multiplier);
                    break;
                }

                case PayrollRuleKind.TieredByAttendance:
                {
                    var tier = PickTier(rule.Tiers, count);
                    var amount = Money(tier * multiplier);

                    lines.Add(new PayrollLine(
                        rule.Id, rule.Name, rule.Kind, amount, $"{count} o'quvchi bosqichi"));

                    baseAmount += amount;
                    multiplierApplied = Math.Max(multiplierApplied, multiplier);
                    break;
                }

                case PayrollRuleKind.PerAttendedStudent:
                {
                    var amount = Money(rule.Amount * count);

                    lines.Add(new PayrollLine(
                        rule.Id, rule.Name, rule.Kind, amount, $"{count} o'quvchi"));

                    bonusAmount += amount;
                    break;
                }

                // Davr qamrovidagi turlar bu yerga TUSHMAYDI (yuqorida
                // filtrlangan) — lekin `switch` to'liq bo'lsin, aks holda
                // yangi tur qo'shilganda jimgina 0 chiqardi.
                case PayrollRuleKind.FixedMonthly:
                case PayrollRuleKind.PercentOfRevenue:
                case PayrollRuleKind.MonthlyPerActiveStudent:
                default:
                    break;
            }
        }

        return new PayrollSessionResult(lines, baseAmount, bonusAmount, multiplierApplied, false, false);
    }

    /// <summary>
    /// Bosqichli stavkadan mos summani tanlaydi: o'quvchi soniga TENG yoki
    /// undan KICHIK eng katta bosqich. Mos bosqich yo'q bo'lsa — 0.
    ///
    /// ★ YUQORI CHEGARA YO'Q: eng katta bosqichdan ko'p o'quvchi kelsa,
    /// eng katta bosqich summasi beriladi (HolliHop'dagi bilan ayni).
    /// Aks holda "8 tagacha sozlangan, 9 kishi keldi" holatida ustoz
    /// HECH NARSA olmasdi — eng ko'p mehnat qilgan darsi uchun.
    /// </summary>
    public static decimal PickTier(IEnumerable<PayrollRuleTier> tiers, int studentCount)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        return tiers
            .Where(t => t.StudentCount <= studentCount)
            .OrderByDescending(t => t.StudentCount)
            .Select(t => t.Amount)
            .FirstOrDefault();
    }

    // ================================================================= DAVR

    /// <summary>
    /// Oyga BIR MARTA hisoblanadigan qoidalar: oklad, tushumdan foiz,
    /// oylik o'quvchi bonusi.
    /// </summary>
    public static IReadOnlyList<PayrollLine> ComputePeriod(
        IEnumerable<PayrollRule> rules, PayrollPeriodContext ctx)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(ctx);

        var lines = new List<PayrollLine>();

        // ── 1) XODIM darajasi: oklad va oylik o'quvchi bonusi ──────────────
        foreach (var rule in PayrollRuleSelection.PickStaffRules(rules, ctx.UserId, ctx.Role, ctx.PeriodEnd))
        {
            switch (rule.Kind)
            {
                case PayrollRuleKind.FixedMonthly:
                    lines.Add(new PayrollLine(
                        rule.Id, rule.Name, rule.Kind, Money(rule.Amount), "oylik oklad"));
                    break;

                case PayrollRuleKind.MonthlyPerActiveStudent:
                {
                    // ★ KOEFFITSIENT: `WeightedStudentUnits` — o'quvchilar
                    //   SONI emas, ularning ulushlari yig'indisi (0.5 + 1 + 1
                    //   = 2.5). Sabab `PayrollStudentCoefficient` izohida.
                    var amount = Money(rule.Amount * ctx.WeightedStudentUnits);

                    lines.Add(new PayrollLine(
                        rule.Id, rule.Name, rule.Kind, amount,
                        ctx.WeightedStudentUnits == ctx.ActiveStudentCount
                            ? $"{ctx.ActiveStudentCount} faol o'quvchi"
                            : $"{ctx.ActiveStudentCount} faol o'quvchi → {ctx.WeightedStudentUnits:0.##} ulush"));
                    break;
                }

                default:
                    break;
            }
        }

        // ── 2) TUSHUMDAN FOIZ: guruhma-guruh moslash, keyin qoida bo'yicha yig'ish ──
        //
        // Har guruhning tushumi O'Z qoidasini topadi (arab tilida 10%,
        // ingliz tilida 8% — ikkovi bitta ustozda bo'lishi mumkin), so'ng
        // bir xil qoidaga tushgan guruhlar BITTA qatorga yig'iladi: hisobot
        // "10% × 4 200 000" deb o'qilsin, guruh boshiga o'nta mayda qator
        // bo'lib ketmasin.
        var percentRules = rules
            .Where(r => r.Kind == PayrollRuleKind.PercentOfRevenue)
            .ToList();

        if (percentRules.Count > 0 && ctx.GroupRevenues.Count > 0)
        {
            var byRule = new Dictionary<long, RevenueBucket>();

            foreach (var group in ctx.GroupRevenues)
            {
                if (group.Amount <= 0) continue;

                var match = PayrollRuleSelection.PickBest(percentRules.Where(r => r.AppliesToGroup(
                    ctx.UserId, ctx.Role, group.GroupId, group.CourseId,
                    group.CategoryId, group.GroupType, ctx.PeriodEnd)));

                if (match is null) continue;

                if (byRule.TryGetValue(match.Id, out var bucket))
                    byRule[match.Id] = bucket with { Revenue = bucket.Revenue + group.Amount };
                else
                    byRule[match.Id] = new RevenueBucket(match, group.Amount);
            }

            foreach (var (rule, revenue) in byRule.Values.OrderBy(b => b.Rule.Id))
            {
                // ★ REJA XODIMNING BUTUN OYIGA qaraladi (qoidaning o'z
                //   ulushiga emas): "reja" markazda oylik SHAXSIY maqsad
                //   sifatida tushuniladi. Qoida bo'yicha tekshirilsa,
                //   ustozning yuki bir nechta kursga bo'lingani uchun
                //   hech bir qoida rejaga yetmasdi va bonus hech qachon
                //   ishlamasdi.
                var reachedPlan = rule.PlanAmount is { } plan
                    && rule.PlanReachedPercent is not null
                    && ctx.TotalAccruedRevenue >= plan;

                var percent = reachedPlan ? rule.PlanReachedPercent!.Value : rule.Amount;
                var amount = Money(revenue * percent / 100m);

                lines.Add(new PayrollLine(
                    rule.Id, rule.Name, rule.Kind, amount,
                    reachedPlan
                        ? $"{percent:0.##}% (reja bajarildi) × {revenue:N0}"
                        : $"{percent:0.##}% × {revenue:N0}"));
            }
        }

        return lines;
    }

    /// <summary>Bitta foiz qoidasiga tushgan guruhlarning yig'ma tushumi.</summary>
    private readonly record struct RevenueBucket(PayrollRule Rule, decimal Revenue);
}
