using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Tests.Dtos;
using Zinnur.Application.Tests.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Onlayn testlar: tuzish (o'quv bo'limi/admin) va yechish (o'quvchi).
///
/// ★ IKKI KO'RINISH IKKI XIL TUR QAYTARADI:
///   • <c>GET /tests/{id}</c>      -> <see cref="TestAuthoringDto"/> — to'g'ri
///     javoblar BILAN, faqat xodim uchun;
///   • <c>GET /tests/{id}/take</c> -> <see cref="TakeTestDto"/> — to'g'ri javob
///     maydoni UMUMAN YO'Q.
///
/// Nima uchun bitta DTO'dan maydonni "olib tashlash" emas: bitta tur bo'lsa
/// javoblarni yashirish PROGRAMMIST E'TIBORIGA bog'liq bo'lardi va bir joyda
/// unutilsa hech kim sezmasdi (test o'z-o'zidan ishlayveradi). Alohida tur
/// bilan bu xato KOMPILYATSIYA darajasida imkonsiz.
/// </summary>
[ApiController]
[Route("api/v1/tests")]
[Authorize]
[Produces("application/json")]
public sealed class TestsController(ITestService tests) : ControllerBase
{
    // ================================================================= tuzish (xodim)

    [HttpGet]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<PagedResult<TestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TestDto>>> List(
        [FromQuery] TestListQuery query, CancellationToken ct) =>
        Ok(await tests.ListAsync(query, CurrentUserId, ct));

    /// <summary>Tahrirlash ko'rinishi — TO'G'RI JAVOBLAR BILAN (faqat xodim).</summary>
    [HttpGet("{id:long}")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<TestAuthoringDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TestAuthoringDto>> Get(long id, CancellationToken ct) =>
        Ok(await tests.GetForAuthoringAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Yangi test. <c>kind</c> JSON'da SATR: <c>"Lesson"</c> (kurs darsiga
    /// bog'langan, sur'at nazoratiga kiradi) yoki <c>"Competition"</c>.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<TestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TestDto>> Create(
        [FromBody] CreateTestRequest request, CancellationToken ct)
    {
        var created = await tests.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<TestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TestDto>> Update(
        long id, [FromBody] UpdateTestRequest request, CancellationToken ct) =>
        Ok(await tests.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>O'chirish. Urinish boshlangan bo'lsa 409 (natijalar yo'qolmasin).</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await tests.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Savol + variantlar qo'shish. Kamida 2 variant, kamida 1 to'g'ri.</summary>
    [HttpPost("{id:long}/questions")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<AuthoringQuestionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthoringQuestionDto>> AddQuestion(
        long id, [FromBody] SaveQuestionRequest request, CancellationToken ct)
    {
        var created = await tests.AddQuestionAsync(id, request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id }, created);
    }

    /// <summary>Savolni tahrirlash — variantlar BUTUNLAY almashtiriladi.</summary>
    [HttpPut("{id:long}/questions/{questionId:long}")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<AuthoringQuestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthoringQuestionDto>> UpdateQuestion(
        long id, long questionId, [FromBody] SaveQuestionRequest request, CancellationToken ct) =>
        Ok(await tests.UpdateQuestionAsync(id, questionId, request, CurrentUserId, ct));

    [HttpDelete("{id:long}/questions/{questionId:long}")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteQuestion(long id, long questionId, CancellationToken ct)
    {
        await tests.DeleteQuestionAsync(id, questionId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>E'lon qilish — bo'sh yoki nuqsonli test rad etiladi (409).</summary>
    [HttpPost("{id:long}/publish")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<TestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TestDto>> Publish(long id, CancellationToken ct) =>
        Ok(await tests.SetPublishedAsync(id, published: true, CurrentUserId, ct));

    [HttpPost("{id:long}/unpublish")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<TestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestDto>> Unpublish(long id, CancellationToken ct) =>
        Ok(await tests.SetPublishedAsync(id, published: false, CurrentUserId, ct));

    /// <summary>Natijalar: BITTA URINISH = BITTA QATOR (ikki guruhdagi o'quvchi takrorlanmaydi).</summary>
    [HttpGet("{id:long}/results")]
    [Authorize(Roles = AuthorRoles)]
    [ProducesResponseType<IReadOnlyList<TestResultRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TestResultRowDto>>> Results(
        long id, CancellationToken ct) =>
        Ok(await tests.ListResultsAsync(id, CurrentUserId, ct));

    /// <summary>CSV eksport (Excel uchun BOM bilan, mahalliy vaqtda).</summary>
    [HttpGet("{id:long}/results/export")]
    [Authorize(Roles = AuthorRoles)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportResults(long id, CancellationToken ct)
    {
        var export = await tests.ExportResultsCsvAsync(id, CurrentUserId, ct);

        return File(export.Content.ToArray(), export.ContentType, export.FileName);
    }

    // ================================================================= yechish (o'quvchi)

    /// <summary>
    /// Mavjud testlar: musobaqalar + darsi OCHIQ bo'lgan dars testlari.
    /// </summary>
    [HttpGet("available")]
    [Authorize(Roles = StudentRole)]
    [ProducesResponseType<IReadOnlyList<AvailableTestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AvailableTestDto>>> Available(CancellationToken ct) =>
        Ok(await tests.ListAvailableAsync(CurrentUserId, ct));

    /// <summary>
    /// Urinishni boshlaydi. IDEMPOTENT: qayta chaqirilsa AYNI urinish qaytadi
    /// va taymer noldan boshlanmaydi.
    /// </summary>
    [HttpPost("{id:long}/start")]
    [Authorize(Roles = StudentRole)]
    [ProducesResponseType<StartAttemptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StartAttemptDto>> Start(long id, CancellationToken ct) =>
        Ok(await tests.StartAsync(id, CurrentUserId, ct));

    /// <summary>★ Yechish varaqasi. Javobda <c>isCorrect</c> MAYDONI YO'Q.</summary>
    [HttpGet("{id:long}/take")]
    [Authorize(Roles = StudentRole)]
    [ProducesResponseType<TakeTestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TakeTestDto>> Take(long id, CancellationToken ct) =>
        Ok(await tests.GetForTakingAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Javoblarni topshiradi. Baholash SERVERDA. Ikkinchi topshirish 409
    /// qaytaradi (500 emas) — unikal indeks va `xmin` qulfi tufayli.
    /// </summary>
    [HttpPost("{id:long}/submit")]
    [Authorize(Roles = StudentRole)]
    [ProducesResponseType<MyResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MyResultDto>> Submit(
        long id, [FromBody] SubmitTestRequest request, CancellationToken ct) =>
        Ok(await tests.SubmitAsync(id, request, CurrentUserId, ct));

    [HttpGet("{id:long}/my-result")]
    [Authorize(Roles = StudentRole)]
    [ProducesResponseType<MyResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MyResultDto>> MyResult(long id, CancellationToken ct) =>
        Ok(await tests.GetMyResultAsync(id, CurrentUserId, ct));

    // ---------------------------------------------------------------- ichki

    /// <summary>
    /// Test TUZISH darvozasi. Ustoz kirmaydi: test kurs darsiga yoki butun
    /// platformaga taalluqli (ROADMAP 3.4).
    /// </summary>
    private const string AuthorRoles = "Academic,Admin";

    private const string StudentRole = "Student";

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
