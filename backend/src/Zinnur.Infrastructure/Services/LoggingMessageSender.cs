using Microsoft.Extensions.Logging;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IMessageSender"/> ning VAQTINCHALIK amalga oshirilishi: xabarni
/// hech qayerga yubormaydi, faqat logga yozadi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN KERAK: Telegram boti FAZA 5.1 da yoziladi, navbat esa
/// hozir tayyor bo'lishi kerak. Yuboruvchisiz navbat "yuborilmagan"
/// xabarlar bilan to'lib borardi va uni sinab ko'rib bo'lmasdi.
///
/// ★ ALMASHTIRISH: FAZA 5.1 da <c>TelegramMessageSender</c> ro'yxatdan
/// o'tkaziladi va u AYNI kanal uchun bo'lgani sababli bu sinfning
/// o'rnini oladi (<c>OutboxDispatcher</c> kanal bo'yicha oxirgi
/// yuboruvchini tanlaydi). Port shartnomasi o'zgarmaydi.
///
/// ★ MAXFIYLIK: matn logga QISQARTIRIB yoziladi. Bu vaqtinchalik
/// ishlab chiqish qulayligi — haqiqiy yuboruvchi paydo bo'lgach bu sinf
/// bilan birga yo'qoladi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LoggingMessageSender(ILogger<LoggingMessageSender> logger) : IMessageSender
{
    /// <summary>Logga chiqadigan matnning eng katta uzunligi.</summary>
    private const int PreviewLength = 200;

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Telegram;

    /// <inheritdoc />
    public Task<MessageSendResult> SendAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        ct.ThrowIfCancellationRequested();

        SenderLog.Logged(
            logger,
            message.Id,
            message.TemplateKey,
            message.RecipientAddress ?? "-",
            Preview(message.Body));

        // Har doim muvaffaqiyat: bu yuboruvchining vazifasi — navbat oqimini
        // (Pending -> Sent) uchdan-uchgacha sinab ko'rish imkonini berish.
        return Task.FromResult(MessageSendResult.Ok);
    }

    private static string Preview(string body) =>
        body.Length <= PreviewLength ? body : body[..PreviewLength] + "…";
}

/// <summary>Manba-generatsiyali log metodi (CA1848).</summary>
internal static partial class SenderLog
{
    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Information,
        Message = "[NOTIFIKATSIYA] id={MessageId} tur={TemplateKey} manzil={Address} matn={Preview}")]
    internal static partial void Logged(
        ILogger logger, long messageId, string templateKey, string address, string preview);
}
