using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Telegram.Dtos;
using Zinnur.Application.Telegram.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Telegram bot webhook'i va Mini App kirishi (FAZA 5.1).
///
/// Controller YUPQA: sirni tekshiradi → servisni chaqiradi → javob beradi.
/// Butun mantiq <see cref="ITelegramUpdateHandler"/> va
/// <see cref="ITelegramMiniAppAuth"/> ichida.
///
/// ★★ SIR HAR SO'ROVDA QAYTA O'QILADI (<see cref="IRuntimeOptions{TOptions}"/>).
/// Webhook siri paneldan almashtirilishi mumkin; agar u ishga tushish
/// paytida qotib qolsa, admin sirni yangilab `setWebhook` ni qayta
/// chaqirgach, Telegram YANGI sir bilan kelardi-yu server ESKISI bilan
/// solishtirib har yangilanishni 403 bilan rad etardi — ya'ni bot jimgina
/// ishlamay qolardi.
/// </summary>
[ApiController]
[Route("api/v1/telegram")]
[Produces("application/json")]
public sealed class TelegramController(
    IRuntimeOptions<TelegramOptions> options,
    ITelegramUpdateHandler updates,
    ITelegramMiniAppAuth miniApp,
    ILogger<TelegramController> logger) : ControllerBase
{
    /// <summary>Telegram sirni AYNAN shu sarlavhada qaytaradi (Bot API hujjati).</summary>
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    // ================================================================= webhook

    /// <summary>
    /// Telegram yangilanishini qabul qiladi.
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// ★ JAVOB QOIDASI — TELEGRAM'GA DOIM 200
    ///
    /// Telegram 200 dan boshqa har javobda AYNI yangilanishni qayta-qayta
    /// yuboradi. Shuning uchun tushunilmagan yangilanish ham, ichki xato
    /// ham 200 bilan yopiladi (xato logga tushadi). YAGONA istisno —
    /// sir tekshiruvi: u Telegram'dan kelmagan so'rov, unga javob
    /// berishning ma'nosi yo'q.
    ///
    /// ★ OG'IR ISH BU YERDA BAJARILMAYDI. Javob xabari navbatga yoziladi,
    /// yuborishni fon worker'i qiladi. Webhook ichida `sendMessage`
    /// chaqirilsa (eski tizimda shunday edi) Telegram javobni kutolmay
    /// yangilanishni qayta yuborardi va bitta hodisa ikki marta ishlanardi.
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Webhook(
        [FromBody] TelegramUpdateDto update, CancellationToken ct)
    {
        // So'rov boshida BIR MARTA: "sozlanganmi?" tekshiruvi va sirni
        // solishtirish AYNI kesimdan bo'lishi kerak.
        var settings = options.Current;

        // ★ SIR SOZLANMAGAN BO'LSA ENDPOINT UMUMAN YO'Q.
        //
        // Ochiq qolgandan ko'ra o'chiq bo'lgani xavfsiz: sirsiz webhook —
        // bu "istalgan odam qalbaki kontakt yuborishi mumkin" degani.
        // 404 (403 emas) ataylab: skanerga endpoint borligini ham
        // bildirmaymiz.
        if (!settings.IsConfigured)
            return NotFound();

        if (!HasValidSecret(settings.WebhookSecret))
        {
            // Manba IP yoki sarlavha qiymati LOGGA YOZILMAYDI: birinchisi
            // foydasiz (Telegram IP'lari o'zgaradi), ikkinchisi esa
            // hujumchining taxminini log'ga ko'chirardi.
            TelegramApiLog.WebhookSecretRejected(logger);

            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var outcome = await updates.HandleAsync(update, ct);

            return Ok(new WebhookAck(true, outcome.ToString()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Klient (Telegram) uzdi — qayta ishlash ma'nosiz.
            throw;
        }
        catch (Exception ex)
        {
            // ★ ATAYLAB KENG USHLASH: har qanday kutilmagan xato
            //   Telegram'ni CHEKSIZ qayta yuborishga majbur qilmasin.
            //   Xato logga (va Sentry'ga) tushadi, foydalanuvchi esa
            //   kerak bo'lsa qaytadan urinadi.
            TelegramApiLog.WebhookFailed(logger, ex, update?.UpdateId ?? 0);

            return Ok(new WebhookAck(true, "Failed"));
        }
    }

    /// <summary>
    /// Sirni DOIMIY VAQTDA solishtiradi.
    ///
    /// ★ Oddiy <c>==</c> birinchi farq qilgan baytda to'xtaydi va javob
    /// vaqtidagi farq orqali sirni bayt-bayt topish mumkin bo'lardi
    /// (timing attack). Uzunlik farqi ham shu yerda sizib chiqmaydi:
    /// <c>FixedTimeEquals</c> mos kelmagan uzunlikda darhol <c>false</c>
    /// qaytaradi, lekin bu OLDINDAN ma'lum (sarlavha uzunligini hujumchi
    /// o'zi tanlaydi) — sirning MAZMUNI haqida hech narsa aytmaydi.
    /// </summary>
    private bool HasValidSecret(string expected)
    {
        if (!Request.Headers.TryGetValue(SecretHeader, out var values))
            return false;

        var provided = values.ToString();

        if (string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }

    // ================================================================= Mini App

    /// <summary>
    /// Telegram Mini App orqali kirish.
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// ★ MAVJUD AUTH OQIMI QAYTA ISHLATILADI. Bu endpoint faqat
    /// `initData` imzosini tekshiradi; token, `ver` (sessiya versiyasi)
    /// va rol tekshiruvi `IAuthService` da — ya'ni email+parol bilan AYNI
    /// joyda. Javob ham AYNI `AuthResponse`: frontend uchun kirish yo'li
    /// bitta, faqat "kim ekanini" isbotlash usuli boshqacha.
    ///
    /// ★ TELEFON RAQAM BU YERDA UMUMAN QABUL QILINMAYDI. Bog'lash faqat
    /// botdagi «Raqamni ulashish» tugmasi orqali bo'ladi (audit: X-1b —
    /// eski tizimdagi qo'lda telefon kiritish oynasi).
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    /// <response code="200">Kirish muvaffaqiyatli — tokenlar qaytadi.</response>
    /// <response code="401">`initData` imzosi yaroqsiz yoki muddati o'tgan.</response>
    /// <response code="403">O'quvchi emas yoki profil faol emas.</response>
    /// <response code="409">Telegram akkaunt hali profilga bog'lanmagan.</response>
    /// <response code="503">Telegram integratsiyasi sozlanmagan.</response>
    [HttpPost("mini-app/auth")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthController.RefreshRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AuthResponse>> MiniAppAuth(
        [FromBody] MiniAppAuthRequest request, CancellationToken ct) =>
        Ok(await miniApp.AuthenticateAsync(request?.InitData, ct));
}

/// <summary>
/// Webhook javobi. Telegram tanani O'QIMAYDI — bu faqat qo'lda tekshirish
/// (`curl`) va integratsiya testlari uchun: qaysi qaror qabul qilinganini
/// ko'rsatadi.
/// </summary>
/// <param name="Ok">Har doim <c>true</c> (izoh: <see cref="TelegramController.Webhook"/>).</param>
/// <param name="Outcome">
/// <c>TelegramUpdateOutcome</c> nomi: <c>Linked</c>, <c>ContactMismatch</c>,
/// <c>Duplicate</c> va h.k.
/// </param>
public sealed record WebhookAck(bool Ok, string Outcome);

/// <summary>Manba-generatsiyali log metodlari (CA1848).</summary>
internal static partial class TelegramApiLog
{
    [LoggerMessage(
        EventId = 6240,
        Level = LogLevel.Warning,
        Message = "Telegram webhook: sir mos kelmadi, so'rov rad etildi.")]
    internal static partial void WebhookSecretRejected(ILogger logger);

    [LoggerMessage(
        EventId = 6241,
        Level = LogLevel.Error,
        Message = "Telegram webhook ichida kutilmagan xato: update={UpdateId}")]
    internal static partial void WebhookFailed(ILogger logger, Exception exception, long updateId);
}
