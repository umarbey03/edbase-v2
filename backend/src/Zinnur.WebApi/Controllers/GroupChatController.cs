using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Application.GroupChat.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.WebApi.Media;

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
    /// <param name="query">
    /// R38 filtri: <c>?type=Group|Individual</c> va/yoki <c>?categoryId=N</c>.
    ///
    /// 🔴 FILTR SERVERDA QO'LLANADI VA BU MAJBURIY: ro'yxat saralashdan keyin
    /// 200 qatorda kesiladi, ya'ni mijozdagi filtr kesilgandan keyingi
    /// guruhlarni UMUMAN ko'rmasdi va "bunday guruh yo'q" degan yolg'on
    /// javob berardi (batafsil <c>GroupChatThreadQuery</c> izohida).
    ///
    /// ⚠️ <c>type=Curator</c> — 400: kurator guruhlarining alohida chati
    /// yo'q va ular bu ro'yxatda hech qachon ko'rinmaydi.
    /// </param>
    [HttpGet("threads")]
    [ProducesResponseType<IReadOnlyList<GroupChatThreadDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<GroupChatThreadDto>>> Threads(
        [FromQuery] GroupChatThreadQuery query, CancellationToken ct) =>
        Ok(await chat.ListThreadsAsync(CurrentUserId, query, ct));

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

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// FAYL/RASM BILAN XABAR (R16b) — `multipart/form-data`
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Maydonlar: <c>files</c> (1..5 ta), ixtiyoriy <c>body</c> izoh va
    /// ixtiyoriy <c>channel</c>.
    ///
    /// 🔴 BU YO'LNING SIGNALR MUQOBILI YO'Q va bo'lishi ham mumkin emas:
    /// hub metodi satr qabul qiladi, baytlarni base64 bilan satrga solish
    /// esa hub'ning freym chegarasidan oshib, ULANISHNI uzardi. Ya'ni
    /// klient uchun qoida: <b>biriktirma bor -> shu endpoint, yo'q ->
    /// hub yoki oddiy POST</b>. Qarshi tomon farqni sezmaydi — xabar har
    /// ikkala holatda ham o'sha `GroupChatMessage` hodisasi bilan
    /// tarqatiladi.
    ///
    /// 🔴 Tur MAZMUNDAN aniqlanadi: `.jpg` deb nomlangan PDF qabul
    /// qilinadi (PDF ruxsat etilgan), `.jpg` deb nomlangan EXE esa **400**
    /// oladi. Hajm chegarasi sozlamadan (`lesson.image_max_mb`) — oshsa
    /// **413**.
    ///
    /// ⚠️ IKKI TEZLIK BUDJETI: xabar budjeti (REST va hub bilan UMUMIY) va
    /// undan qat'iyroq YUKLASH budjeti. Ikkinchisi bo'lmasa, fayl yuklash
    /// matn uchun o'lchangan chegara ostida yashirinib, yarim gigabaytlik
    /// flood yo'lini ochib qo'yardi (sabab `GroupChatService` da).
    /// </summary>
    [HttpPost("groups/{groupId:long}/messages/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAttachmentRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentRequestBytes)]
    [ProducesResponseType<GroupChatMessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GroupChatMessageDto>> SendWithAttachments(
        long groupId,
        [FromForm] IFormFileCollection? files,
        [FromForm] string? body,
        [FromForm] GroupChatChannel? channel,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            throw MediaResponse.MissingFile();

        // Oqimlar `finally` da yopiladi: aks holda katta yuklashda
        // vaqtinchalik fayl deskriptorlari so'rov tugagach ham ushlanib
        // turardi (`AssignmentsController.Submit` dagi AYNI naqsh).
        var streams = new List<Stream>(files.Count);

        try
        {
            var uploads = new List<LessonAssetUpload>(files.Count);

            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);

                uploads.Add(new LessonAssetUpload(
                    file.FileName,

                    // Klient AYTGAN tur faqat XATO XABARI uchun uzatiladi —
                    // haqiqiy tur MAZMUNDAN aniqlanadi.
                    file.ContentType,
                    stream,
                    file.Length));
            }

            var created = await chat.SendWithAttachmentsAsync(
                CurrentUserId,
                groupId,
                new SendGroupChatAttachmentRequest(channel, body, uploads),
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
    /// Chat biriktirmasini OQIM bilan beradi (`Range` qo'llab-quvvatlanadi).
    ///
    /// 🔴 RUXSAT — OQIMNI O'QISH BILAN AYNI: xabar qaysi `(guruh, kanal)`
    /// da yozilgan bo'lsa, uni KO'RA oladigan har kim faylni ham oladi.
    /// Vazifa javobidagi "faqat egasi va uning ustozi" qoidasi bu yerga
    /// KO'CHIRILMAYDI — chatda guruhdoshlar bir-birining rasmini ko'rishi
    /// funksiyaning O'ZI (sabab `IGroupChatService.OpenAttachmentAsync` da).
    ///
    /// ⚠️ FRONTEND UCHUN: brauzer `&lt;img src&gt;` bilan `Authorization`
    /// sarlavhasini YUBORMAYDI, ya'ni bu manzilni to'g'ridan-to'g'ri
    /// `src` ga qo'yib bo'lmaydi — `http.download()` orqali `Blob` olinib,
    /// `URL.createObjectURL` yasaladi (vazifa fayllaridagi AYNI naqsh).
    ///
    /// ★ NIMA UCHUN DARS MEDIASIDAGI KABI `?ticket=` YO'LI QO'SHILMADI:
    /// chipta faqat `&lt;video src&gt;` uchun kerak edi (u sarlavha yubora
    /// olmaydi va faylni oqim bilan o'qiydi). Chatda esa rasm/hujjat
    /// `Blob` sifatida bir marta olinadi — ya'ni chipta yangi imzolash
    /// mexanizmini kiritib, hech qanday muammoni hal qilmasdi.
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
        var download = await chat.OpenAttachmentAsync(
            attachmentId, MediaResponse.RawRange(Request.Headers.Range), CurrentUserId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    /// <summary>Oqimdagi xabarlarni "o'qildi" deb belgilaydi (idempotent, faqat oldinga).</summary>
    [HttpPost("groups/{groupId:long}/read")]
    [ProducesResponseType<GroupChatReadResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupChatReadResultDto>> MarkRead(
        long groupId, [FromBody] MarkGroupChatReadRequest request, CancellationToken ct) =>
        Ok(await chat.MarkReadAsync(CurrentUserId, groupId, request, ct));

    /// <summary>
    /// Biriktirmali xabar so'rovining QAT'IY yuqori chegarasi (bayt).
    ///
    /// = fayl soni × `lesson.image_max_mb` ning `Maximum` i (100 MB) +
    /// sarlavhalar uchun 1 MB zaxira.
    ///
    /// 🔴 NIMA UCHUN QAT'IY CHEGARA HAM KERAK (haqiqiy chegara sozlamadan
    /// kelsa ham): ASP.NET `multipart` faylni MODEL BOG'LASHDA — bizning
    /// kodimizdan OLDIN — vaqtinchalik DISKKA buferlaydi. Chegarasiz
    /// atribut bilan istalgan o'quvchi serverning diskini to'ldirib
    /// qo'yardi (`LessonAssetsController` dagi AYNI asos).
    ///
    /// ⚠️ Bu HIMOYA, biznes qoidasi EMAS. Foydalanuvchi ko'radigan chegara
    /// — sozlamadagi qiymat (standart 10 MB) va u servis ichida 413 bilan
    /// qo'llanadi.
    /// </summary>
    private const long MaxAttachmentRequestBytes =
        (GroupChatAttachment.MaxPerMessage * 100L * 1024 * 1024) + (1024 * 1024);

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
