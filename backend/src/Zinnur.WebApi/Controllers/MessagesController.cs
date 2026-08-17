using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Messaging.Dtos;
using Zinnur.Application.Messaging.Services;
using Zinnur.Domain.Entities;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Kurator ↔ o'quvchi shaxsiy yozishmasi (DM).
///
/// ★ BU JONLI DARS CHATI EMAS. Dars chati SignalR orqali ishlaydi
/// (<c>/hubs/live-class</c>) va <c>/api/v1/live-sessions/{id}/messages</c>
/// dan o'qiladi. Ikkalasi boshqa jadval va boshqa qoida.
///
/// Ruxsat servis ichida (<c>IDirectMessageService</c>): o'quvchi faqat
/// o'z kuratori bilan, kurator faqat o'ziga biriktirilgan o'quvchilar
/// bilan. Boshqa juftlik — 403.
/// </summary>
[ApiController]
[Route("api/v1/messages")]
[Authorize]
[Produces("application/json")]
public sealed class MessagesController(IDirectMessageService messages) : ControllerBase
{
    /// <summary>
    /// Suhbatlar ro'yxati. O'quvchida 0 yoki 1 ta (kuratori),
    /// kuratorda — o'quvchilari (o'qilmagani borlar tepada).
    /// Kurator biriktirilmagan bo'lsa bo'sh massiv (404 emas).
    /// </summary>
    [HttpGet("conversations")]
    [ProducesResponseType<IReadOnlyList<ConversationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ConversationDto>>> Conversations(
        CancellationToken ct) =>
        Ok(await messages.ListConversationsAsync(CurrentUserId, ct));

    /// <summary>
    /// Yozishma tarixi — kursorli sahifalash, eskidan yangiga tartibda.
    /// ★ O'QISH HOLATNI O'ZGARTIRMAYDI: "o'qildi" uchun alohida
    /// <c>POST .../read</c> bor.
    /// </summary>
    /// <param name="peerId">Suhbatdosh.</param>
    /// <param name="beforeId">Shu Id'dan eskiroq xabarlar (keyingi sahifa).</param>
    /// <param name="take">1..100, standart 50.</param>
    /// <param name="moduleLessonId">
    /// Berilsa — faqat shu kurs darsidan yozilgan xabarlar (Dars Dashboard
    /// mini-chat'i). Berilmasa — butun yozishma (mavjud xatti-harakat).
    /// </param>
    [HttpGet("conversations/{peerId:long}/messages")]
    [ProducesResponseType<MessagePageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessagePageDto>> Thread(
        long peerId,
        [FromQuery] long? beforeId,
        [FromQuery] int take = 50,
        [FromQuery] long? moduleLessonId = null,
        CancellationToken ct = default) =>
        Ok(await messages.GetThreadAsync(CurrentUserId, peerId, beforeId, take, moduleLessonId, ct));

    /// <summary>Xabar yuborish.</summary>
    [HttpPost("conversations/{peerId:long}/messages")]
    [ProducesResponseType<DirectMessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DirectMessageDto>> Send(
        long peerId, [FromBody] SendDirectMessageRequest request, CancellationToken ct)
    {
        var created = await messages.SendAsync(CurrentUserId, peerId, request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// FAYL/RASM BILAN XABAR (2026-08-17) — `multipart/form-data`.
    /// `GroupChatController.SendWithAttachments` bilan AYNI naqsh, KANAL
    /// YO'Q (shaxsiy yozishma bitta oqim).
    /// </summary>
    [HttpPost("conversations/{peerId:long}/messages/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAttachmentRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentRequestBytes)]
    [ProducesResponseType<DirectMessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DirectMessageDto>> SendWithAttachments(
        long peerId,
        [FromForm] IFormFileCollection? files,
        [FromForm] string? body,
        [FromForm] long? moduleLessonId,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            throw MediaResponse.MissingFile();

        // Oqimlar `finally` da yopiladi — `GroupChatController.SendWithAttachments`
        // dagi AYNI naqsh.
        var streams = new List<Stream>(files.Count);

        try
        {
            var uploads = new List<LessonAssetUpload>(files.Count);

            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);

                uploads.Add(new LessonAssetUpload(file.FileName, file.ContentType, stream, file.Length));
            }

            var created = await messages.SendWithAttachmentsAsync(
                CurrentUserId,
                peerId,
                new SendDirectMessageAttachmentRequest(body, moduleLessonId, uploads),
                ct);

            return StatusCode(StatusCodes.Status201Created, created);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    /// <summary>
    /// Yozishma biriktirmasini OQIM bilan beradi (`Range` qo'llab-quvvatlanadi).
    /// ⚠️ FRONTEND UCHUN: `GroupChatController.DownloadAttachment` dagi AYNI
    /// eslatma — `&lt;img src&gt;` ga to'g'ridan-to'g'ri qo'yib bo'lmaydi.
    /// </summary>
    [HttpGet("attachments/{attachmentId:long}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadAttachment(long attachmentId, CancellationToken ct)
    {
        var download = await messages.OpenAttachmentAsync(
            attachmentId, MediaResponse.RawRange(Request.Headers.Range), CurrentUserId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    /// <summary>
    /// R40 — DARS savollari navbati (xodim uchun): o'quvchilar aynan kurs
    /// darsi sahifasidan yozgan savollar, javobsizlar tepada va eng uzoq
    /// kutgani birinchi.
    ///
    /// ★ ALOHIDA EKRAN EMAS, SARALANGAN KIRISH NUQTASI: har qator
    /// <c>peerId</c> beradi va u AYNI yozishma endpointlariga olib boradi.
    /// Ikkinchi xabar tizimi qurilmagan (sabab <c>IDirectMessageService</c>
    /// izohida).
    /// </summary>
    /// <param name="take">1..100, standart 50.</param>
    [HttpGet("lesson-questions")]
    [ProducesResponseType<IReadOnlyList<LessonQuestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<LessonQuestionDto>>> LessonQuestions(
        [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await messages.ListLessonQuestionsAsync(CurrentUserId, take, ct));

    /// <summary>Suhbatdagi kiruvchi xabarlarni "o'qildi" deb belgilaydi (idempotent).</summary>
    [HttpPost("conversations/{peerId:long}/read")]
    [ProducesResponseType<MarkReadResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MarkReadResultDto>> MarkRead(
        long peerId, CancellationToken ct) =>
        Ok(await messages.MarkReadAsync(CurrentUserId, peerId, ct));

    /// <summary>
    /// Biriktirmali xabar so'rovining QAT'IY yuqori chegarasi (bayt) —
    /// `GroupChatController.MaxAttachmentRequestBytes` bilan AYNI hisob.
    /// </summary>
    private const long MaxAttachmentRequestBytes =
        (DirectMessageAttachment.MaxPerMessage * 100L * 1024 * 1024) + (1024 * 1024);

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
