using Zinnur.Application.Telegram;
using Zinnur.Application.Telegram.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ITelegramInitDataValidator"/> portining amalga oshirilishi.
///
/// ★ MANTIQ BU YERDA EMAS. Imzo algoritmi
/// <see cref="TelegramInitData.Verify"/> da — u sof funksiya va unit
/// testlar bilan qoplangan. Bu sinf faqat IKKI narsani beradi: bot tokeni
/// (sozlamalardan) va hozirgi vaqt (<c>TimeProvider</c> dan).
///
/// ★★ TOKEN HAR TEKSHIRUVDA QAYTA O'QILADI
/// (<see cref="IRuntimeOptions{TOptions}"/>, <c>IOptions&lt;T&gt;</c> EMAS).
/// Sabab aniq: <c>initData</c> imzosining kaliti — AYNAN bot tokeni. Token
/// panelda almashtirilgach, Telegram yangi kalit bilan imzolaydi; agar
/// tekshiruvchi eski tokenni ushlab qolsa, HAR Mini App kirishi 401 bilan
/// rad etilardi va o'quvchilar ilovaga umuman kira olmasdi.
///
/// HOLATSIZ — Singleton. Har so'rovda qayta yaratish hech narsa
/// tejamasdi va faqat ortiqcha allokatsiya bo'lardi.
/// </summary>
public sealed class TelegramInitDataValidator(
    IRuntimeOptions<TelegramOptions> options,
    TimeProvider clock) : ITelegramInitDataValidator
{
    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Current.BotToken);

    /// <inheritdoc />
    public TelegramInitDataResult Validate(string? initData)
    {
        // Kesim BIR MARTA olinadi: token va muddat chegarasi AYNI
        // sozlamalardan chiqishi kerak.
        var settings = options.Current;

        return TelegramInitData.Verify(
            initData,
            settings.BotToken,
            clock.GetUtcNow(),
            settings.InitDataMaxAge);
    }
}
