using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Shop;
using Zinnur.Domain.Enums;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Onlayn sotuv (2026-10-29): tariflar (ommaviy), o'quvchi buyurtmalari va
/// xodim tasdiqlashi. Atribut — DARVOZA, rol qoidasi servisda ham tekshiriladi.
/// </summary>
[ApiController]
[Route("api/v1/shop")]
[Authorize]
[Produces("application/json")]
public sealed class ShopController(IShopService shop) : ControllerBase
{
    private const string ManageRoles = "Academic,Admin";

    /// <summary>Saytdagi tariflar — login talab qilinmaydi (landing sahifa).</summary>
    [HttpGet("offers")]
    [AllowAnonymous]
    [ProducesResponseType<ShopOffersDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ShopOffersDto>> Offers(CancellationToken ct) =>
        Ok(await shop.GetOffersAsync(ct));

    [HttpGet("me")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType<ShopMeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ShopMeDto>> Me(CancellationToken ct) =>
        Ok(await shop.GetMeAsync(CurrentUserId, ct));

    [HttpPost("orders")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType<ShopOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopOrderDto>> CreateOrder(
        [FromBody] CreateOrderRequest request, CancellationToken ct) =>
        Ok(await shop.CreateOrderAsync(CurrentUserId, request, ct));

    /// <summary>Mustaqil o'qish tarifini balansdan to'lash — xodim tasdig'isiz (Q-4).</summary>
    [HttpPost("orders/from-balance")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType<ShopOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopOrderDto>> PayFromBalance(
        [FromBody] CreateOrderRequest request, CancellationToken ct) =>
        Ok(await shop.PayFromBalanceAsync(CurrentUserId, request, ct));

    [HttpPost("orders/{id:long}/cancel")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType<ShopOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopOrderDto>> CancelOrder(long id, CancellationToken ct) =>
        Ok(await shop.CancelOrderAsync(CurrentUserId, id, ct));

    [HttpPost("orders/{id:long}/paid")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType<ShopOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopOrderDto>> ClaimPaid(long id, CancellationToken ct) =>
        Ok(await shop.ClaimPaidAsync(CurrentUserId, id, ct));

    [HttpGet("orders")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<StaffOrderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffOrderDto>>> List(
        [FromQuery] PurchaseOrderStatus? status, CancellationToken ct) =>
        Ok(await shop.ListOrdersAsync(CurrentUserId, status, ct));

    [HttpPost("orders/{id:long}/confirm")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<StaffOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffOrderDto>> Confirm(
        long id, [FromBody] DecideOrderRequest request, CancellationToken ct) =>
        Ok(await shop.ConfirmAsync(id, request, CurrentUserId, ct));

    [HttpPost("orders/{id:long}/reject")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<StaffOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffOrderDto>> Reject(
        long id, [FromBody] DecideOrderRequest request, CancellationToken ct) =>
        Ok(await shop.RejectAsync(id, request, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
