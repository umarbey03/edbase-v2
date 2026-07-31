using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// Kanalning UMUMIY tezlik chegarasi (port).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ CHEGARA JARAYON XOTIRASIDA EMAS, REDIS'DA.
///
/// Telegram global chegarasi ~30 xabar/sekund va u BOT uchun, instance
/// uchun emas. API ikki konteynerda ishlaganda har biri o'z hisoblagichini
/// yuritsa, haqiqiy tezlik ikki barobar bo'lib chegarani buzardi —
/// Telegram esa javob sifatida 429 va vaqtincha bloklash beradi.
///
/// Shu sababdan hisoblagich UMUMIY joyda (Redis) turadi. Bu
/// <c>ICacheService</c> dagi chat rate-limit qarorining aynan takrori:
/// u yerda ham MemoryCache "instance soniga ko'paytirilgan spam" bergani
/// uchun rad etilgan.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface IMessageRateLimiter
{
    /// <summary>
    /// Bitta xabar yuborishga ruxsat so'raydi.
    /// </summary>
    /// <returns>
    /// Ruxsat berilsa <see cref="RateLimitDecision.Allowed"/> = <c>true</c>;
    /// aks holda <see cref="RateLimitDecision.RetryAfter"/> da qancha kutish
    /// kerakligi qaytadi (worker shu vaqtga xabarlarni keyinga suradi).
    /// </returns>
    Task<RateLimitDecision> TryAcquireAsync(
        NotificationChannel channel, CancellationToken ct = default);
}
