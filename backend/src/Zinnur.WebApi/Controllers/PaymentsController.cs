using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Payments.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ========================================================================
/// MOLIYA (FAZA 4.3): to'lovlar, tariflar, chegirmalar, bloklash
/// ========================================================================
///
/// Controller YUPQA: biznes qoidasi yo'q, faqat "so'rov -> servis -> javob".
///
/// RUXSAT IKKI QATLAMLI:
///  1) sinf darajasida <c>[Authorize]</c> — "tizimga kirgan bo'l";
///  2) pulga tegadigan HAR bir endpointda
///     <c>[Authorize(Roles = ManageRoles)]</c> — ustoz va kurator umuman
///     kirmasin.
///
/// Atributsiz uch endpoint bor va ular ATAYLAB shunday: o'quvchi O'Z
/// hisobini, o'z jurnalini va o'z blok holatini ko'radi. "O'ZINIKI" ekanini
/// atribut bilan ifodalab bo'lmaydi — u tekshiruv servis ichida
/// (<c>EnsureCanViewStudent</c>), va begona hisob so'ralsa 403 qaytadi.
///
/// XATOLAR (global middleware xaritalaydi):
///   400 — kiruvchi ma'lumot xatosi (`errors` ichida maydon nomi bilan)
///   403 — ruxsat yo'q YOKI qarz uchun bloklangan (matn ko'rsatiladi)
///   404 — o'quvchi/tarif/chegirma topilmadi
///   409 — biznes qoidasi (to'langan oyni kechirish) yoki TO'QNASHUV
///         (ikki kassir bir vaqtda bir oyni yopdi — `xmin` optimistik qulf)
/// </summary>
[ApiController]
[Route("api/v1/payments")]
[Authorize]
[Produces("application/json")]
public sealed class PaymentsController(
    IPaymentService payments,
    IPaymentBlockService blocks,
    IPaymentSummaryService summary) : ControllerBase
{
    // ================================================================= yig'ma hisobot

    /// <summary>
    /// ★ MOLIYA BOSHQARUV PANELI — BITTA SO'ROVDA.
    ///
    /// KPI kartochkalar, qarz yoshi (0-30/31-60/61-90/90+), oxirgi 12 oy
    /// dinamikasi hamda guruh va to'lov usuli kesimlari.
    ///
    /// NIMA UCHUN ALOHIDA ENDPOINT (va nega mijoz o'zi hisoblamaydi):
    /// bu raqamlar uchun minglab to'lov qatorini brauzerga yuklash kerak
    /// bo'lardi. Barcha yig'indi SQL tomonda bajariladi.
    ///
    /// <paramref name="from"/> va <paramref name="to"/> — MAHALLIY sanalar
    /// (Asia/Tashkent), IKKALASI HAM KIRADI. Berilmasa: joriy oy boshidan
    /// bugungacha. Qarz, balans va qarz yoshi — davrdan QAT'I NAZAR
    /// BUGUNGI holat (qarz oraliqda sodir bo'ladigan hodisa emas).
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<PaymentSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PaymentSummaryDto>> Summary(
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null) =>
        Ok(await summary.GetSummaryAsync(new PaymentSummaryQuery(from, to), CurrentUserId, ct));

    /// <summary>
    /// AYNI hisobotning CSV ko'rinishi (Excel uchun BOM va <c>sep=</c> bilan).
    ///
    /// Ma'lumot yuqoridagi endpoint bilan BITTA yo'ldan hisoblanadi —
    /// ekrandagi raqam va fayldagi raqam hech qachon farq qilmaydi.
    /// </summary>
    [HttpGet("summary/export")]
    [Authorize(Roles = ManageRoles)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportSummary(
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var file = await summary.ExportSummaryCsvAsync(
            new PaymentSummaryQuery(from, to), CurrentUserId, ct);

        return File(file.Content.ToArray(), file.ContentType, file.FileName);
    }

    // ================================================================= oy ochish

    /// <summary>
    /// Joriy (yoki so'ralgan) oy uchun to'lov yozuvlarini ochadi.
    ///
    /// IDEMPOTENT: takror chaqirilsa yangi qator YARATILMAYDI va xato ham
    /// bermaydi — javobdagi <c>alreadyOpen</c> nechtasi o'tkazib
    /// yuborilganini ko'rsatadi. Ochilgandan keyin o'quvchining balansi
    /// avtomatik sarflanadi (<c>balanceApplied</c>).
    /// </summary>
    [HttpPost("periods/open")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<OpenPeriodResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OpenPeriodResult>> OpenPeriod(
        [FromBody] OpenPeriodRequest request, CancellationToken ct) =>
        Ok(await payments.OpenPeriodAsync(request, CurrentUserId, ct));

    // ================================================================= pul

    /// <summary>
    /// ★ To'lov kiritishning YAGONA yo'li.
    ///
    /// Pul eng eski qarzdan boshlab taqsimlanadi, ortig'i balansga tushadi,
    /// jurnalga kvitansiya raqami bilan yoziladi — hammasi bitta
    /// tranzaksiyada. Javob — KVITANSIYA.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<PaymentReceiptDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentReceiptDto>> Record(
        [FromBody] RecordPaymentRequest request, CancellationToken ct)
    {
        var receipt = await payments.RecordPaymentAsync(request, CurrentUserId, ct);

        return CreatedAtAction(
            nameof(GetStudentAccount), new { studentId = receipt.StudentId }, receipt);
    }

    /// <summary>
    /// Oyni kechiradi: pul olinmaydi, lekin oy qarz bo'lib qolmaydi.
    /// To'liq to'langan oy uchun 409.
    /// </summary>
    [HttpPost("{paymentId:long}/waive")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Waive(
        long paymentId, [FromBody] WaiveRequest request, CancellationToken ct) =>
        Ok(await payments.WaiveAsync(paymentId, request, CurrentUserId, ct));

    /// <summary>
    /// Pulni orqaga qaytaradi (avval balansdan, so'ng eng yangi to'langan
    /// oylardan). Qisman qaytarish — XATO EMAS: qoldiq <c>unreturned</c> da
    /// qaytadi. Umuman qaytariladigan pul bo'lmasa — 409.
    /// </summary>
    [HttpPost("reverse")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<ReversalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReversalDto>> Reverse(
        [FromBody] ReversePaymentRequest request, CancellationToken ct) =>
        Ok(await payments.ReverseAsync(request, CurrentUserId, ct));

    // ================================================================= o'qish

    /// <summary>Oylik yozuvlar ro'yxati (qarzdorlar hisoboti uchun ham).</summary>
    [HttpGet]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<PagedResult<PaymentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<PaymentDto>>> List(
        [FromQuery] PaymentListQuery query, CancellationToken ct) =>
        Ok(await payments.ListPaymentsAsync(query, CurrentUserId, ct));

    /// <summary>
    /// O'quvchining moliya hisobi: qarz, balans, oylar tarixi, jurnal.
    ///
    /// ★ ROL FILTRI ATRIBUTDA YO'Q: o'quvchi O'Z hisobini ko'radi. Begona
    /// hisob so'ralsa servis 403 beradi; ustoz va kurator esa umuman ko'ra
    /// olmaydi.
    /// </summary>
    [HttpGet("students/{studentId:long}")]
    [ProducesResponseType<StudentAccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentAccountDto>> GetStudentAccount(
        long studentId, CancellationToken ct) =>
        Ok(await payments.GetStudentAccountAsync(studentId, CurrentUserId, ct));

    /// <summary>To'lovlar jurnali (sahifalangan). Ruxsat — hisob bilan bir xil.</summary>
    [HttpGet("students/{studentId:long}/transactions")]
    [ProducesResponseType<PagedResult<PaymentTransactionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<PaymentTransactionDto>>> ListTransactions(
        long studentId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25) =>
        Ok(await payments.ListTransactionsAsync(studentId, page, pageSize, CurrentUserId, ct));

    // ================================================================= blok

    /// <summary>
    /// Bloklash holati — 403 ga DUCH KELMASDAN oldin tekshirish uchun
    /// (frontend ogohlantirish ko'rsatadi: "qarzingiz X so'm").
    ///
    /// <paramref name="scope"/> — qaysi turkum so'ralayapti
    /// (<c>Video</c>, <c>Live</c>, <c>Platform</c>).
    /// </summary>
    [HttpGet("students/{studentId:long}/block")]
    [ProducesResponseType<PaymentBlockDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentBlockDto>> GetBlockStatus(
        long studentId,
        CancellationToken ct,
        [FromQuery] PaymentBlockScope scope = PaymentBlockScope.Video)
    {
        // Ruxsat: hisobni ko'rish qoidasi bilan BIR XIL — servisdagi YAGONA
        // tekshiruv qayta ishlatiladi (bu yerda ikkinchi nusxa yozilsa, ikki
        // qoida vaqt o'tib bir-biridan ajralib ketardi).
        await payments.EnsureCanViewStudentAsync(studentId, CurrentUserId, ct);

        return Ok(await blocks.EvaluateAsync(studentId, scope, ct));
    }

    /// <summary>Bloklashdan istisno qilish (yoki bekor qilish).</summary>
    [HttpPost("students/{studentId:long}/exempt")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<PaymentBlockDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentBlockDto>> SetExempt(
        long studentId, [FromBody] SetExemptRequest request, CancellationToken ct) =>
        Ok(await payments.SetExemptAsync(studentId, request, CurrentUserId, ct));

    // ================================================================= sozlama

    /// <summary>
    /// Bloklash sozlamalari. <c>blockThreshold</c> va <c>blockScope</c>
    /// BAZADA (ish jarayonida o'zgartiriladi), <c>enforce</c> esa
    /// KONFIGURATSIYADAN (muhit xossasi) — shuning uchun u faqat o'qish
    /// uchun qaytadi.
    /// </summary>
    [HttpGet("settings")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<FinanceSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FinanceSettingsDto>> GetSettings(CancellationToken ct) =>
        Ok(await payments.GetSettingsAsync(CurrentUserId, ct));

    [HttpPut("settings")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<FinanceSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FinanceSettingsDto>> UpdateSettings(
        [FromBody] UpdateFinanceSettingsRequest request, CancellationToken ct) =>
        Ok(await payments.UpdateSettingsAsync(request, CurrentUserId, ct));

    // ================================================================= tarif

    [HttpGet("tariffs")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<TariffDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<TariffDto>>> ListTariffs(
        CancellationToken ct, [FromQuery] bool? isActive = null) =>
        Ok(await payments.ListTariffsAsync(isActive, CurrentUserId, ct));

    /// <summary>
    /// Guruh uchun AYNAN qaysi tarif tushishini ko'rsatadi (aniqlikdan
    /// umumiyga). Mos tarif bo'lmasa — 204: bu xato emas, shunchaki
    /// "sozlanmagan".
    /// </summary>
    [HttpGet("tariffs/resolve")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<TariffDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TariffDto>> ResolveTariff(
        [FromQuery] long groupId, CancellationToken ct, [FromQuery] DateOnly? onDate = null)
    {
        var tariff = await payments.ResolveTariffAsync(groupId, onDate, CurrentUserId, ct);

        return tariff is null ? NoContent() : Ok(tariff);
    }

    [HttpPost("tariffs")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<TariffDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TariffDto>> CreateTariff(
        [FromBody] CreateTariffRequest request, CancellationToken ct)
    {
        var created = await payments.CreateTariffAsync(request, CurrentUserId, ct);

        return CreatedAtAction(nameof(ListTariffs), new { }, created);
    }

    /// <summary>
    /// ★ <c>PUT</c> — TO'LIQ ALMASHTIRISH: yuborilmagan maydon standart
    /// qiymatga tushadi (<c>courseId</c> yuborilmasa tarif "barcha kurslar"
    /// ga aylanadi). Qisman o'zgartirish uchun avval <c>GET</c> qiling.
    /// </summary>
    [HttpPut("tariffs/{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<TariffDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TariffDto>> UpdateTariff(
        long id, [FromBody] UpdateTariffRequest request, CancellationToken ct) =>
        Ok(await payments.UpdateTariffAsync(id, request, CurrentUserId, ct));

    [HttpDelete("tariffs/{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTariff(long id, CancellationToken ct)
    {
        await payments.DeleteTariffAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    // ================================================================= chegirma

    [HttpGet("students/{studentId:long}/discounts")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<StudentDiscountDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StudentDiscountDto>>> ListDiscounts(
        long studentId, CancellationToken ct) =>
        Ok(await payments.ListDiscountsAsync(studentId, CurrentUserId, ct));

    [HttpPost("students/{studentId:long}/discounts")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<StudentDiscountDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentDiscountDto>> CreateDiscount(
        long studentId, [FromBody] CreateDiscountRequest request, CancellationToken ct)
    {
        var created = await payments.CreateDiscountAsync(studentId, request, CurrentUserId, ct);

        return CreatedAtAction(nameof(ListDiscounts), new { studentId }, created);
    }

    /// <summary>★ <c>PUT</c> — TO'LIQ ALMASHTIRISH (izoh <see cref="UpdateTariff"/> da).</summary>
    [HttpPut("students/{studentId:long}/discounts/{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<StudentDiscountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentDiscountDto>> UpdateDiscount(
        long studentId, long id, [FromBody] UpdateDiscountRequest request, CancellationToken ct) =>
        Ok(await payments.UpdateDiscountAsync(studentId, id, request, CurrentUserId, ct));

    [HttpDelete("students/{studentId:long}/discounts/{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDiscount(
        long studentId, long id, CancellationToken ct)
    {
        await payments.DeleteDiscountAsync(studentId, id, CurrentUserId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- ichki

    /// <summary>Moliyani boshqara oladigan rollar. Ustoz/kurator YO'Q.</summary>
    private const string ManageRoles = "Academic,Admin";

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
