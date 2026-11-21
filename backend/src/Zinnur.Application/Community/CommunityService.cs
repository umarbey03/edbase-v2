using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Application.Community;

// ================================================================ DTO

/// <param name="Enabled">Sozlamada yoqilganmi (o'chiq paytda faqat xodim kiradi).</param>
/// <param name="CanRead">Xonani ko'ra oladimi.</param>
/// <param name="CanWrite">Hozir yoza oladimi (to'xtatilmagan).</param>
/// <param name="IsModerator">Xodim: o'chirish va to'xtatish huquqi.</param>
/// <param name="MutedUntil">Yozishdan to'xtatilgan bo'lsa — qachongacha.</param>
public sealed record CommunityAccessDto(
    bool Enabled, bool CanRead, bool CanWrite, bool IsModerator, DateTimeOffset? MutedUntil, int MemberCount, int Unread);

public sealed record CommunityReplyDto(long Id, string AuthorName, string Snippet, bool Deleted);

/// <param name="AuthorKind">Student yoki Staff (xodim belgisi bilan ko'rinadi).</param>
public sealed record CommunityMessageDto(
    long Id,
    long AuthorId,
    string AuthorName,
    string AuthorKind,
    bool Mine,
    string Body,
    bool Deleted,
    CommunityReplyDto? ReplyTo,
    DateTimeOffset CreatedAt);

public sealed record CommunityPageDto(IReadOnlyList<CommunityMessageDto> Items, bool HasMore);

public sealed record SendCommunityMessageRequest(string? Body, long? ReplyToId);

public sealed record MuteCommunityMemberRequest(int Hours, string? Reason);

public interface ICommunityService
{
    Task<CommunityAccessDto> GetAccessAsync(long userId, CancellationToken ct = default);

    Task<CommunityPageDto> ListAsync(long userId, long? beforeId, long? afterId, int take, CancellationToken ct = default);

    Task<CommunityMessageDto> SendAsync(long userId, SendCommunityMessageRequest request, CancellationToken ct = default);

    Task DeleteAsync(long userId, long messageId, CancellationToken ct = default);

    Task MarkReadAsync(long userId, long lastId, CancellationToken ct = default);

    Task MuteAsync(long moderatorId, long studentId, MuteCommunityMemberRequest request, CancellationToken ct = default);

    Task UnmuteAsync(long moderatorId, long studentId, CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// HAMJAMIYAT XIZMATI (2026-11-21) — qoidalar
/// ════════════════════════════════════════════════════════════════════
///
/// KIM KIRADI:
///   • o'quvchi — obunasi HOZIR faol (Mustaqil) va sozlama yoqilgan;
///   • xodim (moderator) — Admin, O'quv bo'limi va Mustaqil kuratorlar
///     ro'yxatidagi kurator/ustoz. Xodim sozlama o'chiq paytda ham kiradi.
///
/// XAVFSIZLIK (o'quvchi xabarlari uchun; xodimga tegmaydi):
///   • telefon raqami va havola — rad etiladi (reklama, tashqi sotuv);
///   • tezlik: <see cref="MinSecondsBetween"/> soniyada bitta xabar;
///   • ism "Ism F." ko'rinishida — to'liq familiya va telefon ko'rinmaydi.
/// </summary>
public sealed partial class CommunityService(
    IApplicationDbContext db,
    ISettingsResolver settings,
    TimeProvider clock) : ICommunityService
{
    public const int MinSecondsBetween = 3;
    public const int MaxTake = 100;
    public const int MaxMuteHours = 24 * 30;

    private static readonly SettingDefinition EnabledSetting =
        SettingsRegistry.TryGet(SettingsRegistry.Keys.CommunityEnabled, out var d)
            ? d
            : throw new InvalidOperationException("community.enabled registrda yo'q");

    // ================================================================ kirish

    private sealed record Viewer(long Id, UserRole Role, bool IsModerator, bool IsMember, bool Enabled)
    {
        public bool CanRead => IsModerator || (Enabled && IsMember);
    }

    private async Task<Viewer> ResolveAsync(long userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Id, u.Role })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new ForbiddenException("Foydalanuvchi topilmadi.");

        var resolved = await settings.ResolveManyAsync([EnabledSetting], ct).ConfigureAwait(false);
        var enabled = SettingValueParser.TryReadBool(resolved[0].Value, out var on) && on;

        var now = clock.GetUtcNow();
        var isModerator = user.Role is UserRole.Admin or UserRole.Academic
            || (user.Role is UserRole.Assistant or UserRole.Teacher
                && await db.SelfStudyCuratorPoolMembers.AsNoTracking()
                    .AnyAsync(m => m.CuratorId == userId, ct).ConfigureAwait(false));

        var isMember = user.Role == UserRole.Student
            && await db.SubscriptionPeriods.AsNoTracking()
                .AnyAsync(p => p.StudentId == userId && p.CancelledAt == null && p.StartsAt <= now && p.EndsAt > now, ct)
                .ConfigureAwait(false);

        return new Viewer(user.Id, user.Role, isModerator, isMember, enabled);
    }

