using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Exceptions;
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
/// ⚠️ O'CHIRISH ENDPOINTI 2026-08-15 DA QO'SHILDI. Bungacha bu yerda
/// "o'chirish ATAYLAB yo'q" deb yozilgan edi (o'quvchi bahoni tasodifan
/// yo'qotmasin degan qo'rquv). Loyiha egasi qo'ng'iroqchaga o'chirish
/// tugmasi, belgilash rejimi va "belgilanganlarni o'chirish" talab qildi.
/// Eski qo'rquv IKKI to'siq bilan qoplandi:
///   • klientda TASDIQLASH OYNASI (`ConfirmDialog`) — majburiy;
///   • serverda BO'SH ro'yxat "hammasini" degani EMAS (400) — ya'ni
///     klientdagi xato butun ro'yxatni yo'q qila olmaydi.
/// Baholashning O'ZI (`Submission`) tegilmaydi: bu jadval faqat XABAR
/// yozuvi, ya'ni "isbot" baribir `Submissions` da qoladi.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
[Produces("application/json")]
public sealed class NotificationsController(INotificationFeed notifications) : ControllerBase
{
    /// <summary>
    /// Bir o'chirish so'rovidagi eng ko'p Id soni.
    ///
    /// ★ 50 — <c>NotificationFeed.MaxTake</c> BILAN BIR XIL: klient ko'pi
    /// bilan bitta sahifani belgilay oladi, ya'ni undan katta ro'yxat
    /// faqat qo'lda yasalgan so'rovdan keladi.
    /// </summary>
    private const int MaxDeleteIds = 50;

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

    /// <summary>
    /// Belgilangan bildirishnomalarni BUTUNLAY o'chiradi (idempotent).
    ///
    /// ★ <c>POST .../delete</c>, <c>DELETE</c> emas: bu KO'PLIK ustidagi
    /// amal va Id'lar TANADA keladi. <c>DELETE</c> so'rovining tanasi
    /// HTTP da rasman belgilanmagan — oraliq proksilar va ba'zi klientlar
    /// uni tashlab yuboradi, natijada "o'chirdim, lekin hech nima
    /// o'chmadi" turkumidagi xato paydo bo'lardi. <c>POST .../read</c>
    /// bilan bir xil shakl.
    ///
    /// 🔴 BO'SH RO'YXAT — 400, "hammasini o'chir" EMAS. Sabab
    /// <see cref="INotificationFeed.DeleteAsync"/> izohida: klientdagi
    /// bitta xato butun ro'yxatni yo'q qila olmasligi kerak.
    /// </summary>
    [HttpPost("delete")]
    [ProducesResponseType<NotificationDeleteResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationDeleteResultDto>> Delete(
        [FromBody] DeleteNotificationsRequest? request, CancellationToken ct)
    {
        var ids = request?.Ids;

        if (ids is not { Count: > 0 })
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["ids"] = ["Kamida bitta bildirishnoma tanlanishi kerak."],
            });
        }

        // Chegarasiz qoldirilsa, `IN (...)` ro'yxatiga o'n minglab Id
        // yuborib so'rovni sekinlashtirish mumkin bo'lardi.
        if (ids.Count > MaxDeleteIds)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["ids"] = [$"Bir so'rovda ko'pi bilan {MaxDeleteIds} ta bildirishnoma o'chiriladi."],
            });
        }

        return Ok(await notifications.DeleteAsync(CurrentUserId, ids, ct));
    }

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

/// <summary>
/// O'chirish so'rovi.
///
/// 🔴 <see cref="MarkNotificationsReadRequest"/> DAN FARQI: bu yerda tana
/// BO'SH BO'LSA "hammasini" DEGANI EMAS — 400 qaytadi. Ikkalasi bir xil
/// turdan foydalanmasligining sababi ham shu: bir turda ikki xil "bo'sh"
/// semantikasi vaqt o'tib albatta chalkashtirilardi.
/// </summary>
/// <param name="Ids">O'chiriladigan qatorlar (1..50 ta).</param>
public sealed record DeleteNotificationsRequest(IReadOnlyList<long>? Ids);
