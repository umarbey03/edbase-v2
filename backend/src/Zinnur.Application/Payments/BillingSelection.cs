using Zinnur.Domain.Entities;

namespace Zinnur.Application.Payments;

/// <summary>
/// ========================================================================
/// TARIF VA CHEGIRMANI TANLASH — SOF FUNKSIYA
/// ========================================================================
///
/// Bir o'quvchiga bir vaqtda BIR NECHTA tarif va chegirma to'g'ri kelishi
/// mumkin (guruhga atalgani, kursga atalgani, umumiysi). Qaysi biri
/// ishlatilishi — QOIDA, va u shu yerda bitta joyda yozilgan.
///
/// TARTIB: ANIQLIKDAN UMUMIYGA, so'ng eng YANGISI.
///     1) <c>Specificity</c>  — guruh (2) &gt; kurs (1) &gt; umumiy (0)
///     2) kuchga kirish sanasi — kechroq boshlangani ustun (narx tarixi)
///     3) <c>Id</c>           — bir kunda ikkita kiritilgan bo'lsa, oxirgisi
///
/// ★ UCHINCHI QADAM MAJBURIY: usiz bir kunda kiritilgan ikki tarif orasidagi
/// tanlov `OrderBy` barqaror bo'lmagan joyda tasodifiy bo'lib qolardi va
/// bir xil o'quvchiga har oy boshqa narx tushishi mumkin edi.
///
/// ★ CHEGIRMALAR QO'SHILMAYDI (Domain qoidasi): bir nechtasi amalda bo'lsa
/// BITTASI tanlanadi. Qo'shilib ketsa summa manfiyga tushishi mumkin edi.
///
/// NIMA UCHUN SQL <c>ORDER BY</c> EMAS: tanlov mantig'i so'rov ichiga
/// yashiringanda uni test qilib bo'lmasdi va nomzodlar ro'yxati o'zgarganda
/// (masalan yangi filtr qo'shilganda) jimgina buzilardi.
/// </summary>
public static class BillingSelection
{
    /// <summary>
    /// Guruh uchun amaldagi tarifni tanlaydi. Topilmasa <c>null</c> —
    /// bu XATO EMAS, chaqiruvchi buni "tarif sozlanmagan" deb hisobotga yozadi.
    /// </summary>
    /// <param name="candidates">Nomzodlar (odatda faol va sanasi kelgan tariflar).</param>
    /// <param name="on">Qaysi sanaga — hisob oyining BIRINCHI kuni.</param>
    public static Tariff? PickTariff(
        IEnumerable<Tariff> candidates,
        long groupId,
        long? courseId,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(t => t.AppliesTo(groupId, courseId, on))
            .OrderByDescending(t => t.Specificity)
            .ThenByDescending(t => t.ActiveFrom)
            .ThenByDescending(t => t.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// O'quvchining shu guruh uchun amaldagi chegirmasini tanlaydi.
    /// Boshqa guruhga atalgan chegirma NOMZOD EMAS.
    /// </summary>
    public static StudentDiscount? PickDiscount(
        IEnumerable<StudentDiscount> candidates,
        long groupId,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(d => d.IsActiveOn(on) && (d.GroupId is null || d.GroupId == groupId))
            .OrderByDescending(d => d.Specificity)
            .ThenByDescending(d => d.ValidFrom)
            .ThenByDescending(d => d.Id)
            .FirstOrDefault();
    }
}
