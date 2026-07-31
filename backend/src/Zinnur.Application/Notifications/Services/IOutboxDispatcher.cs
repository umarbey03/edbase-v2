using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// Navbatni bir marta aylantirib chiqadi: oladi → yuboradi → holatni yozadi.
///
/// ★ NIMA UCHUN <c>BackgroundService</c> DAN AJRATILGAN: hosting (qachon,
/// qanchadan keyin ishga tushish, to'xtatish) — WebApi qatlamining ishi;
/// "qaysi tartibda, nechta urinish, chegaraga urilganda nima qilish" esa
/// BIZNES qoidasi va u shu yerda, testda mock'siz sinaladi. Testlar
/// aylanishni O'ZI chaqiradi va natijani darhol tekshiradi — fon xizmatining
/// uyquda kutishini kutib o'tirmaydi.
/// </summary>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Bitta paketni yuborib chiqadi.
    /// </summary>
    /// <param name="batchSize">Bir aylanishda ko'pi bilan nechta xabar.</param>
    /// <param name="lease">
    /// Band qilish muddati — shu vaqt ichida boshqa instance bu xabarlarni
    /// ko'rmaydi (izoh: <see cref="IOutboxStore.ClaimAsync"/>).
    /// </param>
    Task<OutboxDispatchResult> DispatchAsync(
        int batchSize, TimeSpan lease, CancellationToken ct = default);
}
