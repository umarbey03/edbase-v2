using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Users.Dtos;
using Zinnur.Application.Users.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Foydalanuvchilarni boshqarish (o'quv bo'limi / admin paneli).
///
/// Controller YUPQA: <c>[Authorize(Roles=...)]</c> — bu faqat DARVOZA
/// ("umuman kira oladimi"). "Kim kimni tahrirlay oladi" degan asosiy qoida
/// <see cref="IUserService"/> ICHIDA — aks holda yangi endpoint qo'shilganda
/// uni takrorlash unutilardi (eski tizim zaifligi X-4).
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Roles = "Academic,Admin")]
[Produces("application/json")]
public sealed class UsersController(IUserService users) : ControllerBase
{
    /// <summary>Ro'yxat: qidiruv, rol/faollik filtri, sahifalash.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserDetailsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserDetailsDto>>> List(
        [FromQuery] UserListQuery query, CancellationToken ct) =>
        Ok(await users.ListAsync(query, CurrentUserId, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailsDto>> Get(long id, CancellationToken ct) =>
        Ok(await users.GetAsync(id, CurrentUserId, ct));

    /// <summary>Yangi foydalanuvchi. Parol berilmasa server generatsiya qiladi.</summary>
    [HttpPost]
    [ProducesResponseType<CreateUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateUserResponse>> Create(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.User.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDetailsDto>> Update(
        long id, [FromBody] UpdateUserRequest request, CancellationToken ct) =>
        Ok(await users.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>Profilni o'chirish — barcha sessiyalari darhol bekor qilinadi.</summary>
    [HttpPost("{id:long}/deactivate")]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDetailsDto>> Deactivate(long id, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, isActive: false, CurrentUserId, ct));

    [HttpPost("{id:long}/activate")]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDetailsDto>> Activate(long id, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, isActive: true, CurrentUserId, ct));

    /// <summary>Vaqtinchalik parol. Javobda BIR MARTA ko'rinadi — saqlab qo'ying.</summary>
    [HttpPost("{id:long}/reset-password")]
    [ProducesResponseType<ResetPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(long id, CancellationToken ct) =>
        Ok(await users.ResetPasswordAsync(id, CurrentUserId, ct));

    /// <summary>CSV import: <c>full_name,phone,email,role</c>. Xato qatorlar hisobotda qaytadi.</summary>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType<UserImportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserImportResponse>> Import(IFormFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        await using var stream = file.OpenReadStream();
        return Ok(await users.ImportCsvAsync(stream, CurrentUserId, ct));
    }

    /// <summary>Yuklash chegarasi servisdagi chegara bilan bir xil (2 MB).</summary>
    private const long MaxUploadBytes = 2 * 1024 * 1024;

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
