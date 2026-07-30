using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Courses.Dtos;
using Zinnur.Application.Courses.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Kurs kontenti: kurs -> modul -> dars (CRUD va TARTIB).
///
/// Controller YUPQA: hech qanday biznes qoidasi yo'q, faqat
/// "so'rov -> servis -> javob".
///
/// RUXSAT IKKI QATLAMLI:
///  1) Sinf darajasida <c>[Authorize]</c> — shunchaki "tizimga kirgan bo'l".
///     O'QUVCHI bu kontrollerga KIRADI (u o'z kursini ko'rishi kerak),
///     shuning uchun sinfda rol filtri YO'Q — `GroupsController` dan
///     ATAYLAB farq qiladi.
///  2) O'zgartiruvchi endpointlarda <c>[Authorize(Roles = ManageRoles)]</c>
///     — ustoz va kurator o'zgartira olmasin.
///
/// Haqiqiy qoida (kim nimani KO'RADI, kim nimani O'ZGARTIRADI) esa
/// <see cref="ICourseService"/> ICHIDA — atribut faqat darvoza. Ayniqsa
/// o'quvchi uchun: u faqat O'Z kursini ko'radi va faqat GATING ochgan
/// darslarning mazmunini oladi. Buni atribut bilan ifodalab bo'lmaydi.
/// </summary>
[ApiController]
[Route("api/v1/courses")]
[Authorize]
[Produces("application/json")]
public sealed class CoursesController(ICourseService courses) : ControllerBase
{
    // ================================================================= kurs

    /// <summary>
    /// Kurslar ro'yxati (sahifalangan). O'QUVCHI uchun faqat O'Z kursi
    /// qaytadi; guruhiga kurs biriktirilmagan bo'lsa — bo'sh ro'yxat.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CourseDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CourseDto>>> List(
        [FromQuery] CourseListQuery query, CancellationToken ct) =>
        Ok(await courses.ListAsync(query, CurrentUserId, ct));

    /// <summary>
    /// Kurs DARAXTI: modullar va darslar.
    ///
    /// O'quvchi uchun har dars <c>unlocked</c> bayrog'i bilan keladi va
    /// QULFLANGAN darsning <c>description</c> maydoni <c>null</c> bo'ladi
    /// (sarlavha ko'rinadi, mazmun yo'q).
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<CourseTreeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseTreeDto>> Get(long id, CancellationToken ct) =>
        Ok(await courses.GetAsync(id, CurrentUserId, ct));

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CourseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CourseDto>> Create(
        [FromBody] CreateCourseRequest request, CancellationToken ct)
    {
        var created = await courses.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Kursni tahrirlaydi. <c>position</c> bu yerda O'ZGARMAYDI — tartib
    /// faqat <see cref="ReorderCourses"/> orqali boshqariladi.
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CourseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseDto>> Update(
        long id, [FromBody] UpdateCourseRequest request, CancellationToken ct) =>
        Ok(await courses.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>
    /// Kursni o'chiradi.
    ///
    /// 409 QAYTADI agar: kursga guruh biriktirilgan bo'lsa, YOKI uning
    /// biror darsiga o'quvchi javob topshirgan / test urinishi bo'lsa —
    /// ular Cascade bilan yo'qolib ketardi.
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await courses.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>
    /// Kurslar tartibi. TO'LIQ ro'yxat kutiladi (barcha Id'lar) — server uni
    /// 0,1,2... qilib zich qayta raqamlaydi.
    /// </summary>
    [HttpPost("reorder")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<PositionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PositionDto>>> ReorderCourses(
        [FromBody] ReorderRequest request, CancellationToken ct) =>
        Ok(await courses.ReorderCoursesAsync(request, CurrentUserId, ct));

    // ================================================================= modul

    [HttpPost("{courseId:long}/modules")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CourseModuleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseModuleDto>> CreateModule(
        long courseId, [FromBody] CreateModuleRequest request, CancellationToken ct)
    {
        var created = await courses.CreateModuleAsync(courseId, request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = courseId }, created);
    }

    [HttpPut("{courseId:long}/modules/{moduleId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CourseModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseModuleDto>> UpdateModule(
        long courseId, long moduleId, [FromBody] UpdateModuleRequest request, CancellationToken ct) =>
        Ok(await courses.UpdateModuleAsync(courseId, moduleId, request, CurrentUserId, ct));

    /// <summary>
    /// Modulni o'chiradi (ichidagi darslar bilan). Biror darsga o'quvchi
    /// javobi yoki test urinishi bog'langan bo'lsa — 409.
    /// </summary>
    [HttpDelete("{courseId:long}/modules/{moduleId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteModule(
        long courseId, long moduleId, CancellationToken ct)
    {
        await courses.DeleteModuleAsync(courseId, moduleId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Kurs ichidagi modullar tartibi (to'liq ro'yxat).</summary>
    [HttpPost("{courseId:long}/modules/reorder")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<PositionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PositionDto>>> ReorderModules(
        long courseId, [FromBody] ReorderRequest request, CancellationToken ct) =>
        Ok(await courses.ReorderModulesAsync(courseId, request, CurrentUserId, ct));

    // ================================================================= dars

    [HttpPost("{courseId:long}/modules/{moduleId:long}/lessons")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CourseLessonDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseLessonDto>> CreateLesson(
        long courseId, long moduleId, [FromBody] CreateLessonRequest request, CancellationToken ct)
    {
        var created = await courses.CreateLessonAsync(courseId, moduleId, request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = courseId }, created);
    }

    [HttpPut("{courseId:long}/modules/{moduleId:long}/lessons/{lessonId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CourseLessonDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseLessonDto>> UpdateLesson(
        long courseId, long moduleId, long lessonId,
        [FromBody] UpdateLessonRequest request, CancellationToken ct) =>
        Ok(await courses.UpdateLessonAsync(courseId, moduleId, lessonId, request, CurrentUserId, ct));

    /// <summary>
    /// Darsni o'chiradi. Unga topshirilgan javob yoki test urinishi bo'lsa —
    /// 409 (baholar Cascade bilan yo'qolib ketardi).
    /// </summary>
    [HttpDelete("{courseId:long}/modules/{moduleId:long}/lessons/{lessonId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteLesson(
        long courseId, long moduleId, long lessonId, CancellationToken ct)
    {
        await courses.DeleteLessonAsync(courseId, moduleId, lessonId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>
    /// Modul ichidagi darslar tartibi (to'liq ro'yxat).
    ///
    /// ★ Bu amal GATING ketma-ketligini o'zgartiradi: darslar tartibi
    /// "oldingi dars tugatilganmi" qoidasining asosi.
    /// </summary>
    [HttpPost("{courseId:long}/modules/{moduleId:long}/lessons/reorder")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<PositionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PositionDto>>> ReorderLessons(
        long courseId, long moduleId, [FromBody] ReorderRequest request, CancellationToken ct) =>
        Ok(await courses.ReorderLessonsAsync(courseId, moduleId, request, CurrentUserId, ct));

    // ---------------------------------------------------------------- ichki

    /// <summary>Kurs kontentini O'ZGARTIRA oladigan rollar.</summary>
    private const string ManageRoles = "Academic,Admin";

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
