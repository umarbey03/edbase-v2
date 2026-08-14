using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Courses;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Application.Media;
using Zinnur.Application.Messaging.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
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
    IMediaStorage storage,
    ISettingsResolver settings,
    ILogger<GroupChatService> logger,
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

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// ★ YUKLASH BUDJETI — XABAR BUDJETIDAN ALOHIDA VA QAT'IYROQ (R16b)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 NIMA UCHUN MAVJUD BUDJET YETMAYDI: <see cref="RateLimitMaxMessages"/>
    /// (10 xabar / 10 s) MATN uchun o'lchangan — matnli xabarning narxi
    /// bir necha kilobayt va bitta <c>INSERT</c>. Fayl esa 10 MB gacha
    /// bo'ladi va u UCH resursni yeydi: tarmoq, vaqtinchalik disk
    /// (ASP.NET multipart'ni bizning kodimizdan OLDIN diskka buferlaydi)
    /// va R2'dagi PUL BILAN o'lchanadigan joy. Bitta budjetda qolsa, bir
    /// foydalanuvchi 10 sekundda 50 ta fayl (5 × 10) — ya'ni yarim
    /// gigabaytgacha — yuklay olardi va buni HECH NIMA to'xtatmasdi.
    ///
    /// ★ HISOBLASH FAYL BO'YICHA, XABAR BO'YICHA EMAS: 5 ta rasmli bitta
    /// xabar budjetdan 5 birlik yeydi. Aks holda "bitta xabar" degan sanoq
    /// eng qimmat holatni (5 fayl) eng arzoni bilan tenglashtirardi.
    ///
    /// ★ OYNA UZUNROQ (1 daqiqa, 10 sekund emas): fayl yuklash o'zi sekin
    /// amal — qisqa oynada chegara amalda hech qachon ishlamasdi, chunki
    /// yuklashning O'ZI oynadan uzoq davom etardi.
    ///
    /// ⚠️ IKKALA BUDJET HAM QO'LLANADI: xabar budjeti ham tekshiriladi
    /// (biriktirmali so'rov XABAR ham yaratadi), ya'ni REST va hub bitta
    /// umumiy xabar budjetini bo'lishishda davom etadi.
    /// </summary>
    private const int UploadLimitMaxFiles = 12;

    private static readonly TimeSpan UploadLimitWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// CHATDA qabul qilinadigan turkumlar.
    ///
    /// ★ <c>Video</c> ATAYLAB YO'Q — <c>AssignmentAttachmentService</c>
    /// dagi AYNI sabab: video uchun <c>LessonAsset</c> yo'li bor (u yerda
    /// alohida, ancha katta hajm chegarasi va tartiblash bor). Bu yerda
    /// ruxsat etilsa, 1 GB fayl uchun mo'ljallanmagan yo'ldan o'tib ketardi
    /// va chatning hajm chegarasi (10 MB) uni baribir rad etardi — ya'ni
    /// foydalanuvchi "video yuborsa bo'ladi" deb o'ylab, har safar xato
    /// olardi.
    ///
    /// ★ TO'PLAM AYNI PAYTDA IKKI MA'NOLI KONTEYNERLARNI (`ftyp`, EBML)
    /// hal qilish MEZONI ham: <c>Audio</c> ruxsat etilgani uchun iOS
    /// Safari'ning ovozli xabari — VIDEO brendi bilan kelsa ham — ovoz deb
    /// qabul qilinadi (batafsil <see cref="MediaSignatures"/> izohida).
    /// </summary>
    private const MediaCategories AttachmentCategories =
        MediaCategories.Image | MediaCategories.Audio | MediaCategories.Document;

    /// <summary>Ombordagi mantiqiy papka — operator uni PREFIKS bo'yicha taniydi.</summary>
    private const string AttachmentFolder = "group-chat";

    /// <summary>O'quvchi va nazoratchi rollar ko'radigan ikkala oqim.</summary>
    private static readonly GroupChatChannel[] BothChannels =
        [GroupChatChannel.Teacher, GroupChatChannel.Curator];

    // ================================================================= chatlar hubi

    /// <inheritdoc />
    public async Task<IReadOnlyList<GroupChatThreadDto>> ListThreadsAsync(
        long userId, GroupChatThreadQuery? query = null, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);

        var filter = query ?? new GroupChatThreadQuery();
        EnsureFilterIsUsable(filter);

        var threads = await AccessibleThreadsAsync(user, filter, ct);

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

        // ★ `Include` — ko'chirma uchun: matnsiz (faqat rasmli) xabarda
        //   ro'yxat qatori bo'm-bo'sh ko'rinmasin (izohi `Preview` da).
        //   Bu N+1 EMAS: `lastIds` allaqachon bitta agregatdan kelgan va
        //   bu bitta qo'shimcha JOIN.
        var lastMessages = lastIds.Count == 0
            ? []
            : await db.GroupChatMessages.AsNoTracking()
                .Include(m => m.Attachments)
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
                last is null ? null : Preview(last),
                last?.SenderName,
                last?.SentAt,
                unread.GetValueOrDefault(key),

                // ★ R38 · ATAYLAB NOMLI ARGUMENT: DTO oxirida ketma-ket
                // `long? / string?` juftligi turibdi va yangi maydonni bir
                // pozitsiya adashtirish KOMPILYATSIYA XATOSI bermasdi
                // (`GroupService.Map` dagi AYNI mulohaza).
                GroupType: thread.GroupType,
                CategoryId: thread.CategoryId,
                CategoryName: thread.CategoryName);
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

        // ★ `Include` — biriktirmalar sahifa bilan BIRGA keladi (bitta
        //   qo'shimcha JOIN). Alohida so'rov bo'lsa 50 xabarli sahifa
        //   uchun 50 ta borish-kelish bo'lardi (klassik N+1) — chat esa
        //   eng tez-tez ochiladigan ekran.
        var query = db.GroupChatMessages.AsNoTracking()
            .Include(m => m.Attachments)
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

    // ================================================================= biriktirmali xabar (R16b)

    /// <inheritdoc />
    public async Task<GroupChatMessageDto> SendWithAttachmentsAsync(
        long userId,
        long groupId,
        SendGroupChatAttachmentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Files.Count == 0)
            throw Invalid("Fayl yuborilmadi.");

        if (request.Files.Count > GroupChatAttachment.MaxPerMessage)
        {
            throw Invalid(
                "Bitta xabarga ko'pi bilan "
                + GroupChatAttachment.MaxPerMessage.ToString(CultureInfo.InvariantCulture)
                + " ta fayl biriktiriladi.");
        }

        // 1) RUXSAT — yozish bilan AYNI darvoza (`SendAsync` dagi o'sha metod).
        var access = await AuthorizeAsync(userId, groupId, request.Channel, ct);

        // 2) IKKI BUDJET: xabar (REST va hub bilan umumiy) + yuklash
        //    (qat'iyroq). Sabab `UploadLimitMaxFiles` izohida.
        await EnsureNotFloodingAsync(userId, groupId, access.Channel, ct);
        await EnsureUploadBudgetAsync(userId, groupId, access.Channel, request.Files.Count, ct);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — fayl qabul qilinmaydi. "
                + "Administrator uchun: `Storage:ServiceUrl`, `Storage:Bucket`, "
                + "`Storage:AccessKey`, `Storage:SecretKey` to'ldirilishi kerak.");
        }

        var limitBytes = await AttachmentLimitBytesAsync(ct);

        // 3) HAMMA FAYL AVVAL TEKSHIRILADI, KEYIN BITTASI HAM YOZILADI.
        //
        // ★ NIMA UCHUN IKKI BOSQICH: aks holda 3-fayl noto'g'ri turda
        //   bo'lsa, 1- va 2-fayl OMBORGA allaqachon yozilgan bo'lardi va
        //   ularni orqaga qaytarish kerak bo'lardi. Tekshiruv arzon
        //   (32 bayt sarlavha), yozish esa qimmat — shuning uchun qimmat
        //   ish faqat HAMMASI to'g'ri ekani ma'lum bo'lgach boshlanadi.
        var prepared = new List<PreparedAttachment>(request.Files.Count);

        foreach (var file in request.Files)
        {
            if (file.Length <= 0)
                throw Invalid("Fayl bo'sh.");

            var signature = await DetectAttachmentAsync(file, ct);

            if (file.Length > limitBytes)
                throw AttachmentTooLarge(limitBytes);

            prepared.Add(new PreparedAttachment(file, signature));
        }

        // 4) OMBORGA YOZISH. Kalitlar ro'yxatda saqlanadi: baza qabul
        //    qilmasa, ularni O'ZIMIZ o'chiramiz (yetim obyekt qolmasin).
        var savedKeys = new List<string>(prepared.Count);

        try
        {
            var message = GroupChatMessage.CreateWithAttachments(
                groupId,
                access.Channel,
                userId,
                access.SenderName,
                access.SenderRole,
                request.Body,
                prepared.Count,
                clock.GetUtcNow());

            for (var index = 0; index < prepared.Count; index++)
            {
                var (file, signature) = prepared[index];

                file.Content.Position = 0;

                var objectKey = await storage.SaveAsync(
                    new MediaUpload(
                        AttachmentFolder, signature.Extension, signature.ContentType,
                        file.Content, file.Length),
                    ct);

                savedKeys.Add(objectKey);

                var attachment = new GroupChatAttachment
                {
                    Kind = ToAttachmentKind(signature.Category),
                    Position = index,
                    ObjectKey = objectKey,
                    ContentType = signature.ContentType,
                    FileName = GroupChatAttachment.SanitizeFileName(file.ClientFileName),
                    SizeBytes = file.Length,
                    DurationSec = file.DurationSec,
                    CreatedAt = message.SentAt,
                };

                message.Attachments.Add(attachment);
            }

            db.GroupChatMessages.Add(message);

            // ★ COMMIT-THEN-SEND — matnli yo'l bilan AYNI tartib
            //   (sabab `SendAsync` izohida batafsil).
            await db.SaveChangesAsync(ct);

            // Baza qabul qildi — endi kalitlar "yetim" emas, ular qatorga
            // bog'langan. Ro'yxat tozalanadi, aks holda `catch` bo'lmagan
            // holatda ham o'chirish mantiqiy jihatdan yaqin turardi.
            savedKeys.Clear();

            var dto = Map(message);

            await notifier.MessageSentAsync(dto, ct);

            return dto;
        }
        finally
        {
            // 🔴 Faqat MUVAFFAQIYATSIZ yo'lda to'ladi (yuqorida `Clear`).
            //    `DeleteAsync` idempotent, ya'ni ikki marta chaqirilishi
            //    ham xavfsiz.
            foreach (var key in savedKeys)
                await TryDeleteFromStorageAsync(key, ct);
        }
    }

    /// <inheritdoc />
    public async Task<LessonAssetDownload> OpenAttachmentAsync(
        long attachmentId,
        string? rangeHeader,
        long userId,
        CancellationToken ct = default)
    {
        var row = await db.GroupChatAttachments.AsNoTracking()
            .Where(a => a.Id == attachmentId)
            .Select(a => new AttachmentRow(
                a.Id,
                a.Message!.GroupId,
                a.Message.Channel,
                a.Kind,
                a.ObjectKey,
                a.ContentType,
                a.FileName,
                a.SizeBytes))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(GroupChatAttachment), attachmentId);

        // 🔴 RUXSAT — OMBORGA MUROJAATDAN OLDIN: aks holda javob vaqti yoki
        //    503 kabi belgilardan faylning bor-yo'qligi payqalardi.
        //
        // ★ AYNI `AuthorizeAsync`: tarixni o'qish bilan bir xil qoida, bir
        //   xil kod. Kanal ANIQ beriladi (xabar qaysi oqimda yozilgan bo'lsa
        //   o'sha) — server "standart oqim"ni tanlab yubormasin, aks holda
        //   ustoz kurator oqimidagi rasmni o'z oqimi orqali ochib olardi.
        await AuthorizeAsync(userId, row.GroupId, row.Channel, ct);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — faylni ochib bo'lmadi. "
                + "Administrator uchun: `Storage:ServiceUrl`, `Storage:Bucket`, "
                + "`Storage:AccessKey`, `Storage:SecretKey` to'ldirilishi kerak.");
        }

        var outcome = RangeHeader.TryParse(rangeHeader, row.SizeBytes, out var range);

        if (outcome == RangeParseOutcome.Unsatisfiable)
            throw new RangeNotSatisfiableException(row.SizeBytes);

        var requested = outcome == RangeParseOutcome.Satisfiable ? range : null;

        var stored = await storage.OpenReadAsync(row.ObjectKey, requested, ct)
            ?? throw new NotFoundException(nameof(GroupChatAttachment), attachmentId);

        // TUR BAZADAN ustun (yuklashda mazmundan aniqlangan).
        var contentType = string.IsNullOrWhiteSpace(row.ContentType)
            ? stored.ContentType
            : row.ContentType;

        return new LessonAssetDownload(
            stored,
            contentType,
            SuggestAttachmentFileName(row),
            stored.TotalLength ?? row.SizeBytes,
            stored.IsPartial ? requested : null);
    }

    // ---------------------------------------------------------------- biriktirma yordamchilari

    /// <summary>
    /// Yuklash budjeti — Redis'da, xabar budjetidan ALOHIDA kalit bilan.
    ///
    /// ★ Kalitda OQIM bor (xabar budjetidagi bilan bir xil sabab): ustoz
    /// oqimidagi faollik o'quvchining kurator oqimiga rasm yuborishini
    /// bloklab qo'ymasin.
    ///
    /// ⚠️ HISOBLAGICH SO'ROV BOSHIDA oshiriladi (fayl yozilgandan keyin
    /// emas): aks holda yiqilgan yoki rad etilgan yuklash budjetdan hech
    /// nima yemasdi va cheksiz urinish yo'li ochiq qolardi.
    /// </summary>
    private async Task EnsureUploadBudgetAsync(
        long userId, long groupId, GroupChatChannel channel, int fileCount, CancellationToken ct)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"groupchat:upload:{groupId}:{(int)channel}:{userId}");

        var hits = 0L;

        // ★ Har FAYL uchun bittadan — `ICacheService` da "n ga oshir"
        //   metodi yo'q va uni port'ga qo'shish faqat shu yer uchun
        //   bo'lardi. Fayl soni ko'pi bilan 5, ya'ni narxi ko'pi bilan
        //   5 ta Redis buyrug'i.
        for (var i = 0; i < fileCount; i++)
            hits = await cache.IncrementAsync(key, UploadLimitWindow, ct);

        if (hits > UploadLimitMaxFiles)
        {
            throw new TooManyRequestsException(
                "Juda ko'p fayl yuborilmoqda. Bir daqiqadan keyin urinib ko'ring.",
                (int)Math.Ceiling(UploadLimitWindow.TotalSeconds));
        }
    }

    /// <summary>
    /// Fayl turini SEHRLI BAYTLARDAN aniqlaydi.
    ///
    /// 🔴 Kengaytma va klientning <c>Content-Type</c> sarlavhasi HISOBGA
    /// OLINMAYDI — ikkalasini ham istalgan klient xohlagan qiymatga yozib
    /// yubora oladi (jadval va to'liq asoslash: <see cref="MediaSignatures"/>).
    /// </summary>
    private static async Task<MediaSignature> DetectAttachmentAsync(
        LessonAssetUpload upload, CancellationToken ct)
    {
        var header = new byte[MediaSignatures.HeaderSize];

        upload.Content.Position = 0;

        var length = await upload.Content
            .ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct)
            .ConfigureAwait(false);

        if (length == 0)
            throw Invalid("Fayl bo'sh.");

        if (!MediaSignatures.TryDetect(
                header.AsSpan(0, length), AttachmentCategories, out var signature))
        {
            throw Invalid(
                "Faylning turi qo'llab-quvvatlanmaydi. Rasm (jpg, png, webp, gif, heic), "
                + "ovoz (mp3, m4a, ogg, webm, wav) yoki PDF yuboring. "
                + "⚠️ Fayl NOMI hisobga olinmaydi — tur fayl MAZMUNIDAN aniqlanadi.");
        }

        return signature;
    }

    /// <summary>
    /// Hajm chegarasi — SOZLAMALAR registridan (<c>lesson.image_max_mb</c>).
    ///
    /// ⚠️ ALOHIDA `chat.*` KALITI ATAYLAB QO'SHILMADI —
    /// <see cref="AssignmentAttachmentService"/> dagi AYNI asos: sozlamalar
    /// registri qanchalik kichik bo'lsa, uni to'g'ri sozlash ehtimoli
    /// shunchalik yuqori. Ma'nosi deyarli bir xil uchta kalit ("rasm
    /// hajmi", "shart rasmi hajmi", "chat rasmi hajmi") administratorni
    /// chalg'itardi va bittasi albatta e'tibordan chetda qolardi.
    /// Ehtiyoj chiqsa alohida kalit qo'shish MUMKIN — o'zgarish shu bitta
    /// metodga tushadi.
    /// </summary>
    private async Task<long> AttachmentLimitBytesAsync(CancellationToken ct)
    {
        var resolved = await settings.ResolveAsync(AttachmentLimitSetting, ct);

        var megabytes = SettingValueParser
            .TryReadDecimal(AttachmentLimitSetting, resolved.Value, out var value)
            ? value
            : decimal.Parse(AttachmentLimitSetting.DefaultValue, CultureInfo.InvariantCulture);

        return (long)megabytes * 1024 * 1024;
    }

    private async Task TryDeleteFromStorageAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(objectKey, ct);
        }
        catch (ServiceUnavailableException ex)
        {
            GroupChatAttachmentLog.OrphanedObject(logger, ex, objectKey);
        }
    }

    /// <summary>
    /// Turkum -> saqlanadigan tur. <see cref="AttachmentCategories"/> faqat
    /// uchtasini o'tkazadi, ya'ni to'rtinchi holat BO'LMAYDI.
    /// </summary>
    private static AttachmentKind ToAttachmentKind(MediaCategories category) => category switch
    {
        MediaCategories.Audio => AttachmentKind.Audio,
        MediaCategories.Document => AttachmentKind.Document,
        _ => AttachmentKind.Image,
    };

    /// <summary>
    /// Yuklab olinadigan fayl nomi.
    ///
    /// ★ FOYDALANUVCHI BERGAN NOM USTUN (u allaqachon TOZALANGAN —
    /// <see cref="GroupChatAttachment.SanitizeFileName"/>): hujjat
    /// yuborilganda qarshi tomon "shartnoma.pdf" ni ko'rishi kerak.
    ///
    /// 🔴 NOM YO'Q BO'LSA HAM OBYEKT KALITI BERILMAYDI (ichki tuzilma
    /// oshkor bo'lmasin) — faqat kengaytma olinadi.
    /// </summary>
    private static string SuggestAttachmentFileName(AttachmentRow row)
    {
        if (row.FileName is { Length: > 0 } name) return name;

        var extension = Path.GetExtension(row.ObjectKey.AsSpan());

        var prefix = row.Kind switch
        {
            AttachmentKind.Audio => "ovoz",
            AttachmentKind.Document => "hujjat",
            _ => "rasm",
        };

        return string.Create(CultureInfo.InvariantCulture, $"chat-{prefix}-{row.Id}{extension}");
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["files"] = [message] });

    private static PayloadTooLargeException AttachmentTooLarge(long limitBytes)
    {
        var megabytes = (limitBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);

        return new PayloadTooLargeException(
            $"Fayl hajmi {megabytes} MB dan oshmasligi kerak. Chegarani administrator "
            + $"sozlamalardan (`{SettingsRegistry.Keys.LessonImageMaxMb}`) o'zgartira oladi.");
    }

    private static readonly SettingDefinition AttachmentLimitSetting =
        SettingsRegistry.TryGet(SettingsRegistry.Keys.LessonImageMaxMb, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Registrda '{SettingsRegistry.Keys.LessonImageMaxMb}' sozlamasi yo'q.");

    /// <summary>Tekshirilgan, lekin HALI YOZILMAGAN fayl.</summary>
    private sealed record PreparedAttachment(LessonAssetUpload File, MediaSignature Signature);

    /// <summary>Biriktirmani o'qish uchun kerakli TOR proyeksiya.</summary>
    private sealed record AttachmentRow(
        long Id,
        long GroupId,
        GroupChatChannel Channel,
        AttachmentKind Kind,
        string ObjectKey,
        string ContentType,
        string? FileName,
        long SizeBytes);

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
    /// ========================================================================
    /// "CHATLAR" HUBI UCHUN FOYDALANUVCHI KO'RADIGAN BARCHA OQIMLAR
    /// ========================================================================
    ///
    /// Bitta metodda, chunki ro'yxat va bitta guruh qoidasi bir xil
    /// manbalarga tayanishi shart.
    ///
    /// ── 🔴 R38 · FILTR AYNAN SHU YERDA, SQL DARAJASIDA ─────────────────────
    ///
    /// Uchala shoxda ham <see cref="ApplyFilter"/> `IQueryable` ustida
    /// chaqiriladi, ya'ni shart `WHERE` ga tushadi va keraksiz qatorlar
    /// bazadan UMUMAN kelmaydi. Uch sabab, muhimlik tartibida:
    ///
    ///   1) 🔴 MA'LUMOT YO'QOLISHI. Chaqiruvchi metod ro'yxatni saralab,
    ///      <see cref="MaxThreads"/> (200) da KESADI. Filtr kesishdan KEYIN
    ///      (mijozda yoki xotirada) qo'llansa, 201-o'rindagi guruh filtrga
    ///      to'liq mos kelsa ham natijada CHIQMASDI — foydalanuvchi esa
    ///      "bunday guruh yo'q" degan yolg'on javobni olardi;
    ///   2) TARTIB. Kesish saralashdan keyin bo'lgani uchun, filtrlangan
    ///      so'rovda "eng faol 200 ta" endi FILTRLANGAN to'plamning eng
    ///      faol 200 tasi bo'ladi — kutilgan xulq aynan shu;
    ///   3) NARX. Admin 500 guruhli markazda 1000 qator o'rniga o'nlab
    ///      qator o'qiydi va o'qilmaganlar agregatlari ham shu qadar
    ///      kichrayadi.
    ///
    /// ⚠️ TUR FILTRIDA <see cref="GroupType.Curator"/> BO'LISHI MUMKIN EMAS:
    /// u uchala shoxda ham ISTISNO shart sifatida turibdi (kurator guruhining
    /// alohida chati yo'q). So'rovda kelsa u <see cref="EnsureFilterIsUsable"/>
    /// da 400 bo'lib qaytariladi — ya'ni bu yergacha yetib kelmaydi.
    /// </summary>
    private async Task<List<ThreadRow>> AccessibleThreadsAsync(
        User user, GroupChatThreadQuery filter, CancellationToken ct)
    {
        var threads = new List<ThreadRow>();

        if (user.Role == UserRole.Student)
        {
            // ★ FILTR `m.Group!` USTIDA — a'zolik qatori ustida emas.
            //   Shu sababli shart `ApplyFilter` ga guruh navigatsiyasi
            //   orqali beriladi (`Select` dan OLDIN, ya'ni `WHERE` da).
            var groups = await ApplyFilter(
                    db.GroupMembers.AsNoTracking()
                        .Where(m => m.StudentId == user.Id
                                 && m.Status == MemberStatus.Active
                                 && m.Group!.IsActive
                                 && m.Group.Type != GroupType.Curator)
                        .Select(m => m.Group!),
                    filter)
                .Select(g => new GroupThreadRow(
                    g.Id, g.Name, g.Type, g.CategoryId,
                    g.Category == null ? null : g.Category.Name, g.TeacherId))
                .Distinct()
                .ToListAsync(ct);

            foreach (var group in groups)
            {
                threads.Add(ThreadOf(group, GroupChatChannel.Teacher));
                threads.Add(ThreadOf(group, GroupChatChannel.Curator));
            }

            return threads;
        }

        if (user.Role is UserRole.Admin or UserRole.Academic)
        {
            var groups = await ApplyFilter(
                    db.Groups.AsNoTracking()
                        .Where(g => g.IsActive && g.Type != GroupType.Curator),
                    filter)
                .Select(g => new GroupThreadRow(
                    g.Id, g.Name, g.Type, g.CategoryId,
                    g.Category == null ? null : g.Category.Name, g.TeacherId))
                .ToListAsync(ct);

            foreach (var group in groups)
            {
                threads.Add(ThreadOf(group, GroupChatChannel.Teacher));
                threads.Add(ThreadOf(group, GroupChatChannel.Curator));
            }

            return threads;
        }

        // XODIM: ustoz sifatidagi guruhlari + kurator sifatidagi guruhlari.
        // Bitta odam ikkala rolda ham bo'lishi mumkin — o'shanda ikki oqim
        // ham ro'yxatga tushadi va bu TO'G'RI.
        var curatorScope = await curators.ScopeGroupIdsAsync(user.Id, ct);

        var staffGroups = await ApplyFilter(
                db.Groups.AsNoTracking()
                    .Where(g => g.IsActive
                             && g.Type != GroupType.Curator
                             && (g.TeacherId == user.Id || curatorScope.Contains(g.Id))),
                filter)
            .Select(g => new GroupThreadRow(
                g.Id, g.Name, g.Type, g.CategoryId,
                g.Category == null ? null : g.Category.Name, g.TeacherId))
            .ToListAsync(ct);

        foreach (var group in staffGroups)
        {
            if (group.TeacherId == user.Id)
                threads.Add(ThreadOf(group, GroupChatChannel.Teacher));

            if (curatorScope.Contains(group.GroupId))
                threads.Add(ThreadOf(group, GroupChatChannel.Curator));
        }

        return threads;
    }

    /* ===== R38 · FILTR ===== */

    /// <summary>
    /// Tur va kategoriya shartlarini so'rovga qo'shadi.
    ///
    /// ★ UCHALA SHOX UCHUN BITTA METOD va bu ataylab: shartni har shoxda
    /// qo'lda takrorlash — aynan shu faylda ilgari ko'rilgan xato naqshi
    /// (kurator istisnosi to'rt joyda takrorlangani izohda ogohlantirish
    /// bilan yozilgan). Bitta joyda bo'lgani uchun yangi filtr qo'shilganda
    /// birorta shox unutib qololmaydi.
    /// </summary>
    private static IQueryable<Group> ApplyFilter(
        IQueryable<Group> rows, GroupChatThreadQuery filter)
    {
        if (filter.Type is { } type)
            rows = rows.Where(g => g.Type == type);

        if (filter.CategoryId is { } categoryId)
            rows = rows.Where(g => g.CategoryId == categoryId);

        return rows;
    }

    /// <summary>
    /// Filtrning O'ZI ma'noli ekanini tekshiradi.
    ///
    /// 🔴 <see cref="GroupType.Curator"/> — 400. Kurator TURIDAGI guruhning
    /// alohida chati YO'Q (qoida <see cref="AuthorizeAsync"/> da va uchala
    /// shoxda), ya'ni bu qiymat texnik jihatdan ishlar va DOIM bo'sh ro'yxat
    /// berardi. Jimgina bo'sh natija foydalanuvchini "chatlarim yo'qolibdi"
    /// degan xulosaga olib kelardi; aniq xato esa sababni aytadi. Bu —
    /// sinf izohidagi 2-bo'limdagi qaror (ruxsat etilmagan kanal jimgina
    /// almashtirilmaydi, 403 oladi) bilan AYNI falsafa.
    /// </summary>
    private static void EnsureFilterIsUsable(GroupChatThreadQuery filter)
    {
        if (filter.Type is not { } type) return;

        if (!Enum.IsDefined(type))
            throw new ValidationException(Errors(TypeField, "Noma'lum guruh turi."));

        if (type == GroupType.Curator)
        {
            throw new ValidationException(Errors(
                TypeField,
                "Kurator guruhlarining alohida chati yo'q, shuning uchun ular bu "
                + "ro'yxatda umuman ko'rinmaydi. Faqat 'Group' yoki 'Individual' "
                + "bo'yicha filtrlash mumkin."));
        }
    }

    /// <summary>Guruh qatoridan bitta oqim qatorini yasaydi (nusxa takrorlanmasin).</summary>
    private static ThreadRow ThreadOf(GroupThreadRow group, GroupChatChannel channel) =>
        new(group.GroupId,
            group.GroupName,
            channel,
            group.GroupType,
            group.CategoryId,
            group.CategoryName);

    /* ===== /R38 ===== */

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
            message.SentAt,
            MapAttachments(message.Attachments));

    /// <summary>
    /// Biriktirmalar -> DTO, TARTIB bilan.
    ///
    /// ⚠️ Kolleksiya `Include` bilan yuklanadi va EF NAVIGATSIYA TARTIBINI
    /// KAFOLATLAMAYDI — shuning uchun tartib bu yerda ANIQ qo'yiladi.
    /// Aks holda albomdagi rasmlar har yuklashda boshqa tartibda
    /// chizilishi mumkin edi.
    /// </summary>
    private static List<GroupChatAttachmentDto> MapAttachments(
        IEnumerable<GroupChatAttachment> attachments) =>
        attachments
            .OrderBy(a => a.Position)
            .ThenBy(a => a.Id)
            .Select(a => new GroupChatAttachmentDto(
                a.Id, a.Kind, a.ContentType, a.FileName, a.SizeBytes, a.DurationSec))
            .ToList();

    /// <summary>
    /// Ro'yxat uchun qisqartma. Kesish SURROGAT JUFTLIKNI buzmaydi —
    /// aks holda oxiri emojiga to'g'ri kelgan ko'chirma JSON'da
    /// <c>U+FFFD</c> ga aylanardi (aynan shu xato jonli dars chatida
    /// topilgan edi).
    ///
    /// ★ MATNSIZ (faqat biriktirmali) XABAR UCHUN YORLIQ QAYTADI. R16b
    /// dan keyin <c>Body</c> bo'sh bo'lishi mumkin va o'shanda "Chatlar"
    /// ro'yxatida qator BO'M-BO'SH ko'rinardi — foydalanuvchi yangi
    /// xabar kelganini ko'rib, nima kelganini bilmasdi.
    ///
    /// ⚠️ YORLIQ SERVERDA YASALADI, DTO'GA YANGI MAYDON QO'SHILMADI:
    /// <c>GroupChatThreadDto</c> ni o'zgartirish frontend'ning
    /// mavjud "Chatlar" ekranini qayta yozishni talab qilardi, holbuki
    /// bu yerda kerak bo'lgani — bir qatorlik matn.
    /// </summary>
    private static string Preview(GroupChatMessage message)
    {
        var body = message.Body;

        if (body.Length == 0)
        {
            var first = message.Attachments
                .OrderBy(a => a.Position)
                .ThenBy(a => a.Id)
                .FirstOrDefault();

            return first?.Kind switch
            {
                AttachmentKind.Audio => "🎧 Ovozli xabar",
                AttachmentKind.Document => "📎 " + (first.FileName ?? "Hujjat"),
                AttachmentKind.Image => "🖼 Rasm",

                // Biriktirmasi ham, matni ham yo'q — Domain buni RUXSAT
                // ETMAYDI. Bu holat faqat eski/buzuq ma'lumotda uchraydi
                // va bo'sh satr uni JIMGINA yashirmasligi kerak emas,
                // lekin ro'yxatni ham buzmasligi kerak.
                _ => string.Empty,
            };
        }

        if (body.Length <= PreviewLength) return body;

        var cut = PreviewLength;
        if (char.IsHighSurrogate(body[cut - 1])) cut--;

        return body[..cut];
    }

    private static Dictionary<string, string[]> Errors(string field, string message) =>
        new(StringComparer.Ordinal) { [field] = [message] };

    /// <summary>
    /// R38 · `problem.errors` kaliti — QUERY parametri nomi bilan AYNAN bir
    /// xil (camelCase), aks holda frontend xatoni tanlagich yoniga qo'ya
    /// olmasdi.
    /// </summary>
    private const string TypeField = "type";

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

    /// <summary>
    /// Bitta oqim qatori — DTO'ning ichki nusxasi (R38 dan keyin tur va
    /// kategoriya ham olib yuriladi).
    /// </summary>
    private sealed record ThreadRow(
        long GroupId,
        string GroupName,
        GroupChatChannel Channel,
        GroupType GroupType,
        long? CategoryId,
        string? CategoryName);

    /// <summary>
    /// Guruhning oqim ro'yxati uchun kerakli TOR proyeksiyasi.
    ///
    /// ★ ANONIM TUR EMAS, NOMLI record — uchala shox uni bir xil shaklda
    /// yasaydi va <see cref="ThreadOf"/> ga uzatadi. Anonim tur bo'lsa
    /// yordamchi metod umuman yozilmasdi va qator yasash mantiqi uch
    /// nusxada qolardi.
    ///
    /// ★ <c>Distinct()</c> O'QUVCHI SHOXIDA TO'G'RI ISHLAYDI: `record`
    /// tuzilmaviy tenglikka ega, ya'ni bitta guruhning ikki a'zolik qatori
    /// AYNI qiymatlarni beradi va ular birlashadi (avvalgi anonim tur ham
    /// shu xossaga tayangan edi).
    /// </summary>
    private sealed record GroupThreadRow(
        long GroupId,
        string GroupName,
        GroupType GroupType,
        long? CategoryId,
        string? CategoryName,
        long? TeacherId);

    private sealed record ThreadKey(long GroupId, GroupChatChannel Channel);

    private sealed record UnreadRow(long GroupId, GroupChatChannel Channel, int Count);
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). EventId makoni: 5230–5239.
///
/// ⚠️ <c>Zinnur.WebApi.Hubs.GroupChatLog</c> BILAN ARALASHTIRILMASIN: u
/// TRANSPORT hodisalarini (obuna, tarqatish) yozadi va boshqa qatlamda
/// yashaydi. Bu yerdagisi — OMBOR bilan bog'liq yagona hodisa.
/// </summary>
internal static partial class GroupChatAttachmentLog
{
    [LoggerMessage(
        EventId = 5230,
        Level = LogLevel.Warning,
        Message = "Chat biriktirmasining ombordagi obyekti o'chirilmadi — YETIM qoldi "
                  + "(bazaga yozish bekor qilingan). key={Key}")]
    internal static partial void OrphanedObject(ILogger logger, Exception exception, string key);
}
