using System.Globalization;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// <see cref="IOutboxDispatcher"/> ning amalga oshirilishi.
///
/// TARTIB QAT'IY: xabar OLINADI (band qilinadi) → tezlik chegarasidan
/// ruxsat so'raladi → kanalga uzatiladi → natija bazaga yoziladi.
///
/// ★ HECH QACHON ISTISNO BILAN TO'XTAMAYDI (bekor qilishdan tashqari):
/// bitta buzuq xabar butun navbatni to'xtatib qo'ymasligi kerak. Port
/// shartnomasi buzilib sender istisno tashlasa ham, u vaqtinchalik xato
/// sifatida qayd etiladi va keyingi xabarga o'tiladi.
/// </summary>
public sealed class OutboxDispatcher : IOutboxDispatcher
{
    private readonly IOutboxStore _store;
    private readonly IMessageRateLimiter _rateLimiter;
    private readonly ILogger<OutboxDispatcher> _logger;

    /// <summary>
    /// Kanal → yuboruvchi. Ro'yxat BIR MARTA aylantiriladi: DI dan kelgan
    /// <c>IEnumerable</c> har chaqiruvda qayta hisoblanishi mumkin.
    /// </summary>
    private readonly Dictionary<NotificationChannel, IMessageSender> _senders;

    public OutboxDispatcher(
        IOutboxStore store,
        IEnumerable<IMessageSender> senders,
        IMessageRateLimiter rateLimiter,
        ILogger<OutboxDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(senders);

        _store = store;
        _rateLimiter = rateLimiter;
        _logger = logger;

        // Bir kanalga ikkita yuboruvchi ro'yxatdan o'tsa OXIRGISI g'olib
        // bo'ladi. Bu ataylab: FAZA 5.1 da Telegram yuboruvchisi qo'shilganda
        // vaqtinchalik log-yuboruvchi ustidan yozadi va DI ro'yxatidan
        // birontasini olib tashlash shart emas.
        _senders = [];
        foreach (var sender in senders)
            _senders[sender.Channel] = sender;
    }

    /// <inheritdoc />
    public async Task<OutboxDispatchResult> DispatchAsync(
        int batchSize, TimeSpan lease, CancellationToken ct = default)
    {
        var claimed = await _store.ClaimAsync(batchSize, lease, ct).ConfigureAwait(false);

        if (claimed.Count == 0)
            return default;

        var delivered = 0;
        var rejected = 0;

        // Chegaraga urilgandan keyingi qolgan xabarlar. `null` — hali urilmagan.
        List<long>? postponed = null;
        var postponeDelay = TimeSpan.Zero;

        foreach (var message in claimed)
        {
            ct.ThrowIfCancellationRequested();

            // Chegara allaqachon to'lgan bo'lsa qolganini ham darhol
            // qaytaramiz: keyingi xabarga ruxsat so'rash baribir rad etilardi,
            // lekin har biri uchun ortiqcha Redis chaqiruvi ketardi.
            if (postponed is not null)
            {
                postponed.Add(message.Id);
                continue;
            }

            var decision = await _rateLimiter
                .TryAcquireAsync(message.Channel, ct)
                .ConfigureAwait(false);

            if (!decision.Allowed)
            {
                postponed = [message.Id];
                postponeDelay = decision.RetryAfter;
                continue;
            }

            if (await SendOneAsync(message, ct).ConfigureAwait(false))
                delivered++;
            else
                rejected++;
        }

        if (postponed is not null)
        {
            await _store.PostponeAsync(postponed, postponeDelay, ct).ConfigureAwait(false);
            NotificationLog.RateLimited(_logger, postponed.Count, postponeDelay.TotalMilliseconds);
        }

        return new OutboxDispatchResult(delivered, rejected, postponed?.Count ?? 0);
    }

    /// <summary>Bitta xabarni yuboradi va natijani yozadi. <c>true</c> — yetkazildi.</summary>
    private async Task<bool> SendOneAsync(OutboxMessage message, CancellationToken ct)
    {
        if (!_senders.TryGetValue(message.Channel, out var sender))
        {
            // Konfiguratsiya xatosi: qayta urinish holatni o'zgartirmaydi.
            // Xabar darhol Failed bo'ladi va logda ANIQ sabab qoladi.
            await RejectAsync(
                message,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{message.Channel}' kanali uchun yuboruvchi ro'yxatdan o'tmagan."),
                retryable: false,
                ct).ConfigureAwait(false);

            return false;
        }

        MessageSendResult result;

        try
        {
            result = await sender.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // To'xtatilyapmiz — xabar band qilingan holda qoladi va
            // ko'rinmaslik muddati tugagach qaytadi.
            throw;
        }
        catch (Exception ex)
        {
            // Port shartnomasi "istisno tashlamaydi" deydi; bu — himoya
            // to'sig'i. Xato VAQTINCHALIK deb qaraladi: kutilmagan istisno
            // ko'pincha tarmoq yoki serializatsiya nosozligi bo'ladi.
            NotificationLog.SenderThrew(_logger, ex, message.Id);
            result = MessageSendResult.Retry(ex.GetType().Name);
        }

        if (result.Delivered)
        {
            await _store.MarkDeliveredAsync(message.Id, ct).ConfigureAwait(false);
            NotificationLog.Delivered(_logger, message.Id, message.TemplateKey);
            return true;
        }

        await RejectAsync(
            message,
            result.Reason ?? "Sabab ko'rsatilmagan.",
            result.Retryable,
            ct).ConfigureAwait(false);

        return false;
    }

    private async Task RejectAsync(
        OutboxMessage message, string reason, bool retryable, CancellationToken ct)
    {
        // `AttemptCount` — SHU urinishgacha bo'lgan yiqilishlar soni, ya'ni
        // joriy urinish (+1) hisobga olinadi.
        var failedAttempts = message.AttemptCount + 1;

        var retryAfter = retryable ? OutboxRetryPolicy.NextDelay(failedAttempts) : null;

        await _store
            .MarkRejectedAsync(message.Id, reason, retryAfter, ct)
            .ConfigureAwait(false);

        if (retryAfter is null)
            NotificationLog.GaveUp(_logger, message.Id, failedAttempts, reason);
        else
            NotificationLog.WillRetry(_logger, message.Id, failedAttempts, retryAfter.Value.TotalSeconds, reason);
    }
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848 — oddiy <c>LogInformation("...")</c>
/// har chaqiruvda massiv ajratadi va bokslash qiladi).
/// </summary>
internal static partial class NotificationLog
{
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Debug,
        Message = "Xabar yetkazildi: id={MessageId} tur={TemplateKey}")]
    internal static partial void Delivered(ILogger logger, long messageId, string templateKey);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Warning,
        Message = "Xabar yuborilmadi, qayta urinamiz: id={MessageId} urinish={Attempt} "
                  + "keyingisi={RetrySeconds}s sabab={Reason}")]
    internal static partial void WillRetry(
        ILogger logger, long messageId, int attempt, double retrySeconds, string reason);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Error,
        Message = "Xabar YAKUNIY yiqildi: id={MessageId} urinish={Attempt} sabab={Reason}")]
    internal static partial void GaveUp(ILogger logger, long messageId, int attempt, string reason);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Error,
        Message = "Yuboruvchi istisno tashladi (port shartnomasi buzilgan): id={MessageId}")]
    internal static partial void SenderThrew(ILogger logger, Exception exception, long messageId);

    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Information,
        Message = "Tezlik chegarasi: {Count} xabar {DelayMs} ms ga surildi")]
    internal static partial void RateLimited(ILogger logger, int count, double delayMs);
}
