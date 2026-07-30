using Zinnur.Application.Scheduling.Dtos;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Scheduling.Services;

/// <summary>
/// Dars jadvali use-case'lari.
///
/// ⚠️ RUXSAT TEKSHIRUVI BU YERDA YO'Q. Bu servis <see cref="Group"/> entity'sini
/// ALLAQACHON yuklab, huquqni tekshirib bo'lgan chaqiruvchidan oladi
/// (<c>GroupService</c>). Shu sabab jadval qoidasi bitta joyda qoladi va
/// ruxsat qoidasi ham bitta joyda (ikki servisda takrorlanmaydi).
///
/// ⚠️ HECH BIR METOD <c>SaveChangesAsync</c> CHAQIRMAYDI. Saqlash qarorini
/// chaqiruvchi qabul qiladi — shu tufayli "guruhni tahrirlash + jadvalni
/// qayta tuzish" BITTA tranzaksiya bo'ladi. Aks holda guruh yangilanib,
/// jadval eski holda qolib ketishi mumkin edi.
/// </summary>
public interface IScheduleService
{
    /// <summary>
    /// YANGI guruh uchun butun kurs jadvalini quradi (o'tgan sanalar ham —
    /// guruh o'rtadan boshlab kiritilgan bo'lsa tarix ham to'liq bo'lsin).
    /// Guruh hali bazaga yozilmagan bo'lishi mumkin: darslar navigatsiya
    /// orqali bog'lanadi va EF ikkalasini bitta <c>SaveChanges</c> da yozadi.
    /// </summary>
    /// <returns>Rejalashtirilgan darslar soni.</returns>
    Task<int> GenerateForNewGroupAsync(Group group, CancellationToken ct = default);

    /// <summary>
    /// Jadvalni QAYTA TUZADI: faqat kelajakdagi va hali boshlanmagan
    /// (<c>Scheduled</c>) darslar o'chiriladi, qolgani saqlanadi.
    /// </summary>
    Task<ScheduleChangeSummary> RegenerateAsync(Group group, CancellationToken ct = default);

    /// <summary>
    /// Kelajakdagi <c>Scheduled</c> darslarda dars hostini O'RNIDA yangilaydi
    /// (dars Id'lari va LiveKit xona nomlari saqlanadi).
    /// </summary>
    /// <returns>O'zgartirilgan darslar soni.</returns>
    Task<int> RetargetHostAsync(Group group, CancellationToken ct = default);

    /// <summary>
    /// Kelajakdagi <c>Scheduled</c> darslarning sarlavhasini O'RNIDA yangilaydi
    /// (guruh nomi o'zgarganda).
    /// </summary>
    /// <returns>O'zgartirilgan darslar soni.</returns>
    Task<int> RenameFutureSessionsAsync(Group group, CancellationToken ct = default);

    /// <summary>Guruh jadvali (ixtiyoriy vaqt oralig'i bilan).</summary>
    Task<IReadOnlyList<ScheduledSessionDto>> ListAsync(
        long groupId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default);
}
