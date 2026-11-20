using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Application.SelfStudy.Pool;

/// <summary>Ro'yxatdagi bitta kurator va uning yuki.</summary>
/// <param name="ActiveStudents">Biriktirilgan va obunasi HOZIR faol o'quvchilar (sig'imni shular egallaydi).</param>
public sealed record CuratorPoolMemberDto(
    long CuratorId,
    string FullName,
    string Role,
    bool IsActive,
    int Capacity,
    int ActiveStudents,
    bool IsAccepting,
    DateTimeOffset AddedAt);

/// <summary>Mustaqil kuratorlari ro'yxati va navbat holati.</summary>
/// <param name="WaitingStudents">Faol obunali, guruhsiz, kuratorsiz o'quvchilar.</param>
/// <param name="FreeSlots">Qabul qilayotgan kuratorlardagi bo'sh joylar.</param>
/// <param name="NeedsNewCurator">Navbatda o'quvchi bor va bo'sh joy yo'q — eslatma sharti.</param>
public sealed record CuratorPoolDto(
    IReadOnlyList<CuratorPoolMemberDto> Members,
    int TotalCapacity,
    int FreeSlots,
    int WaitingStudents,
    bool NeedsNewCurator);

public sealed record AddCuratorPoolMemberRequest(long CuratorId, int? Capacity);

public sealed record UpdateCuratorPoolMemberRequest(int Capacity, bool IsAccepting);

/// <summary>Avtomatik taqsimlash natijasi.</summary>
public sealed record AutoAssignResultDto(int Assigned, int StillWaiting);

/// <summary>
/// Mustaqil kuratorlari ro'yxati va AVTOMATIK BIRIKTIRISH (2026-11-20).
/// Qoida va sabablar: <see cref="SelfStudyCuratorPoolMember"/>.
///
/// ★ QACHON TAQSIMLANADI: kurator qo'shilganda, sig'imi/holati o'zgarganda
///   (darhol) va har daqiqada fon vazifasida (<c>SelfStudyAutoAssignJob</c>) —
///   obuna qayerdan kelmasin (xodim berdi, buyurtma tasdiqlandi, balansdan
///   to'landi), o'quvchi ko'pi bilan bir daqiqada kuratorli bo'ladi. Obuna
///   oqimiga to'g'ridan-to'g'ri ulanmadi: u ba'zan boshqa tranzaksiya ichida
///   chaqiriladi va u yerdagi xato to'lovning o'zini yiqitardi.
/// </summary>
public interface ICuratorPoolService
{
    Task<CuratorPoolDto> GetAsync(long actorId, CancellationToken ct = default);

    Task<CuratorPoolDto> AddAsync(AddCuratorPoolMemberRequest request, long actorId, CancellationToken ct = default);

    Task<CuratorPoolDto> UpdateAsync(
        long curatorId, UpdateCuratorPoolMemberRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Ro'yxatdan chiqaradi — mavjud biriktirishlar QOLADI.</summary>
    Task<CuratorPoolDto> RemoveAsync(long curatorId, long actorId, CancellationToken ct = default);

    /// <summary>Panel tugmasi: navbatni hozir taqsimlash.</summary>
    Task<AutoAssignResultDto> RunAsync(long actorId, CancellationToken ct = default);

    /// <summary>Fon vazifasi uchun — ruxsat tekshiruvisiz.</summary>
    Task<AutoAssignResultDto> AutoAssignAsync(CancellationToken ct = default);
}

public sealed class CuratorPoolService(
    IApplicationDbContext db,
    INotificationOutbox outbox,
    ISettingsResolver settings,
    TimeProvider clock,
    ILogger<CuratorPoolService> logger) : ICuratorPoolService
{
    private static readonly UserRole[] CuratorRoles = [UserRole.Assistant, UserRole.Teacher];

    public async Task<CuratorPoolDto> GetAsync(long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);
        return await SnapshotAsync(ct);
    }

    public async Task<CuratorPoolDto> AddAsync(
        AddCuratorPoolMemberRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);

