using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Auth.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Kirish/chiqish. Controller YUPQA: validatsiya -> servis -> javob.
/// Biznes mantiq <see cref="IAuthService"/> ichida.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAuthService auth,
    IPhoneLoginService phone,
    ITelegramLoginService telegramLogin) : ControllerBase
{
    /// <summary>
    /// Kod topishga (brute force) qarshi rate-limit siyosatining nomi.
    ///
    /// Nom SHU YERDA e'lon qilinadi, `Program.cs` esa siyosatni AYNAN shu
    /// const bilan ro'yxatdan o'tkazadi. NEGA const: nom ikki joyda oddiy
    /// satr bo'lsa, atributni umuman unutish JIMGINA "cheklovsiz endpoint"
    /// beradi va buni hech kim sezmaydi. Aynan shu bo'lgan edi — siyosat
    /// e'lon qilingan, hech qayerga qo'llanmagan va bitta IP'dan 1500 ta
    /// kirish so'rovi to'siqsiz o'tgan.
    ///
    /// ⚠️ SIYOSAT NOMI `"auth"` BO'LIB QOLDI (parol oqimi olib tashlangan
    /// bo'lsa ham). Uni o'zgartirish `RateLimiting:Auth:*` konfiguratsiya
    /// kalitlarini ham, `docs/DEPLOY_UBUNTU.md` dagi qiymatlarni ham,
    /// prod'dagi `.env` ni ham birdaniga buzardi — ya'ni foyda nol,
    /// xavf esa real.
    ///
    /// 🔴 BU SIYOSAT YETARLI EMAS VA U YOLG'IZ ISHLAMAYDI. U IP bo'yicha
    /// bo'linadi (`Program.cs` dagi `FixedWindowByIp` izohi), reverse-proxy
    /// ortida esa HAMMA bitta bo'limga tushadi. Shuning uchun asosiy
    /// himoya RAQAM bo'yicha va use-case ichida: `IPhoneLoginCodeStore`
    /// (60 s qayta yuborish oynasi, sutkalik chegara, urinishlar cheklovi).
    /// </summary>
    public const string LoginRateLimitPolicy = "auth";

    /// <summary>
    /// Token yangilash uchun ALOHIDA siyosat (kengroq budjet).
    ///
    /// NEGA KIRISH BILAN BIR XIL EMAS — tahdid modeli boshqa. Parolni
    /// taxmin qilib bo'ladi, refresh tokenni esa yo'q: u HS256 bilan
    /// imzolangan va `TokenVersion` ga bog'langan. Bu yerdagi cheklov
    /// faqat bazani bekorga urishga qarshi.
    ///
    /// Aksincha, umumiy budjet ZARAR qilardi: bitta maktab bitta NAT IP
    /// orqasida turadi va kirish tokeni har 15 daqiqada yangilanadi.
    /// 100 o'quvchi ≈ 7 yangilash/daqiqa, ustiga dars boshidagi kirishlar —
    /// birinchi dars soatidayoq hamma tizimdan uchib chiqardi.
    /// </summary>
    public const string RefreshRateLimitPolicy = "auth-refresh";

    /// <summary>
    /// Chipta holatini SO'RAB TURISH uchun alohida siyosat (2026-08-28).
    ///
    /// ★ NIMA UCHUN KIRISH SIYOSATI TO'G'RI KELMAYDI: bu endpoint
    /// so'rov emas, KUTISH usuli — brauzer uni bir necha soniyada bir
    /// chaqiradi. 20/daqiqalik budjet bilan foydalanuvchi botni ochib
    /// ulgurmasidan 429 olardi.
    ///
    /// 🔴 KENG BUDJET XAVFSIZ, CHUNKI ENDPOINT SIR OCHMAYDI: 128 bitlik
    /// chiptasiz u faqat "yo'q" deydi. Batafsil — `Program.cs` dagi
    /// `authPollPermitLimit` izohi.
    /// </summary>
    public const string PollRateLimitPolicy = "auth-poll";

    // ================================================================ telefon
    //
    // ⚠️ `POST /api/v1/auth/login` (email + parol) OLIB TASHLANDI —
    //    2026-08-13, loyiha egasining qarori (talab R26). O'rniga ikki
    //    bosqichli telefon oqimi. Sabab va tahdid tahlili:
    //    `IPhoneLoginService` izohida.

    /// <summary>
    /// 1-BOSQICH: telefon raqamiga bir martalik kod so'rash.
    ///
    /// Tana: <c>{ "phone": "+998901234567" }</c> — xom ko'rinish ham
    /// bo'ladi, normalizatsiya serverda.
    ///
    /// 🔴 JAVOB SABABNI OCHIQ AYTADI (2026-08-29 dan) — ILGARI AYTMASDI.
    ///
    /// Ilgari javob har doim 200 va har doim bir xil edi: maqsad —
    /// endpoint "bu raqam markazda bormi?" degan savolga javob beradigan
    /// qidiruv vositasiga aylanmasin (hisob sanash).
    ///
    /// Loyiha egasi 2026-08-29 da ayirboshlash tushuntirilgandan keyin
    /// ochiq xabarni talab qildi: o'quvchi "kod kelmadi" deb kutardi va
    /// sababni bilmasdi, bot esa AYNI holatda allaqachon ochiq aytardi.
    ///
    /// ★ HISOB SANASHGA QARSHI HIMOYA SAQLANDI, LEKIN ENDI YAGONA:
    ///   raqam bo'yicha kvota profil qidiruvidan OLDIN ishlaydi
    ///   (`PhoneLoginService.RequestCodeAsync`). Har raqamni tekshirish
    ///   uchun bir daqiqa kerak — ro'yxatni skanerlash amalda imkonsiz.
    ///
    /// <c>400</c> — raqam ro'yxatda yo'q · profil faol emas ·
    /// Telegram bog'lanmagan (sabab `errors.phone` da) ·
    /// <c>429</c> — kvota (`Retry-After` sarlavhasi bilan) ·
    /// <c>503</c> — Telegram sozlanmagan, kod yuboradigan kanal yo'q.
    /// </summary>
    [HttpPost("phone/request-code")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [ProducesResponseType<PhoneCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PhoneCodeResponse>> RequestPhoneCode(
        [FromBody] PhoneCodeRequest request, CancellationToken ct) =>
        Ok(await phone.RequestCodeAsync(request, ct));

    /// <summary>
    /// 2-BOSQICH: kodni tasdiqlash va sessiya ochish.
    ///
    /// Tana: <c>{ "phone": "...", "code": "123456" }</c>.
    /// Javob — mavjud <see cref="AuthResponse"/> bilan AYNAN bir xil
    /// (Mini App oqimi bilan ham bir xil), ya'ni klient tokenlarni
    /// odatdagidek saqlaydi.
    ///
    /// <c>401</c> — kod xato yoki muddati o'tgan (ikkalasi uchun AYNI
    /// matn) · <c>403</c> — profil faol emas · <c>429</c> — urinishlar
    /// tugadi, yangi kod kerak.
    /// </summary>
    [HttpPost("phone/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> VerifyPhoneCode(
        [FromBody] PhoneVerifyRequest request, CancellationToken ct) =>
        Ok(await phone.VerifyAsync(request, ct));

    // ================================================================ bot
    //
    // BOT ORQALI KIRISH — DEEP-LINK OQIMI (2026-08-28).
    //
    // Uch qadam: `start` -> (bot) -> `status` -> `verify`. Tahdid tahlili
    // va nima uchun kod baribir so'ralishi — `ITelegramLoginService`
    // izohida.
    //
    // ⚠️ TELEFON OQIMI (yuqorida) OLIB TASHLANMADI — u ZAXIRA yo'l
    //    bo'lib qoladi: bot bloklangan, havola ochilmagan yoki Telegram
    //    boshqa qurilmada bo'lgan holatlar bor.

    /// <summary>
    /// 1-QADAM: bir martalik chipta ochadi va bot havolasini qaytaradi.
    ///
    /// Tanasi YO'Q — foydalanuvchidan hech narsa so'ralmaydi.
    ///
    /// <c>503</c> — bot tokeni yoki bot nomi sozlanmagan.
    /// </summary>
    [HttpPost("telegram/start")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [ProducesResponseType<TelegramLoginStartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TelegramLoginStartResponse>> StartTelegramLogin(
        CancellationToken ct) =>
        Ok(await telegramLogin.StartAsync(ct));

    /// <summary>
    /// 2-QADAM: chipta holati. Brauzer buni bir necha soniyada bir so'raydi.
    ///
    /// 🔴 HAR DOIM 200 — noma'lum yoki eskirgan chipta ham
    /// <c>"yoq"</c> holati bilan qaytadi. Sabab
    /// <see cref="ITelegramLoginService.StatusAsync"/> izohida.
    /// </summary>
    [HttpGet("telegram/status")]
    [AllowAnonymous]
    [EnableRateLimiting(PollRateLimitPolicy)]
    [ProducesResponseType<TelegramLoginStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TelegramLoginStatusResponse>> TelegramLoginStatus(
        [FromQuery] string? token, CancellationToken ct) =>
        Ok(await telegramLogin.StatusAsync(token, ct));

    /// <summary>
    /// 3-QADAM: bot yuborgan kodni tasdiqlash va sessiya ochish.
    ///
    /// Tana: <c>{ "token": "...", "code": "123456" }</c>.
    /// Javob — mavjud <see cref="AuthResponse"/> bilan AYNAN bir xil.
    ///
    /// <c>401</c> — kod xato yoki chipta muddati o'tgan ·
    /// <c>403</c> — profil faol emas · <c>429</c> — urinishlar tugadi.
    /// </summary>
    [HttpPost("telegram/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> VerifyTelegramLogin(
        [FromBody] TelegramLoginVerifyRequest request, CancellationToken ct) =>
        Ok(await telegramLogin.VerifyAsync(request, ct));

    /// <summary>Kirish tokenini yangilash.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RefreshRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken ct) =>
        Ok(await auth.RefreshAsync(request.RefreshToken, ct));

    /// <summary>
    /// Barcha qurilmalardan chiqish.
    /// TokenVersion oshiriladi — mavjud tokenlarning HAMMASI darhol bekor bo'ladi.
    /// (Eski tizimda "chiqish" faqat cookie'ni o'chirardi, token 14 kun yashardi.)
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAllAsync(CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Joriy foydalanuvchi ma'lumoti.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) =>
        Ok(await auth.GetCurrentAsync(CurrentUserId, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
