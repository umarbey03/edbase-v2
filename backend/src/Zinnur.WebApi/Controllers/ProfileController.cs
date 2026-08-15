using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Profile.Dtos;
using Zinnur.Application.Profile.Services;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// O'Z PROFILI — ism, rasm va telefon raqami (2026-08-15).
///
/// ★ ROL ATRIBUTI YO'Q VA BU ATAYLAB: qoida rolga UMUMAN bog'liq emas —
/// "har qanday user" o'z profilini tahrirlaydi (loyiha egasining talabi).
/// <c>userId</c> HAR DOIM TOKENDAN olinadi, so'rovdan hech qachon
/// (<see cref="NotificationsController"/> bilan bir xil qoida).
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

    /* -------------------------------------------------------------- ism */

    /// <summary>Ismni o'zgartiradi.</summary>
    [HttpPut]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> UpdateName(
        [FromBody] UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await profile.UpdateNameAsync(CurrentUserId, request, ct));

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

    /* ---------------------------------------------------------- telefon */

    /// <summary>
    /// TELEFON ALMASHTIRISH — 1-BOSQICH: niyat qayd etiladi.
    ///
    /// Javob foydalanuvchiga NIMA QILISHNI aytadi: botga YANGI raqamdan
    /// «Raqamni ulashish» yuborish. Kod aynan o'sha Telegram hisobiga
    /// keladi — sabab <see cref="IPhoneChangeStore"/> izohida.
    /// </summary>
    [HttpPost("phone")]
    [ProducesResponseType<PhoneChangeStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PhoneChangeStatusDto>> RequestPhoneChange(
        [FromBody] ChangePhoneRequest request, CancellationToken ct) =>
        Ok(await profile.RequestPhoneChangeAsync(CurrentUserId, request, ct));

    /// <summary>
    /// Kutayotgan almashtirish holati (yo'q bo'lsa <c>204</c>).
    ///
    /// ★ 204, 404 EMAS: "so'rov yo'q" — bu XATO emas, oddiy holat.
    /// 404 bo'lsa klientda har so'rov konsolga qizil satr chiqarardi.
    /// </summary>
    [HttpGet("phone")]
    [ProducesResponseType<PhoneChangeStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<PhoneChangeStatusDto>> GetPhoneChange(CancellationToken ct)
    {
        var status = await profile.GetPhoneChangeAsync(CurrentUserId, ct);

        return status is null ? NoContent() : Ok(status);
    }

    /// <summary>Kutayotgan almashtirishni bekor qiladi (idempotent).</summary>
    [HttpDelete("phone")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelPhoneChange(CancellationToken ct)
    {
        await profile.CancelPhoneChangeAsync(CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>
    /// TELEFON ALMASHTIRISH — 2-BOSQICH: Telegramga kelgan kod.
    /// </summary>
    [HttpPost("phone/confirm")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UserDto>> ConfirmPhoneChange(
        [FromBody] ConfirmPhoneRequest request, CancellationToken ct) =>
        Ok(await profile.ConfirmPhoneChangeAsync(CurrentUserId, request, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
