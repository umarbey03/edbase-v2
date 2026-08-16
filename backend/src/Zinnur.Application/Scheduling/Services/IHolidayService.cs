using Zinnur.Application.Scheduling.Dtos;

namespace Zinnur.Application.Scheduling.Services;

/// <summary>
/// Bayram kalendari (2026-08-16) — o'quv bo'limi/admin boshqaradi.
/// Ruxsat tekshiruvi SHU servis ICHIDA (controller faqat darvoza,
/// loyihadagi umumiy kelishuv).
/// </summary>
public interface IHolidayService
{
    /// <summary>Kalendar ro'yxati, sana bo'yicha (yangisidan eskisiga).</summary>
    Task<IReadOnlyList<HolidayDto>> ListAsync(long actorId, CancellationToken ct = default);

    /// <summary>
    /// Yangi bayram qo'shadi va o'sha kunga to'g'ri keladigan BARCHA
    /// guruhlarning darsini bekor qiladi (jadval avtomatik oldinga suriladi).
    /// 400 — bo'sh nom; 409 — sana allaqachon bayram sifatida belgilangan.
    /// </summary>
    Task<HolidayImpactDto> CreateAsync(
        CreateHolidayRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// O'chiradi. RETROAKTIV TIKLAMAYDI — allaqachon bekor qilingan
    /// darslar bekor bo'lib qoladi (izoh: <c>Holiday</c> entity).
    /// </summary>
    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);
}
