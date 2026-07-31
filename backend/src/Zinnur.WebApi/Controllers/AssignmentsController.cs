using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Assignments;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Models;
using Zinnur.Domain.Entities;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Uy vazifalari: tuzish (o'quv bo'limi/ustoz), topshirish (o'quvchi),
/// baholash (ustoz/kurator).
///
/// Controller YUPQA: <c>[Authorize(Roles=...)]</c> — faqat DARVOZA
/// ("umuman kira oladimi"). "Ustoz FAQAT O'Z guruhiga" degan haqiqiy qoida
/// <see cref="IAssignmentService"/> ICHIDA — aks holda yangi endpoint
/// qo'shilganda uni takrorlash unutilardi.
/// </summary>
[ApiController]
[Route("api/v1/assignments")]
[Authorize]
[Produces("application/json")]
public sealed class AssignmentsController(IAssignmentService assignments) : ControllerBase
{
    // ================================================================= xodim

    /// <summary>Ro'yxat (xodim). Ustoz/kurator uchun avtomatik o'z guruhlariga cheklanadi.</summary>
    [HttpGet]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<PagedResult<AssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AssignmentDto>>> List(
        [FromQuery] AssignmentListQuery query, CancellationToken ct) =>
        Ok(await assignments.ListAsync(query, CurrentUserId, ct));

    /// <summary>Vazifa kartochkasi. O'quvchi ham ko'radi — faqat o'ziga tegishlisini.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<AssignmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentDto>> Get(long id, CancellationToken ct) =>
        Ok(await assignments.GetAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Yangi vazifa. <c>moduleLessonId</c> — KURS vazifasi (faqat o'quv bo'limi),
    /// <c>groupId</c> — GURUH vazifasi (ustoz/kurator o'z guruhiga).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<AssignmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignmentDto>> Create(
        [FromBody] CreateAssignmentRequest request, CancellationToken ct)
    {
        var created = await assignments.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Tahrirlash. Nishon (guruh/dars) o'zgartirilmaydi.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<AssignmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AssignmentDto>> Update(
        long id, [FromBody] UpdateAssignmentRequest request, CancellationToken ct) =>
        Ok(await assignments.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>O'chirish. Javob topshirilgan bo'lsa 409 (baholar yo'qolmasin).</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await assignments.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Topshirilgan javoblar (baholash ro'yxati).</summary>
    [HttpGet("{id:long}/submissions")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<IReadOnlyList<SubmissionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<SubmissionDto>>> Submissions(
        long id, CancellationToken ct) =>
        Ok(await assignments.ListSubmissionsAsync(id, CurrentUserId, ct));

    // ================================================================= o'quvchi

    /// <summary>Mening vazifalarim — o'z javobim holati va gating bilan.</summary>
    [HttpGet("mine")]
    [Authorize(Roles = StudentRole)]
    [ProducesResponseType<IReadOnlyList<StudentAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StudentAssignmentDto>>> Mine(CancellationToken ct) =>
        Ok(await assignments.ListMineAsync(CurrentUserId, ct));

    /// <summary>
    /// Javob topshirish: <c>multipart/form-data</c> — <c>text</c> maydoni
    /// va/yoki <c>files</c> fayllari.
    ///
    /// ★ HAJM IKKI QATLAMDA CHEKLANADI:
    ///   1) <see cref="RequestSizeLimitAttribute"/> — BUTUN so'rov (Kestrel
    ///      uni o'qishdan oldin uzadi);
    ///   2) <see cref="SubmissionAttachmentReader"/> — HAR FAYL, OQIM
    ///      DAVOMIDA (eski tizim faylni to'liq o'qib, keyin tekshirardi —
    ///      Q-2 bugi).
    ///
    /// Oqimlar `finally` da yopiladi: aks holda katta yuklashda vaqtinchalik
    /// fayl deskriptorlari so'rov tugagach ham ushlanib turardi.
    /// </summary>
    [HttpPost("{id:long}/submit")]
    [Authorize(Roles = StudentRole)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<StudentSubmissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<StudentSubmissionDto>> Submit(
        long id,
        [FromForm] string? text,
        [FromForm] IFormFileCollection? files,
        CancellationToken ct)
    {
        var streams = new List<Stream>(files?.Count ?? 0);

        try
        {
            var incoming = new List<IncomingFile>(files?.Count ?? 0);

            foreach (var file in files ?? (IReadOnlyList<IFormFile>)[])
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);

                // Klient AYTGAN `ContentType` faqat xato xabari uchun uzatiladi —
                // haqiqiy tur MAZMUNDAN aniqlanadi.
                incoming.Add(new IncomingFile(file.FileName, file.ContentType, stream));
            }

            return Ok(await assignments.SubmitAsync(id, text, incoming, CurrentUserId, ct));
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    // ================================================================= baholash

    /// <summary>
    /// Baho qo'yish. Yo'l ATAYLAB <c>/submissions/{id}</c>: baholanadigan
    /// narsa vazifa emas, aynan JAVOB (bitta javobning ID'si yetarli).
    /// </summary>
    [HttpPost("~/api/v1/submissions/{id:long}/grade")]
    [Authorize(Roles = GradeRoles)]
    [ProducesResponseType<SubmissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubmissionDto>> Grade(
        long id, [FromBody] GradeSubmissionRequest request, CancellationToken ct) =>
        Ok(await assignments.GradeAsync(id, request, CurrentUserId, ct));

    /// <summary>
    /// Qayta topshirishga ruxsat. Ruxsat BIR MARTALIK — o'quvchi yuborgach
    /// Domain uni o'zi yopadi.
    /// </summary>
    [HttpPost("~/api/v1/submissions/{id:long}/reopen")]
    [Authorize(Roles = GradeRoles)]
    [ProducesResponseType<SubmissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SubmissionDto>> Reopen(
        long id, [FromBody] ReopenSubmissionRequest? request, CancellationToken ct) =>
        Ok(await assignments.ReopenAsync(
            id, request ?? new ReopenSubmissionRequest(), CurrentUserId, ct));

    // ================================================================= fayl o'qish

    /// <summary>
    /// Ilova qilingan faylni beradi (rasm yoki ovoz).
    ///
    /// ========================================================================
    /// ★ ESKI TIZIMNING X-6 KAMCHILIGI SHU YERDA YOPILGAN
    /// ========================================================================
    /// Eski loyihada fayllar `/media` katalogida AUTENTIFIKATSIYASIZ turardi:
    /// havolani bilgan istalgan odam — o'quvchining o'zi tanishiga yuborsa
    /// ham, qidiruv roboti topsa ham — begona bolaning ishini ko'ra olardi.
    /// Bu yerda "havola" degan tushuncha YO'Q: har so'rov `Authorization`
    /// bilan keladi va ruxsat qoidasi (`IAssignmentService`) HAR SAFAR
    /// qaytadan tekshiriladi.
    ///
    /// NIMA UCHUN PRESIGNED URL EMAS — sabab <see cref="ISubmissionStorage"/>
    /// izohida batafsil (qisqasi: presigned havola ulashilishi mumkin va uni
    /// bekor qilib bo'lmaydi).
    ///
    /// ⚠️ FRONTEND UCHUN MUHIM: brauzer `&lt;img src&gt;` yoki `&lt;a href&gt;`
    /// bilan `Authorization` sarlavhasini YUBORMAYDI. Shuning uchun bu
    /// endpointga `http.download()` yordamchisi orqali murojaat qilinadi
    /// (u `fetch` bilan token qo'yadi) va natijadagi `Blob` dan
    /// `URL.createObjectURL` yasaladi.
    /// </summary>
    [HttpGet("~/api/v1/submissions/files/{fileId:long}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadFile(long fileId, CancellationToken ct)
    {
        var download = await assignments.OpenFileAsync(fileId, CurrentUserId, ct);

        // OQIM EGALIGI: `File(...)` faqat oqimni yopadi, uning ostidagi HTTP
        // javobini emas. Javob yopilmasa ombor bilan ulanish hovuzga
        // qaytmaydi — sekin, ko'rinmas soket oqishi. Shuning uchun butun
        // `StoredFile` so'rov tugagach o'chiriladigan qilib ro'yxatga olinadi.
        Response.RegisterForDisposeAsync(download.Content);

        // nosniff: brauzer turni O'ZI taxmin qilib, faylni HTML deb
        // ko'rsatib yubormasin (saqlangan XSS'ning klassik yo'li).
        Response.Headers.XContentTypeOptions = "nosniff";

        // O'quvchining ishi — SHAXSIY ma'lumot: oraliq proksi ham, brauzer
        // diski ham uni saqlab qolmasin. `private` yetarli emas: umumiy
        // kompyuterda keyingi foydalanuvchi keshdan ochib olardi.
        Response.Headers.CacheControl = "no-store";

        // `enableRangeProcessing` ATAYLAB yoqilmagan: tarmoq oqimi
        // izlanmaydi (seek), ya'ni Range so'rovini bajarish uchun fayl
        // baribir to'liq xotiraga tushishi kerak bo'lardi.
        return File(download.Content.Content, download.ContentType, download.FileName);
    }

    // ---------------------------------------------------------------- ichki

    private const string StudentRole = "Student";

    /// <summary>Vazifa tuza oladigan rollar (aniq qoida servisda).</summary>
    private const string StaffRoles = "Teacher,Assistant,Academic,Admin";

    /// <summary>O'chirish — faqat o'quv bo'limi/admin.</summary>
    private const string ManageRoles = "Academic,Admin";

    /// <summary>
    /// Baholash darvozasi. Asosiy egasi — ustoz/kurator; o'quv bo'limi va
    /// admin ham kiritilgan, chunki ular ustozning xatosini tuzatishi kerak
    /// (aks holda noto'g'ri baho tizimda mangu qolardi).
    /// </summary>
    private const string GradeRoles = "Teacher,Assistant,Academic,Admin";

    /// <summary>
    /// BUTUN so'rov chegarasi = fayl soni × fayl chegarasi + sarlavhalar uchun
    /// zaxira. Doimiylar Domain va Application'dan olinadi — ikki joyda
    /// boshqa-boshqa raqam bo'lib qolmasin.
    /// </summary>
    private const long MaxRequestBytes =
        (Submission.MaxAttachments * (long)SubmissionAttachmentReader.MaxAnyBytes) + (1024 * 1024);

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
