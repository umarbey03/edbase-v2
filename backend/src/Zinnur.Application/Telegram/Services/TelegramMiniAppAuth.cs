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
            throw new ServiceUnavailableException(
                "Telegram integratsiyasi sozlanmagan. Iltimos, email va parol bilan kiring.");
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
