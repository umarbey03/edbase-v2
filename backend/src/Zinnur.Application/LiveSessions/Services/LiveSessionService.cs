using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.LiveSessions.Services;

/// <summary>
/// Jonli dars use-case'lari. Ruxsat tekshiruvining YAGONA joyi —
/// controller ham, SignalR hub ham shu servisga tayanadi (DRY).
/// </summary>
public sealed class LiveSessionService(
    IApplicationDbContext db,
    ILiveKitTokenService liveKit,
    TimeProvider clock) : ILiveSessionService
{
    public async Task<IReadOnlyList<LiveSessionDto>> ListForUserAsync(
        long userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);
        var now = clock.GetUtcNow();

        // Kelajakdagi va hozir jonli bo'lgan darslar
        var query = db.LiveSessions
            .AsNoTracking()
            .Include(s => s.Group)
            .Where(s => s.Status != SessionStatus.Cancelled && s.ScheduledEnd >= now.AddHours(-6));

        query = user.Role switch
        {
            UserRole.Admin or UserRole.Academic => query,

            UserRole.Teacher or UserRole.Assistant =>
                query.Where(s => s.Group!.TeacherId == userId || s.Group!.AssistantId == userId),

            _ => query.Where(s => db.GroupMembers.Any(m =>
                    m.GroupId == s.GroupId &&
                    m.StudentId == userId &&
                    m.Status == MemberStatus.Active)),
        };

        var rows = await query
            .OrderBy(s => s.ScheduledStart)
            .Take(100)
            .ToListAsync(ct);

        return rows.Select(s => Map(s, IsHost(s, user))).ToList();
    }

    public async Task<LiveSessionDto> GetAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        var (session, user) = await LoadAndAuthorizeAsync(sessionId, userId, ct);
        return Map(session, IsHost(session, user));
    }

    public async Task<LiveSessionDto> StartAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        var (session, user) = await LoadAndAuthorizeAsync(sessionId, userId, ct, tracking: true);

        if (!IsHost(session, user))
            throw new ForbiddenException("Faqat dars hosti darsni boshlay oladi.");

        session.Start(clock.GetUtcNow());       // biznes qoidalari Domain'da
        await db.SaveChangesAsync(ct);

        return Map(session, isHost: true);
    }

    public async Task<LiveSessionDto> EndAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        var (session, user) = await LoadAndAuthorizeAsync(sessionId, userId, ct, tracking: true);

        if (!IsHost(session, user))
            throw new ForbiddenException("Faqat dars hosti darsni yakunlay oladi.");

        var now = clock.GetUtcNow();
        session.End(now);

        // Ochiq davomat seanslarini yopamiz va yakuniy holatni qo'yamiz
        var attendances = await db.Attendances
            .AsTracking()
            .Where(a => a.SessionId == sessionId)
            .ToListAsync(ct);

        foreach (var a in attendances)
            a.Finalize(now);

        await db.SaveChangesAsync(ct);
        return Map(session, isHost: true);
    }

    public async Task<LiveKitJoinDto> CreateJoinTokenAsync(
        long sessionId, long userId, CancellationToken ct = default)
    {
        var (session, user) = await LoadAndAuthorizeAsync(sessionId, userId, ct);
        var host = IsHost(session, user);

        // O'quvchi faqat dars BOSHLANGANDAN keyin kira oladi
        if (!host)
        {
            if (session.Status == SessionStatus.Scheduled)
                throw new ConflictException("Dars hali boshlanmagan — ustoz boshlaganda kira olasiz.");

            if (session.Status is SessionStatus.Ended or SessionStatus.Cancelled)
                throw new ConflictException("Dars yakunlangan.");
        }

        var token = liveKit.CreateAccessToken(new LiveKitTokenRequest(
            RoomName: session.RoomName,
            Identity: user.Id.ToString(CultureInfo.InvariantCulture),
            DisplayName: user.FullName,
            CanPublish: true,
            IsHost: host));

        return new LiveKitJoinDto(liveKit.ServerUrl, token, session.RoomName, host, session.EndsAt);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetRecentMessagesAsync(
        long sessionId, long userId, int take = 50, CancellationToken ct = default)
    {
        await LoadAndAuthorizeAsync(sessionId, userId, ct);

        take = Math.Clamp(take, 1, 200);

        var rows = await db.ChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);

        rows.Reverse();     // eski -> yangi tartibda qaytaramiz

        return rows
            .Select(m => new ChatMessageDto(m.Id, m.SenderId, m.SenderName, m.Body, m.SentAt))
            .ToList();
    }

    public async Task RegisterJoinAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        var (session, user) = await LoadAndAuthorizeAsync(sessionId, userId, ct);

        if (IsHost(session, user)) return;      // host uchun davomat yozilmaydi

        var att = await db.Attendances
            .AsTracking()
            .FirstOrDefaultAsync(a => a.SessionId == sessionId && a.StudentId == userId, ct);

        if (att is null)
        {
            att = new Attendance { SessionId = sessionId, StudentId = userId };
            db.Attendances.Add(att);
        }

        att.RegisterJoin(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    public async Task RegisterLeaveAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        var att = await db.Attendances
            .AsTracking()
            .FirstOrDefaultAsync(a => a.SessionId == sessionId && a.StudentId == userId, ct);

        if (att is null) return;

        att.RegisterLeave(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- ichki yordamchi

    private async Task<User> LoadUserAsync(long userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
        ?? throw new NotFoundException(nameof(User), userId);

    /// <summary>Darsni yuklaydi va foydalanuvchining unga kirish huquqini tekshiradi.</summary>
    private async Task<(LiveSession Session, User User)> LoadAndAuthorizeAsync(
        long sessionId, long userId, CancellationToken ct, bool tracking = false)
    {
        var query = db.LiveSessions.Include(s => s.Group).AsQueryable();
        query = tracking ? query.AsTracking() : query.AsNoTracking();

        var session = await query.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(LiveSession), sessionId);

        var user = await LoadUserAsync(userId, ct);

        if (IsHost(session, user)) return (session, user);

        var isMember = await db.GroupMembers.AsNoTracking().AnyAsync(m =>
            m.GroupId == session.GroupId &&
            m.StudentId == userId &&
            m.Status == MemberStatus.Active, ct);

        if (!isMember)
            throw new ForbiddenException("Bu darsga ruxsatingiz yo'q.");

        if (session.Group is { IsActive: false })
            throw new ForbiddenException("Guruh arxivlangan.");

        return (session, user);
    }

    private static bool IsHost(LiveSession session, User user) =>
        user.Role is UserRole.Admin or UserRole.Academic
        || session.HostId == user.Id
        || (session.Group?.IsStaff(user.Id) ?? false);

    private static LiveSessionDto Map(LiveSession s, bool isHost) => new(
        s.Id,
        s.GroupId,
        s.Group?.Name ?? string.Empty,
        s.Title,
        s.Type.ToString(),
        s.Status.ToString(),
        s.ScheduledStart,
        s.ScheduledEnd,
        s.ActualStart,
        s.EndsAt,
        isHost);
}
