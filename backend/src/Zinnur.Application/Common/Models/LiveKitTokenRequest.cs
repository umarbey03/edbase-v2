namespace Zinnur.Application.Common.Models;

/// <summary>LiveKit kirish tokeni uchun so'rov.</summary>
/// <param name="RoomName">LiveKit xona nomi (<c>LiveSession.RoomName</c>).</param>
/// <param name="Identity">Ishtirokchi identifikatori — foydalanuvchi Id'si (satr).</param>
/// <param name="DisplayName">Xonada ko'rinadigan ism.</param>
/// <param name="CanPublish">Kamera/mikrofon uzata oladimi.</param>
/// <param name="IsHost">Host (ustoz/kurator) — <c>roomAdmin</c> huquqini beradi.</param>
/// <param name="Ttl">
/// Amal qilish muddati. Bo'sh bo'lsa zaxira qiymat (2 soat) ishlatiladi —
/// lekin uni BO'SH QOLDIRMANG: token muddati xavfsizlik chegarasi, chunki
/// klient LiveKit'ga to'g'ridan-to'g'ri ulanadi va berilgan tokenni
/// serverdan qaytarib bo'lmaydi. Namuna hisob-kitob —
/// <c>LiveSessionService.JoinTokenTtl</c> (dars tugashi + 30 daqiqa).
/// </param>
public sealed record LiveKitTokenRequest(
    string RoomName,
    string Identity,
    string DisplayName,
    bool CanPublish,
    bool IsHost,
    TimeSpan? Ttl = null);
