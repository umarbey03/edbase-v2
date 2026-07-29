using Zinnur.Domain.Entities;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Chat xabarlarini bazaga YOZISH kanali (fon navbati orqali).
///
/// NIMA UCHUN NAVBAT: 200 kishilik darsda xabar oqimi tez. Agar har xabar
/// broadcast'dan oldin bazaga yozilsa, DB yozuvi kechikishi butun chatni
/// sekinlashtiradi. Shuning uchun: avval broadcast (tez), keyin fon
/// xizmati paketlab (batch) bazaga yozadi.
/// </summary>
public interface IChatMessageWriter
{
    /// <summary>Navbatga qo'shadi. Bloklamaydi.</summary>
    ValueTask EnqueueAsync(ChatMessage message, CancellationToken ct = default);
}
