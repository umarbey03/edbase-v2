using Zinnur.Application.Broadcasts.Dtos;
using Zinnur.Application.Common.Models;

namespace Zinnur.Application.Broadcasts.Services;

/// <summary>
/// "Xabarlar" paneli (2026-08-16) — o'quv bo'limi tanlagan guruhlarga
/// (shablon yoki qo'lda yozilgan) xabar yuborish, faqat Academic/Admin.
/// </summary>
public interface IGroupBroadcastService
{
    /// <summary>
    /// Yuboradi: tanlangan har guruhga (turi bo'yicha) platforma chatiga
    /// yozadi va/yoki Telegram orqali har FAOL a'zoga navbatga qo'yadi.
    /// Natija — YARATILGAN TARIX YOZUVI (nechta odamga yetgani bilan).
    /// </summary>
    Task<GroupBroadcastDto> SendAsync(
        SendGroupBroadcastRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Yuborilgan xabarlar tarixi (yangisidan eskisiga, sahifalangan).</summary>
    Task<PagedResult<GroupBroadcastDto>> ListAsync(
        GroupBroadcastListQuery query, long actorId, CancellationToken ct = default);
}
