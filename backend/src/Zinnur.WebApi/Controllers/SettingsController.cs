using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Settings.Dtos;
using Zinnur.Application.Settings.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ========================================================================
/// TIZIM SOZLAMALARI — SUPER-ADMIN PANELI
/// ========================================================================
///
/// 🔴 FAQAT <c>Admin</c>. <c>Academic</c> ham kirmaydi — bu boshqa
/// controller'lardagi <c>"Academic,Admin"</c> juftligidan ATAYLAB farq
/// qiladi. Sabab: bu yerda bloklash chegarasi va tashqi integratsiya
/// kalitlari turadi; eski tizimning eng og'ir zaifligi (audit X-4) aynan
/// <c>academic</c> rolining ortiqcha huquqidan boshlangan edi.
///
/// ★ ATRIBUT — FAQAT DARVOZA. Haqiqiy tekshiruv <see cref="ISettingsService"/>
/// ichida va u rolni BAZADAN qayta o'qiydi: kirish tokeni 15 daqiqa
/// yashaydi, ya'ni endigina roli pasaytirilgan xodim eski token bilan
/// atributdan bemalol o'tardi.
///
/// ★ NIMA UCHUN <c>PUT /{key}</c>, <c>PUT /</c> EMAS:
/// PUT — TO'LIQ ALMASHTIRISH. Agar butun ro'yxat bitta PUT bilan
/// yuborilganda, bitta maydonni o'zgartirmoqchi bo'lgan interfeys
/// yuborilmagan kalitlarni jimgina o'chirib yuborardi. Har kalit ALOHIDA
/// resurs bo'lgani uchun bu xavf butunlay yo'q va amal idempotent.
///
/// XATOLAR (global middleware xaritalaydi):
///   400 — qiymat qoidaga to'g'ri kelmadi YOKI kalit "faqat o'qish" uchun
///         (sababi <c>problem.errors[key]</c> ichida, o'zbekcha)
///   403 — rol <c>Admin</c> emas (yoki profil o'chirilgan)
///   404 — registrda bunday kalit yo'q
/// </summary>
[ApiController]
[Route("api/v1/settings")]
[Authorize(Roles = AdminOnly)]
[Produces("application/json")]
public sealed class SettingsController(ISettingsService settings) : ControllerBase
{
    /// <summary>
    /// Yagona ruxsat etilgan rol. <c>const</c> — atribut argumenti
    /// kompilyatsiya vaqtida ma'lum bo'lishi shart, va nom bir joyda tursin.
    /// </summary>
    private const string AdminOnly = "Admin";

    /// <summary>
    /// Barcha sozlamalar, guruhlarga bo'lingan holda.
    ///
    /// Javobda HAR maydon uchun: turi, chegaralari, tahrirlash mumkinligi va
    /// (mumkin bo'lmasa) sababi bor — interfeys formani AYNAN shundan quradi.
    /// Sirlar maskalangan holda (<c>maskedValue</c>) qaytadi, <c>value</c>
    /// esa ular uchun HAR DOIM <c>null</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SettingsPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SettingsPageDto>> List(CancellationToken ct) =>
        Ok(await settings.ListAsync(CurrentUserId, ct));

    /// <summary>Bitta sozlama (kalit bo'yicha).</summary>
    [HttpGet("{key}")]
    [ProducesResponseType<SettingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SettingDto>> Get(string key, CancellationToken ct) =>
        Ok(await settings.GetAsync(key, CurrentUserId, ct));

    /// <summary>
    /// Qiymatni o'zgartiradi. Faqat <c>isEditable = true</c> kalitlar uchun.
    /// Qiymat HAR DOIM satr sifatida yuboriladi (<c>"true"</c>, <c>"600000"</c>) —
    /// turini server registrdan biladi va o'zi tekshiradi.
    /// </summary>
    [HttpPut("{key}")]
    [ProducesResponseType<SettingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SettingDto>> Update(
        string key, [FromBody] UpdateSettingRequest request, CancellationToken ct) =>
        Ok(await settings.UpdateAsync(key, request, CurrentUserId, ct));

    /// <summary>
    /// Standart qiymatga qaytaradi: bazadagi qator O'CHIRILADI va qiymat
    /// yana muhit o'zgaruvchisidan (u ham bo'lmasa registrdagi standartdan)
    /// olinadi.
    ///
    /// ★ NIMA UCHUN <c>DELETE</c> EMAS: <c>DELETE /settings/{key}</c>
    /// "sozlamani o'chirish" degan taassurot berardi, holbuki sozlama
    /// qoladi — faqat qiymat manbai o'zgaradi. Nom amalni aniq aytishi kerak.
    /// </summary>
    [HttpPost("{key}/reset")]
    [ProducesResponseType<SettingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SettingDto>> Reset(string key, CancellationToken ct) =>
        Ok(await settings.ResetAsync(key, CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
