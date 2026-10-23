namespace Zinnur.Application.LiveSessions.Dtos;

/// <summary>
/// Ichki guruhlar (breakout) holati — so'rovchiga MOS ko'rinishda.
///
/// Ustoz (va kuzatuvchi xodim) hamma guruhni ko'radi. O'quvchi FAQAT o'z
/// guruhini ko'radi (2026-10-23, loyiha egasi qarori): <see cref="Rooms"/>
/// unda bitta element yoki bo'sh, <see cref="Unassigned"/> esa doim bo'sh.
/// </summary>
/// <param name="Active">Ichki guruhlar hozir ochiqmi.</param>
/// <param name="EndsAt">Taymer tugash vaqti; taymer qo'yilmagan bo'lsa <c>null</c>.</param>
/// <param name="Rooms">Guruhlar (raqami 1 dan boshlanadi).</param>
/// <param name="Unassigned">Darsda bor, lekin hech qaysi guruhga tushmagan o'quvchilar.</param>
/// <param name="MyRoom">So'rovchi o'quvchining guruhi; asosiy xonada bo'lsa <c>null</c>.</param>
/// <param name="CanManage">Guruhlarni boshqara oladimi (faqat darsni olib borayotgan ustoz).</param>
public sealed record BreakoutStateDto(
    bool Active,
    DateTimeOffset? EndsAt,
    IReadOnlyList<BreakoutRoomDto> Rooms,
    IReadOnlyList<BreakoutMemberDto> Unassigned,
    int? MyRoom,
    bool CanManage);

/// <param name="Number">Guruh raqami (1..N). LiveKit xonasi: <c>{RoomName}-b{Number}</c>.</param>
/// <param name="HelpRequested">Guruhdan "ustozni chaqirish" bosilgan.</param>
public sealed record BreakoutRoomDto(
    int Number,
    IReadOnlyList<BreakoutMemberDto> Members,
    bool HelpRequested);

/// <param name="Online">Hozir darsda (hub presence) bormi.</param>
public sealed record BreakoutMemberDto(long UserId, string DisplayName, bool Online);

/// <param name="DurationMinutes">Taymer (1..120 daqiqa); <c>null</c> — taymersiz.</param>
public sealed record StartBreakoutsRequest(int? DurationMinutes);

/// <param name="Room">Maqsad guruh; <c>0</c> — asosiy xona, <c>N+1</c> — yangi guruh.</param>
public sealed record MoveBreakoutStudentRequest(int Room);

public sealed record BreakoutHelpRequest(bool Raised);

/// <param name="Room">
/// Ustoz uchun: kiradigan guruh. O'quvchi uchun e'tiborga olinmaydi — u
/// faqat O'Z guruhining tokenini oladi.
/// </param>
/// <param name="Monitor">
/// Ustozning ko'rinmas "tinglovchi" ulanishi: kim gapirayotganini bilish
/// uchun. Efirga chiqmaydi va guruhdagilarga ko'rinmaydi.
/// </param>
public sealed record BreakoutTokenRequest(int? Room, bool Monitor = false);
