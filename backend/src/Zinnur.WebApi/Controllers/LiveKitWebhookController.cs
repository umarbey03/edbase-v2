using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// LIVEKIT WEBHOOK — dars yozuvi hodisalari
/// ════════════════════════════════════════════════════════════════════════
///
/// Controller YUPQA va uning YAGONA mas'uliyati — DARVOZA:
/// imzo → tana xeshi → servis. Holat o'zgarishi
/// <see cref="IRecordingWebhookHandler"/> ichida va u umuman imzoni
/// tekshirmaydi (va tekshirmasligi ham kerak) — shu tufayli "tekshirishni
/// unutish" MUMKIN EMAS: kirish nuqtasi bitta.
///
/// ── UCHTA QAT'IY QOIDA ──────────────────────────────────────────────────
///
/// 🔴 1) SIR SOZLANMAGAN BO'LSA ENDPOINT UMUMAN YO'Q (404).
///       Telegram webhook'i bilan AYNI qat'iylik. 403 EMAS: skanerga
///       endpoint borligini ham bildirmaymiz. "Sirsiz qabul qilish"
///       degan rejim MAVJUD EMAS — eski tizimda esa aynan shunday edi
///       (`if settings.LIVEKIT_API_SECRET:` — sir bo'sh bo'lsa BUTUN
///       tekshiruv chetlab o'tilardi).
///
/// 🔴 2) IMZO YOKI TANA XESHI MOS KELMASA — 401, TANASIZ.
///       Sababi (imzomi, xeshmi, muddatimi) javobda HECH QACHON
///       ko'rsatilmaydi: u hujumchiga qaysi bosqichda to'xtaganini
///       aytardi. Sabab faqat LOGDA.
///
/// ⚠️ NIMA UCHUN 401, TELEGRAM'DAGIDEK 403 EMAS: LiveKit o'zini AYNAN
///    <c>Authorization</c> sarlavhasi bilan tanitadi, ya'ni bu
///    AUTENTIFIKATSIYA muvaffaqiyatsizligi. Telegram esa o'z sirini
///    maxsus sarlavhada yuboradi (<c>Authorization</c> emas) — u yerda
///    403 to'g'ri.
///
/// 3) IMZODAN O'TGAN HAR NARSAGA — 200.
///    LiveKit 200 dan boshqa har javobda hodisani QAYTA yuboradi. Buzuq
///    JSON ham, ichki xato ham 200 bilan yopiladi (xato logga tushadi) —
///    aks holda cheksiz qayta yuborish sikli boshlanardi.
///
/// ── OG'IR ISH BU YERDA BAJARILMAYDI ─────────────────────────────────────
///
/// Servis faqat bazaga bir necha so'rov yuboradi. Egress'ni to'xtatish va
/// ombordan tekshirish ATAYLAB watchdog'ga qoldirilgan: webhook ichida
/// sekin tashqi chaqiruv bo'lsa, LiveKit javobni kutolmay hodisani QAYTA
/// yuborardi va bitta hodisa bir necha marta ishlanardi.
///
/// ⚠️ 2026-09-05 DAN BU QOIDANING ONGLI ISTISNOSI BOR (yangi trek quvuri,
/// SPEC-RECORDING-V2 §3.3): <see cref="ITrackRecordingWebhookHandler"/>
/// trek egress'ini AYNAN webhook ichida boshlaydi va to'xtatadi.
/// Yuqoridagi mulohaza ESKI quvurga tegishli bo'lib qoladi va u yerda
/// hech narsa o'zgarmadi. Istisnoning sababi: yangi quvurda trek
/// navbatdagi vazifadan kutib turolmaydi — <c>Jobs:TickSeconds</c> (30 s)
/// har ekran ulashishning boshidan yarim daqiqani qirqardi. Bu yerda
/// hech kim javobni kutmaydi (LiveKit uni o'qimaydi), Twirp mijozining
/// esa o'z 10 soniyalik muhlati bor.
/// </summary>
[ApiController]
[Route("api/v1/livekit")]
[Produces("application/json")]
public sealed class LiveKitWebhookController(
    ILiveKitWebhookVerifier verifier,
    ITrackRecordingWebhookHandler trackHandler,
    IRecordingWebhookHandler handler,
    ILogger<LiveKitWebhookController> logger) : ControllerBase
{
    /// <summary>
    /// So'rov tanasining chegarasi.
    ///
    /// LiveKit hodisasi ~1–3 KB. 64 KB — zaxira bilan, lekin cheksiz
    /// emas: chegarasiz endpoint xotirani to'ldiradigan arzon hujum
    /// vositasi bo'lardi. Kestrel bu chegaradan katta tanani BIZ o'qishdan
    /// oldin uzadi.
    /// </summary>
    private const long MaxBodyBytes = 64 * 1024;

    /// <summary>LiveKit hodisasini qabul qiladi.</summary>
    /// <response code="200">Hodisa qabul qilindi (natija tanada).</response>
    /// <response code="401">Imzo yoki tana xeshi mos kelmadi.</response>
    /// <response code="404">LiveKit kaliti sozlanmagan — endpoint yo'q.</response>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<WebhookAck>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        if (!verifier.IsConfigured)
            return NotFound();

        // ⚠️ TANA XOM BAYT SIFATIDA O'QILADI, model bog'lash (model
        //    binding) ORQALI EMAS. Sabab hal qiluvchi: xesh AYNAN kelgan
        //    baytlar ustidan hisoblanadi. Deserializatsiya qilib, keyin
        //    qayta serializatsiya qilingan tana boshqa baytlar bo'lardi
        //    (bo'shliq, maydonlar tartibi, son formati) va xesh HECH
        //    QACHON mos kelmasdi.
        var body = await ReadBodyAsync(ct).ConfigureAwait(false);

        var verification = verifier.Verify(Request.Headers.Authorization.ToString(), body.Span);

        if (!verification.IsValid)
        {
            // Manba IP yoki sarlavha qiymati LOGGA YOZILMAYDI: birinchisi
            // foydasiz, ikkinchisi esa hujumchining tokenini log'ga
            // ko'chirardi. Faqat SABAB turkumi yoziladi.
            LiveKitApiLog.WebhookRejected(logger, verification.Reason ?? "noma'lum");

            return Unauthorized();
        }

        try
        {
            // ══════════════════════════════════════════════════════════
            // IKKI QUVUR, BITTA KIRISH NUQTASI (SPEC-RECORDING-V2 §3.3)
            //
            // Avval YANGI (trek) ishlovchi. U hodisani o'ziniki deb
            // tanimasa AYNAN `Ignored` qaytaradi — o'shanda hodisa ESKI
            // ishlovchiga, hech qanday o'zgarishsiz uzatiladi.
            //
            // 🔴 TARTIB TESKARI BO'LMASLIGI KERAK. Eski ishlovchi
            //    `egress_id` bo'lgan HAR hodisani o'ziniki deb qabul
            //    qiladi (topolmasa "noma'lum egress" deb takror
            //    jurnaliga yozib qo'yadi) — ya'ni birinchi bo'lib
            //    yursa, trek egress'larining hodisalari yangi
            //    ishlovchiga UMUMAN yetib bormasdi.
            //
            // ⚠️ ESKI QUVURNING XULQI O'ZGARMAGAN: trek ishlovchisi
            //    o'ziniki bo'lmagan hodisaga tegmaydi va takror
            //    jurnalini ham band qilmaydi (shartnoma:
            //    `ITrackRecordingWebhookHandler`).
            // ══════════════════════════════════════════════════════════
            var outcome = await trackHandler.HandleAsync(body, ct).ConfigureAwait(false);

            if (outcome == RecordingWebhookOutcome.Ignored)
                outcome = await handler.HandleAsync(body, ct).ConfigureAwait(false);

            return Ok(new WebhookAck(true, outcome.ToString()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Klient (LiveKit) uzdi — qayta ishlash ma'nosiz.
            throw;
        }
        catch (Exception ex)
        {
            // ★ ATAYLAB KENG USHLASH (TelegramController bilan AYNI
            //   mulohaza): har qanday kutilmagan xato LiveKit'ni CHEKSIZ
            //   qayta yuborishga majbur qilmasin. Xato logga va Sentry'ga
            //   tushadi, holatni esa watchdog tiklaydi.
            LiveKitApiLog.WebhookFailed(logger, ex);

            return Ok(new WebhookAck(true, "Failed"));
        }
    }

    /// <summary>
    /// So'rov tanasini xom baytlarga o'qiydi.
    ///
    /// Hajm <see cref="RequestSizeLimitAttribute"/> bilan ALLAQACHON
    /// cheklangan (Kestrel darajasida), shuning uchun bu yerda ikkinchi
    /// hisoblagich yo'q — u faqat ikki xil chegara bo'lib chalkashtirardi.
    /// </summary>
    private async Task<ReadOnlyMemory<byte>> ReadBodyAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();

        await Request.Body.CopyToAsync(buffer, ct).ConfigureAwait(false);

        return buffer.ToArray();
    }
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). EventId makoni: <c>6630–6639</c>.
///
/// 🔴 TOKEN, TANA VA MANBA IP LOGGA YOZILMAYDI (sabab controller izohida).
/// </summary>
internal static partial class LiveKitApiLog
{
    [LoggerMessage(
        EventId = 6630,
        Level = LogLevel.Warning,
        Message = "LiveKit webhook: imzo tekshiruvidan o'tmadi. sabab={Reason}")]
    internal static partial void WebhookRejected(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 6631,
        Level = LogLevel.Error,
        Message = "LiveKit webhook ichida kutilmagan xato.")]
    internal static partial void WebhookFailed(ILogger logger, Exception exception);
}