        var curator = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.CuratorId, ct);
        if (curator is null || !curator.IsActive || !CuratorRoles.Contains(curator.Role))
            throw Invalid("curatorId", "Faqat faol kurator yoki ustoz qo'shiladi.");

        if (await db.SelfStudyCuratorPoolMembers.AnyAsync(m => m.CuratorId == curator.Id, ct))
            throw new ConflictException("Bu xodim allaqachon Mustaqil kuratorlari ro'yxatida.");

        try
        {
            db.SelfStudyCuratorPoolMembers.Add(
                SelfStudyCuratorPoolMember.Create(curator.Id, request.Capacity, actor.Id, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            throw Invalid("capacity", ex.Message);
        }
        catch (DbUpdateException ex)
        {
            throw new ConflictException("Bu xodim allaqachon ro'yxatga qo'shilgan — sahifani yangilang.", ex);
        }

        await AutoAssignAsync(ct);
        return await SnapshotAsync(ct);
    }

    public async Task<CuratorPoolDto> UpdateAsync(
        long curatorId, UpdateCuratorPoolMemberRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureManagerAsync(actorId, ct);

        var member = await db.SelfStudyCuratorPoolMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.CuratorId == curatorId, ct)
            ?? throw new NotFoundException(nameof(SelfStudyCuratorPoolMember), curatorId);

        try
        {
            member.SetCapacity(request.Capacity);
        }
        catch (DomainException ex)
        {
            throw Invalid("capacity", ex.Message);
        }

        member.IsAccepting = request.IsAccepting;
        await db.SaveChangesAsync(ct);

        await AutoAssignAsync(ct);
        return await SnapshotAsync(ct);
    }

    public async Task<CuratorPoolDto> RemoveAsync(long curatorId, long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var member = await db.SelfStudyCuratorPoolMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.CuratorId == curatorId, ct);
        if (member is not null)
        {
            db.SelfStudyCuratorPoolMembers.Remove(member);
            await db.SaveChangesAsync(ct);
        }

        return await SnapshotAsync(ct);
    }

    public async Task<AutoAssignResultDto> RunAsync(long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);
        return await AutoAssignAsync(ct);
    }

    public async Task<AutoAssignResultDto> AutoAssignAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var waiting = await WaitingStudents(now)
            .Select(u => new
            {
                u.Id,
                // Kim birinchi kelgan bo'lsa — birinchi biriktiriladi.
                Since = db.SubscriptionPeriods
                    .Where(p => p.StudentId == u.Id && p.CancelledAt == null)
                    .Min(p => p.CreatedAt),
            })
            .OrderBy(r => r.Since)
            .ThenBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (waiting.Count == 0) return new AutoAssignResultDto(0, 0);

        var members = await MembersWithLoadAsync(now, ct);
        var open = members
            .Where(m => m.IsAccepting && m.CuratorActive)
            .OrderBy(m => m.AddedAt)
            .ThenBy(m => m.Id)
            .ToList();

        var assigned = 0;
        foreach (var studentId in waiting)
        {
            // ★ KETMA-KET TO'LDIRISH: ro'yxatdagi BIRINCHI bo'sh kurator.
            var target = open.FirstOrDefault(m => m.Load < m.Capacity);
            if (target is null) break;

            var assignment = SelfStudyCuratorAssignment.Create(
                studentId, target.CuratorId, backupCuratorId: null, target.AddedById, now);
            db.SelfStudyCuratorAssignments.Add(assignment);

            try
            {
                await db.SaveChangesAsync(ct);
                target.Load++;
                assigned++;
            }
            catch (DbUpdateException ex)
            {
                // Shu paytda xodim qo'lda biriktirgan (qisman unikal indeks) —
                // bu o'quvchi endi navbatda emas, keyingisiga o'tamiz.
                // `Added` yozuvni `Remove` kuzatuvdan chiqaradi: keyingi
                // `SaveChanges` uni qayta yozishga urinmaydi.
                db.SelfStudyCuratorAssignments.Remove(assignment);
                CuratorPoolLog.AssignRaced(logger, ex, studentId);
            }
        }

        var stillWaiting = waiting.Count - assigned;
        if (assigned > 0) CuratorPoolLog.Assigned(logger, assigned, stillWaiting);

        if (stillWaiting > 0)
            await RemindStaffAsync(stillWaiting, open.Sum(m => m.Capacity), open.Count, now, ct);

        return new AutoAssignResultDto(assigned, stillWaiting);
    }

    // ================================================================ yordamchilar

    /// <summary>
    /// Navbat: faol obunali, faol guruh a'zosi emas, faol kuratorsiz o'quvchi
    /// (<c>SelfStudyCuratorService.ListUnassignedAsync</c> bilan AYNI shart +
    /// guruh tekshiruvi — guruh o'quvchisining kuratori guruhdan keladi).
    /// </summary>
    private IQueryable<User> WaitingStudents(DateTimeOffset now) =>
        db.Users.AsNoTracking().Where(u =>
            u.Role == UserRole.Student
            && u.IsActive
            && db.SubscriptionPeriods.Any(p =>
                p.StudentId == u.Id && p.CancelledAt == null && p.StartsAt <= now && now < p.EndsAt)
            && !db.GroupMembers.Any(m => m.StudentId == u.Id && m.Status == MemberStatus.Active)
            && !db.SelfStudyCuratorAssignments.Any(a => a.StudentId == u.Id && a.EndedAt == null));

    private async Task<List<MemberLoad>> MembersWithLoadAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.SelfStudyCuratorPoolMembers.AsNoTracking()
            .Select(m => new MemberLoad
            {
                Id = m.Id,
                CuratorId = m.CuratorId,
                FullName = m.Curator!.FullName,
                Role = m.Curator.Role,
                CuratorActive = m.Curator.IsActive,
                Capacity = m.Capacity,
                IsAccepting = m.IsAccepting,
                AddedById = m.AddedById,
                AddedAt = m.AddedAt,
                Load = db.SelfStudyCuratorAssignments.Count(a =>
                    a.EndedAt == null && a.CuratorId == m.CuratorId
                    && db.SubscriptionPeriods.Any(p =>
                        p.StudentId == a.StudentId && p.CancelledAt == null && p.StartsAt <= now && now < p.EndsAt)),
            })
            .ToListAsync(ct);

    private async Task<CuratorPoolDto> SnapshotAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var members = await MembersWithLoadAsync(now, ct);
        var waiting = await WaitingStudents(now).CountAsync(ct);

        var open = members.Where(m => m.IsAccepting && m.CuratorActive).ToList();
        var free = open.Sum(m => Math.Max(0, m.Capacity - m.Load));

        var rows = members
            .OrderBy(m => m.AddedAt)
            .ThenBy(m => m.Id)
            .Select(m => new CuratorPoolMemberDto(
                m.CuratorId, m.FullName, m.Role.ToString(), m.CuratorActive,
                m.Capacity, m.Load, m.IsAccepting, m.AddedAt))
            .ToList();

        return new CuratorPoolDto(rows, open.Sum(m => m.Capacity), free, waiting, waiting > 0 && free == 0);
    }

    /// <summary>
    /// Xodimlar guruhiga eslatma — KUNIGA BIR MARTA (idempotentlik kaliti sanada).
    /// Guruh sozlanmagan bo'lsa jim: eslatma panelda baribir ko'rinadi.
    /// </summary>
    private async Task RemindStaffAsync(
        int waiting, int totalCapacity, int curators, DateTimeOffset now, CancellationToken ct)
    {
        if (!SettingsRegistry.TryGet(SettingsRegistry.Keys.SalesStaffChatId, out var definition)) return;

        var raw = (await settings.ResolveAsync(definition, ct)).Value?.Trim();
        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var chatId)
            || chatId == 0)
        {
            return;
        }

        var queued = await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                TemplateKey = TelegramTemplates.SelfStudyCuratorsFull,
                Body = TelegramTemplates.SelfStudyCuratorsFullText(waiting, totalCapacity, curators),
                IdempotencyKey = string.Create(
                    CultureInfo.InvariantCulture, $"selfstudy_curators_full:{now.UtcDateTime:yyyy-MM-dd}"),
            },
            ct);

        if (queued) await db.SaveChangesAsync(ct);
    }

    private async Task<User> EnsureManagerAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive || actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Kuratorlar ro'yxatini faqat o'quv bo'limi yoki administrator boshqaradi.");

        return actor;
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private sealed class MemberLoad
    {
        public long Id { get; init; }
        public long CuratorId { get; init; }
        public string FullName { get; init; } = string.Empty;
        public UserRole Role { get; init; }
        public bool CuratorActive { get; init; }
        public int Capacity { get; init; }
        public bool IsAccepting { get; init; }
        public long AddedById { get; init; }
        public DateTimeOffset AddedAt { get; init; }
        public int Load { get; set; }
    }
}

/// <summary>Manba-generatsiyali loglar (CA1848).</summary>
internal static partial class CuratorPoolLog
{
    [LoggerMessage(EventId = 7410, Level = LogLevel.Information,
        Message = "Mustaqil: {Assigned} ta o'quvchi kuratorga avtomatik biriktirildi, navbatda {Waiting} ta.")]
    internal static partial void Assigned(ILogger logger, int assigned, int waiting);

    [LoggerMessage(EventId = 7411, Level = LogLevel.Warning,
        Message = "Mustaqil: avtomatik biriktirishda to'qnashuv (qo'lda biriktirilgan bo'lishi mumkin). student={StudentId}")]
    internal static partial void AssignRaced(ILogger logger, Exception exception, long studentId);
}
