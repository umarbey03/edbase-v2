using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payroll;

/// <summary>
/// QOIDANI TANLASH — SOF FUNKSIYA. <see cref="Zinnur.Application.Payments.
/// BillingSelection.PickTariff"/> va eski <c>TeacherRateSelection</c> bilan
/// AYNI naqsh va AYNI sabab: tanlov QOIDA, u bitta joyda va sinovga oson
/// bo'lishi kerak.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ IKKI BOSQICHLI TANLOV — ENG MUHIM QISM
/// ══════════════════════════════════════════════════════════════════════
///   1) Nomzodlar TURI bo'yicha guruhlanadi (<see cref="PayrollRuleKind"/>).
///   2) HAR TURDAN faqat BITTASI — eng aniqi — olinadi.
///
/// Natijada turli turlar QO'SHILADI, bir xil tur esa QO'SHILMAYDI.
/// Sabab <see cref="PayrollRule"/> sinf izohida batafsil: barcha mos
/// qoidani qo'shish umumiy ("barcha ustozlar") va shaxsiy ("Dilnoza uchun")
/// qoidalar birga mos kelganda jimgina ikki baravar to'lovga olib kelardi.
///
/// TARTIB: ANIQLIKDAN UMUMIYGA, so'ng eng YANGISI.
///     1) <c>Specificity</c> — guruh &gt; xodim &gt; kurs &gt; kategoriya/tur
///     2) <c>ActiveFrom</c> — kechroq boshlangani ustun
///     3) <c>Id</c> — barqaror tie-break (aks holda bir xil sanadagi ikki
///        qoidadan qaysi biri chiqishi so'rovdan so'rovga o'zgarardi)
/// </summary>
public static class PayrollRuleSelection
{
    /// <summary>Nomzodlardan eng aniqini tanlaydi. Bo'sh bo'lsa <c>null</c>.</summary>
    public static PayrollRule? PickBest(IEnumerable<PayrollRule> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderByDescending(r => r.Specificity)
            .ThenByDescending(r => r.ActiveFrom)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Har TUR uchun bittadan g'olib qoida. Kalit — <see cref="PayrollRuleKind"/>.
    /// Natija tartibi turning enum qiymati bo'yicha barqaror.
    /// </summary>
    public static IReadOnlyList<PayrollRule> PickPerKind(IEnumerable<PayrollRule> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .GroupBy(r => r.Kind)
            .OrderBy(g => g.Key)
            .Select(g => PickBest(g)!)
            .ToList();
    }

    /// <summary>
    /// XODIM darajasidagi davr qoidalari — oklad va oylik o'quvchi bonusi.
    /// Ular butun oyga tegishli, guruhga bog'lanmaydi
    /// (<c>PayrollRuleKinds.SupportsGroupTargeting</c> izohi), shuning uchun
    /// tanlov faqat xodim + rol + sana bo'yicha ketadi.
    ///
    /// ★ "Tushumdan foiz" BU YERGA KIRMAYDI: u davr qamrovida bo'lsa ham
    /// guruhga bog'lanishi mumkin, ya'ni bitta xodimda bir vaqtda bir
    /// nechta foiz qoidasi ishlashi mumkin (arab tilida 10%, ingliz tilida
    /// 8%). Uni bu yerdan o'tkazish "har turdan bittasi" qoidasi bilan
    /// ikkinchisini jimgina yo'q qilardi — hisob `PayrollCalculator` da
    /// guruhma-guruh yuritiladi.
    /// </summary>
    public static IReadOnlyList<PayrollRule> PickStaffRules(
        IEnumerable<PayrollRule> all, long userId, UserRole role, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(all);

        return PickPerKind(all.Where(r =>
            !PayrollRuleKinds.IsSessionScoped(r.Kind)
            && !PayrollRuleKinds.SupportsGroupTargeting(r.Kind)
            && r.AppliesToStaff(userId, role, on)));
    }
}
