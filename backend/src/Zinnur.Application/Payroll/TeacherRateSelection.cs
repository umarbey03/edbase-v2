using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payroll;

/// <summary>
/// STAVKANI TANLASH — SOF FUNKSIYA. <see cref="Zinnur.Application.Payments.
/// BillingSelection.PickTariff"/> bilan AYNI naqsh va AYNI sabab: tanlov
/// QOIDA, u yerda bitta joyda, sinovga oson bo'lishi kerak.
///
/// TARTIB: ANIQLIKDAN UMUMIYGA, so'ng eng YANGISI.
///     1) <c>Specificity</c> — aniq xodim (1) &gt; rol standarti (0)
///     2) kuchga kirish sanasi — kechroq boshlangani ustun
///     3) <c>Id</c> — barqaror tie-break
/// </summary>
public static class TeacherRateSelection
{
    /// <summary>
    /// Xodim uchun amaldagi stavkani tanlaydi. Topilmasa <c>null</c> —
    /// bu XATO EMAS, chaqiruvchi "stavka sozlanmagan" deb hisoblaydi.
    /// </summary>
    public static TeacherRate? PickRate(
        IEnumerable<TeacherRate> candidates,
        long userId,
        UserRole role,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(r => r.AppliesTo(userId, role, on))
            .OrderByDescending(r => r.Specificity)
            .ThenByDescending(r => r.ActiveFrom)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }
}
