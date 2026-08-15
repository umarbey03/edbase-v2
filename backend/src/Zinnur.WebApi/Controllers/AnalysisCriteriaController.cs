using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.AnalysisCriteria.Dtos;
using Zinnur.Application.AnalysisCriteria.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// DARS TAHLILI MEZONLARI (R29/R30 kengaytmasi) — o'quv bo'limi (yoki Admin)
/// "O'quv bo'limi sozlamalari" sahifasining "Mezonlar" bo'limidan dinamik
/// boshqaradi.
///
/// Controller YUPQA — haqiqiy qoida (nom/maksimal ball chegaralari, snapshot)
/// <see cref="Zinnur.Domain.Entities.AnalysisCriterion"/> va
/// <see cref="IAnalysisCriterionService"/> ichida.
///
/// Rollar <c>SessionReviewsController.WriteRoles</c> bilan AYNI:
/// kim mezon belgilay olsa, tahlil ham o'sha yozadi.
/// </summary>
[ApiController]
[Route("api/v1/analysis-criteria")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class AnalysisCriteriaController(IAnalysisCriterionService criteria) : ControllerBase
{
    /// <response code="200">Mezonlar ro'yxati, ko'rsatish tartibida.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AnalysisCriterionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AnalysisCriterionDto>>> List(CancellationToken ct) =>
        Ok(await criteria.ListAsync(ct));

    /// <response code="201">Yaratilgan mezon.</response>
    /// <response code="409">Nom bo'sh yoki maksimal ball chegaradan tashqari.</response>
    [HttpPost]
    [ProducesResponseType<AnalysisCriterionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AnalysisCriterionDto>> Create(
        [FromBody] SaveAnalysisCriterionRequest request, CancellationToken ct)
    {
        var created = await criteria.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <response code="200">Yangilangan mezon.</response>
    /// <response code="404">Mezon topilmadi.</response>
    /// <response code="409">Nom bo'sh yoki maksimal ball chegaradan tashqari.</response>
    [HttpPut("{id:long}")]
    [ProducesResponseType<AnalysisCriterionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AnalysisCriterionDto>> Update(
        long id, [FromBody] SaveAnalysisCriterionRequest request, CancellationToken ct) =>
        Ok(await criteria.UpdateAsync(id, request, ct));

    /// <summary>O'chiradi. IDEMPOTENT: mezon bo'lmasa ham <c>204</c>.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await criteria.DeleteAsync(id, ct);
        return NoContent();
    }
}