    private async Task<Viewer> RequireReaderAsync(long userId, CancellationToken ct)
    {
        var viewer = await ResolveAsync(userId, ct).ConfigureAwait(false);
        if (!viewer.CanRead)
            throw new ForbiddenException(viewer.IsMember
                ? "Hamjamiyat hozircha yopiq."
                : "Hamjamiyat faqat obunasi faol Mustaqil o'quvchilar uchun.");
        return viewer;
    }

    private async Task<Viewer> RequireModeratorAsync(long userId, CancellationToken ct)
    {
        var viewer = await ResolveAsync(userId, ct).ConfigureAwait(false);
        if (!viewer.IsModerator) throw new ForbiddenException("Faqat moderator (kurator, o'quv bo'limi).");
        return viewer;
    }

    public async Task<CommunityAccessDto> GetAccessAsync(long userId, CancellationToken ct = default)
    {
        var viewer = await ResolveAsync(userId, ct).ConfigureAwait(false);
        if (!viewer.CanRead)
            return new CommunityAccessDto(viewer.Enabled, false, false, false, null, 0, 0);

        var now = clock.GetUtcNow();
        var mutedUntil = viewer.IsModerator ? null : await MutedUntilAsync(userId, now, ct).ConfigureAwait(false);

        var members = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.CancelledAt == null && p.StartsAt <= now && p.EndsAt > now && p.Student!.IsActive)
            .Select(p => p.StudentId)
            .Distinct()
            .CountAsync(ct)
            .ConfigureAwait(false);

        var lastRead = await db.CommunityReadStates.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => (long?)r.LastReadMessageId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? 0;

        var unread = await db.CommunityMessages.AsNoTracking()
            .CountAsync(m => m.Id > lastRead && m.AuthorId != userId && m.DeletedAt == null, ct)
            .ConfigureAwait(false);

