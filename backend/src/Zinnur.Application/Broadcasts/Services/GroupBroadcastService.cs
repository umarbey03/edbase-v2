using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Broadcasts.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Application.GroupChat.Services;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Broadcasts.Services;

/// <summary>
/// <see cref="IGroupBroadcastService"/> ning amalga oshirilishi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ IKKI MAVJUD PORTGA TAYANADI, YANGISINI YOZMAYDI:
///
///  1) <see cref="IGroupChatService.SendAsync"/> — "platforma chati" qismi.
///     Academic/Admin bu metodga ALLAQACHON kira oladi
///     (`GroupChatService.AvailableChannelsAsync`: nazorat rollari ikkala
///     oqimni ham ko'radi). Yozib chiqishning o'zi TAKRORLANMAYDI.
///
///  2) <see cref="INotificationOutbox"/> — "Telegram" qismi. Bu ANIQ o'sha
///     bo'shliq: `GroupChatService.SendAsync` izohida "HAR xabar uchun
///     Telegram yuborilmaydi ... to'g'ri yechim FAQAT E'LONLAR uchun,
///     KEYINGI QADAM deb belgilandi, port TAYYOR turibdi" deb yozilgan —
///     shu "keyingi qadam" aynan shu klass.
/// ══════════════════════════════════════════════════════════════════════
///
/// ⚠️ KURATOR GURUHI: platforma chatiga YOZILMAYDI (`GroupChatService.
/// AuthorizeAsync` bunday guruhda 403 beradi — "kurator guruhining alohida
/// chati yo'q"), lekin Telegram DM baribir yuboriladi (guruh a'zolari bor,
/// ular DM olishi kerak). Ya'ni ikki kanal MUSTAQIL: biri o'tkazib
/// yuborilsa ham ikkinchisi ishlayveradi.
/// </summary>
public sealed class GroupBroadcastService(
    IApplicationDbContext db,
    IGroupChatService groupChat,
    INotificationOutbox outbox,
    TimeProvider clock) : IGroupBroadcastService
{
    private const string TemplateKey = "group_broadcast";

    public async Task<GroupBroadcastDto> SendAsync(
        SendGroupBroadcastRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        if (request.GroupIds is null || request.GroupIds.Count == 0)
            throw Invalid("groupIds", "Kamida bitta guruh tanlanishi shart.");

        var body = (request.Body ?? string.Empty).Trim();

        if (body.Length == 0)
            throw Invalid("body", "Xabar matni kiritilishi shart.");

        if (body.Length > NotificationText.MaxBodyLength)
            throw Invalid("body", "Xabar matni juda uzun.");

        if (!request.SendToTelegram && !request.SendToPlatformChat)
        {
            throw Invalid(
                "sendToTelegram", "Yuborish kanali tanlanmagan — Telegram yoki platforma chatidan kamida bittasi kerak.");
        }

        if (request.TemplateId is { } templateId)
        {
            var templateExists = await db.MessageTemplates.AsNoTracking()
                .AnyAsync(t => t.Id == templateId, ct);
            if (!templateExists)
                throw new NotFoundException(nameof(MessageTemplate), templateId);
        }

        var distinctGroupIds = request.GroupIds.Distinct().ToList();

        var groups = await db.Groups.AsNoTracking()
            .Where(g => distinctGroupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.Type, g.IsActive })
            .ToListAsync(ct);

        var missing = distinctGroupIds.Except(groups.Select(g => g.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException(
                nameof(Group), string.Join(", ", missing.Select(id => id.ToString(CultureInfo.InvariantCulture))));
        }

        // ★ ARXIVLANGAN GURUH ATAYLAB TO'SILADI: "bu yerda hech kim yo'q"
        // degan xabar yuborish ma'nosiz va chalkashtiradi (kim yuborsa ham
        // hech kimga yetmaydi, lekin tarixda "yuborildi" bo'lib qoladi).
        var archived = groups.Where(g => !g.IsActive).Select(g => g.Name).ToList();
        if (archived.Count > 0)
        {
            throw new ConflictException(
                "Arxivlangan guruhga xabar yuborilmaydi: " + string.Join(", ", archived));
        }

        // ── 1) TARIX YOZUVI — DARHOL SAQLANADI, ID KERAK (idempotentlik kaliti uchun) ──
        var broadcast = new GroupBroadcast
        {
            AuthorId = actorId,
            TemplateId = request.TemplateId,
            Body = body,
            TargetGroupNames = TruncateNames(string.Join(", ", groups.Select(g => g.Name))),
            TargetGroupCount = groups.Count,
            SentToTelegram = request.SendToTelegram,
            SentToPlatformChat = request.SendToPlatformChat,
        };

        broadcast.Validate();

        db.GroupBroadcasts.Add(broadcast);
        await db.SaveChangesAsync(ct);

        // ── 2) PLATFORMA CHATI — mavjud servis, TAKRORLANMAYDI ──
        if (request.SendToPlatformChat)
        {
            foreach (var group in groups)
            {
                // Kurator guruhida chat yo'q — sinf izohidagi mustaqillik
                // qoidasi: bu yerda 403 tashlanmaydi, shunchaki o'tkazib
                // yuboriladi (Telegram DM baribir ketadi).
                if (group.Type == GroupType.Curator) continue;

                await groupChat.SendAsync(
                    actorId, group.Id, new SendGroupChatMessageRequest(GroupChatChannel.Teacher, body), ct);
            }
        }

        // ── 3) TELEGRAM — INotificationOutbox orqali, HAR A'ZOGA ALOHIDA ──
        var recipientCount = 0;

        if (request.SendToTelegram)
        {
            var groupIds = groups.Select(g => g.Id).ToList();

            // ═══════════════════════════════════════════════════════════
            // ★ KURATOR GURUHI HISOBGA OLINADI (2026-08-18 da to'g'rilandi)
            //
            // Qoida `GroupMembershipScope` da: kurator guruhida o'quvchilar
            // TO'G'RIDAN-TO'G'RI a'zo BO'LMAYDI. Ilgari bu yerda faqat
            // `m.GroupId` bor edi va kurator guruhi tanlanganda oluvchilar
            // soni NOL chiqardi — yuqoridagi platforma chati shoxi esa
            // "Telegram baribir ketadi" deb o'tkazib yuborardi, ya'ni
            // xabar HECH KIMGA yetmasdi, tarixda esa "yuborildi" bo'lib
            // qolardi (aynan `archived` tekshiruvi qochgan holat).
            //
            // ★ IFODA QO'LDA: `GroupMembershipScope.ActiveIn` bitta
            //   `groupId` ni oladi, bu yerda esa ID'lar TO'PLAMI kerak.
            //   `long?` ro'yxati — `CuratorGroupId` nullable ustun.
            //
            // ★ `Distinct()` MUHIM: bitta kurator guruhiga bir necha ustoz
            //   guruhi bog'lanadi va ikkalasi ham tanlangan bo'lsa,
            //   o'quvchi ikki marta chiqardi.
            // ═══════════════════════════════════════════════════════════
            var curatorGroupIds = groupIds.ConvertAll(id => (long?)id);

            var recipients = await db.GroupMembers.AsNoTracking()
                .Where(m => (groupIds.Contains(m.GroupId)
                        || curatorGroupIds.Contains(m.Group!.CuratorGroupId))
                    && m.Status == MemberStatus.Active)
                .Select(m => m.StudentId)
                .Distinct()
                .ToListAsync(ct);

            var chatIds = await db.Users.AsNoTracking()
                .Where(u => recipients.Contains(u.Id) && u.TelegramId != null)
                .Select(u => new { u.Id, u.TelegramId })
                .ToListAsync(ct);

            var escapedBody = NotificationText.Escape(body);
            var now = clock.GetUtcNow();

            foreach (var recipient in chatIds)
            {
                await outbox.EnqueueAsync(
                    new NotificationRequest
                    {
                        Channel = NotificationChannel.Telegram,
                        RecipientUserId = recipient.Id,
                        RecipientAddress = recipient.TelegramId!.Value.ToString(CultureInfo.InvariantCulture),
                        TemplateKey = TemplateKey,
                        Body = escapedBody,

                        // Kalit "hodisa + obyekt + qabul qiluvchi": AYNI
                        // yuborish (bitta `GroupBroadcast.Id`) bir odamga
                        // ikki marta navbatga tushmaydi, lekin YANGI
                        // yuborish (yangi Id) — YANGI xabar.
                        IdempotencyKey = string.Create(
                            CultureInfo.InvariantCulture,
                            $"group_broadcast:{broadcast.Id}:{recipient.Id}"),
                        SendAfter = now,
                    },
                    ct);

                recipientCount++;
            }

            broadcast.TelegramRecipientCount = recipientCount;
            await db.SaveChangesAsync(ct);
        }

        return await GetDtoAsync(broadcast.Id, ct);
    }

    public async Task<PagedResult<GroupBroadcastDto>> ListAsync(
        GroupBroadcastListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var rows = db.GroupBroadcasts.AsNoTracking();

        var total = await rows.CountAsync(ct);

        var items = await Project(rows
                .OrderByDescending(b => b.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<GroupBroadcastDto>(items, page, pageSize, total);
    }

    private async Task<GroupBroadcastDto> GetDtoAsync(long id, CancellationToken ct) =>
        await Project(db.GroupBroadcasts.AsNoTracking().Where(b => b.Id == id)).FirstAsync(ct);

    private static IQueryable<GroupBroadcastDto> Project(IQueryable<GroupBroadcast> rows) =>
        rows.Select(b => new GroupBroadcastDto(
            b.Id,
            b.AuthorId,
            b.Author == null ? "Noma'lum xodim" : (b.Author.FullName ?? "Noma'lum xodim"),
            b.TemplateId,
            b.Template == null ? null : b.Template.Name,
            b.Body,
            b.TargetGroupNames,
            b.TargetGroupCount,
            b.SentToTelegram,
            b.SentToPlatformChat,
            b.TelegramRecipientCount,
            b.CreatedAt));

    private static string TruncateNames(string value) =>
        value.Length > GroupBroadcast.MaxTargetNamesLength
            ? string.Concat(value.AsSpan(0, GroupBroadcast.MaxTargetNamesLength - 1), "…")
            : value;

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    private static void EnsureCanManage(User actor)
    {
        if (actor.Role is UserRole.Academic or UserRole.Admin) return;

        throw new ForbiddenException("Guruhlarga xabar yuborishni faqat o'quv bo'limi yoki admin bajaradi.");
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
