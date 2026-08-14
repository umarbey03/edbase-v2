using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Groups.Dtos;
using Zinnur.Application.Groups.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// GURUH KATEGORIYALARI LUG'ATI (R21b) — o'quv yo'nalishlari
/// ("ATF", "Grammatika", "CEFR", "IELTS").
///
/// Controller YUPQA: hech qanday biznes qoidasi yo'q, faqat
/// "so'rov -> servis -> javob".
///
/// RUXSAT IKKI QATLAMLI (<c>GroupsController</c> bilan AYNI naqsh):
///  1) Sinf darajasidagi <c>[Authorize(Roles = "Teacher,Assistant,Academic,Admin")]</c>
///     — umumiy DARVOZA. O'QUVCHI bu kontrollerga umuman kirmaydi.
///     Ustoz va kurator esa KIRADI, chunki lug'at ularning ekranlaridagi
///     FILTR tanlagichini to'ldiradi (guruhlar ro'yxati va chatlar ro'yxati).
///  2) O'zgartiruvchi endpointlarda <c>[Authorize(Roles = "Academic,Admin")]</c>.
///
/// Haqiqiy qoida <see cref="IGroupCategoryService"/> ICHIDA — atribut faqat
/// darvoza (loyihadagi umumiy kelishuv).
///
/// ★ NEGA <c>/api/v1/groups/categories</c> EMAS, ILDIZ DARAJADA: bu guruhning
/// ost-resursi emas, MUSTAQIL lug'at. <c>groups/{id}/...</c> naqshi ostiga
/// qo'yilsa "qaysi guruhning kategoriyalari?" degan noto'g'ri savol tug'ilardi.
/// </summary>
[ApiController]
[Route("api/v1/group-categories")]
[Authorize(Roles = "Teacher,Assistant,Academic,Admin")]
[Produces("application/json")]
public sealed class GroupCategoriesController(IGroupCategoryService categories) : ControllerBase
{
    /// <summary>
    /// Kategoriyalar ro'yxati, tartib bo'yicha.
    ///
    /// SAHIFALANMAYDI (ataylab): bu tanlagichni to'ldiradigan lug'at va
    /// sahifalangan bo'lsa oxirgi bandlar jimgina tushib qolardi — sabab
    /// <see cref="GroupCategoryListQuery"/> izohida.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GroupCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GroupCategoryDto>>> List(
        [FromQuery] GroupCategoryListQuery query, CancellationToken ct) =>
        Ok(await categories.ListAsync(query, CurrentUserId, ct));

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupCategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupCategoryDto>> Create(
        [FromBody] CreateGroupCategoryRequest request, CancellationToken ct)
    {
        var created = await categories.CreateAsync(request, CurrentUserId, ct);

        // ★ `CreatedAtAction(nameof(List))` — alohida `GET /{id}` endpointi
        //   ATAYLAB YO'Q: bitta kategoriyani yolg'iz o'qish holati kod
        //   bazasida yo'q (UI doim butun lug'atni oladi). Bo'sh endpoint
        //   qo'shishdan ko'ra `Location` ni ro'yxatga qaratish halolroq.
        return CreatedAtAction(nameof(List), null, created);
    }

    /// <summary>
    /// Tahrirlash — TO'LIQ shakl (PUT semantikasi): yuborilmagan maydon
    /// standart qiymatga tushadi.
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<GroupCategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupCategoryDto>> Update(
        long id, [FromBody] UpdateGroupCategoryRequest request, CancellationToken ct) =>
        Ok(await categories.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>
    /// O'chiradi.
    ///
    /// 🔴 409 QAYTADI agar kategoriyaga guruh biriktirilgan bo'lsa: FK
    /// <c>SET NULL</c> bo'lgani uchun o'chirish JIMGINA muvaffaqiyatli
    /// tugab, o'nlab guruh yorlig'ini yo'qotardi. Bunday holatda javob
    /// ARXIVLASHNI taklif qiladi.
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- ichki

    /// <summary>Lug'atni O'ZGARTIRA oladigan rollar (sinf darvozasi bilan kesishadi).</summary>
    private const string ManageRoles = "Academic,Admin";

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
