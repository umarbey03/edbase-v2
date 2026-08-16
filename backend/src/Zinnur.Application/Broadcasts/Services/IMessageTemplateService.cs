using Zinnur.Application.Broadcasts.Dtos;

namespace Zinnur.Application.Broadcasts.Services;

/// <summary>
/// Xabar shablonlari CRUD'i (2026-08-16) — "Xabarlar" panelidagi tanlagichni
/// to'ldiradigan lug'at. Naqsh <c>IGroupCategoryService</c> BILAN AYNI:
/// o'quv bo'limi/admin yozadi, ular o'qiydi (o'quvchi umuman kirmaydi).
/// </summary>
public interface IMessageTemplateService
{
    Task<IReadOnlyList<MessageTemplateDto>> ListAsync(
        MessageTemplateListQuery query, long actorId, CancellationToken ct = default);

    Task<MessageTemplateDto> CreateAsync(
        CreateMessageTemplateRequest request, long actorId, CancellationToken ct = default);

    Task<MessageTemplateDto> UpdateAsync(
        long id, UpdateMessageTemplateRequest request, long actorId, CancellationToken ct = default);

    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);
}
