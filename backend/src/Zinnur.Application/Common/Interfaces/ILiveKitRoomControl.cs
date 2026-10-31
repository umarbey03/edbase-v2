namespace Zinnur.Application.Common.Interfaces;

/// <summary>Ishtirokchining qaysi oqimi boshqarilayotgani.</summary>
public enum ParticipantMediaSource
{
    Microphone = 0,
    Camera = 1,
}

/// <summary>
/// Xona boshqaruvi natijasi. ISTISNO O'RNIGA — LiveKit'ning rad etishi
/// ("participant not found") xodim uchun ODDIY holat: o'quvchi shu lahzada
/// xonadan chiqib ketgan bo'lishi mumkin. Chaqiruvchi xabarni o'zi tanlaydi.
/// </summary>
public readonly record struct RoomControlResult(bool Succeeded, string? Error)
{
    public static RoomControlResult Ok() => new(true, null);

    public static RoomControlResult Fail(string error) => new(false, error);
}

/// <summary>
/// LiveKit xonasini SERVER tomonidan boshqarish (RoomService API).
///
/// NIMA UCHUN SERVER ORQALI, KLIENT ORQALI EMAS: brauzer SDK'sida "boshqa
/// ishtirokchini o'chirish" amali YO'Q — <c>roomAdmin</c> granti faqat
/// server API'siga taalluqli. Ma'lumot kanali (data message) orqali
/// "o'zingni o'chir" deb so'rash esa HAMKORLIKKA tayanadi: o'zgartirilgan
/// klient uni e'tiborsiz qoldiradi. Server chaqiruvi bilan trek SFU
/// darajasida jim bo'ladi — o'quvchi xohlasa ham uzata olmaydi.
/// </summary>
public interface ILiveKitRoomControl
{
    /// <summary>
    /// Ishtirokchining mikrofon yoki kamera trekini O'CHIRADI (mute).
    ///
    /// ⚠️ FAQAT O'CHIRISH — yoqish yo'q: <c>livekit.yaml</c> da
    /// <c>enable_remote_unmute: false</c> (ataylab — birovning mikrofonini
    /// uning xohishisiz yoqish mumkin emas; Telegram/Zoom ham shunday).
    /// </summary>
    Task<RoomControlResult> MuteTrackAsync(
        string roomName,
        string identity,
        ParticipantMediaSource source,
        CancellationToken ct = default);

    /// <summary>
    /// Ishtirokchini xonadan CHIQARADI (<c>RemoveParticipant</c>, 2026-10-31).
    ///
    /// ★ IDEMPOTENT: ishtirokchi allaqachon xonada bo'lmasa — muvaffaqiyat
    /// (natija kerakli holatda).
    ///
    /// ⚠️ TOKENNI BEKOR QILMAYDI: LiveKit tokenni qaytarib olib bo'lmaydi.
    /// Rasmiy klient <c>PARTICIPANT_REMOVED</c> dan keyin qayta ulanmaydi
    /// (<c>useLiveKitRoom.shouldReconnect</c>), yangi token esa server
    /// tomonda rad etiladi (<c>LiveSessionService.CreateJoinTokenAsync</c>).
    /// O'zgartirilgan klient eski token bilan muddati tugaguncha (dars
    /// oxiri + 30 daqiqa) qayta kira olishi mumkin — qolgan risk.
    /// </summary>
    Task<RoomControlResult> RemoveParticipantAsync(
        string roomName,
        string identity,
        CancellationToken ct = default);

    /// <summary>
    /// Xonani YOPADI (<c>DeleteRoom</c>, 2026-10-31): hamma ishtirokchi
    /// <c>ROOM_DELETED</c> bilan uziladi, xona yo'qoladi.
    ///
    /// ★ IDEMPOTENT: xona yo'q bo'lsa (hamma chiqib ketgan va
    /// <c>empty_timeout</c> o'tgan) — muvaffaqiyat.
    /// </summary>
    Task<RoomControlResult> DeleteRoomAsync(
        string roomName,
        CancellationToken ct = default);
}
