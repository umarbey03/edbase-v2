using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Application.GroupChat.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Guruhning DOIMIY chati — o'quvchi savol beradi, ustoz/kurator javob
/// beradi, dars vaqtidan tashqarida ham.
///
/// ★ BU IKKI MAVJUD CHATNING HECH BIRI EMAS:
///   • jonli dars xonasi chati — <c>/hubs/live</c> +
///     <c>/api/v1/live-sessions/{id}/messages</c>;
///   • kurator bilan shaxsiy yozishma — <c>/api/v1/messages</c>.
/// Farqi <c>GroupChatMessage</c> sinfi izohida batafsil.
///
/// ★ RUXSAT SERVIS ICHIDA (<see cref="IGroupChatService"/>) — bu yerda
/// atribut bilan rol tekshirilmaydi. Sabab: qoida ROLGA emas,
/// BIRIKTIRUVGA bog'liq (kim shu guruhning ustozi/kuratori/o'quvchisi) va
/// AYNI qoidani SignalR hub'i ham chaqiradi. Atribut hub'da ishlamaydi,
/// ya'ni ikki nusxa qoida paydo bo'lardi.
///
/// ★ REALTIME BILAN BOG'LIQLIK: bu yerdan yuborilgan xabar
/// <c>/hubs/group-chat</c> obunachilariga ham yetadi (use-case ichida,
/// bazaga yozilgandan KEYIN). Klient ikkalasini ishlatishi mumkin: REST —
/// tarix va yuborish, hub — kelayotgan xabarlar.
/// </summary>
[ApiController]
[Route("api/v1/group-chat")]
[Authorize]
[Produces("application/json")]
public sealed class GroupChatController(IGroupChatService chat) : ControllerBase
{
    /// <summary>
    /// "Chatlar" hubi: foydalanuvchining BARCHA guruh chatlari bitta
    /// ro'yxatda — guruh nomi, oxirgi xabar va o'qilmaganlar soni bilan.
    ///
    /// O'quvchida har guruh IKKI qator beradi (Ustoz va Kurator oqimlari),
    /// ustozda — o'z guruhlarining Ustoz oqimi, kuratorda — nazoratidagi
    /// guruhlarning Kurator oqimi.
    /// </summary>
    [HttpGet("threads")]
    [ProducesResponseType<IReadOnlyList<GroupChatThreadDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GroupChatThreadDto>>> Threads(
        CancellationToken ct) =>
        Ok(await chat.ListThreadsAsync(CurrentUserId, ct));

    /// <summary>
    /// Tarix — kursorli sahifalash, eskidan yangiga tartibda.
    /// ★ O'QISH HOLATNI O'ZGARTIRMAYDI: "o'qildi" uchun alohida
    /// <c>POST .../read</c> bor.
    /// </summary>
    /// <param name="groupId">Guruh.</param>
    /// <param name="channel">
    /// <c>Teacher</c> yoki <c>Curator</c>. Berilmasa server foydalanuvchi
    /// biriktiruviga qarab tanlaydi. Ruxsat etilmagan kanal — 403.
    /// </param>
    /// <param name="beforeId">Shu Id'dan eskiroq xabarlar (keyingi sahifa).</param>
    /// <param name="take">1..100, standart 50.</param>
    [HttpGet("groups/{groupId:long}/messages")]
    [ProducesResponseType<GroupChatPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupChatPageDto>> Messages(
        long groupId,
        [FromQuery] GroupChatChannel? channel,
        [FromQuery] long? beforeId,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await chat.GetMessagesAsync(CurrentUserId, groupId, channel, beforeId, take, ct));

    /// <summary>
    /// Xabar yuborish. Xabar avval BAZAGA yoziladi, keyin oqim
    /// obunachilariga tarqatiladi (commit-then-send).
    /// </summary>
    [HttpPost("groups/{groupId:long}/messages")]
    [ProducesResponseType<GroupChatMessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GroupChatMessageDto>> Send(
        long groupId, [FromBody] SendGroupChatMessageRequest request, CancellationToken ct)
    {
        var created = await chat.SendAsync(CurrentUserId, groupId, request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Oqimdagi xabarlarni "o'qildi" deb belgilaydi (idempotent, faqat oldinga).</summary>
    [HttpPost("groups/{groupId:long}/read")]
    [ProducesResponseType<GroupChatReadResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupChatReadResultDto>> MarkRead(
        long groupId, [FromBody] MarkGroupChatReadRequest request, CancellationToken ct) =>
        Ok(await chat.MarkReadAsync(CurrentUserId, groupId, request, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
