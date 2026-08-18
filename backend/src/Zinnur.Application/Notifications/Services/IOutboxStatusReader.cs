using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// NAVBAT HOLATINI O'QISH — TOR PORT (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NEGA ALOHIDA PORT, NEGA `IApplicationDbContext` GA `MessageOutbox`
/// QO'SHILMADI: navbat jadvali ATAYLAB Application qatlamiga ochilmagan
/// (`IApplicationDbContext` izohi). Uni ochish har use-case'ga navbatni
/// to'g'ridan-to'g'ri o'zgartirish imkonini berardi va "yuborish faqat
/// outbox orqali" degan qoida buzilardi.
///
/// Bu port esa FAQAT O'QIYDI va faqat BITTA savolga javob beradi:
/// "shu kalitli xabar yetkazildimi?".
///
/// ★ NIMA UCHUN KERAK BO'LDI: kelmagan o'quvchiga yuborilgan xabar
/// ro'yxatida "yuborildi" deb yozish yetarli emas — Telegram uni rad
/// etgan bo'lishi mumkin (bot bloklangan, chat topilmadi). Kurator buni
/// ko'rmasa, xabar bormaganini bilmay qolardi va o'quvchi "aytishmadi"
/// deb aybdor bo'lardi.
/// </summary>
public interface IOutboxStatusReader
{
    /// <param name="idempotencyKeys">Qidiriladigan kalitlar.</param>
    /// <returns>
    /// Kalit → holat xaritasi. Topilmagan kalitlar xaritada BO'LMAYDI
    /// (chaqiruvchi buni "hali navbatga qo'yilmagan" deb talqin qiladi).
    /// </returns>
    Task<IReadOnlyDictionary<string, OutboxStatusDto>> GetStatusesAsync(
        IReadOnlyCollection<string> idempotencyKeys, CancellationToken ct = default);
}
