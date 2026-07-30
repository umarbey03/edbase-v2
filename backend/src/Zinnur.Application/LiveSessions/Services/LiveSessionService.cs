using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
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
    ILiveSessionNotifier notifier,
    IPaymentBlockService paymentBlock,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : ILiveSessionService
{
    /// <summary>
    /// Kalendar so'rovining eng uzun oralig'i (kun).
    ///
    /// NIMA UCHUN CHEGARA BOR: <c>?from=2000-01-01&amp;to=2100-01-01</c>
    /// butun bazani bitta javobga yig'ishga urinardi. Uch oy — kalendar
    /// ko'rinishi uchun yetarlidan ham ko'p (odatda bir oy so'raladi).
    /// </summary>
    private const int MaxCalendarDays = 92;

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

        query = ScopeByRole(query, user);

        var rows = await query
            .OrderBy(s => s.ScheduledStart)
            .Take(100)
            .ToListAsync(ct);

        return rows.Select(s => Map(s, IsHost(s, user))).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarSessionDto>> GetCalendarAsync(
        long userId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);

        if (fromDate > toDate)
            throw Invalid("fromDate", "Boshlanish sanasi tugash sanasidan keyin bo'lishi mumkin emas.");

        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxCalendarDays)
            throw Invalid("toDate", $"Oraliq {MaxCalendarDays} kundan oshmasin.");

        var zone = timeZone.TimeZone;

        // Mahalliy kun chegaralari -> UTC. `to` KIRADI, shuning uchun
        // keyingi kunning boshi olinadi: kun oxirini `23:59:59` deb yozish
        // o'sha oxirgi soniyadagi darsni yo'qotardi.
        var fromUtc = LocalWallClock.StartOfDayUtc(fromDate, zone);
        var toUtc = LocalWallClock.StartOfDayUtc(toDate.AddDays(1), zone);

        // ★ Bekor qilingani ham QAYTADI (izoh interfeysda).
        var query = db.LiveSessions.AsNoTracking()
            .Where(s => s.ScheduledStart >= fromUtc && s.ScheduledStart < toUtc);

        query = ScopeByRole(query, user);

        // ★ N+1 YO'Q: davomat ichki (korrelyatsion) so'rov bilan AYNI
        // `SELECT` da keladi. Har dars uchun alohida so'rov yuborilsa
        // bir oylik kalendar 20+ borish-kelish bo'lardi.
        var rows = await query
            .OrderBy(s => s.ScheduledStart)
            .ThenBy(s => s.Id)
            .Select(s => new CalendarRow(
                s.Id,
                s.GroupId,
                s.Group!.Name,
                s.Title,
                s.Type,
                s.Status,
                s.HostId,
                s.Group.TeacherId,
                s.Group.AssistantId,
                db.Attendances
                    .Where(a => a.SessionId == s.Id && a.StudentId == userId)
                    .Select(a => (AttendanceStatus?)a.Status)
                    .FirstOrDefault(),
                s.ScheduledStart,
                s.ScheduledEnd))
            .ToListAsync(ct);

        return rows.ConvertAll(row => new CalendarSessionDto(
            row.Id,
            row.GroupId,
            row.GroupName,
            row.Title,
            row.Type.ToString(),
            row.Status.ToString(),
            LocalWallClock.LocalDate(row.ScheduledStart, zone),
            row.ScheduledStart,
            row.ScheduledEnd,
            IsHost(user, row.HostId, row.TeacherId, row.AssistantId),
            row.MyAttendance?.ToString()));
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

        // ★ COMMIT-THEN-SEND: xabar faqat ma'lumot YOZILGANDAN keyin ketadi.
        // Teskarisi bo'lsa (avval xabar, keyin saqlash) saqlash yiqilganda
        // o'quvchilarda "dars tugadi" ekrani chiqib, baza esa darsni jonli deb
        // turaverardi — eski tizimning xatosi aynan shu edi (`docs/ROADMAP.md`).
        //
        // Yuborish O'ZI hech qachon istisno ko'tarmaydi (port kelishuvi),
        // shuning uchun bu yerda try/catch YO'Q: xato ikki joyda yutilsa
        // sababini topib bo'lmay qolardi.
        await notifier.SessionEndedAsync(sessionId, ct);

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

        // ★ QARZDORLIK DARVOZASI (FAZA 4.3) — AYNAN SHU YERDA.
        //
        // Token JONLI XONAGA KIRISH kaliti: u berilgandan keyin serverning
        // "yo'q" deyishi mumkin emas, chunki klient to'g'ridan-to'g'ri
        // LiveKit'ga ulanadi. Ya'ni tekshiruv token BERILISHIDAN oldin
        // bo'lishi shart — ro'yxat yoki sahifa darajasida emas.
        //
        // Faqat O'QUVCHIGA: ustoz va kurator o'z darsiga hech qachon
        // bloklanmaydi. Qarzsiz o'quvchi uchun bu bitta indeksli
        // `SUM` so'rovi (`IX_Payments_StudentId_Status`).
        if (!host && user.Role == UserRole.Student)
            await paymentBlock.EnsureAllowedAsync(user.Id, PaymentBlockScope.Live, ct);

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

    /// <summary>
    /// Foydalanuvchini yuklaydi va hisob FAOL ekanini tekshiradi.
    ///
    /// ★ `IsActive` tekshiruvi ilgari shu servisda TUSHIB QOLGAN edi (kurs,
    /// vazifa va guruh servislarida bor edi). Natijada o'chirilgan o'quvchi
    /// eski kirish tokeni bilan jonli darsga LiveKit tokeni olib, video/audio
    /// efirga chiqa olardi — jonli tekshiruvda isbotlangan.
    ///
    /// Asosiy himoya endi markaziy (`OnTokenValidated` da sessiya versiyasi
    /// tekshiriladi); bu yerdagi tekshiruv ikkinchi qatlam: kelajakda kimdir
    /// servisni boshqa yo'l bilan chaqirsa ham qoida saqlanadi.
    /// </summary>
    private async Task<User> LoadUserAsync(long userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return user;
    }

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
        IsHost(user, session.HostId, session.Group?.TeacherId, session.Group?.AssistantId);

    /// <summary>
    /// "Host" qoidasi — entity YUKLANMAGAN holat uchun ham.
    ///
    /// Kalendar darslarni to'liq entity sifatida emas, tor proyeksiya
    /// bilan o'qiydi (faqat kerakli ustunlar), shuning uchun qoida
    /// navigatsiyaga emas, ID'larga tayanadi. Ikki nusxa bo'lmasin deb
    /// entity'li variant ham shu metodni chaqiradi.
    /// </summary>
    private static bool IsHost(User user, long? hostId, long? teacherId, long? assistantId) =>
        user.Role is UserRole.Admin or UserRole.Academic
        || hostId == user.Id
        || teacherId == user.Id
        || assistantId == user.Id;

    /// <summary>
    /// Rol bo'yicha ko'rinish filtri — <see cref="ListForUserAsync"/> va
    /// <see cref="GetCalendarAsync"/> uchun YAGONA qoida.
    ///
    /// NIMA UCHUN AJRATILDI: ikki ro'yxat ikki xil filtrga ega bo'lsa,
    /// bittasida a'zolik tekshiruvi zaifroq qolishi mumkin edi — ya'ni
    /// kalendarda begona guruh darslari ko'rinib qolardi.
    /// </summary>
    private IQueryable<LiveSession> ScopeByRole(IQueryable<LiveSession> query, User user) =>
        user.Role switch
        {
            UserRole.Admin or UserRole.Academic => query,

            UserRole.Teacher or UserRole.Assistant =>
                query.Where(s => s.Group!.TeacherId == user.Id || s.Group!.AssistantId == user.Id),

            _ => query.Where(s => db.GroupMembers.Any(m =>
                    m.GroupId == s.GroupId &&
                    m.StudentId == user.Id &&
                    m.Status == MemberStatus.Active)),
        };

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

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    /// <summary>Kalendar so'rovining tor proyeksiyasi (butun entity tortilmaydi).</summary>
    private sealed record CalendarRow(
        long Id,
        long GroupId,
        string GroupName,
        string? Title,
        SessionType Type,
        SessionStatus Status,
        long? HostId,
        long? TeacherId,
        long? AssistantId,
        AttendanceStatus? MyAttendance,
        DateTimeOffset ScheduledStart,
        DateTimeOffset ScheduledEnd);
}
