using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Messaging.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Messaging.Services;

/// <summary>
/// ========================================================================
/// KURATOR YOZISHMASI — USE-CASE QATLAMI
/// ========================================================================
///
/// Uchta ish bor: (1) suhbatdoshni ANIQLASH va ruxsatni tekshirish,
/// (2) ma'lumotni N+1 siz o'qish, (3) Domain qoidasini chaqirish.
/// Xabarni yaratish va o'qilgan deb belgilash qoidasi
/// <see cref="DirectMessage"/> ichida.
///
/// ── QARZDORLIK DARVOZASI ATAYLAB QO'YILMADI ────────────────────────────
///
/// Eski tizim <c>curator-chat</c> ni ham <c>ensure_not_blocked</c> bilan
/// yopardi. v2 da bu QAYTARILMADI: kurator chati — o'quvchining markaz
/// bilan YAGONA yozma aloqa kanali. Uni qarz uchun yopish "to'lov haqida
/// gaplashish uchun avval to'lang" degan yopiq halqa hosil qiladi va
/// qarzni undirishni QIYINLASHTIRADI. Video darslar va jonli darsga
/// kirish esa avvalgidek bloklanadi (<c>PaymentBlockScope</c>).
/// </summary>
public sealed class DirectMessageService(
    IApplicationDbContext db,
    ICuratorDirectory curators,
    TimeProvider clock) : IDirectMessageService
{
    /// <summary>Suhbatlar ro'yxatidagi oxirgi xabar ko'chirmasining uzunligi.</summary>
    private const int PreviewLength = 120;

    private const int DefaultTake = 50;
    private const int MaxTake = 100;

    // ================================================================= suhbatlar

    public async Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(
        long userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);

        return user.Role == UserRole.Student
            ? await StudentConversationsAsync(user, ct)
            : await StaffConversationsAsync(user, ct);
    }

    private async Task<IReadOnlyList<ConversationDto>> StudentConversationsAsync(
        User student, CancellationToken ct)
    {
        var curator = await curators.ResolveCuratorAsync(student.Id, ct);

        // Kurator biriktirilmagan — bo'sh ro'yxat, XATO EMAS. Frontend
        // "Sizga hali kurator biriktirilmagan" deb ko'rsatadi.
        if (curator is null) return [];

        var stats = await db.DirectMessages.AsNoTracking()
            .Where(m => m.StudentId == student.Id && m.StaffId == curator.Id)
            .GroupBy(m => m.StaffId)
            .Select(g => new ThreadStats(
                g.Key,
                g.Max(m => m.Id),
                g.Count(m => m.SenderId != student.Id && !m.ReadByStudent)))
            .FirstOrDefaultAsync(ct);

        var last = await LoadLastMessagesAsync(stats is null ? [] : [stats.LastMessageId], ct);

        return
        [
            BuildConversation(
                peerId: curator.Id,
                peerName: curator.FullName,
                peerRole: curator.Role,
                groupName: null,
                stats: stats,
                last: stats is null ? null : last.GetValueOrDefault(stats.LastMessageId),
                viewerId: student.Id),
        ];
    }

    private async Task<IReadOnlyList<ConversationDto>> StaffConversationsAsync(
        User staff, CancellationToken ct)
    {
        var studentIds = await curators.StudentIdsAsync(staff.Id, ct);

        if (studentIds.Count == 0) return [];

        // ★ N+1 YO'Q: oxirgi xabar va o'qilmaganlar soni BITTA agregat
        // so'rovda. Eski tizim BUTUN yozishmani xotiraga tortib
        // (`select DmMessage where staff_id = ...` — cheklovsiz!), keyin
        // Python siklida oxirgisini topardi. 200 o'quvchili kuratorda bu
        // o'n minglab qator degani edi.
        var stats = await db.DirectMessages.AsNoTracking()
            .Where(m => m.StaffId == staff.Id && studentIds.Contains(m.StudentId))
            .GroupBy(m => m.StudentId)
            .Select(g => new ThreadStats(
                g.Key,
                g.Max(m => m.Id),
                g.Count(m => m.SenderId != staff.Id && !m.ReadByStaff)))
            .ToListAsync(ct);

        var statsByPeer = stats.ToDictionary(s => s.PeerId);

        var last = await LoadLastMessagesAsync(
            stats.ConvertAll(s => s.LastMessageId), ct);

        var peers = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id) && u.IsActive)
            .Select(u => new PeerRow(u.Id, u.FullName, u.Role))
            .ToListAsync(ct);

        var groupNames = await GroupNamesAsync(studentIds, ct);

        var conversations = peers.ConvertAll(peer =>
        {
            var threadStats = statsByPeer.GetValueOrDefault(peer.Id);

            return BuildConversation(
                peer.Id,
                peer.FullName,
                peer.Role,
                groupNames.GetValueOrDefault(peer.Id),
                threadStats,
                threadStats is null ? null : last.GetValueOrDefault(threadStats.LastMessageId),
                staff.Id);
        });

        // KURATOR ISH OQIMI TARTIBI (eski tizimdagi bilan bir xil niyat,
        // lekin soddaroq): avval o'qilmagan xabari borlar, keyin oxirgi
        // faollik bo'yicha yangi avval, oxirida — hali hech yozmaganlar.
        conversations.Sort(static (left, right) =>
        {
            var byUnread = right.UnreadCount.CompareTo(left.UnreadCount);
            if (byUnread != 0) return byUnread;

            var byRecency = Nullable.Compare(right.LastMessageAt, left.LastMessageAt);
            if (byRecency != 0) return byRecency;

            return string.CompareOrdinal(left.PeerName, right.PeerName);
        });

        return conversations;
    }

    // ================================================================= tarix

    public async Task<MessagePageDto> GetThreadAsync(
        long userId, long peerId, long? beforeId, int take, CancellationToken ct = default)
    {
        var pair = await ResolvePairAsync(userId, peerId, ct);

        take = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

        var query = db.DirectMessages.AsNoTracking()
            .Where(m => m.StudentId == pair.StudentId && m.StaffId == pair.StaffId);

        if (beforeId is { } cursor)
            query = query.Where(m => m.Id < cursor);

        // `take + 1` — "yana bormi?" savoliga qo'shimcha COUNT so'rovisiz
        // javob berish uchun klassik usul.
        var rows = await query
            .OrderByDescending(m => m.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        rows.Reverse();     // eskidan yangiga — ekranga shundayligicha chiziladi

        var lessonNames = await LessonNamesAsync(rows, ct);

        var unread = await UnreadCountAsync(pair, ct);

        var items = rows.ConvertAll(m => Map(m, userId, pair, lessonNames));

        return new MessagePageDto(
            peerId,
            pair.PeerName,
            items,
            hasMore,
            hasMore && rows.Count > 0 ? rows[0].Id : null,
            unread);
    }

    // ================================================================= yuborish

    public async Task<DirectMessageDto> SendAsync(
        long userId, long peerId, SendDirectMessageRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pair = await ResolvePairAsync(userId, peerId, ct);

        // Kontekst darsi HAQIQATAN mavjudligini tekshiramiz. Aks holda
        // yaroqsiz Id to'g'ridan-to'g'ri tashqi kalit xatosiga (500) olib
        // borardi — foydalanuvchiga tushunarsiz "ichki server xatosi".
        if (request.ModuleLessonId is { } lessonId)
        {
            var exists = await db.ModuleLessons.AsNoTracking().AnyAsync(l => l.Id == lessonId, ct);

            if (!exists)
                throw Invalid(nameof(request.ModuleLessonId), "Bunday dars topilmadi.");
        }

        // Domain qoidasi: matn tozalanadi, bo'sh rad etiladi, o'qilgan
        // bayroqlari yuboruvchiga qarab qo'yiladi.
        var message = DirectMessage.Create(
            pair.StudentId,
            pair.StaffId,
            userId,
            request.ModuleLessonId,
            request.Body,
            clock.GetUtcNow());

        db.DirectMessages.Add(message);
        await db.SaveChangesAsync(ct);

        var lessonNames = await LessonNamesAsync([message], ct);

        return Map(message, userId, pair, lessonNames);
    }

    // ================================================================= o'qildi

    public async Task<MarkReadResultDto> MarkReadAsync(
        long userId, long peerId, CancellationToken ct = default)
    {
        var pair = await ResolvePairAsync(userId, peerId, ct);

        // ★ FAQAT O'QILMAGANLAR TORTILADI. Ularning soni 1:1 suhbatda
        // tabiiy ravishda kichik, shuning uchun kuzatiladigan (tracked)
        // yangilash tanlandi: qoida Domain'da qoladi (`MarkRead` faqat
        // QARSHI tomon xabarini belgilaydi va idempotent), `UpdatedAt`
        // esa avtomatik yoziladi. `ExecuteUpdate` bilan ikkalasi ham
        // yo'qolardi va shart SQL'da qayta yozilib, ikkinchi haqiqat
        // manbai paydo bo'lardi.
        var unreadQuery = db.DirectMessages.AsTracking()
            .Where(m => m.StudentId == pair.StudentId
                     && m.StaffId == pair.StaffId
                     && m.SenderId != userId);

        unreadQuery = pair.ViewerIsStudent
            ? unreadQuery.Where(m => !m.ReadByStudent)
            : unreadQuery.Where(m => !m.ReadByStaff);

        var unread = await unreadQuery.ToListAsync(ct);

        if (unread.Count == 0)
            return new MarkReadResultDto(0, 0);

        var now = clock.GetUtcNow();
        var marked = unread.Count(message => message.MarkRead(userId, now));

        await db.SaveChangesAsync(ct);

        return new MarkReadResultDto(marked, 0);
    }

    // ================================================================= ruxsat

    /// <summary>
    /// ========================================================================
    /// RUXSATNING YAGONA JOYI
    /// ========================================================================
    ///
    /// <c>(userId, peerId)</c> juftligidan suhbat kalitini
    /// <c>(StudentId, StaffId)</c> chiqaradi. Juftlik qoidaga mos kelmasa
    /// bu yerda 403 bo'ladi — ya'ni HECH BIR metod tekshiruvni "unutib"
    /// qololmaydi, chunki suhbat kalitini boshqa yo'l bilan olishning
    /// iloji yo'q.
    /// </summary>
    private async Task<ConversationPair> ResolvePairAsync(
        long userId, long peerId, CancellationToken ct)
    {
        if (userId == peerId)
            throw new ForbiddenException("O'zingiz bilan yozisha olmaysiz.");

        var user = await LoadUserAsync(userId, ct);

        if (user.Role == UserRole.Student)
        {
            var curator = await curators.ResolveCuratorAsync(user.Id, ct)
                ?? throw new NotFoundException("Kurator", peerId);

            if (curator.Id != peerId)
                throw new ForbiddenException("Bu suhbatga ruxsatingiz yo'q.");

            return new ConversationPair(user.Id, curator.Id, ViewerIsStudent: true, curator.FullName);
        }

        // XODIM. Ruxsat ROLGA emas, BIRIKTIRUVGA qarab beriladi: kim
        // guruhga kurator qilib qo'yilgan bo'lsa — o'shaning o'quvchisi.
        // Shu sabab admin ham avtomatik kira olmaydi (izoh interfeysda).
        var studentIds = await curators.StudentIdsAsync(user.Id, ct);

        if (!studentIds.Contains(peerId))
            throw new ForbiddenException("Bu o'quvchi sizga biriktirilmagan.");

        var peer = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == peerId, ct)
            ?? throw new NotFoundException(nameof(User), peerId);

        return new ConversationPair(peer.Id, user.Id, ViewerIsStudent: false, peer.FullName);
    }

    // ================================================================= yordamchi

    private async Task<int> UnreadCountAsync(ConversationPair pair, CancellationToken ct)
    {
        var query = db.DirectMessages.AsNoTracking()
            .Where(m => m.StudentId == pair.StudentId && m.StaffId == pair.StaffId);

        return pair.ViewerIsStudent
            ? await query.CountAsync(m => m.SenderId != pair.StudentId && !m.ReadByStudent, ct)
            : await query.CountAsync(m => m.SenderId != pair.StaffId && !m.ReadByStaff, ct);
    }

    private async Task<Dictionary<long, DirectMessage>> LoadLastMessagesAsync(
        List<long> messageIds, CancellationToken ct)
    {
        if (messageIds.Count == 0) return [];

        return await db.DirectMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);
    }

    /// <summary>
    /// O'quvchi -> guruh nomi (kurator ro'yxatida ko'rsatiladi).
    /// Bitta o'quvchi bir nechta guruhda bo'lsa birinchisi olinadi.
    /// </summary>
    private async Task<Dictionary<long, string>> GroupNamesAsync(
        IReadOnlyCollection<long> studentIds, CancellationToken ct)
    {
        var rows = await db.GroupMembers.AsNoTracking()
            .Where(m => studentIds.Contains(m.StudentId)
                     && m.Status == MemberStatus.Active
                     && m.Group!.Type == GroupType.Group
                     && m.Group.IsActive)
            .OrderBy(m => m.Id)
            .Select(m => new { m.StudentId, GroupName = m.Group!.Name })
            .ToListAsync(ct);

        var names = new Dictionary<long, string>(rows.Count);

        foreach (var row in rows)
            names.TryAdd(row.StudentId, row.GroupName);

        return names;
    }

    private async Task<Dictionary<long, string>> LessonNamesAsync(
        IReadOnlyCollection<DirectMessage> messages, CancellationToken ct)
    {
        var lessonIds = messages
            .Where(m => m.ModuleLessonId is not null)
            .Select(m => m.ModuleLessonId!.Value)
            .Distinct()
            .ToList();

        if (lessonIds.Count == 0) return [];

        return await db.ModuleLessons.AsNoTracking()
            .Where(l => lessonIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);
    }

    private async Task<User> LoadUserAsync(long userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return user;
    }

    private static ConversationDto BuildConversation(
        long peerId,
        string peerName,
        UserRole peerRole,
        string? groupName,
        ThreadStats? stats,
        DirectMessage? last,
        long viewerId) =>
        new(peerId,
            peerName,
            peerRole.ToString(),
            groupName,
            last?.Id,
            last is null ? null : Preview(last.Body),
            last?.SentAt,
            last is null ? null : last.SenderId == viewerId,
            stats?.UnreadCount ?? 0);

    private static string Preview(string body) =>
        body.Length <= PreviewLength ? body : body[..PreviewLength];

    private static DirectMessageDto Map(
        DirectMessage message,
        long viewerId,
        ConversationPair pair,
        IReadOnlyDictionary<long, string> lessonNames)
    {
        var mine = message.SenderId == viewerId;

        // "Suhbatdosh o'qidimi" faqat MENING xabarim uchun ma'noli.
        var readByPeer = !mine
            || (pair.ViewerIsStudent ? message.ReadByStaff : message.ReadByStudent);

        return new DirectMessageDto(
            message.Id,
            message.SenderId,
            mine ? "Siz" : pair.PeerName,
            mine,
            message.Body,
            message.ModuleLessonId,
            message.ModuleLessonId is { } id ? lessonNames.GetValueOrDefault(id) : null,
            message.SentAt,
            readByPeer);
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ---------------------------------------------------------------- ichki shakllar

    /// <summary>Suhbat kaliti + ko'ruvchi qaysi tomonda turgani.</summary>
    private sealed record ConversationPair(
        long StudentId, long StaffId, bool ViewerIsStudent, string PeerName);

    private sealed record ThreadStats(long PeerId, long LastMessageId, int UnreadCount);

    private sealed record PeerRow(long Id, string FullName, UserRole Role);
}
