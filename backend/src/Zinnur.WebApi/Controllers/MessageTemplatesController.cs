using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Broadcasts.Dtos;
using Zinnur.Application.Broadcasts.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Xabar shablonlari (2026-08-16) — "Xabarlar" panelining tanlagichini
/// to'ldiradigan lug'at, Sozlamalar bo'limidan boshqariladi.
///
/// Controller YUPQA: <c>[Authorize(Roles=...)]</c> — faqat DARVOZA. Haqiqiy
/// qoida <see cref="IMessageTemplateService"/> ICHIDA.
/// </summary>
[ApiController]
[Route("api/v1/message-templates")]
[Authorize(Roles = ManageRoles)]
[Produces("application/json")]
public sealed class MessageTemplatesController(IMessageTemplateService templates) : ControllerBase
{
    private const string ManageRoles = "Academic,Admin";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MessageTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MessageTemplateDto>>> List(
        [FromQuery] MessageTemplateListQuery query, CancellationToken ct) =>
        Ok(await templates.ListAsync(query, CurrentUserId, ct));

    [HttpPost]
    [ProducesResponseType<MessageTemplateDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MessageTemplateDto>> Create(
        [FromBody] CreateMessageTemplateRequest request, CancellationToken ct)
    {
        var created = await templates.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(List), null, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<MessageTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessageTemplateDto>> Update(
        long id, [FromBody] UpdateMessageTemplateRequest request, CancellationToken ct) =>
        Ok(await templates.UpdateAsync(id, request, CurrentUserId, ct));

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await templates.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
