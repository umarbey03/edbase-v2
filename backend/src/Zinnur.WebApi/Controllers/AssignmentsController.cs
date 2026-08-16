using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Assignments;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Courses.Services;
using Zinnur.Domain.Entities;
using Zinnur.WebApi.Media;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Uy vazifalari: tuzish (FAQAT o'quv bo'limi), topshirish (o'quvchi),
/// baholash (ustoz/kurator).
///
/// Controller YUPQA: <c>[Authorize(Roles=...)]</c> — faqat DARVOZA
/// ("umuman kira oladimi"). Nishon darajasidagi qoidalar (kimning javobi,
/// kimning guruhi) <see cref="IAssignmentService"/> ICHIDA — aks holda yangi
/// endpoint qo'shilganda ularni takrorlash unutilardi.
///
/// ═══════════════════════════════════════════════════════════════════════
/// R32 (2026-08-13 talabi) — USTOZ VAZIFA YARATMAYDI
/// ═══════════════════════════════════════════════════════════════════════
/// Loyiha egasi: *"teacher vazifa yaratishi kerakmas, o'quv bo'limi yaratadi
/// vazifalarni"*. Q10 QAT'IY o'qilishda hal qilindi: ustoz/kurator vazifa
/// yaratish, tahrirlash va SHART BIRIKTIRMALARIDAN butunlay chetlatildi.
///
/// ★ NIMA UCHUN BU YERDA DARVOZA HAM O'ZGARDI, faqat servis emas: rol
/// gate'i atributda qolib, qoida faqat servisda bo'lsa, ustoz UI'siz
/// so'rov yuborganda 403 ni SERVIS qaytarardi — bir xil natija, lekin
/// "kim umuman kira oladi" degan savolga javob ikki joyda ikki xil
/// yozilgan bo'lardi. Endi ikkalasi ham bitta gapni aytadi.
///
/// ⚠️ BAHOLASH TEGILMADI (<see cref="GradeRoles"/>) — talab faqat
/// YARATISHGA tegishli. Ustoz javoblarni ko'radi, baholaydi va qayta
/// topshirishga ruxsat beradi, avvalgidek.
/// </summary>
[ApiController]
[Route("api/v1/assignments")]
[Authorize]
[Produces("application/json")]
public sealed class AssignmentsController(
    IAssignmentService assignments,
    IAssignmentAttachmentService attachments,
    ISubmissionFeedbackFileService feedbackFiles) : ControllerBase
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
    /// Yangi vazifa — FAQAT o'quv bo'limi/admin (R32).
    /// <c>moduleLessonId</c> — KURS vazifasi, <c>groupId</c> — GURUH vazifasi.
    /// Ikkalasi ham endi AYNI darvozadan o'tadi.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<AssignmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignmentDto>> Create(
        [FromBody] CreateAssignmentRequest request, CancellationToken ct)
    {
        var created = await assignments.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Tahrirlash — FAQAT o'quv bo'limi/admin (R32). Nishon (guruh/dars)
    /// o'zgartirilmaydi.
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = ManageRoles)]
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

    // ================================================================= o'quv bo'limi umumiy ko'rinishi
    //
    // ★ 2026-08-15 talabi: "bir ko'rganda qaysi guruhdagi vazifalar
    // tekshirilmagani, nechtasi tekshirilgani/tekshirilmagani, javoblari
    // qachon yuborilgani/tekshirilgani, kim tekshirishi kerakligi, javob va
    // baho — hammasi ko'rinib turishi kerak" (ustoz/guruh turi/guruh filtri
    // bilan). Yo'l ATAYLAB `overview/...` — `{id:long}/submissions` bilan
    // ADASHTIRILMASIN: bu yerda ID YO'Q, butun to'plam ustidan xulosa.
    //
    // ⚠️ FAQAT `ManageRoles`: bu — o'quv bo'limining nazorat ekrani. Ustoz/
    // kurator o'z guruhini yuqoridagi `{id}/submissions` orqali tekshiradi.

    /// <summary>Guruh (va "Kurs vazifalari") bo'yicha xulosa — tekshirilgan/tekshirilmagan sonlari.</summary>
    [HttpGet("overview/groups")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<AssignmentGroupOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AssignmentGroupOverviewDto>>> GroupsOverview(
        [FromQuery] AssignmentOverviewFilter query, CancellationToken ct) =>
        Ok(await assignments.GetGroupsOverviewAsync(query, CurrentUserId, ct));

    /// <summary>Javoblarning yassilangan, sahifalangan ro'yxati (guruh/ustoz/tekshiruvchi konteksti bilan).</summary>
    [HttpGet("overview/submissions")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<PagedResult<SubmissionOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SubmissionOverviewDto>>> SubmissionsOverview(
        [FromQuery] SubmissionOverviewQuery query, CancellationToken ct) =>
        Ok(await assignments.ListSubmissionsOverviewAsync(query, CurrentUserId, ct));

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

    // ================================================================= WAVE 1: shart biriktirmalari

    /// <summary>
    /// Vazifa SHARTIGA fayl biriktiradi (`multipart/form-data`, maydon
    /// nomi — `file`).
    ///
    /// Rasm / audio / PDF qabul qilinadi va bir vazifada BIR NECHTA bo'lishi
    /// mumkin. Video ATAYLAB qabul qilinmaydi — u dars mediasi
    /// (`POST /api/v1/lessons/{lessonId}/assets`), u yerda `Range` bilan
    /// oqim va alohida hajm chegarasi bor.
    ///
    /// 🔴 Tur MAZMUNDAN aniqlanadi: `.jpg` deb nomlangan PDF ham qabul
    /// qilinadi (PDF ruxsat etilgan), lekin `.jpg` deb nomlangan EXE
    /// **400** oladi. Hajm chegarasi sozlamadan
    /// (`lesson.image_max_mb`) — oshsa **413**.
    ///
    /// RUXSAT: vazifani TAHRIRLASH huquqi bilan AYNI — R32 dan keyin bu
    /// FAQAT o'quv bo'limi/admin degani. Biriktirma vazifa SHARTINING bir
    /// qismi, ya'ni uni yuklash — vazifani tahrirlash bilan bir xil amal;
    /// darvoza ajralib qolsa, ustoz yarata olmagan vazifaning shartini
    /// o'zgartira olib qolardi.
    /// </summary>
    [HttpPost("{id:long}/attachments")]
    [Authorize(Roles = ManageRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAttachmentRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentRequestBytes)]
    [ProducesResponseType<AssignmentAttachmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssignmentAttachmentDto>> UploadAttachment(
        long id,
        IFormFile file,
        [FromForm] int? durationSec,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw MediaResponse.MissingFile();

        await using var stream = file.OpenReadStream();

        var created = await attachments.UploadAsync(
            id,
            new LessonAssetUpload(
                file.FileName,

                // Klient AYTGAN tur faqat XATO XABARI uchun uzatiladi.
                file.ContentType,
                stream,
                file.Length,
                Title: null,
                durationSec),
            CurrentUserId,
            ct);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// Shart biriktirmasini OQIM bilan beradi. `Range` qo'llab-quvvatlanadi
    /// (`206` + `Accept-Ranges: bytes`) — uzun audio namunada oldinga o'tish
    /// uchun.
    ///
    /// RUXSAT: vazifani KO'RISH huquqi bilan AYNI — o'quvchi ham oladi,
    /// LEKIN faqat O'ZIGA TEGISHLI vazifani (kurs vazifasida darsning
    /// kursga tegishliligi ham tekshiriladi).
    /// </summary>
    [HttpGet("~/api/v1/assignments/attachments/{attachmentId:long}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadAttachment(long attachmentId, CancellationToken ct)
    {
        var download = await attachments.OpenAsync(
            attachmentId, MediaResponse.RawRange(Request.Headers.Range), CurrentUserId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    /// <summary>
    /// Shart biriktirmasini o'chiradi (bazadan, so'ng ombordan).
    /// RUXSAT — yuklash bilan AYNI (R32: faqat o'quv bo'limi/admin).
    /// </summary>
    [HttpDelete("~/api/v1/assignments/attachments/{attachmentId:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAttachment(long attachmentId, CancellationToken ct)
    {
        await attachments.DeleteAsync(attachmentId, CurrentUserId, ct);
        return NoContent();
    }

    // ================================================================= R37: tekshiruv fayllari

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// USTOZ TEKSHIRISHDA FAYL BIRIKTIRADI (R37)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// `multipart/form-data`, maydon nomi — `file`. Rasm / ovoz / PDF.
    ///
    /// ★ NIMA UCHUN `POST /grade` MULTIPART'GA AYLANTIRILMADI: u bugun
    /// JSON qabul qiladi va `Consumes("multipart/form-data")` qo'shilishi
    /// bilan HAR BIR mavjud chaqiruv **415** olardi. To'liq asoslash
    /// (uchta sabab) <see cref="ISubmissionFeedbackFileService"/> izohida.
    ///
    /// ★ BAHODAN MUSTAQIL: faylni baho qo'yishdan OLDIN ham, KEYIN ham
    /// biriktirish mumkin. Ular bir tranzaksiyada bo'lishi SHART EMAS —
    /// baho ham, fayl ham mustaqil ravishda ma'noga ega.
    ///
    /// RUXSAT: baholash bilan AYNI (<see cref="GradeRoles"/> + javob
    /// darajasidagi tekshiruv servis ichida: ustoz faqat O'Z o'quvchisini).
    /// </summary>
    [HttpPost("~/api/v1/submissions/{id:long}/feedback-files")]
    [Authorize(Roles = GradeRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAttachmentRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentRequestBytes)]
    [ProducesResponseType<SubmissionFeedbackFileDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SubmissionFeedbackFileDto>> UploadFeedbackFile(
        long id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw MediaResponse.MissingFile();

        await using var stream = file.OpenReadStream();

        var created = await feedbackFiles.UploadAsync(
            id,
            new LessonAssetUpload(
                file.FileName,

                // Klient AYTGAN tur faqat XATO XABARI uchun uzatiladi.
                file.ContentType,
                stream,
                file.Length),
            CurrentUserId,
            ct);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// Tekshiruv faylini OQIM bilan beradi (`Range` qo'llab-quvvatlanadi).
    ///
    /// 🔴 RUXSAT — JAVOBNI KO'RISH huquqi, ya'ni O'QUVCHI HAM OLADI. Bu
    /// R37 talabining MOHIYATI: ustoz biriktirgan tuzatish o'quvchiga
    /// yetib borishi kerak. Rol atributi ATAYLAB yo'q — qoida ROLGA emas,
    /// javobning EGALIGIGA bog'liq (begona o'quvchi baribir 403 oladi).
    ///
    /// ⚠️ FRONTEND UCHUN: brauzer `&lt;img src&gt;` bilan `Authorization`
    /// yubormaydi — `http.download()` orqali `Blob` olinadi (o'quvchi
    /// javob fayllaridagi AYNI naqsh).
    /// </summary>
    [HttpGet("~/api/v1/submissions/feedback-files/{fileId:long}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadFeedbackFile(long fileId, CancellationToken ct)
    {
        var download = await feedbackFiles.OpenAsync(
            fileId, MediaResponse.RawRange(Request.Headers.Range), CurrentUserId, ct);

        return await MediaResponse.WriteAsync(this, download, ct);
    }

    /// <summary>
    /// Tekshiruv faylini o'chiradi (bazadan, so'ng ombordan).
    ///
    /// ⚠️ O'QUVCHI O'CHIRA OLMAYDI — bu ustozning sharhi, o'quvchining
    /// javobi emas.
    /// </summary>
    [HttpDelete("~/api/v1/submissions/feedback-files/{fileId:long}")]
    [Authorize(Roles = GradeRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFeedbackFile(long fileId, CancellationToken ct)
    {
        await feedbackFiles.DeleteAsync(fileId, CurrentUserId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- ichki

    private const string StudentRole = "Student";

    /// <summary>
    /// XODIM darvozasi — faqat O'QISH yo'llari uchun (ro'yxat, kartochka,
    /// topshirilgan javoblar).
    ///
    /// ★ R32 dan keyin bu nom "yarata oladigan rollar"ni ANGLATMAYDI: ustoz
    /// va kurator vazifani KO'RADI (o'z o'quvchisini baholash uchun kerak),
    /// lekin yaratmaydi va tahrirlamaydi. Yozish yo'llari
    /// <see cref="ManageRoles"/> ga o'tkazildi.
    /// </summary>
    private const string StaffRoles = "Teacher,Assistant,Academic,Admin";

    /// <summary>
    /// Vazifani BOSHQARISH (yaratish, tahrirlash, o'chirish, shart
    /// biriktirmalari) — faqat o'quv bo'limi/admin.
    /// </summary>
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

    /// <summary>
    /// SHART biriktirmasi uchun so'rov tanasining QAT'IY chegarasi.
    ///
    /// ★ QIYMAT `lesson.image_max_mb` sozlamasining `Maximum` i bilan MOS
    ///   (100 MB) + 1 MB zaxira. HAQIQIY chegara sozlamadan keladi
    ///   (standart 10 MB) va servis ichida 413 bilan qo'llanadi; bu atribut
    ///   esa faqat "diskni to'ldirib qo'yish"dan himoya (multipart fayl
    ///   MODEL BOG'LASHDA, bizning kodimizdan OLDIN buferlanadi).
    /// </summary>
    private const long MaxAttachmentRequestBytes = (100L * 1024 * 1024) + (1024 * 1024);

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
