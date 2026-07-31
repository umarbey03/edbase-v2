using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// Navbat omborining PORTI (worker tomoni).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ KO'P INSTANCE: bir qatorni IKKI worker OLMASLIGI SHART
///
/// <see cref="ClaimAsync"/> ning implementatsiyasi
/// <c>FOR UPDATE SKIP LOCKED</c> ishlatadi: ikkinchi worker qulflangan
/// qatorni KUTMAYDI, balki uni o'tkazib yuborib keyingisini oladi.
/// Shu tufayli ikki instance parallel ishlaganda ham har xabar aynan bir
/// marta olinadi va gorizontal masshtablash uchun qo'shimcha "leader lock"
/// kerak emas.
///
/// ★ "Ko'rinmaslik muddati" (lease): olingan qator darhol kelajakka
/// suriladi (<c>NextAttemptAt = now + lease</c>). Worker qulasa qator
/// muddat o'tgach O'ZI qaytib ko'rinadi — hech kim uni qo'lda
/// "tiklashi" shart emas.
///
/// ★ URINISH HISOBLAGICHI OLISH PAYTIDA EMAS, YIQILGANDA oshadi. Sabab:
/// tezlik chegarasi tufayli keyinga surilgan xabar "urinish" sarflamasligi
/// kerak — aks holda Telegram uzilganda barcha xabarlar chegarani yeb,
/// yuborilmasdan turib <c>Failed</c> bo'lib qolardi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Yuborishga tayyor xabarlarni OLADI (band qiladi).
    /// </summary>
    /// <param name="batchSize">Ko'pi bilan nechta qator olinadi.</param>
    /// <param name="lease">
    /// Band qilish muddati: shu vaqt ichida boshqa worker bu qatorni
    /// ko'rmaydi. Bitta xabarni yuborish uchun ketadigan vaqtdan sezilarli
    /// uzun bo'lishi kerak.
    /// </param>
    Task<IReadOnlyList<OutboxMessage>> ClaimAsync(
        int batchSize, TimeSpan lease, CancellationToken ct = default);

    /// <summary>Xabar yetkazildi deb belgilaydi.</summary>
    Task MarkDeliveredAsync(long messageId, CancellationToken ct = default);

    /// <summary>
    /// Yiqilishni yozadi va urinishlar hisoblagichini oshiradi.
    /// </summary>
    /// <param name="retryAfter">
    /// Qancha kutib qayta urinish. <c>null</c> — urinishlar tugadi, xabar
    /// <see cref="OutboxStatus.Failed"/> ga o'tadi.
    /// </param>
    Task MarkRejectedAsync(
        long messageId, string reason, TimeSpan? retryAfter, CancellationToken ct = default);

    /// <summary>
    /// Band qilingan xabarlarni navbatga QAYTARADI (urinish sarflamasdan).
    /// Tezlik chegarasiga urilganda ishlatiladi.
    /// </summary>
    Task PostponeAsync(
        IReadOnlyCollection<long> messageIds, TimeSpan delay, CancellationToken ct = default);
}
