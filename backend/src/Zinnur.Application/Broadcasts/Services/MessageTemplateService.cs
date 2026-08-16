using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Broadcasts.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Broadcasts.Services;

/// <summary>
/// <see cref="IMessageTemplateService"/> ning amalga oshirilishi.
///
/// RUXSAT: <c>GroupCategoryService</c> BILAN AYNI qoida — o'qish/yozish
/// faqat Academic/Admin (o'quvchi va boshqa xodim shablon matnlarini
/// ko'rmaydi/o'zgartirmaydi, chunki bu ICHKI xabar vositasi).
/// </summary>
public sealed class MessageTemplateService(IApplicationDbContext db) : IMessageTemplateService
{
    public async Task<IReadOnlyList<MessageTemplateDto>> ListAsync(
        MessageTemplateListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await EnsureCanManageAsync(actorId, ct);

        var rows = db.MessageTemplates.AsNoTracking();

        if (query.IsActive is { } isActive)
            rows = rows.Where(t => t.IsActive == isActive);

        return await rows
            .OrderByDescending(t => t.IsActive)
            .ThenBy(t => t.Name)
            .Select(t => new MessageTemplateDto(t.Id, t.Name, t.Body, t.IsActive, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<MessageTemplateDto> CreateAsync(
        CreateMessageTemplateRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureCanManageAsync(actorId, ct);

        var template = new MessageTemplate
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Body = (request.Body ?? string.Empty).Trim(),
            IsActive = request.IsActive,
        };

        template.Validate();

        db.MessageTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return Map(template);
    }

    public async Task<MessageTemplateDto> UpdateAsync(
        long id, UpdateMessageTemplateRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureCanManageAsync(actorId, ct);

        var template = await db.MessageTemplates.AsTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(MessageTemplate), id);

        template.Name = (request.Name ?? string.Empty).Trim();
        template.Body = (request.Body ?? string.Empty).Trim();
        template.IsActive = request.IsActive;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        template.Validate();

        await db.SaveChangesAsync(ct);

        return Map(template);
    }

    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        await EnsureCanManageAsync(actorId, ct);

        var template = await db.MessageTemplates.AsTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(MessageTemplate), id);

        // ★ ESKI YUBORILGAN XABARLARGA TA'SIR QILMAYDI: `GroupBroadcast.Body`
        // snapshot va `TemplateId` FK `SetNull` (izohi entity'da) —
        // o'chirish tarixni buzmaydi.
        db.MessageTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureCanManageAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct);

        if (role is UserRole.Academic or UserRole.Admin) return;

        throw new ForbiddenException("Xabar shablonlarini faqat o'quv bo'limi yoki admin boshqaradi.");
    }

    private static MessageTemplateDto Map(MessageTemplate template) =>
        new(template.Id, template.Name, template.Body, template.IsActive, template.CreatedAt, template.UpdatedAt);
}
