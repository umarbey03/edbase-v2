using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Students.Dtos;
using Zinnur.Application.Students.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// "MENING GURUHIM" OYNASI (2026-08-17) — bosh sahifadagi karta/tugma
/// bosilganda ochiladigan modal uchun.
///
/// ★ ALOHIDA KONTROLLER, <see cref="GroupsController"/> GA QO'SHILMADI:
/// o'sha kontroller sinf darajasida <c>[Authorize(Roles =
/// "Teacher,Assistant,Academic,Admin")]</c> — O'QUVCHI UMUMAN KIRA
/// OLMAYDI. Bitta amal uchun butun kontrollerning darvozasini ochish
/// (yoki metodga ikkinchi <c>[Authorize]</c> qo'shib "VA" mantig'ini
/// sinash) xatoga ochiqroq yo'l bo'lardi — yangi, faqat o'quvchiga
/// mo'ljallangan kontroller aniqroq.
///
/// 🔴 <c>studentId</c> HAR DOIM TOKENDAN olinadi (`ProfileController` bilan
/// AYNI qoida) — o'quvchi boshqa birovning guruhini so'ray olmaydi.
/// </summary>
[ApiController]
[Route("api/v1/students/me/classroom")]
[Authorize(Roles = "Student")]
[Produces("application/json")]
public sealed class StudentClassroomController(IStudentClassroomService classroom) : ControllerBase
{
    /// <summary>Guruh(lar)im: ustoz, kurator, guruhdoshlar va bog'lanish kontakti.</summary>
    [HttpGet]
    [ProducesResponseType<ClassroomDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassroomDto>> Get(CancellationToken ct) =>
        Ok(await classroom.GetAsync(CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