        return new CommunityAccessDto(viewer.Enabled, true, mutedUntil is null, viewer.IsModerator, mutedUntil, members, unread);
    }

    private Task<DateTimeOffset?> MutedUntilAsync(long userId, DateTimeOffset now, CancellationToken ct) =>
        db.CommunityMutes.AsNoTracking()
            .Where(m => m.UserId == userId && m.MutedUntil > now)
            .Select(m => (DateTimeOffset?)m.MutedUntil)
            .FirstOrDefaultAsync(ct);

    // ================================================================ o'qish

    public async Task<CommunityPageDto> ListAsync(
        long userId, long? beforeId, long? afterId, int take, CancellationToken ct = default)
    {
        await RequireReaderAsync(userId, ct).ConfigureAwait(false);
        take = Math.Clamp(take, 1, MaxTake);

        var query = db.CommunityMessages.AsNoTracking();
        List<MessageRow> rows;

        if (afterId is { } after)
        {
            rows = await Project(query.Where(m => m.Id > after).OrderBy(m => m.Id).Take(take + 1))
                .ToListAsync(ct).ConfigureAwait(false);
            var more = rows.Count > take;
            return new CommunityPageDto([.. rows.Take(take).Select(r => ToDto(r, userId))], more);
        }

        if (beforeId is { } before) query = query.Where(m => m.Id < before);
        rows = await Project(query.OrderByDescending(m => m.Id).Take(take + 1)).ToListAsync(ct).ConfigureAwait(false);
        var hasMore = rows.Count > take;
        return new CommunityPageDto([.. rows.Take(take).OrderBy(r => r.Id).Select(r => ToDto(r, userId))], hasMore);
    }

    private sealed record MessageRow(
        long Id, long AuthorId, string AuthorName, UserRole AuthorRole, string Body, bool Deleted,
        long? ReplyId, string? ReplyAuthor, string? ReplyBody, bool ReplyDeleted, DateTimeOffset CreatedAt);

    private static IQueryable<MessageRow> Project(IQueryable<CommunityMessage> query) =>
        query.Select(m => new MessageRow(
            m.Id,
            m.AuthorId,
            m.Author!.FullName,
            m.Author.Role,
            m.Body,
            m.DeletedAt != null,
            m.ReplyToId,
            m.ReplyTo != null ? m.ReplyTo.Author!.FullName : null,
            m.ReplyTo != null ? m.ReplyTo.Body : null,
            m.ReplyTo != null && m.ReplyTo.DeletedAt != null,
            m.CreatedAt));

    private static CommunityMessageDto ToDto(MessageRow r, long viewerId) => new(
        r.Id,
        r.AuthorId,
        CommunityNames.Display(r.AuthorName),
        r.AuthorRole == UserRole.Student ? "Student" : "Staff",
        r.AuthorId == viewerId,
        r.Deleted ? string.Empty : r.Body,
        r.Deleted,
        r.ReplyId is { } replyId
            ? new CommunityReplyDto(
                replyId,
                CommunityNames.Display(r.ReplyAuthor ?? string.Empty),
                r.ReplyDeleted ? string.Empty : Snippet(r.ReplyBody),
                r.ReplyDeleted)
            : null,
        r.CreatedAt);

    private static string Snippet(string? body) =>
        body is null ? string.Empty : body.Length <= 80 ? body : body[..80] + "…";

    // ================================================================ yozish

    public async Task<CommunityMessageDto> SendAsync(
        long userId, SendCommunityMessageRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewer = await RequireReaderAsync(userId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();

        if (!viewer.IsModerator)
        {
            if (await MutedUntilAsync(userId, now, ct).ConfigureAwait(false) is { } until)
                throw new ForbiddenException($"Siz hamjamiyatda vaqtincha yoza olmaysiz ({until:dd.MM HH:mm} gacha).");

            var body = request.Body ?? string.Empty;
            if (PhoneRegex().IsMatch(body) || LinkRegex().IsMatch(body))
                throw Invalid("Hamjamiyatda telefon raqami va havola yuborilmaydi — shaxsiy ma'lumotingizni asrang.");

            var since = now.AddSeconds(-MinSecondsBetween);
            if (await db.CommunityMessages.AnyAsync(m => m.AuthorId == userId && m.CreatedAt > since, ct).ConfigureAwait(false))
                throw new TooManyRequestsException("Biroz sekinroq — bir necha soniyadan keyin yozing.", MinSecondsBetween);
        }

        if (request.ReplyToId is { } replyId
            && !await db.CommunityMessages.AnyAsync(m => m.Id == replyId, ct).ConfigureAwait(false))
            throw new NotFoundException("Xabar", replyId);

        CommunityMessage message;
        try
        {
            message = CommunityMessage.Create(userId, request.Body, request.ReplyToId, now);
        }
        catch (DomainException ex)
        {
            throw Invalid(ex.Message);
        }

        db.CommunityMessages.Add(message);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await MarkReadAsync(userId, message.Id, ct).ConfigureAwait(false);

        var row = await Project(db.CommunityMessages.AsNoTracking().Where(m => m.Id == message.Id))
            .FirstAsync(ct).ConfigureAwait(false);
        return ToDto(row, userId);
    }

    public async Task DeleteAsync(long userId, long messageId, CancellationToken ct = default)
    {
        var viewer = await RequireReaderAsync(userId, ct).ConfigureAwait(false);
        var message = await db.CommunityMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("Xabar", messageId);

        if (message.AuthorId != userId && !viewer.IsModerator)
            throw new ForbiddenException("Faqat o'z xabaringizni o'chira olasiz.");

        message.Delete(userId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkReadAsync(long userId, long lastId, CancellationToken ct = default)
    {
        var state = await db.CommunityReadStates.FirstOrDefaultAsync(r => r.UserId == userId, ct).ConfigureAwait(false);
        if (state is null)
        {
            db.CommunityReadStates.Add(new CommunityReadState { UserId = userId, LastReadMessageId = lastId });
        }
        else if (lastId > state.LastReadMessageId)
        {
            state.LastReadMessageId = lastId;
            state.UpdatedAt = clock.GetUtcNow();
        }
        else
        {
            return;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ================================================================ moderatsiya

    public async Task MuteAsync(
        long moderatorId, long studentId, MuteCommunityMemberRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireModeratorAsync(moderatorId, ct).ConfigureAwait(false);
        if (request.Hours is < 1 or > MaxMuteHours)
            throw Invalid($"Muddat 1 soatdan {MaxMuteHours} soatgacha.");

        var isStudent = await db.Users.AnyAsync(u => u.Id == studentId && u.Role == UserRole.Student, ct).ConfigureAwait(false);
        if (!isStudent) throw new NotFoundException("O'quvchi", studentId);

        var now = clock.GetUtcNow();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > CommunityMute.MaxReasonLength }) reason = reason[..CommunityMute.MaxReasonLength];

        var mute = await db.CommunityMutes.FirstOrDefaultAsync(m => m.UserId == studentId, ct).ConfigureAwait(false);
        if (mute is null)
        {
            mute = new CommunityMute { UserId = studentId, CreatedAt = now };
            db.CommunityMutes.Add(mute);
        }

        mute.MutedUntil = now.AddHours(request.Hours);
        mute.Reason = reason;
        mute.MutedById = moderatorId;
        mute.UpdatedAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UnmuteAsync(long moderatorId, long studentId, CancellationToken ct = default)
    {
        await RequireModeratorAsync(moderatorId, ct).ConfigureAwait(false);
        var mute = await db.CommunityMutes.FirstOrDefaultAsync(m => m.UserId == studentId, ct).ConfigureAwait(false);
        if (mute is null) return;
        db.CommunityMutes.Remove(mute);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["body"] = [message] });

    /// <summary>7 va undan ko'p raqam (bo'shliq, chiziqcha, qavs bilan) — telefon raqami.</summary>
    [GeneratedRegex(@"(?:\+?\d[\s\-\(\)]*){7,}", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    /// <summary>Havola: http(s), www., t.me, domen (.uz/.com/.ru...) yoki @username.</summary>
    [GeneratedRegex(
        @"(https?://|www\.|t\.me/|telegram\.me/|\b[\w-]+\.(uz|com|ru|org|net|me|io|app|link|info)\b|@[A-Za-z][A-Za-z0-9_]{4,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();
}

/// <summary>
/// "Ism F." — hamjamiyatda to'liq familiya ko'rinmaydi (2026-11-21).
///
/// ★ BAZADA ISM VA FAMILIYA ALOHIDA EMAS, tartibi ham har xil ("Xursanaliyev
///   Ulug'bek" va "Uluğbek Xursanaliyev"). Birinchi so'z familiya
///   qo'shimchasi bilan tugasa (-ov, -ev, -ova, -eva, -zoda...), joylari
///   almashtiriladi.
/// </summary>
public static class CommunityNames
{
    private static readonly string[] SurnameEndings =
        ["ova", "eva", "yeva", "ov", "ev", "yev", "zoda", "zade", "skiy", "skaya", "iy", "bekov", "boyev"];

    public static string Display(string? fullName)
    {
        var parts = (fullName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "O'quvchi";
        if (parts.Length == 1) return parts[0];

        var (first, last) = LooksLikeSurname(parts[0]) && !LooksLikeSurname(parts[1])
            ? (parts[1], parts[0])
            : (parts[0], parts[1]);

        return $"{first} {char.ToUpperInvariant(last[0])}.";
    }

    private static bool LooksLikeSurname(string word)
    {
        var w = word.ToLowerInvariant();
        return SurnameEndings.Any(e => w.EndsWith(e, StringComparison.Ordinal));
    }
}
