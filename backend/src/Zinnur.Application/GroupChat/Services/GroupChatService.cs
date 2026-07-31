using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Application.Messaging.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.GroupChat.Services;

/// <summary>
/// ========================================================================
/// GURUH CHATI — USE-CASE QATLAMI
/// ========================================================================
///
/// ── 1. RUXSAT QOIDASI O'ZIMDAN TO'QILMAGAN ─────────────────────────────
///
/// "Kim shu guruhga tegishli" savoliga javob loyihada ALLAQACHON bor va
/// shu yerda AYNAN o'sha manbalar ishlatiladi:
///
///   • O'QUVCHI      — <c>GroupMembers.Status == Active</c>. Aynan shu shart
///                     <c>LiveSessionService.LoadAndAuthorizeAsync</c>,
///                     <c>CuratorDirectory</c> va <c>DirectMessageService</c>
///                     da turibdi (va eski ilovadagi <c>_is_member</c> da).
///   • USTOZ         — <c>Group.TeacherId</c>.
///   • KURATOR       — <see cref="ICuratorDirectory.ScopeGroupIdsAsync"/>.
///                     ★ Bu MUHIM: kurator guruhga IKKI yo'l bilan
///                     bog'lanadi — bevosita <c>AssistantId</c> orqali yoki
///                     <c>CuratorGroupId</c> havolasi orqali. Shartni bu
///                     yerda qo'lda yozsam, ikkinchi yo'l tushib qolardi va
///                     bog'lanish qilingan markazlarda kurator o'ziga
///                     yozilgan savollarni KO'RMASDI — eski tizimdagi
///                     xatoning aynan o'zi (izohi <c>ICuratorDirectory</c> da).
///   • ACADEMIC/ADMIN — hammasi (nazorat). Eski ilovada ham admin har
///                     ikkala kanalni ocha olardi (<c>_channel_for</c>).
///
/// ── 2. KANAL IZOLYATSIYASI ─────────────────────────────────────────────
///
/// Ustoz FAQAT <see cref="GroupChatChannel.Teacher"/>, kurator FAQAT
/// <see cref="GroupChatChannel.Curator"/> oqimini ko'radi. O'quvchi
/// ikkalasini ham — ular uning O'Z savollari.
///
/// ★ ESKI ILOVADAN FARQ (ataylab): eski <c>_channel_for</c> ruxsat
/// etilmagan kanal so'ralganda JIMGINA foydalanuvchining o'z kanaliga
/// almashtirardi. Ya'ni ustoz "kurator oqimini ochdim" deb o'ylab, aslida
/// o'z oqimini ko'rib turardi. Bu yerda bunday so'rov 403 oladi: jimgina
/// noto'g'ri ma'lumot ko'rsatishdan ko'ra aniq rad javobi xavfsizroq.
///
/// ── 3. PAUZADAGI/CHIQARILGAN O'QUVCHI ──────────────────────────────────
///
/// Ko'ra OLMAYDI (<c>Status != Active</c>). Sabab: eski ilovadagi
/// <c>_is_member</c> izohida aynan shunday yozilgan ("Chiqarilgan/pauza/
/// o'tgan o'quvchi ... kira olmaydi") va v2 da HAR modul shu shartni
/// ishlatadi. Guruh chatida yumshoqroq qoida qo'ysam, ikkinchi haqiqat
/// manbai paydo bo'lardi: pauzadagi o'quvchi darsga kira olmay, lekin
/// dars muhokamasini o'qib turardi.
///
/// ── 4. ARXIVLANGAN GURUH ───────────────────────────────────────────────
///
/// Chat YOPIQ (403) — o'quvchiga ham, xodimga ham. Eski ilovada ham
/// shunday edi: <c>_is_member</c> <c>Group.status == "active"</c> talab
/// qilardi, <c>_own_group</c> esa arxivlangan guruhga 404 berardi.
/// </summary>
public sealed class GroupChatService(
    IApplicationDbContext db,
    ICuratorDirectory curators,
    ICacheService cache,
    IGroupChatNotifier notifier,
    TimeProvider clock) : IGroupChatService
{
    /// <summary>Ro'yxatdagi oxirgi xabar ko'chirmasining uzunligi.</summary>
    private const int PreviewLength = 120;

    private const int DefaultTake = 50;
    private const int MaxTake = 100;

    /// <summary>
    /// "Chatlar" ro'yxatidagi maksimal qator soni.
    ///
    /// Ustoz/kuratorda bu chegaraga umuman yetilmaydi (o'nlab guruh), lekin
    /// admin BARCHA guruhlarni ko'radi va markaz o'sganda ro'yxat cheksiz
    /// uzayib ketardi. Eng faol oqimlar tepada bo'lgani uchun kesish
    /// foydalanuvchi uchun sezilmaydi.
    /// </summary>
    private const int MaxThreads = 200;

    /// <summary>
    /// TEZLIK CHEGARASI: <see cref="RateLimitWindow"/> ichida ko'pi bilan
    /// shuncha xabar.
    ///
    /// ★ NIMA UCHUN JONLI DARSDAGIDAN (1 xabar / 2 sekund) BOSHQACHA:
    /// dars chati — 200 kishilik xonaga bir vaqtda tarqatiladigan oqim,
    /// u yerda qat'iy sovish oralig'i o'rinli. Guruh chati esa 30 kishilik
    /// yozishma: odam savolini uch qatorga bo'lib yozadi va har qatorda
    /// 2 sekund kutishga majbur bo'lsa, funksiya ishlatib bo'lmaydigan
    /// bo'lardi. Shuning uchun bu yerda BURSTGA ruxsat beruvchi oyna:
    /// ketma-ket bir necha xabar o'tadi, lekin flood to'xtatiladi.
    /// </summary>
    private const int RateLimitMaxMessages = 10;

    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);

    /// <summary>O'quvchi va nazoratchi rollar ko'radigan ikkala oqim.</summary>
    private static readonly GroupChatChannel[] BothChannels =
        [GroupChatChannel.Teacher, GroupChatChannel.Curator];

    // ================================================================= chatlar hubi

    /// <inheritdoc />
    public async Task<IReadOnlyList<GroupChatThreadDto>> ListThreadsAsync(
        long userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);

        var threads = await AccessibleThreadsAsync(user, ct);

        if (threads.Count == 0) return [];

        var groupIds = threads.Select(t => t.GroupId).Distinct().ToList();

        // ============================================================
        // ★ N+1 YO'Q. N ta guruh uchun so'rovlar soni O'ZGARMAYDI:
        //   (1) oqim boshiga oxirgi xabar Id'si  — bitta agregat,
        //   (2) o'sha xabarlarning o'zi          — bitta IN so'rovi,
        //   (3) o'qilmaganlar soni               — bitta agregat.
        //
        // Naif yo'l (har guruh uchun alohida so'rov) 40 guruhli ustozda
        // 80+ borish-kelish degani edi va "Chatlar" ekrani sekundlab
        // ochilardi.
        // ============================================================
        var lastIds = await db.GroupChatMessages.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId))
            .GroupBy(m => new { m.GroupId, m.Channel })
            .Select(g => g.Max(m => m.Id))
            .ToListAsync(ct);

        var lastMessages = lastIds.Count == 0
            ? []
            : await db.GroupChatMessages.AsNoTracking()
                .Where(m => lastIds.Contains(m.Id))
                .ToListAsync(ct);

        var lastByThread = lastMessages.ToDictionary(
            m => new ThreadKey(m.GroupId, m.Channel));

        var unread = await UnreadByThreadAsync(userId, groupIds, ct);

        var rows = threads.ConvertAll(thread =>
        {
            var key = new ThreadKey(thread.GroupId, thread.Channel);
            var last = lastByThread.GetValueOrDefault(key);

            return new GroupChatThreadDto(
                thread.GroupId,
                thread.GroupName,
                thread.Channel,
                last?.Id,
                last is null ? null : Preview(last.Body),
                last?.SenderName,
                last?.SentAt,
                unread.GetValueOrDefault(key));
        });

        // ISH OQIMI TARTIBI (kurator yozishmasidagi bilan bir xil niyat):
        // avval o'qilmagani borlar, keyin oxirgi faollik bo'yicha yangi
        // avval, oxirida — hali hech kim yozmagan oqimlar.
        rows.Sort(static (left, right) =>
        {
            var byUnread = right.UnreadCount.CompareTo(left.UnreadCount);
            if (byUnread != 0) return byUnread;

            var byRecency = Nullable.Compare(right.LastMessageAt, left.LastMessageAt);
            if (byRecency != 0) return byRecency;

            var byName = string.CompareOrdinal(left.GroupName, right.GroupName);
            return byName != 0 ? byName : left.Channel.CompareTo(right.Channel);
        });

        return rows.Count <= MaxThreads ? rows : rows.GetRange(0, MaxThreads);
    }

    // ================================================================= ruxsat

    /// <inheritdoc />
    public async Task<GroupChatAccessDto> ResolveAccessAsync(
        long userId, long groupId, GroupChatChannel? channel, CancellationToken ct = default)
    {
        var access = await AuthorizeAsync(userId, groupId, channel, ct);

        return new GroupChatAccessDto(
            access.GroupId, access.GroupName, access.Channel, access.AvailableChannels);
    }

    // ================================================================= tarix

    /// <inheritdoc />
    public async Task<GroupChatPageDto> GetMessagesAsync(
        long userId,
        long groupId,
        GroupChatChannel? channel,
        long? beforeId,
        int take,
        CancellationToken ct = default)
    {
        var access = await AuthorizeAsync(userId, groupId, channel, ct);

        take = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

        var query = db.GroupChatMessages.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Channel == access.Channel);

        if (beforeId is { } cursor)
            query = query.Where(m => m.Id < cursor);

        // `take + 1` — "yana bormi?" savoliga qo'shimcha COUNT so'rovisiz
        // javob berishning klassik usuli.
        var rows = await query
            .OrderByDescending(m => m.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        rows.Reverse();     // eskidan yangiga — ekranga shundayligicha chiziladi

        var unread = await UnreadCountAsync(userId, groupId, access.Channel, ct);

        return new GroupChatPageDto(
            groupId,
            access.GroupName,
            access.Channel,
            access.AvailableChannels,
            rows.ConvertAll(Map),
            hasMore,
            hasMore && rows.Count > 0 ? rows[0].Id : null,
            unread);
    }

    // ================================================================= yuborish

    /// <inheritdoc />
    public async Task<GroupChatMessageDto> SendAsync(
        long userId,
        long groupId,
        SendGroupChatMessageRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await AuthorizeAsync(userId, groupId, request.Channel, ct);

        await EnsureNotFloodingAsync(userId, groupId, access.Channel, ct);

        // Domain qoidasi: matn tozalanadi, bo'sh rad etiladi, uzuni
        // surrogat juftlikni buzmasdan qirqiladi.
        var message = GroupChatMessage.Create(
            groupId,
            access.Channel,
            userId,
            access.SenderName,
            access.SenderRole,
            request.Body,
            clock.GetUtcNow());

        db.GroupChatMessages.Add(message);

        // ============================================================
        // ★ COMMIT-THEN-SEND
        //
        // 1) AVVAL bazaga yoziladi, 2) KEYIN tarqatiladi.
        //
        // Jonli dars chatidan (`LiveClassHub.SendMessage`) TESKARI tartib —
        // va bu ataylab. U yerda xabar o'tkinchi: 200 kishilik xonada
        // tezlik muhimroq va bitta replikaning yo'qolishi maqbul.
        // Bu yerda xabar — o'quvchining SAVOLI. Avval tarqatib keyin
        // saqlash yiqilsa, ekranlarda savol turardi, bazada esa yo'q edi:
        // o'quvchi javob kutardi, ustoz esa keyingi ochganda hech nima
        // ko'rmasdi.
        //
        // Ayni tartib `LiveSessionService.EndAsync` da ham qo'llangan va
        // `LiveSessionEndBroadcastTests` bilan qulflangan.
        // ============================================================
        await db.SaveChangesAsync(ct);

        var dto = Map(message);

        await notifier.MessageSentAsync(dto, ct);

        // ★ BILDIRISHNOMA (Telegram) ATAYLAB YUBORILMAYDI.
        //
        // Eski ilova HAR xabar uchun `notify_new_chat_message` chaqirardi:
        // xodim yozsa — guruhdagi BARCHA faol o'quvchiga. 30 kishilik
        // guruhda 10 ta xabar = 300 ta Telegram xabari, ya'ni odamlar
        // botni ovozsiz qilib qo'yardi va MUHIM bildirishnomalar
        // (to'lov, dars bekor qilinishi) ham ko'rilmay ketardi.
        //
        // To'g'ri yechim — yig'ma ("o'qilmagan 5 xabaringiz bor") yoki
        // faqat e'lonlar uchun. Ikkalasi ham bu ishning qamrovidan
        // tashqarida, shuning uchun KEYINGI QADAM deb belgilandi
        // (`INotificationOutbox` porti tayyor turibdi).

        return dto;
    }

    // ================================================================= o'qildi

    /// <inheritdoc />
    public async Task<GroupChatReadResultDto> MarkReadAsync(
        long userId,
        long groupId,
        MarkGroupChatReadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await AuthorizeAsync(userId, groupId, request.Channel, ct);

        var lastId = await db.GroupChatMessages.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Channel == access.Channel)
            .Select(m => (long?)m.Id)
            .MaxAsync(ct) ?? 0L;

        // ★ OQIM OXIRIGA QIRQAMIZ. Klient xohlagan raqamni yubora oladi;
        // qirqmasak "9 999 999 gacha o'qildim" degan so'rov KELAJAKDAGI
        // xabarlarni ham o'qilgan qilib qo'yardi va o'quvchi keyingi
        // savolga javobni umuman sezmasdi.
        var target = request.UpToMessageId is { } requested
            ? Math.Min(Math.Max(requested, 0L), lastId)
            : lastId;

        var marker = await db.GroupChatReads.AsTracking()
            .FirstOrDefaultAsync(
                r => r.UserId == userId && r.GroupId == groupId && r.Channel == access.Channel,
                ct);

        var changed = false;

        if (marker is null)
        {
            // Hali bitta ham xabar yo'q — belgi yaratishning ma'nosi yo'q.
            if (target == 0)
                return new GroupChatReadResultDto(groupId, access.Channel, 0, 0, false);

            marker = new GroupChatRead
            {
                GroupId = groupId,
                Channel = access.Channel,
                UserId = userId,
            };

            changed = marker.Advance(target, clock.GetUtcNow());
            db.GroupChatReads.Add(marker);
        }
        else
        {
            changed = marker.Advance(target, clock.GetUtcNow());
        }

        if (changed)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // ★ POYGA (juda kam uchraydi): AYNI foydalanuvchi bir vaqtda
                // ikki so'rov yuborsa (ekran ochildi + yangi xabar keldi),
                // ikkalasi ham belgini birinchi marta yaratishga urinishi
                // mumkin va unikal indeks ikkinchisini rad etadi.
                //
                // Jarayon ichida qayta urinilmaydi: muvaffaqiyatsiz
                // `SaveChanges` dan keyin kuzatuvchida yaroqsiz yozuv qoladi
                // va uni port orqali (ChangeTracker'siz) tozalab bo'lmaydi.
                // Klient uchun bu zararsiz — 409 dan keyingi takror so'rov
                // mavjud belgini topadi va ishlaydi.
                throw new ConflictException(
                    "O'qilganlik belgisi ayni paytda yangilanmoqda. Qayta urinib ko'ring.", ex);
            }
        }

        var unread = await UnreadCountAsync(userId, groupId, access.Channel, ct);

        return new GroupChatReadResultDto(
            groupId, access.Channel, marker.LastReadMessageId, unread, changed);
    }

    // ================================================================= ruxsatning yagona joyi

    /// <summary>
    /// ========================================================================
    /// RUXSATNING YAGONA JOYI
    /// ========================================================================
    ///
    /// HAR bir ommaviy metod shu yerdan o'tadi va oqimni faqat shu metod
    /// qaytaradi. Ya'ni biror metod tekshiruvni "unutib" qololmaydi —
    /// <see cref="ThreadAccess.Channel"/> ni boshqa yo'l bilan olishning
    /// iloji yo'q.
    /// </summary>
    private async Task<ThreadAccess> AuthorizeAsync(
        long userId, long groupId, GroupChatChannel? requested, CancellationToken ct)
    {
        if (requested is { } value && !Enum.IsDefined(value))
            throw new ValidationException(Errors(nameof(requested), "Noma'lum chat kanali."));

        var user = await LoadUserAsync(userId, ct);

        var group = await db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new GroupRow(g.Id, g.Name, g.IsActive, g.Type, g.TeacherId))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Group), groupId);

        if (group.Type == GroupType.Curator)
        {
            // Kurator guruhida o'quvchi BEVOSITA a'zo bo'lmaydi (izohi
            // `GroupType.Curator` da), ya'ni uning "guruh chati" bo'sh
            // xona bo'lardi. Kurator bilan yozishma o'quvchining O'Z ustoz
            // guruhidagi `Curator` oqimida boradi.
            throw new ForbiddenException(
                "Kurator guruhining alohida chati yo'q — kurator oqimi ustoz guruhi ichida.");
        }

        if (!group.IsActive)
            throw new ForbiddenException("Guruh arxivlangan — chat yopiq.");

        var channels = await AvailableChannelsAsync(user, group, ct);

        if (channels.Count == 0)
            throw new ForbiddenException("Bu guruh chatiga ruxsatingiz yo'q.");

        // Kanal berilmasa — foydalanuvchining O'Z oqimi (ro'yxatdagi
        // birinchisi). Eski ilovadagi standart bilan bir xil: ustozga
        // `teacher`, kuratorga `assistant`, o'quvchiga `teacher`.
        var channel = requested ?? channels[0];

        if (!channels.Contains(channel))
        {
            throw new ForbiddenException(
                "Bu kanalga ruxsatingiz yo'q: ustoz o'quvchining kuratorga "
                + "atalgan savollarini ko'rmaydi (va aksincha).");
        }

        return new ThreadAccess(
            group.Id, group.Name, channel, channels, user.FullName, user.Role);
    }

    /// <summary>
    /// Foydalanuvchi shu guruhda KO'RA oladigan oqimlar. Bo'sh ro'yxat —
    /// umuman ruxsat yo'q. TARTIB muhim: birinchisi standart oqim bo'ladi.
    /// </summary>
    private async Task<IReadOnlyList<GroupChatChannel>> AvailableChannelsAsync(
        User user, GroupRow group, CancellationToken ct)
    {
        // Nazorat rollari — ikkala oqim.
        if (user.Role is UserRole.Admin or UserRole.Academic)
            return BothChannels;

        if (user.Role == UserRole.Student)
        {
            var isMember = await db.GroupMembers.AsNoTracking().AnyAsync(
                m => m.GroupId == group.Id
                  && m.StudentId == user.Id
                  && m.Status == MemberStatus.Active,
                ct);

            // O'quvchi IKKALA oqimni ko'radi — ular uning o'z savollari.
            return isMember ? BothChannels : [];
        }

        // XODIM. Ruxsat ROLGA emas, BIRIKTIRUVGA qarab beriladi.
        var channels = new List<GroupChatChannel>(2);

        if (group.TeacherId == user.Id)
            channels.Add(GroupChatChannel.Teacher);

        // ★ Kurator biriktiruvi UCHUN YAGONA MANBA — qo'lda `AssistantId`
        // tekshiruvi yozilmaydi (sabab sinf izohida).
        var curatorScope = await curators.ScopeGroupIdsAsync(user.Id, ct);

        if (curatorScope.Contains(group.Id))
            channels.Add(GroupChatChannel.Curator);

        return channels;
    }

    /// <summary>
    /// "Chatlar" hubi uchun foydalanuvchi ko'radigan BARCHA oqimlar.
    /// Bitta metodda, chunki ro'yxat va bitta guruh qoidasi bir xil
    /// manbalarga tayanishi shart.
    /// </summary>
    private async Task<List<ThreadRow>> AccessibleThreadsAsync(User user, CancellationToken ct)
    {
        var threads = new List<ThreadRow>();

        if (user.Role == UserRole.Student)
        {
            var groups = await db.GroupMembers.AsNoTracking()
                .Where(m => m.StudentId == user.Id
                         && m.Status == MemberStatus.Active
                         && m.Group!.IsActive
                         && m.Group.Type != GroupType.Curator)
                .Select(m => new { m.GroupId, m.Group!.Name })
                .Distinct()
                .ToListAsync(ct);

            foreach (var group in groups)
            {
                threads.Add(new ThreadRow(group.GroupId, group.Name, GroupChatChannel.Teacher));
                threads.Add(new ThreadRow(group.GroupId, group.Name, GroupChatChannel.Curator));
            }

            return threads;
        }

        if (user.Role is UserRole.Admin or UserRole.Academic)
        {
            var groups = await db.Groups.AsNoTracking()
                .Where(g => g.IsActive && g.Type != GroupType.Curator)
                .Select(g => new { GroupId = g.Id, g.Name })
                .ToListAsync(ct);

            foreach (var group in groups)
            {
                threads.Add(new ThreadRow(group.GroupId, group.Name, GroupChatChannel.Teacher));
                threads.Add(new ThreadRow(group.GroupId, group.Name, GroupChatChannel.Curator));
            }

            return threads;
        }

        // XODIM: ustoz sifatidagi guruhlari + kurator sifatidagi guruhlari.
        // Bitta odam ikkala rolda ham bo'lishi mumkin — o'shanda ikki oqim
        // ham ro'yxatga tushadi va bu TO'G'RI.
        var curatorScope = await curators.ScopeGroupIdsAsync(user.Id, ct);

        var staffGroups = await db.Groups.AsNoTracking()
            .Where(g => g.IsActive
                     && g.Type != GroupType.Curator
                     && (g.TeacherId == user.Id || curatorScope.Contains(g.Id)))
            .Select(g => new { GroupId = g.Id, g.Name, g.TeacherId })
            .ToListAsync(ct);

        foreach (var group in staffGroups)
        {
            if (group.TeacherId == user.Id)
                threads.Add(new ThreadRow(group.GroupId, group.Name, GroupChatChannel.Teacher));

            if (curatorScope.Contains(group.GroupId))
                threads.Add(new ThreadRow(group.GroupId, group.Name, GroupChatChannel.Curator));
        }

        return threads;
    }

    // ================================================================= o'qilmaganlar

    /// <summary>
    /// Bitta oqimdagi o'qilmaganlar soni.
    ///
    /// O'Z xabarim HISOBLANMAYDI: aks holda men yozgan savol o'zimga
    /// "o'qilmagan" bo'lib qaytardi va sanoq hech qachon nolga tushmasdi
    /// (aynan shu xato eski kurator yozishmasida bor edi).
    /// </summary>
    private async Task<int> UnreadCountAsync(
        long userId, long groupId, GroupChatChannel channel, CancellationToken ct)
    {
        var lastRead = await db.GroupChatReads.AsNoTracking()
            .Where(r => r.UserId == userId && r.GroupId == groupId && r.Channel == channel)
            .Select(r => (long?)r.LastReadMessageId)
            .FirstOrDefaultAsync(ct) ?? 0L;

        return await db.GroupChatMessages.AsNoTracking()
            .CountAsync(
                m => m.GroupId == groupId
                  && m.Channel == channel
                  && m.SenderId != userId
                  && m.Id > lastRead,
                ct);
    }

    /// <summary>
    /// BARCHA oqimlar uchun o'qilmaganlar — BITTA so'rovda.
    ///
    /// ★ Chegara (<c>LastReadMessageId</c>) har oqimda BOSHQA, shuning uchun
    /// u SQL ichida korrelyatsiyalangan ost-so'rov bilan olinadi. Muqobili
    /// — har oqim uchun alohida <c>COUNT</c>, ya'ni 40 guruhli ustozda 80 ta
    /// so'rov. Aynan shu joy "o'qilmaganlar" funksiyasida eng oson N+1 ga
    /// aylanadi.
    /// </summary>
    private async Task<Dictionary<ThreadKey, int>> UnreadByThreadAsync(
        long userId, List<long> groupIds, CancellationToken ct)
    {
        var rows = await db.GroupChatMessages.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId) && m.SenderId != userId)
            .Where(m => m.Id > (db.GroupChatReads
                .Where(r => r.UserId == userId
                         && r.GroupId == m.GroupId
                         && r.Channel == m.Channel)
                .Select(r => (long?)r.LastReadMessageId)
                .FirstOrDefault() ?? 0L))
            .GroupBy(m => new { m.GroupId, m.Channel })
            .Select(g => new UnreadRow(g.Key.GroupId, g.Key.Channel, g.Count()))
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => new ThreadKey(row.GroupId, row.Channel),
            row => row.Count);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// Tezlik chegarasi — SERVER tomonda va REDIS'da.
    ///
    /// ★ Hisoblagich Redis'da bo'lishi SHART: jarayon xotirasida bo'lsa
    /// ikkinchi API konteyneri qo'shilganda chegara ikki barobarga
    /// ko'payardi va har qayta ishga tushishda nolga qaytardi.
    ///
    /// ★ Kalitga OQIM kiradi: ustoz oqimidagi faollik o'quvchining kurator
    /// oqimidagi savolini bloklab qo'ymasin.
    /// </summary>
    private async Task EnsureNotFloodingAsync(
        long userId, long groupId, GroupChatChannel channel, CancellationToken ct)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"groupchat:rate:{groupId}:{(int)channel}:{userId}");

        var hits = await cache.IncrementAsync(key, RateLimitWindow, ct);

        if (hits > RateLimitMaxMessages)
        {
            throw new TooManyRequestsException(
                "Juda tez yozyapsiz. Bir necha soniyadan keyin urinib ko'ring.",
                (int)Math.Ceiling(RateLimitWindow.TotalSeconds));
        }
    }

    private async Task<User> LoadUserAsync(long userId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN olinadi: kirish tokeni 15 daqiqa
        // yashaydi va u vaqt ichida rol pasaytirilgan bo'lishi mumkin.
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return user;
    }

    private static GroupChatMessageDto Map(GroupChatMessage message) =>
        new(message.Id,
            message.GroupId,
            message.Channel,
            message.SenderId,
            message.SenderName,
            message.SenderRole,
            message.Body,
            message.SentAt);

    /// <summary>
    /// Ro'yxat uchun qisqartma. Kesish SURROGAT JUFTLIKNI buzmaydi —
    /// aks holda oxiri emojiga to'g'ri kelgan ko'chirma JSON'da
    /// <c>U+FFFD</c> ga aylanardi (aynan shu xato jonli dars chatida
    /// topilgan edi).
    /// </summary>
    private static string Preview(string body)
    {
        if (body.Length <= PreviewLength) return body;

        var cut = PreviewLength;
        if (char.IsHighSurrogate(body[cut - 1])) cut--;

        return body[..cut];
    }

    private static Dictionary<string, string[]> Errors(string field, string message) =>
        new(StringComparer.Ordinal) { [field] = [message] };

    // ---------------------------------------------------------------- ichki shakllar

    /// <summary>Ruxsat natijasi — oqim va uni yozadigan odam haqidagi hamma narsa.</summary>
    private sealed record ThreadAccess(
        long GroupId,
        string GroupName,
        GroupChatChannel Channel,
        IReadOnlyList<GroupChatChannel> AvailableChannels,
        string SenderName,
        UserRole SenderRole);

    /// <summary>Guruhning ruxsat uchun kerakli TOR proyeksiyasi (butun entity emas).</summary>
    private sealed record GroupRow(
        long Id, string Name, bool IsActive, GroupType Type, long? TeacherId);

    private sealed record ThreadRow(long GroupId, string GroupName, GroupChatChannel Channel);

    private sealed record ThreadKey(long GroupId, GroupChatChannel Channel);

    private sealed record UnreadRow(long GroupId, GroupChatChannel Channel, int Count);
}
