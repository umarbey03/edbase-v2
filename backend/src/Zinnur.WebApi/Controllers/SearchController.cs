using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Search.Dtos;
using Zinnur.Application.Search.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// GLOBAL QIDIRUV (2026-08-18) — navbardagi yagona qidiruv maydoni.
///
/// ★ BITTA SO'ROV, KO'P TUR: har bo'lim uchun alohida so'rov yuborilsa,
/// har bosilgan harfda 5 ta HTTP so'rov ketardi va ular tartibsiz
/// qaytib natijalar sakrab turardi.
///
/// ★ NATIJALAR ROLGA QARAB FILTRLANADI — qoida servisda
/// (<see cref="IGlobalSearchService"/> izohi).
///
/// Controller YUPQA.
/// </summary>
[ApiController]
[Route("api/v1/search")]
[Authorize]
[Produces("application/json")]
public sealed class SearchController(IGlobalSearchService search) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GlobalSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GlobalSearchResultDto>> Search(
        [FromQuery] GlobalSearchQuery query, CancellationToken ct) =>
        Ok(await search.SearchAsync(query, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
