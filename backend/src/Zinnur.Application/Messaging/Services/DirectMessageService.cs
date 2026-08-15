using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Services;
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
    // ===== R40 · «KETMA-KETLIK BO'YICHA» =====
    //
    // Gating savol YOZISHDA tekshiriladi: ochilmagan dars haqida savol
    // berish sur'at nazorati atrofidan aylanib o'tish yo'li bo'lardi
    // (batafsil — `SendAsync` ichida).
    IGatingService gating,
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

    /// <summary>
    /// O'quvchining suhbatlari.
    ///
    /// ════════════════════════════════════════════════════════════════════
    /// R40 — RO'YXAT ENDI BIR NECHTA QATOR BO'LISHI MUMKIN
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Ilgari bu yerda ANIQ BITTA qator bo'lardi (kurator). Endi guruhning
    /// <c>QuestionResponderRole</c> sozlamasi <c>Both</c> bo'lsa ustoz ham
    /// qo'shiladi, ya'ni o'quvchida IKKI suhbat bo'ladi.
    ///
    /// ★ TARTIB SERVERDA HAL QILINADI (`ResolveRespondersAsync`) — asosiy
    /// suhbatdosh doim birinchi. Frontend ro'yxatni qayta saralamaydi:
    /// aks holda "kimga yozish kerak" degan qaror ikki joyda bo'lardi.
    ///
    /// ★ STANDART SOZLAMADA (`Assistant`) natija BUGUNGIDEK — bitta qator
    /// yoki bo'sh ro'yxat. Ya'ni migratsiyadan keyin o'quvchi ekranida
    /// hech narsa o'zgarmaydi.
    /// </summary>
    private async Task<IReadOnlyList<ConversationDto>> StudentConversationsAsync(
        User student, CancellationToken ct)
    {
        var responders = await curators.ResolveRespondersAsync(student.Id, ct);

        // Xodim biriktirilmagan — bo'sh ro'yxat, XATO EMAS. Frontend
        // "Sizga hali kurator biriktirilmagan" deb ko'rsatadi.
        if (responders.Count == 0) return [];

        var staffIds = responders.Select(u => u.Id).ToList();

        // ★ BITTA AGREGAT SO'ROV, suhbat boshiga bittadan EMAS: ikki
        // suhbatda ikki so'rov ko'p emas, lekin naqsh xodim tomonidagi
        // bilan bir xil qolsin — u yerda 200 ta suhbat bo'ladi.
        var stats = await db.DirectMessages.AsNoTracking()
            .Where(m => m.StudentId == student.Id && staffIds.Contains(m.StaffId))
            .GroupBy(m => m.StaffId)
            .Select(g => new ThreadStats(
                g.Key,
                g.Max(m => m.Id),
                g.Count(m => m.SenderId != student.Id && !m.ReadByStudent)))
            .ToListAsync(ct);

        var statsByPeer = stats.ToDictionary(s => s.PeerId);

        var last = await LoadLastMessagesAsync(stats.ConvertAll(s => s.LastMessageId), ct);

        return responders.Select(peer =>
        {
            var threadStats = statsByPeer.GetValueOrDefault(peer.Id);

            return BuildConversation(
                peerId: peer.Id,
                peerName: peer.FullName,
                peerRole: peer.Role,
                groupName: null,
                stats: threadStats,
                last: threadStats is null
                    ? null
                    : last.GetValueOrDefault(threadStats.LastMessageId),
                viewerId: student.Id);
        }).ToList();
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
        long userId, long peerId, long? beforeId, int take, long? moduleLessonId = null,
        CancellationToken ct = default)
    {
        var pair = await ResolvePairAsync(userId, peerId, ct);

        take = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

        var query = db.DirectMessages.AsNoTracking()
            .Where(m => m.StudentId == pair.StudentId && m.StaffId == pair.StaffId);

        if (beforeId is { } cursor)
            query = query.Where(m => m.Id < cursor);

        // Dars Dashboard'idagi mini-chat: faqat SHU darsdan yozilgan
        // xabarlar (`askAboutLesson` bilan yozilgan teg bilan AYNI maydon).
        if (moduleLessonId is { } lessonId)
            query = query.Where(m => m.ModuleLessonId == lessonId);

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

            // ═══════════════════════════════════════════════════════════
            // R40 — «KETMA-KETLIK BO'YICHA BO'LSIN»
            //
            // Loyiha egasining talabidagi shu ibora savolning O'ZIGA ham
            // tegishli: o'quvchi HALI OCHILMAGAN dars haqida savol bera
            // olmasligi kerak. Aks holda sur'at nazorati (gating) atrofidan
            // aylanib o'tish yo'li ochilardi — dars matni va topshiriqlari
            // savol-javob orqali oldindan oshkor bo'lardi.
            //
            // ★ FAQAT O'QUVCHIGA: xodim javob yozayotganda AYNI kontekstni
            //   qaytaradi va uning uchun gating umuman qo'llanmaydi
            //   (`GetLessonGateAsync` o'quvchi bo'yicha ishlaydi).
            //
            // ⚠️ ARZON YO'L: `GetLessonGateAsync` butun daraxtni qurmaydi
            //   va so'rov davomida keshlanadi.
            // ═══════════════════════════════════════════════════════════
            if (pair.ViewerIsStudent)
                await gating.EnsureLessonUnlockedAsync(pair.StudentId, lessonId, ct);
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

    // ================================================================= dars savollari

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// R40 — DARS SAVOLLARI NAVBATI (xodim ko'rinishi)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// ★ RUXSAT SHU YERDA QAYTA TEKSHIRILMAYDI va bu XATO EMAS: filtr
    /// `StaffId == userId`, ya'ni so'rovning O'ZI ko'ruvchini o'z
    /// juftliklari bilan cheklaydi. Xodim boshqa birovning yozishmasini
    /// ko'rish uchun o'sha yozishmaning XODIM tomoni bo'lishi kerak
    /// bo'lardi. `ResolvePairAsync` esa suhbat KALITINI chiqarish uchun
    /// kerak — bu yerda kalit allaqachon ma'lum.
    ///
    /// ⚠️ Shu sababli ro'yxatda BIRIKTIRUV BEKOR QILINGAN eski suhbatlar
    /// ham ko'rinadi (kurator guruhdan olib tashlangan bo'lsa). Bu bugungi
    /// xatti-harakat bilan bir xil emas, lekin ONGLI: navbat — yozilgan
    /// savollar tarixi, va yozilgan savol biriktiruv o'zgargani uchun
    /// yo'qolib ketmasligi kerak. Suhbatni OCHISH esa avvalgidek
    /// `ResolvePairAsync` dan o'tadi, ya'ni javob yozish uchun mas'uliyat
    /// SAQLANGAN bo'lishi shart.
    ///
    /// TARTIB: javobsizlar tepada, ular ichida ESKISI birinchi — ya'ni
    /// eng uzoq kutgan savol birinchi bo'ladi ("ketma-ketlik bo'yicha").
    /// </summary>
    public async Task<IReadOnlyList<LessonQuestionDto>> ListLessonQuestionsAsync(
        long userId, int take, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);

        // O'quvchi hech qachon `StaffId` bo'lmaydi, ya'ni natija baribir
        // bo'sh bo'lardi. Ochiq 403 esa "bu ekran senga emas" deb aytadi.
        if (user.Role == UserRole.Student)
            throw new ForbiddenException("Dars savollari navbati faqat xodim uchun.");

        take = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

        // ★ FAQAT O'QUVCHI YOZGAN xabarlar: xodimning o'z javobi ham
        // `ModuleLessonId` bilan saqlanadi (u kontekstni qaytaradi), lekin
        // u SAVOL emas — navbatda o'z javobingni ko'rish ma'nosiz bo'lardi.
        var rows = db.DirectMessages.AsNoTracking()
            .Where(m => m.StaffId == userId
                     && m.ModuleLessonId != null
                     && m.SenderId == m.StudentId);

        /*
          🔴 TARTIB DTO'DAN OLDIN HISOBLANADI — VA BU MAJBURIY.

          Avvalgi ko'rinishda `Select(... new LessonQuestionDto(...))` dan
          KEYIN `OrderBy(q => q.Answered)` yozilgan edi. `Answered` — DTO
          konstruktoriga uzatilgan ICHMA-ICH so'rov natijasi, ya'ni EF uni
          SQL `ORDER BY` ga o'gira olmaydi va butun so'rov
          "could not be translated" bilan **500** qaytarardi.

          ★ Yechim: `Answered` anonim turga chiqariladi, saralash O'SHA
          maydon bo'yicha bajariladi va DTO eng oxirida yig'iladi. Bu
          bosqichlarning hammasi bitta SQL'ga tushadi — mijoz tomonda
          baholash (`AsEnumerable`) YO'Q, ya'ni `Take(take)` ham serverda
          qoladi va butun jadval o'qilmaydi.

          ★ `OrderBy(bool)`: `false` (javobsiz) — `0`, ya'ni TEPADA. Talab
          aynan shu ("javobsizlar tepada").
        */
        var items = await rows
            .Select(m => new
            {
                Message = m,

                // "Javob berilganmi" — AYNI juftlikda shu savoldan KEYIN
                // xodim yozgan xabar bormi. Ichma-ich so'rov asosiy
                // `(StudentId, StaffId, Id)` indeksidan o'qiladi.
                Answered = db.DirectMessages.Any(r => r.StudentId == m.StudentId
                                                   && r.StaffId == m.StaffId
                                                   && r.SenderId == m.StaffId
                                                   && r.Id > m.Id),
            })
            .OrderBy(x => x.Answered)
            .ThenBy(x => x.Message.SentAt)
            .ThenBy(x => x.Message.Id)
            .Take(take)
            .Select(x => new LessonQuestionDto(
                x.Message.Id,
                x.Message.StudentId,
                x.Message.Student!.FullName,
                null,
                x.Message.ModuleLessonId!.Value,
                x.Message.ModuleLesson!.Name,
                x.Message.Body,
                x.Message.SentAt,
                x.Answered,
                x.Message.ReadByStaff))
            .ToListAsync(ct);

        if (items.Count == 0) return items;

        // Guruh nomi ALOHIDA so'rovda — proyeksiya ichida bo'lsa har savol
        // uchun bittadan ichma-ich `SELECT` ketardi, bir o'quvchining esa
        // o'nlab savoli bo'ladi.
        var groupNames = await GroupNamesAsync(
            items.ConvertAll(q => q.PeerId).Distinct().ToList(), ct);

        return items.ConvertAll(q => q with { GroupName = groupNames.GetValueOrDefault(q.PeerId) });
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
            // ═══════════════════════════════════════════════════════════
            // 🔴 R40 — TENGLIK O'RNIGA TO'PLAMGA TEGISHLILIK
            //
            // Ilgari bu yerda `curator.Id != peerId` turardi, ya'ni
            // o'quvchida bitta ruxsat etilgan suhbatdosh bor deb
            // hisoblanardi. Endi ular bir nechta bo'lishi mumkin
            // (`Group.QuestionResponderRole == Both`).
            //
            // ★ DARVOZA KUCHINI YO'QOTMADI: ro'yxat o'sha `StaffResponsibility`
            //   qoidasidan keladi, ya'ni "o'zi tanlagan istalgan xodim"
            //   EMAS. Ro'yxatdan tashqaridagi har qanday `peerId` — 403.
            //
            // ★ VA IKKI SUHBAT BIR-BIRIDAN YOPIQ: kalit
            //   `(StudentId, StaffId)` bo'lgani uchun ustoz kuratorning
            //   yozishmasini so'rasa o'z juftligini oladi, o'zganikini
            //   emas. Aynan shu sabab `DirectMessage.cs` dagi eski tizim
            //   sizib chiqishi (shaxsiy savol butun sinfga ko'rinib
            //   qolgani) bu yerda TAKRORLANMAYDI.
            // ═══════════════════════════════════════════════════════════
            var responders = await curators.ResolveRespondersAsync(user.Id, ct);

            if (responders.Count == 0)
                throw new NotFoundException("Kurator", peerId);

            var peerStaff = responders.FirstOrDefault(u => u.Id == peerId)
                ?? throw new ForbiddenException("Bu suhbatga ruxsatingiz yo'q.");

            return new ConversationPair(
                user.Id, peerStaff.Id, ViewerIsStudent: true, peerStaff.FullName);
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
