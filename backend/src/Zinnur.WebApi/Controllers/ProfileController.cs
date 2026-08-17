using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Profile.Dtos;
using Zinnur.Application.Profile.Services;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// O'Z PROFILI — faqat RASM (2026-08-15, 2026-08-17 da qisqartirildi).
///
/// ⚠️ ISM VA TELEFONNI O'ZI TAHRIRLASH OLIB TASHLANDI (2026-08-17, loyiha
/// egasining qarori): "foydalanuvchi o'z ism familyasi va nomerini edit
/// qilish imkoniga ega bo'lmasligi kerak" — BARCHA rol uchun. Bu ikkala
/// maydonni endi FAQAT o'quv bo'limi/admin <see cref="UsersController"/>
/// orqali o'zgartira oladi. Batafsil sabab — <c>IProfileService</c> izohida.
///
/// ★ ROL ATRIBUTI HALI HAM YO'Q: qolgan yagona amal (rasm) rolga bog'liq
/// emas — "har qanday user" o'z rasmini yuklaydi. <c>userId</c> HAR DOIM
/// TOKENDAN olinadi, so'rovdan hech qachon (<see cref="NotificationsController"/>
/// bilan bir xil qoida).
///
/// 🔴 BU YERDA BOSHQA ODAMNING PROFILINI O'ZGARTIRIB BO'LMAYDI. Xodim
/// vositasi — <see cref="UsersController"/>, va u butunlay boshqa ruxsat
/// qoidalari bilan ishlaydi. Ikkalasi bitta kontrollerga qo'yilsa, har
/// metodda "bu o'zinikimi?" sharti paydo bo'lardi.
/// </summary>
[ApiController]
[Route("api/v1/profile")]
[Authorize]
[Produces("application/json")]
public sealed class ProfileController(IProfileService profile) : ControllerBase
{
    /// <summary>
    /// Rasm so'rovining eng katta hajmi.
    ///
    /// ★ HAQIQIY CHEGARA SOZLAMADA (`lesson.image_max_mb`) — bu esa
    /// TRANSPORT darajasidagi qattiq to'siq: undan kattasi umuman
    /// o'qilmaydi (xotira va disk bufer sarflanmaydi).
    /// </summary>
    private const int MaxAvatarRequestBytes = 16 * 1024 * 1024;

    /* ------------------------------------------------------------- rasm */

    /// <summary>Profil rasmini yuklaydi (eskisi almashtiriladi).</summary>
    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAvatarRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAvatarRequestBytes)]
    [ProducesResponseType<AvatarUploadedDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AvatarUploadedDto>> UploadAvatar(
        IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw MediaResponse.MissingFile();

        await using var stream = file.OpenReadStream();

        return Ok(await profile.UploadAvatarAsync(
            CurrentUserId,
            // Klient AYTGAN tur faqat XATO XABARI uchun uzatiladi — tur
            // fayl MAZMUNIDAN aniqlanadi.
            new LessonAssetUpload(file.FileName, file.ContentType, stream, file.Length),
            ct));
    }

    /// <summary>Profil rasmini olib tashlaydi (idempotent).</summary>
    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        await profile.RemoveAvatarAsync(CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>
    /// Foydalanuvchining rasmini OQIM bilan beradi.
    ///
    /// ⚠️ RUXSAT KENG (tizimga kirgan har kim), sabab
    /// <see cref="IProfileService.OpenAvatarAsync"/> izohida: avatar
    /// ro'yxatlarda ism bilan bir qatorda turadigan ochiq ma'lumot.
    ///
    /// ⚠️ FRONTEND UCHUN: brauzer `&lt;img src&gt;` bilan
    /// `Authorization` sarlavhasini YUBORMAYDI — klient rasmni
    /// `http.download()` orqali `Blob` sifatida oladi (dars mediasi va
    /// javob fayllaridagi AYNI naqsh).
    /// </summary>
    /// <param name="userId">Kimning rasmi.</param>
    /// <param name="v">
    /// Kesh buzish uchun vaqt tamg'asi — SERVER UNI O'QIMAYDI. U faqat
    /// manzilni o'zgartirish uchun bor, aks holda rasm almashtirilganda
    /// brauzer eskisini ko'rsatib turardi.
    /// </param>
    [HttpGet("avatar/{userId:long}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAvatar(
        long userId, [FromQuery] string? v, CancellationToken ct)
    {
        _ = v;

        var media = await profile.OpenAvatarAsync(userId, ct);

        return media is null
            ? NotFound()
            : await MediaResponse.WriteAsync(this, media, ct);
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
