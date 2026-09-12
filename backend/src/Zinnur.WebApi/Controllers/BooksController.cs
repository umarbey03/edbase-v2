using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Books.Dtos;
using Zinnur.Application.Books.Services;
using Zinnur.Application.Media;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// KUTUBXONA (2026-09-09) — jonli darsda ulashiladigan PDF kitoblar.
/// Controller YUPQA: rollar DARVOZA, haqiqiy tekshiruv <c>BookService</c> da.
/// </summary>
[ApiController]
[Route("api/v1/books")]
[Authorize]
[Produces("application/json")]
public sealed class BooksController(IBookService books) : ControllerBase
{
    private const string ManageRoles = "Academic,Admin";

    /// <summary>Kitoblar ro'yxati. <c>includeInactive</c> faqat Academic/Admin uchun ishlaydi.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BookDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BookDto>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await books.ListAsync(CurrentUserId, includeInactive, ct));

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(BookService.MaxBytes + (1024 * 1024))]
    [RequestFormLimits(MultipartBodyLengthLimit = BookService.MaxBytes + (1024 * 1024))]
    [ProducesResponseType<BookDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BookDto>> Upload(
        IFormFile file,
        [FromForm] string? title,
        [FromForm] int? pageCount,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw MediaResponse.MissingFile();

        await using var stream = file.OpenReadStream();

        var created = await books.UploadAsync(
            new BookUpload(file.FileName, file.ContentType, stream, file.Length, title, pageCount),
            CurrentUserId,
            ct);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<BookDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BookDto>> Update(
        long id, [FromBody] UpdateBookRequest request, CancellationToken ct) =>
        Ok(await books.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>
    /// Faylning O'ZI (oqim, <c>Range</c> bilan). Sessiya tokeni YOKI
    /// <c>?ticket=</c> — brauzerdagi PDF o'quvchi sarlavha yubora olmaydi.
    /// </summary>
    [HttpGet("{id:long}/file")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    public async Task<IActionResult> File(long id, [FromQuery] string? ticket, CancellationToken ct)
    {
        if (ResolveActorId(id, ticket) is not { } actorId)
            return Unauthorized();

        var download = await books.OpenAsync(id, MediaResponse.RawRange(Request.Headers.Range), actorId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    [HttpGet("{id:long}/ticket")]
    [ProducesResponseType<MediaAccessTicket>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MediaAccessTicket>> Ticket(long id, CancellationToken ct)
    {
        var ticket = await books.CreateTicketAsync(id, CurrentUserId, ct);

        // Ichida imzo bor — keshlanmasin (`LessonAssetsController.Ticket` bilan ayni).
        Response.Headers.CacheControl = "no-store";

        return Ok(ticket);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await books.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    private long? ResolveActorId(long bookId, string? ticket)
    {
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } subject
            && long.TryParse(subject, CultureInfo.InvariantCulture, out var sessionUserId))
        {
            return sessionUserId;
        }

        return books.ResolveTicket(ticket, bookId);
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
