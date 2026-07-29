namespace Zinnur.Application.Common.Models;

/// <summary>LiveKit kirish tokeni uchun so'rov.</summary>
/// <param name="RoomName">LiveKit xona nomi (<c>LiveSession.RoomName</c>).</param>
/// <param name="Identity">Ishtirokchi identifikatori — foydalanuvchi Id'si (satr).</param>
/// <param name="DisplayName">Xonada ko'rinadigan ism.</param>
/// <param name="CanPublish">Kamera/mikrofon uzata oladimi.</param>
/// <param name="IsHost">Host (ustoz/kurator) — <c>roomAdmin</c> huquqini beradi.</param>
/// <param name="Ttl">Amal qilish muddati. Bo'sh bo'lsa default 6 soat.</param>
public sealed record LiveKitTokenRequest(
    string RoomName,
    string Identity,
    string DisplayName,
    bool CanPublish,
    bool IsHost,
    TimeSpan? Ttl = null);
