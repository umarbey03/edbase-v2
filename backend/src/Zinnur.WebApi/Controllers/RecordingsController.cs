using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Dars yozuvlari (FAZA 5.3): boshlash/to'xtatish, ro'yxat va ko'rish
/// havolasi.
///
/// Controller YUPQA: <c>[Authorize(Roles=…)]</c> — faqat DARVOZA
/// ("umuman kira oladimi"). Haqiqiy qoida ("faqat SHU darsning hosti",
/// "faqat SHU guruh a'zosi", to'lov darvozasi) <see cref="IRecordingService"/>
/// ICHIDA — aks holda yangi endpoint qo'shilganda uni takrorlash unutilardi.
/// </summary>
[ApiController]
[Route("api/v1/recordings")]
[Authorize]
[Produces("application/json")]
public sealed class RecordingsController(IRecordingService recordings) : ControllerBase
{
    /// <summary>Yozuvni BOSHLASHGA umuman urina oladigan rollar.</summary>
    /// <remarks>
    /// O'quvchi bu yerga UMUMAN kira olmaydi — "faqat host" qoidasi esa
    /// servisda: o'quv bo'limi xodimi ham boshqa guruhning darsini yoza
    /// olmasligi kerak, va buni rol atributi ifodalay olmaydi.
    /// </remarks>
    private const string HostRoles = "Teacher,Assistant,Academic,Admin";

    // ================================================================= dars ichidan

    /// <summary>
    /// Yozuvni boshlaydi (faqat JONLI dars, faqat host).
    ///
    /// ★ QAROR: yozuv AVTOMATIK boshlanmaydi — ustoz tugma bosadi. Sabab
    /// va eski tizim bilan farqi <see cref="IRecordingService"/> izohida
    /// batafsil (qisqasi: rozilik, javobgarlik va "yozuv nosozligi darsni
    /// to'xtatmasligi" talabi).
    ///
    /// IDEMPOTENT: tugma ikki marta bosilsa AYNI yozuv qaytadi.
    /// </summary>
    /// <response code="200">Yozuv so'raldi (holat DTO'da).</response>
    /// <response code="403">Host emas.</response>
    /// <response code="409">Dars jonli emas.</response>
    /// <response code="503">Yozuv xizmati yoki ombor sozlanmagan.</response>
    [HttpPost("~/api/v1/live-sessions/{sessionId:long}/recordings/start")]
    [Authorize(Roles = HostRoles)]
    [ProducesResponseType<RecordingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RecordingDto>> Start(long sessionId, CancellationToken ct) =>
        Ok(await recordings.StartAsync(sessionId, CurrentUserId, ct));

    /// <summary>
    /// Yozuvni to'xtatadi (faqat host).
    ///
    /// ⚠️ Fayl DARHOL tayyor bo'lmaydi: Egress videoni yakunlab, omborga
    /// yuklashi kerak. Yakuniy holat webhook bilan keladi, yo'qolsa
    /// watchdog tiklaydi.
    /// </summary>
    [HttpPost("~/api/v1/live-sessions/{sessionId:long}/recordings/stop")]
    [Authorize(Roles = HostRoles)]
    [ProducesResponseType<RecordingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordingDto>> Stop(long sessionId, CancellationToken ct) =>
        Ok(await recordings.StopAsync(sessionId, CurrentUserId, ct));

    /// <summary>
    /// Darsning yozuv urinishlari (yangisi birinchi).
    ///
    /// O'quvchi faqat TAYYOR yozuvlarni ko'radi; xodim — barchasini, xato
    /// sababi bilan (sabab servisda).
    /// </summary>
    [HttpGet("~/api/v1/live-sessions/{sessionId:long}/recordings")]
    [ProducesResponseType<IReadOnlyList<RecordingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RecordingDto>>> ForSession(
        long sessionId, CancellationToken ct) =>
        Ok(await recordings.ListForSessionAsync(sessionId, CurrentUserId, ct));

    // ================================================================= "Dars yozuvlari" bo'limi

    /// <summary>
    /// Sana oralig'idagi yozuvlar — dizayn-parite bo'shlig'i #4 ("Dars
    /// yozuvlari" bo'limi).
    ///
    /// Qamrov KALENDAR orqali olinadi, ya'ni har rol o'zi ko'ra oladigan
    /// darslarning yozuvini ko'radi (izoh: <see cref="IRecordingService"/>).
    /// </summary>
    /// <param name="from">Mahalliy (markaz vaqti) sana — KIRADI.</param>
    /// <param name="to">Mahalliy sana — KIRADI.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RecordingListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RecordingListItemDto>>> List(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await recordings.ListAsync(CurrentUserId, from, to, ct));

    /// <summary>
    /// Ko'rish uchun MUDDATLI imzolangan havola.
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// ★ NIMA UCHUN FAYL API ORQALI OQIM QILINMAYDI (vazifa fayllaridan
    /// FARQLI): sabab <see cref="IRecordingStorage"/> izohida — ikkala
    /// tomon ham o'sha yerda yozilgan. Qisqasi: bir yozuv ~0.5 GB va uni
    /// proxy qilish jonli darsning O'ZI foydalanadigan tarmoq kanalini
    /// yeb qo'yardi; bundan tashqari videoda oldinga o'tish (<c>Range</c>)
    /// uchun butunlay yangi yo'l yozish kerak bo'lardi.
    ///
    /// ⚠️ FRONTEND UCHUN: javobdagi <c>expiresAt</c> — SHARTNOMANING BIR
    /// QISMI. Pleyer havola muddati tugashidan oldin yangisini so'rab,
    /// ko'rish o'rnini (<c>currentTime</c>) saqlab qolishi kerak; busiz
    /// uzun video o'rtada "sababsiz" to'xtardi.
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    /// <response code="200">Havola va uning muddati.</response>
    /// <response code="403">Ruxsat yo'q yoki to'lov qarzi (`Video` qamrovi).</response>
    /// <response code="409">Yozuv hali tayyor emas yoki chiqmagan.</response>
    /// <response code="503">Ombor sozlanmagan.</response>
    [HttpGet("{id:long}/link")]
    [ProducesResponseType<RecordingLinkDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RecordingLinkDto>> Link(long id, CancellationToken ct)
    {
        var link = await recordings.CreateViewLinkAsync(id, CurrentUserId, ct);

        // 🔴 HAVOLA HECH QAYERDA KESHLANMASIN: uning ichida imzo bor va
        // oraliq proksi (yoki umumiy kompyuterdagi brauzer) uni saqlab
        // qolsa, keyingi foydalanuvchi yozuvni ochib olardi. Ruxsat esa
        // HAR so'rovda qaytadan tekshiriladi — kesh bu qoidani chetlab
        // o'tardi.
        Response.Headers.CacheControl = "no-store";

        return Ok(link);
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
