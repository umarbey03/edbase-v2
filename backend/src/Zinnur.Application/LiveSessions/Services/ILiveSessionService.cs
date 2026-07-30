using Zinnur.Application.LiveSessions.Dtos;

namespace Zinnur.Application.LiveSessions.Services;

public interface ILiveSessionService
{
    Task<IReadOnlyList<LiveSessionDto>> ListForUserAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// KALENDAR: berilgan SANA ORALIG'IDAGI barcha darslar.
    ///
    /// ★ <see cref="ListForUserAsync"/> DAN FARQI (u O'ZGARMADI —
    /// shartnomasini frontend allaqachon ishlatadi):
    ///
    ///   • u "yaqin darslar"ni beradi (oxirgi 6 soat + kelajak, 100 ta
    ///     chegara bilan) — bosh sahifadagi "keyingi dars" kartochkasi uchun;
    ///   • bu esa ANIQ oraliqni beradi, o'tgan oylarni ham, va har darsga
    ///     o'quvchining O'Z davomatini qo'shadi.
    ///
    /// Bekor qilingan darslar ham QAYTADI: kalendarda "bekor qilindi" deb
    /// ko'rsatilishi kerak, aks holda o'quvchi jadvaldagi bo'shliqni ko'rib
    /// "tizim adashdimi?" deb o'ylaydi.
    /// </summary>
    /// <param name="fromDate">Mahalliy (markaz vaqti) sana — KIRADI.</param>
    /// <param name="toDate">Mahalliy sana — KIRADI.</param>
    Task<IReadOnlyList<CalendarSessionDto>> GetCalendarAsync(
        long userId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    Task<LiveSessionDto> GetAsync(long sessionId, long userId, CancellationToken ct = default);

    Task<LiveSessionDto> StartAsync(long sessionId, long userId, CancellationToken ct = default);

    Task<LiveSessionDto> EndAsync(long sessionId, long userId, CancellationToken ct = default);

    /// <summary>LiveKit'ga ulanish uchun token. Ruxsat shu yerda tekshiriladi.</summary>
    Task<LiveKitJoinDto> CreateJoinTokenAsync(long sessionId, long userId, CancellationToken ct = default);

    Task<IReadOnlyList<ChatMessageDto>> GetRecentMessagesAsync(
        long sessionId, long userId, int take = 50, CancellationToken ct = default);

    /// <summary>Ishtirokchi darsga kirdi/chiqdi — davomatni yangilaydi.</summary>
    Task RegisterJoinAsync(long sessionId, long userId, CancellationToken ct = default);

    Task RegisterLeaveAsync(long sessionId, long userId, CancellationToken ct = default);
}
