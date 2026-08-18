using Zinnur.Application.Common.Models;
using Zinnur.Application.Penalties.Dtos;

namespace Zinnur.Application.Penalties.Services;

/// <summary>
/// USTOZ/KURATOR JARIMALARI (2026-08-18).
///
/// ★ IKKI BOSQICHLI OQIM (loyiha egasi qarori): avtomatik aniqlangan
/// jarima <c>Pending</c> bo'lib tug'iladi va oylikka TEGMAYDI. Faqat
/// administrator tasdiqlagach <c>PayrollAdjustment</c> (manfiy summa)
/// yaratiladi. Sabab: kechikishning uzrli holati ko'p (internet uzilishi,
/// texnik nosozlik) va avtomatik ushlab qolish adolatsiz bo'lardi.
///
/// ★ AVTOMATIK ANIQLASH IKKI JOYDA:
///   • KECHIKISH — dars boshlanganda, <c>LiveSessionService.StartAsync</c>
///     ichida (o'sha nuqtada aniq ma'lum, fon vazifasi kutish shart emas);
///   • O'TILMAGAN DARS — fon vazifasida (<c>PenaltyScanJob</c>), chunki
///     "boshlanmadi" degan fakt HODISA emas, vaqt o'tishi bilan yuzaga
///     keladi.
/// </summary>
public interface IPenaltyService
{
    Task<PagedResult<PenaltyRowDto>> ListAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default);

    Task<PenaltySummaryDto> GetSummaryAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<PenaltyByUserDto>> GetByUserAsync(
        PenaltyListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>
    /// OYLIK HISOBOT: bir oyning jarimalari xodim → tur kesimida,
    /// SAHIFALANMAGAN holda. Bekor qilinganlar kirmaydi.
    /// </summary>
    /// <param name="period">Davr <c>YYYY-MM</c>.</param>
    Task<PenaltyReportDto> GetReportAsync(
        string period, long actorId, CancellationToken ct = default);

    Task<PenaltyRowDto> CreateManualAsync(
        CreateManualPenaltyRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Tasdiqlash — oylikka manfiy tuzatma yaratiladi.
    ///
    /// ★ RUXSAT JARIMA TURIGA BOG'LIQ: tizim yozgan jarimani o'quv bo'limi
    /// ham tasdiqlaydi, QO'LDA yozilganini esa faqat administrator (aks
    /// holda bitta odam ham yozib, ham pulga aylantirardi).
    ///
    /// ★ OYLIK DAVRI OCHIQ BO'LISHI SHART (2026-08-18): tasdiqlangan yoki
    /// to'langan davrga tuzatma qo'shilmaydi — oylik panelidagi AYNI
    /// qoida (<c>PayrollService.EnsureDraftAsync</c>).
    /// </summary>
    Task<PenaltyRowDto> ApproveAsync(long id, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Bekor qilish (uzrli sabab yoki xato yozuv). Ruxsat — tasdiqlash
    /// bilan AYNI qoida.
    ///
    /// ★ TASDIQLANGAN JARIMA HAM BEKOR QILINADI (2026-08-18): undan
    /// tug'ilgan oylik tuzatmasi AYNI tranzaksiyada olib tashlanadi.
    /// Ilgari bu yo'l berk edi va xato tasdiqni orqaga qaytarib
    /// bo'lmasdi. Bunda ham oylik davri OCHIQ bo'lishi shart.
    /// </summary>
    Task<PenaltyRowDto> CancelAsync(
        long id, CancelPenaltyRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Dars kech boshlanganini tekshirib, kerak bo'lsa jarima yozadi.
    /// <c>SaveChanges</c> CHAQIRILMAYDI — chaqiruvchi bilan bitta
    /// tranzaksiyada saqlanadi.
    /// </summary>
    Task DetectLateStartAsync(
        Domain.Entities.LiveSession session, CancellationToken ct = default);

    /// <summary>
    /// Vaqti o'tgan, lekin boshlanmagan darslar uchun jarima yozadi
    /// (fon vazifasi). Nechta jarima yozilgani qaytadi.
    /// </summary>
    Task<int> ScanMissedLessonsAsync(CancellationToken ct = default);
}
