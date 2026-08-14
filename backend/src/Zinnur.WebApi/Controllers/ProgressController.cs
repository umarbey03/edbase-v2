using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Progress.Dtos;
using Zinnur.Application.Progress.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>O'quvchining o'z progressi (davomat xulosasi, dars progressi).</summary>
[ApiController]
[Route("api/v1/progress")]
[Authorize]
[Produces("application/json")]
public sealed class ProgressController(
    IAttendanceSummaryService attendance, IGatingService gating) : ControllerBase
{
    /// <summary>
    /// Davomat xulosasi: qatnashgan / qoldirgan / jami va foiz
    /// (ustoz va kurator darslari alohida) + ketma-ket qatnashish seriyasi.
    ///
    /// ★ FAQAT O'ZINIKI — servis boshqa o'quvchining Id'sini qabul qilmaydi.
    /// </summary>
    /// <param name="groupId">Berilmasa — barcha faol guruhlar birga.</param>
    /// <param name="from">Mahalliy sana (<c>YYYY-MM-DD</c>), KIRADI.</param>
    /// <param name="to">Mahalliy sana, KIRADI.</param>
    [HttpGet("attendance")]
    [ProducesResponseType<AttendanceSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AttendanceSummaryDto>> Attendance(
        [FromQuery] long? groupId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct) =>
        Ok(await attendance.GetMySummaryAsync(CurrentUserId, groupId, from, to, ct));

    // ================================================================= dars progressi

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// DARS VIDEOSI KO'RILDI
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// ★ NIMA UCHUN BU ENDPOINT KERAK EDI: <c>IGatingService</c> da
    /// <c>MarkVideoWatchedAsync</c> ALLAQACHON bor edi, lekin uni
    /// chaqiradigan HECH KIM yo'q edi — ya'ni
    /// <c>LessonProgress.VideoWatchedAt</c> hech qachon yozilmasdi va
    /// gating'ning video oyog'i o'lik edi. Endi pleyer videoni oxirigacha
    /// (yoki ko'rilgan deb hisoblanadigan chegaragacha) ko'rgach shu
    /// yerga yozadi.
    ///
    /// ⚠️ IDEMPOTENT: BIRINCHI ko'rilgan payt saqlanadi (Domain qoidasi),
    /// qayta chaqirilsa progress ORQAGA KETMAYDI. Shu tufayli pleyer
    /// "yubordimmi?" degan holatni saqlashi shart emas.
    ///
    /// 🔴 FAQAT OCHIQ DARS: yopiq darsning Id'si bilan chaqirilsa **403**.
    /// Aks holda o'quvchi Id'larni ketma-ket yuborib butun kursning video
    /// shartini o'zi bajarilgan qilib qo'yardi (tekshiruv servisda —
    /// <c>GatingService.MarkVideoWatchedAsync</c>).
    ///
    /// ★ FAQAT O'QUVCHI: "dars progressi" — o'quvchining holati. Xodimda
    /// <c>LessonProgress</c> yozuvi umuman bo'lmaydi va uni yaratish
    /// ustozning ko'rishini o'quvchi progressiga aylantirardi.
    ///
    /// Javob — darsning YANGILANGAN gating holati: klient qo'shimcha
    /// so'rovsiz "endi tugatildimi?" savoliga javob oladi.
    /// </summary>
    [HttpPost("lessons/{lessonId:long}/video-watched")]
    [Authorize(Roles = StudentRole)]
    [ProducesResponseType<LessonGateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LessonGateDto>> MarkVideoWatched(
        long lessonId, CancellationToken ct) =>
        Ok(await gating.MarkVideoWatchedAsync(CurrentUserId, lessonId, ct));

    private const string StudentRole = "Student";

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
