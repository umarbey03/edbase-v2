using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Ilova ichidagi bildirishnomalar (qo'ng'iroqcha).
///
/// ★ ROL ATRIBUTI YO'Q VA BU ATAYLAB: qoida rolga UMUMAN bog'liq emas —
/// har kim FAQAT o'zinikini ko'radi va faqat o'zinikini belgilaydi.
/// <c>userId</c> HAR DOIM TOKENDAN olinadi, so'rovdan hech qachon
/// (<see cref="MessagesController"/> bilan bir xil qoida).
///
/// ★ REALTIME BILAN BOG'LIQLIK: yangi bildirishnoma
/// <c>/hubs/notifications</c> obunachilariga ham yetadi (bazaga
/// yozilgandan KEYIN). Klient ikkalasini ishlatadi: REST — ro'yxat va
/// "o'qildi", hub — kelayotgan yangilari.
///
/// 🔴 O'CHIRISH ENDPOINTI ATAYLAB YO'Q. "O'qildi" yetarli: o'chirish
/// tugmasi qo'shilsa o'quvchi bahoni tasodifan yo'qotib, keyin "menga
/// xabar kelmagan" deb shikoyat qilardi — va bazada isbot qolmasdi.
/// Tozalash kerak bo'lganda u FON VAZIFASI bo'ladi (yoshi bo'yicha),
/// foydalanuvchi tugmasi emas.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
[Produces("application/json")]
public sealed class NotificationsController(INotificationFeed notifications) : ControllerBase
{
    /// <summary>
    /// Ro'yxat — kursorli sahifalash, YANGIDAN ESKIGA tartibda.
    ///
    /// ★ O'QISH HOLATNI O'ZGARTIRMAYDI: "o'qildi" uchun alohida
    /// <c>POST .../read</c> bor. Aks holda ro'yxatni fon rejimida
    /// yangilash o'qilmaganlar sanog'ini "yeb qo'yardi".
    /// </summary>
    /// <param name="beforeId">Shu Id'dan ESKIROQ qatorlar (keyingi sahifa).</param>
    /// <param name="unreadOnly">Faqat o'qilmaganlar.</param>
    /// <param name="take">1..50, standart 20.</param>
    [HttpGet]
    [ProducesResponseType<NotificationPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPageDto>> List(
        [FromQuery] long? beforeId,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int take = 20,
        CancellationToken ct = default) =>
        Ok(await notifications.ListAsync(CurrentUserId, beforeId, unreadOnly, take, ct));

    /// <summary>
    /// Faqat o'qilmaganlar soni — qo'ng'iroqcha nishoni uchun.
    ///
    /// ★ ALOHIDA ENDPOINT: bu raqam HAR sahifada ko'rinadi va u uchun 20 ta
    /// qator olib kelish keraksiz trafik bo'lardi.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType<NotificationUnreadDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationUnreadDto>> UnreadCount(CancellationToken ct) =>
        Ok(await notifications.UnreadCountAsync(CurrentUserId, ct));

    /// <summary>
    /// Bildirishnomalarni "o'qildi" deb belgilaydi (idempotent).
    ///
    /// ★ <c>POST</c>, <c>PATCH</c> emas: bu bitta resursning maydonini
    /// tahrirlash emas, KO'PLIK ustidagi amal (aynan shu sabab
    /// <c>POST /api/v1/messages/conversations/{id}/read</c> ham shunday).
    /// </summary>
    /// <param name="request">
    /// <c>ids</c> bo'sh yoki berilmagan bo'lsa — BARCHA o'qilmaganlar
    /// ("hammasini o'qildi qilish").
    /// </param>
    [HttpPost("read")]
    [ProducesResponseType<NotificationReadResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationReadResultDto>> MarkRead(
        [FromBody] MarkNotificationsReadRequest? request, CancellationToken ct) =>
        Ok(await notifications.MarkReadAsync(CurrentUserId, request?.Ids, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}

/// <summary>
/// "O'qildi" so'rovi.
///
/// ★ TANA IXTIYORIY: <c>POST /read</c> ni bo'sh tana bilan yuborish
/// "hammasini o'qildi qil" degani. Alohida <c>/read-all</c> endpointi
/// qo'shilsa, ikki yo'lda AYNI mantiq ikki marta yozilardi.
/// </summary>
/// <param name="Ids">
/// Belgilanadigan qatorlar. Begona Id jimgina e'tiborsiz qoldiriladi —
/// sabab <see cref="INotificationFeed.MarkReadAsync"/> izohida.
/// </param>
public sealed record MarkNotificationsReadRequest(IReadOnlyList<long>? Ids);
