using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.Payments.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.LiveSessions.Services;

/// <inheritdoc cref="IBreakoutService"/>
/// <remarks>
/// ★ RUXSAT <see cref="ILiveSessionService.GetAsync"/> ORQALI: "bu odam shu
///   darsga kira oladimi" qoidasi bitta joyda yashaydi (a'zolik, kuzatuvchi,
///   host). Bu servis faqat ustiga "boshqara oladimi" (<c>IsHost</c>) va
///   "o'quvchimi" degan ikki savolni qo'shadi.
///
/// ★ YOZUV (egress) faqat ASOSIY xonani yozadi: guruhlardagi suhbat dars
///   yozuviga tushmaydi. Bu ataylab — kichik guruh mashqi yozuvni shovqin
///   bilan to'ldirardi.
/// </remarks>
public sealed class BreakoutService(
    IApplicationDbContext db,
    ILiveSessionService sessions,
    ILiveKitTokenService liveKit,
    ICacheService cache,
    IPresenceService presence,
    IPaymentBlockService paymentBlock,
    ILiveSessionNotifier notifier,
    TimeProvider clock) : IBreakoutService
{
    /// <summary>Loyiha egasi talabi: guruhda kamida 2, ko'pi bilan 4 o'quvchi.</summary>
    public const int MinGroupSize = 2;

    public const int MaxGroupSize = 4;

    private const int MaxDurationMinutes = 120;

    /// <summary>Redis kaliti yashash muddati — eng uzun darsdan ham uzun.</summary>
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(8);

    private static readonly TimeSpan TokenGrace = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinTokenTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxTokenTtl = TimeSpan.FromHours(6);

    // ---------------------------------------------------------------- o'qish

    public async Task<BreakoutStateDto> GetAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        var access = await AuthorizeAsync(sessionId, userId, ct);
        var state = await LoadActiveAsync(sessionId, access.Session, ct);
        return await ProjectAsync(sessionId, state, access, ct);
    }

    // ---------------------------------------------------------------- boshqaruv

    public async Task<BreakoutStateDto> StartAsync(
        long sessionId, StartBreakoutsRequest request, long userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await AuthorizeManagerAsync(sessionId, userId, ct);

        if (request.DurationMinutes is { } minutes && minutes is < 1 or > MaxDurationMinutes)
            throw new ConflictException($"Taymer 1 dan {MaxDurationMinutes} daqiqagacha bo'lishi kerak.");

        if (await LoadActiveAsync(sessionId, access.Session, ct) is not null)
            throw new ConflictException("Ichki guruhlar allaqachon ochiq.");

        var students = (await presence.ListAsync(sessionId, ct))
            .Where(IsStudent)
            .Select(entry => entry.UserId)
            .Distinct()
            .ToList();

        if (students.Count < MinGroupSize)
            throw new ConflictException(
                $"Guruhlarga bo'lish uchun darsda kamida {MinGroupSize} o'quvchi bo'lishi kerak.");

        var now = clock.GetUtcNow();
        var state = new BreakoutState(
            StartedAt: now,
            EndsAt: request.DurationMinutes is { } duration ? now.AddMinutes(duration) : null,
            Rooms: Split(students));

        await SaveAsync(sessionId, state, ct);
        await notifier.BreakoutsChangedAsync(sessionId, ct);

        return await ProjectAsync(sessionId, state, access, ct);
    }

    public async Task<BreakoutStateDto> MoveAsync(
        long sessionId, long studentId, MoveBreakoutStudentRequest request, long userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await AuthorizeManagerAsync(sessionId, userId, ct);
        var state = await LoadActiveAsync(sessionId, access.Session, ct)
            ?? throw new ConflictException("Ichki guruhlar ochilmagan.");

        var target = request.Room;
        if (target < 0 || target > state.Rooms.Count + 1)
            throw new ConflictException("Bunday guruh yo'q.");

        // Faqat shu darsning o'quvchisi: guruhda turgan yoki hozir darsda bo'lgan.
        var known = state.Rooms.Any(room => room.StudentIds.Contains(studentId))
            || (await presence.ListAsync(sessionId, ct)).Any(entry => entry.UserId == studentId && IsStudent(entry));
        if (!known)
            throw new ConflictException("O'quvchi darsda emas.");

        var rooms = state.Rooms
            .Select(room => room with { StudentIds = room.StudentIds.Where(id => id != studentId).ToList() })
            .ToList();

        if (target == rooms.Count + 1)
            rooms.Add(new BreakoutRoomState(target, [], HelpRequested: false));

        if (target > 0)
        {
            var room = rooms[target - 1];
            if (room.StudentIds.Count >= MaxGroupSize)
                throw new ConflictException($"Bir guruhda ko'pi bilan {MaxGroupSize} o'quvchi bo'ladi.");

            rooms[target - 1] = room with { StudentIds = [.. room.StudentIds, studentId] };
        }

        state = state with { Rooms = rooms };
        await SaveAsync(sessionId, state, ct);
        await notifier.BreakoutsChangedAsync(sessionId, ct);

        return await ProjectAsync(sessionId, state, access, ct);
    }

    public async Task CloseAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        await AuthorizeManagerAsync(sessionId, userId, ct);
        await cache.RemoveAsync(Key(sessionId), ct);
        await notifier.BreakoutsChangedAsync(sessionId, ct);
    }

    public async Task SetHelpAsync(long sessionId, bool raised, long userId, CancellationToken ct = default)
    {
        var access = await AuthorizeAsync(sessionId, userId, ct);
        if (!access.IsStudent)
            throw new ForbiddenException("Ustozni faqat o'quvchi chaqiradi.");

        var state = await LoadActiveAsync(sessionId, access.Session, ct)
            ?? throw new ConflictException("Ichki guruhlar ochilmagan.");

        var number = RoomOf(state, userId)
            ?? throw new ConflictException("Siz hech qaysi guruhda emassiz.");

        await SaveAsync(sessionId, WithHelp(state, number, raised), ct);
        await notifier.BreakoutsChangedAsync(sessionId, ct);
    }

    // ---------------------------------------------------------------- token

    public async Task<LiveKitJoinDto> CreateTokenAsync(
        long sessionId, BreakoutTokenRequest request, long userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await AuthorizeAsync(sessionId, userId, ct);
        var state = await LoadActiveAsync(sessionId, access.Session, ct)
            ?? throw new ConflictException("Ichki guruhlar yopildi.");

        int number;
        if (access.Session.IsHost)
        {
            number = request.Room ?? throw new ConflictException("Guruh tanlanmagan.");
            if (number < 1 || number > state.Rooms.Count)
                throw new ConflictException("Bunday guruh yo'q.");
        }
        else if (access.IsStudent)
        {
            if (request.Monitor)
                throw new ForbiddenException("Kuzatish faqat ustoz uchun.");

            number = RoomOf(state, userId)
                ?? throw new ConflictException("Siz hech qaysi guruhda emassiz — asosiy xonada qoling.");

            await paymentBlock.EnsureAllowedAsync(userId, PaymentBlockScope.Live, ct);
        }
        else
        {
            throw new ForbiddenException("Ichki guruhlarga faqat ustoz va o'quvchilar kiradi.");
        }

        // Ustoz guruhga KIRDI — "chaqirish" belgisi o'z vazifasini bajardi.
        if (access.Session.IsHost && !request.Monitor && state.Rooms[number - 1].HelpRequested)
        {
            await SaveAsync(sessionId, WithHelp(state, number, raised: false), ct);
            await notifier.BreakoutsChangedAsync(sessionId, ct);
        }

        var roomName = RoomName(access.RoomName, number);
        var token = liveKit.CreateAccessToken(new LiveKitTokenRequest(
            RoomName: roomName,
            // Tinglovchi ulanishi ustozning O'Z ulanishi bilan bir xonaga tushadi
            // (ustoz guruhga kirganda) — bir xil identity LiveKit'da birini
            // chiqarib yuborardi, shuning uchun qo'shimcha belgi.
            Identity: request.Monitor
                ? $"{userId.ToString(CultureInfo.InvariantCulture)}-monitor"
                : userId.ToString(CultureInfo.InvariantCulture),
            DisplayName: access.DisplayName,
            CanPublish: !request.Monitor,
            IsHost: access.Session.IsHost && !request.Monitor,
            Ttl: TokenTtl(access.Session.EndsAt),
            Hidden: request.Monitor));

        return new LiveKitJoinDto(
            liveKit.ServerUrl, token, roomName, access.Session.IsHost, access.Session.EndsAt);
    }

    // ---------------------------------------------------------------- yordamchilar

    private sealed record Access(
        long UserId, LiveSessionDto Session, bool IsStudent, string DisplayName, string RoomName);

    private async Task<Access> AuthorizeAsync(long sessionId, long userId, CancellationToken ct)
    {
        // Kira olmaydigan odam shu yerda 403/404 oladi.
        var session = await sessions.GetAsync(sessionId, userId, ct);

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.FullName })
            .FirstAsync(ct);

        var roomName = await db.LiveSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.RoomName)
            .FirstAsync(ct);

        return new Access(userId, session, user.Role == UserRole.Student, user.FullName, roomName);
    }

    private async Task<Access> AuthorizeManagerAsync(long sessionId, long userId, CancellationToken ct)
    {
        var access = await AuthorizeAsync(sessionId, userId, ct);
        if (!access.Session.IsHost)
            throw new ForbiddenException("Ichki guruhlarni faqat darsni olib borayotgan ustoz boshqaradi.");
        if (!IsLive(access.Session))
            throw new ConflictException("Dars jonli emas.");
        return access;
    }

    private static bool IsLive(LiveSessionDto session) =>
        string.Equals(session.Status, nameof(SessionStatus.Live), StringComparison.Ordinal);

    private static bool IsStudent(PresenceEntry entry) =>
        string.Equals(entry.Role, nameof(UserRole.Student), StringComparison.Ordinal);

    private static string Key(long sessionId) =>
        $"breakout:{sessionId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>LiveKit xona nomi. Asosiy xona nomidan yasaladi — begona darsga to'qnashmaydi.</summary>
    public static string RoomName(string mainRoom, int number) =>
        $"{mainRoom}-b{number.ToString(CultureInfo.InvariantCulture)}";

    private Task SaveAsync(long sessionId, BreakoutState state, CancellationToken ct) =>
        cache.SetAsync(Key(sessionId), state, StateTtl, ct);

    /// <summary>
    /// Ochiq holat yoki <c>null</c>. Dars jonli bo'lmasa yoki taymer tugagan
    /// bo'lsa holat SHU YERDA yopiladi (alohida fon vazifasi shart emas):
    /// ustoz brauzeri yopilib qolsa ham o'quvchilar guruhda qamalib qolmaydi.
    /// </summary>
    private async Task<BreakoutState?> LoadActiveAsync(
        long sessionId, LiveSessionDto session, CancellationToken ct)
    {
        var state = await cache.GetAsync<BreakoutState>(Key(sessionId), ct);
        if (state is null) return null;

        var expired = state.EndsAt is { } endsAt && endsAt <= clock.GetUtcNow();
        if (!expired && IsLive(session)) return state;

        await cache.RemoveAsync(Key(sessionId), ct);
        await notifier.BreakoutsChangedAsync(sessionId, ct);
        return null;
    }

    private static int? RoomOf(BreakoutState state, long userId) =>
        state.Rooms.FirstOrDefault(room => room.StudentIds.Contains(userId))?.Number;

    private static BreakoutState WithHelp(BreakoutState state, int number, bool raised) =>
        state with
        {
            Rooms = state.Rooms
                .Select(room => room.Number == number ? room with { HelpRequested = raised } : room)
                .ToList(),
        };

    /// <summary>
    /// Tasodifiy, teng bo'lish: guruhlar soni = ⌈n/4⌉, o'lchamlar farqi ≤ 1.
    /// n ≥ 2 bo'lganda har guruhda kamida 2 kishi chiqadi (masalan 5 → 3+2,
    /// 7 → 4+3, 9 → 3+3+3).
    /// </summary>
    public static List<BreakoutRoomState> Split(IReadOnlyList<long> students)
    {
        var shuffled = students.ToArray();
        Random.Shared.Shuffle(shuffled);

        var count = (int)Math.Ceiling(shuffled.Length / (double)MaxGroupSize);
        var rooms = Enumerable.Range(1, count)
            .Select(number => new BreakoutRoomState(number, [], HelpRequested: false))
            .ToList();

        for (var i = 0; i < shuffled.Length; i++)
            rooms[i % count].StudentIds.Add(shuffled[i]);

        return rooms;
    }

    private TimeSpan TokenTtl(DateTimeOffset? endsAt)
    {
        if (endsAt is not { } end) return TimeSpan.FromHours(2);
        var ttl = end - clock.GetUtcNow() + TokenGrace;
        return ttl < MinTokenTtl ? MinTokenTtl : ttl > MaxTokenTtl ? MaxTokenTtl : ttl;
    }

    private async Task<BreakoutStateDto> ProjectAsync(
        long sessionId, BreakoutState? state, Access access, CancellationToken ct)
    {
        if (state is null)
            return new BreakoutStateDto(false, null, [], [], null, access.Session.IsHost);

        var online = (await presence.ListAsync(sessionId, ct))
            .Where(IsStudent)
            .DistinctBy(entry => entry.UserId)
            .ToDictionary(entry => entry.UserId, entry => entry.DisplayName);

        var assigned = state.Rooms.SelectMany(room => room.StudentIds).ToHashSet();
        var ids = assigned.Union(online.Keys).ToList();

        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        BreakoutMemberDto Member(long id) =>
            new(id, names.GetValueOrDefault(id) ?? online.GetValueOrDefault(id) ?? $"#{id}", online.ContainsKey(id));

        BreakoutRoomDto Room(BreakoutRoomState room) =>
            new(room.Number, room.StudentIds.Select(Member).ToList(), room.HelpRequested);

        // O'quvchi FAQAT o'z guruhini ko'radi.
        if (access.IsStudent)
        {
            var myRoom = RoomOf(state, access.UserId);
            IReadOnlyList<BreakoutRoomDto> own = myRoom is { } n ? [Room(state.Rooms[n - 1])] : [];
            return new BreakoutStateDto(true, state.EndsAt, own, [], myRoom, CanManage: false);
        }

        var unassigned = online.Keys.Where(id => !assigned.Contains(id)).Select(Member).ToList();
        return new BreakoutStateDto(
            true, state.EndsAt, state.Rooms.Select(Room).ToList(), unassigned, null, access.Session.IsHost);
    }
}

/// <summary>Redis'dagi holat. JSON — <c>JsonSerializerDefaults.Web</c>.</summary>
public sealed record BreakoutState(
    DateTimeOffset StartedAt,
    DateTimeOffset? EndsAt,
    List<BreakoutRoomState> Rooms);

public sealed record BreakoutRoomState(int Number, List<long> StudentIds, bool HelpRequested);
