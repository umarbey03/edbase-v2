using Microsoft.Extensions.Logging;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Auth.Services;
using Zinnur.Application.Common.Exceptions;

namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// <see cref="ITelegramMiniAppAuth"/> ning amalga oshirilishi.
/// </summary>
public sealed class TelegramMiniAppAuth(
    ITelegramInitDataValidator validator,
    IAuthService auth,
    ILogger<TelegramMiniAppAuth> logger) : ITelegramMiniAppAuth
{
    /// <inheritdoc />
    public async Task<AuthResponse> AuthenticateAsync(
        string? initData, CancellationToken ct = default)
    {
        if (!validator.IsConfigured)
        {
            // 503, 500 EMAS: bu bizning bug'imiz emas — muhit sozlanmagan.
            // (`ISubmissionStorage` bilan bir xil qaror.)
            //
            // 🔴 MATN 2026-08-13 DA O'ZGARDI. Ilgari bu yerda "email va
            //    parol bilan kiring" deb yozilgan edi — endi bunday eshik
            //    YO'Q, ya'ni eski matn foydalanuvchini mavjud bo'lmagan
            //    ekranga yuborardi.
            //
            //    Zaxira yo'l ham qolmadi: telefon + kod oqimi HAM AYNI
            //    bot tokeniga tayanadi (kod Telegram orqali ketadi).
            //    Ya'ni token buzilsa PLATFORMAGA HECH KIM KIRA OLMAYDI.
            //    Aynan shu sababli tokenni muhit o'zgaruvchisi bilan
            //    ustidan yozish yo'li qo'shildi — `ZINNUR_TELEGRAM_*`,
            //    batafsil `docs/DEPLOY_UBUNTU.md`.
            throw new ServiceUnavailableException(
                "Telegram integratsiyasi sozlanmagan — hozircha tizimga kirib bo'lmaydi. "
                + "Administrator bilan bog'laning.");
        }

        var result = validator.Validate(initData);

        if (!result.IsValid)
        {
            // ★ ANIQ SABAB FAQAT LOGDA. Foydalanuvchiga yagona umumiy
            //   xabar boradi: "imzo mos kelmadi" va "muddati o'tgan" ni
            //   ajratib ko'rsatish hujumchiga qaysi urinish qanchalik
            //   yaqin ekanini aytib berardi.
            TelegramAuthLog.InitDataRejected(logger, result.Reason ?? "-");

            throw new UnauthorizedException(
                "Telegram ma'lumoti tasdiqlanmadi. Ilovani yopib, qaytadan oching.");
        }

        return await auth.LoginWithTelegramAsync(result.TelegramUserId, ct).ConfigureAwait(false);
    }
}

/// <summary>Manba-generatsiyali log metodlari (CA1848).</summary>
internal static partial class TelegramAuthLog
{
    [LoggerMessage(
        EventId = 6220,
        Level = LogLevel.Warning,
        Message = "Mini App initData rad etildi: sabab={Reason}")]
    internal static partial void InitDataRejected(ILogger logger, string reason);
}
