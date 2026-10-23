using Zinnur.Application.LiveSessions.Dtos;

namespace Zinnur.Application.LiveSessions.Services;

/// <summary>
/// Jonli dars ichidagi kichik guruhlar (breakout rooms).
///
/// Holat VAQTINCHALIK va Redis'da yashaydi (<c>breakout:{sessionId}</c>):
/// u faqat dars davomida kerak, bazaga yozilmaydi. Har bir guruh — alohida
/// LiveKit xonasi; davomat esa hub presence'ga bog'langani uchun o'quvchi
/// guruhga o'tganda ham "darsda" bo'lib qoladi.
/// </summary>
public interface IBreakoutService
{
    Task<BreakoutStateDto> GetAsync(long sessionId, long userId, CancellationToken ct = default);

    /// <summary>Darsdagi o'quvchilarni ko'pi bilan 4 kishilik guruhlarga tasodifiy bo'ladi.</summary>
    Task<BreakoutStateDto> StartAsync(
        long sessionId, StartBreakoutsRequest request, long userId, CancellationToken ct = default);

    Task<BreakoutStateDto> MoveAsync(
        long sessionId, long studentId, MoveBreakoutStudentRequest request, long userId,
        CancellationToken ct = default);

    /// <summary>Hammani asosiy xonaga qaytaradi.</summary>
    Task CloseAsync(long sessionId, long userId, CancellationToken ct = default);

    Task SetHelpAsync(long sessionId, bool raised, long userId, CancellationToken ct = default);

    Task<LiveKitJoinDto> CreateTokenAsync(
        long sessionId, BreakoutTokenRequest request, long userId, CancellationToken ct = default);
}
