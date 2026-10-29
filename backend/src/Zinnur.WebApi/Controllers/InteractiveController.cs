using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Interactive.Dtos;
using Zinnur.Application.Interactive.Services;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// INTERAKTIV DARSLIK (ATF) — 2026-10-29
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ Kirish qoidasi BITTA joyda — `InteractiveUnitService.EnsureReaderAsync`:
///   o'quvchi uchun huquq (Mustaqil+ / guruh), birlik darsga bog'langan va
///   kitob nashr qilingan, dars gating bo'yicha ochiq. Bu yerda faqat HTTP.
/// 🔴 Javob kalitlari va ustoz izohlari o'quvchi javobiga HECH QACHON
///   tushmaydi (oq ro'yxat proyeksiyasi); to'liq kontent — faqat `/staff`.
/// </summary>
[ApiController]
[Route("api/v1/interactive")]
[Authorize]
[Produces("application/json")]
public sealed class InteractiveController(IInteractiveUnitService units) : ControllerBase
{
    private const string StaffRoles = "Teacher,Assistant,Academic,Admin";
    private const string ManageRoles = "Academic,Admin";

    /// <summary>Dars sahifasidagi "Darslik" kartasi: huquq holati va birlik progressi.</summary>
    [HttpGet("lessons/{lessonId:long}")]
    [ProducesResponseType<LessonBookDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LessonBookDto>> Lesson(long lessonId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await units.GetLessonBookAsync(lessonId, CurrentUserId, ct));
    }

    /// <summary>Birlik — o'quvchi proyeksiyasi (kalitsiz) + mening progressim.</summary>
    [HttpGet("units/{unitId:long}")]
    [ProducesResponseType<JsonObject>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JsonObject>> Unit(long unitId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-cache";
        return Ok(await units.GetUnitAsync(unitId, CurrentUserId, ct));
    }

    /// <summary>To'liq kontent (kalitlar, `ustozga`, ustoz qo'llanmasi) — faqat xodim.</summary>
    [HttpGet("units/{unitId:long}/staff")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<StaffUnitDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffUnitDto>> Staff(long unitId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await units.GetStaffUnitAsync(unitId, CurrentUserId, ct));
    }

    /// <summary>Birlikning barcha audiosi uchun bitta chipta (15 daqiqa).</summary>
    [HttpGet("units/{unitId:long}/audio-ticket")]
    [ProducesResponseType<AudioTicketDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AudioTicketDto>> AudioTicket(long unitId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await units.CreateAudioTicketAsync(unitId, CurrentUserId, ct));
    }

    /// <summary>
    /// Audio fayl (`Range` bilan). `&lt;audio src&gt;` sarlavha yubora olmaydi —
    /// shuning uchun `?t=` chipta; ruxsat baribir HAR so'rovda tekshiriladi.
    /// </summary>
    [HttpGet("units/{unitId:long}/audio/{handle}")]
    [AllowAnonymous]
    [Produces("audio/mpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Audio(long unitId, string handle, [FromQuery] string? t, CancellationToken ct)
    {
        if (ResolveActorId(unitId, t) is not { } actorId) return Unauthorized();

        var download = await units.OpenAudioAsync(
            unitId, handle, MediaResponse.RawRange(Request.Headers.Range), actorId, ct);

        // Kontent manzilli fayl — o'zgarmaydi; `private`: oraliq kesh saqlamasin.
        return await MediaResponse.WriteAsync(this, download, "private, max-age=86400, immutable", ct);
    }

    /// <summary>Bitta savol yoki mashqni tekshirish (birinchi urinish hisobga olinadi).</summary>
    [HttpPost("units/{unitId:long}/items/{itemKey}/check")]
    [ProducesResponseType<CheckItemResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CheckItemResult>> Check(
        long unitId, string itemKey, [FromBody] CheckItemRequest request, CancellationToken ct) =>
        Ok(await units.CheckAsync(unitId, itemKey, request, CurrentUserId, ct));

    [HttpPost("units/{unitId:long}/blocks/{blockId}/done")]
    [ProducesResponseType<UnitProgressDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnitProgressDto>> BlockDone(long unitId, string blockId, CancellationToken ct) =>
        Ok(await units.MarkBlockDoneAsync(unitId, blockId, CurrentUserId, ct));

    /// <summary>`yozish` pog'onasining namunasi — o'quvchi "Yozdim" dan keyin.</summary>
    [HttpPost("units/{unitId:long}/blocks/{blockId}/reveal")]
    [ProducesResponseType<RevealResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RevealResult>> Reveal(
        long unitId, string blockId, [FromBody] RevealRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await units.RevealAsync(unitId, blockId, request.N, CurrentUserId, ct));
    }

    /// <summary>Mini-test savollari — kalitsiz.</summary>
    [HttpPost("units/{unitId:long}/test/start")]
    [ProducesResponseType<JsonObject>(StatusCodes.Status200OK)]
    public async Task<ActionResult<JsonObject>> StartTest(long unitId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await units.StartTestAsync(unitId, CurrentUserId, ct));
    }

    [HttpPost("units/{unitId:long}/test/submit")]
    [ProducesResponseType<TestSubmitResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestSubmitResult>> SubmitTest(
        long unitId, [FromBody] TestSubmitRequest request, CancellationToken ct) =>
        Ok(await units.SubmitTestAsync(unitId, request, CurrentUserId, ct));

    // ---------------------------------------------------------------- boshqaruv

    [HttpGet("books")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<AdminBookDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminBookDto>>> Books(CancellationToken ct) =>
        Ok(await units.ListBooksAsync(CurrentUserId, ct));

    [HttpPut("units/{unitId:long}/lesson")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<AdminUnitDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUnitDto>> SetLesson(
        long unitId, [FromBody] SetUnitLessonRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await units.SetUnitLessonAsync(unitId, request.ModuleLessonId, CurrentUserId, ct));
    }

    [HttpPost("books/{bookId:long}/publish")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<AdminBookDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminBookDto>> Publish(
        long bookId, [FromBody] PublishBookRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await units.PublishAsync(bookId, request.Published, CurrentUserId, ct));
    }

    // ---------------------------------------------------------------- ichki

    private long? ResolveActorId(long unitId, string? ticket)
    {
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } subject
            && long.TryParse(subject, CultureInfo.InvariantCulture, out var sessionUserId))
        {
            return sessionUserId;
        }

        return units.ResolveTicket(ticket, unitId);
    }

    private long CurrentUserId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
