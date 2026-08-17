using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Telegram.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ITelegramCallbackAcknowledger"/> ning Telegram amalga oshirilishi
/// (<c>answerCallbackQuery</c>). Sabab va falsafa port izohida.
/// </summary>
public sealed class TelegramCallbackAcknowledger(
    IHttpClientFactory httpClientFactory,
    IRuntimeOptions<TelegramOptions> options,
    ILogger<TelegramCallbackAcknowledger> logger) : ITelegramCallbackAcknowledger
{
    /// <inheritdoc />
    public async Task AcknowledgeAsync(string callbackQueryId, string? toastText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callbackQueryId)) return;

        var settings = options.Current;
        if (string.IsNullOrWhiteSpace(settings.BotToken)) return;

        var buffer = new ArrayBufferWriter<byte>(128);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("callback_query_id", callbackQueryId);

            if (!string.IsNullOrWhiteSpace(toastText))
            {
                // Telegram'ning o'z chegarasi — 200 belgi. Undan uzuni
                // baribir RAD ETILADI, biz esa xato yutamiz (port izohi).
                writer.WriteString("text", toastText.Length <= 200 ? toastText : toastText[..200]);
            }

            writer.WriteEndObject();
        }

        try
        {
            using var content = new ByteArrayContent(buffer.WrittenSpan.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            var client = httpClientFactory.CreateClient(TelegramMessageSender.HttpClientName);
            var uri = new Uri(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/bot{settings.BotToken}/answerCallbackQuery");

            using var response = await client.PostAsync(uri, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                TelegramCallbackAckLog.Failed(logger, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // ★ ATAYLAB YUTILADI — port izohidagi qoida: asosiy javob
            // xabari baribir outbox orqali ketadi, faqat "⏳" belgisi
            // biroz uzoqroq turishi mumkin.
            TelegramCallbackAckLog.NetworkError(logger, ex);
        }
    }
}

/// <summary>Manba-generatsiyali log metodlari (CA1848).</summary>
internal static partial class TelegramCallbackAckLog
{
    [LoggerMessage(
        EventId = 6310,
        Level = LogLevel.Debug,
        Message = "answerCallbackQuery rad etildi: status={Status}")]
    internal static partial void Failed(ILogger logger, int status);

    [LoggerMessage(
        EventId = 6311,
        Level = LogLevel.Debug,
        Message = "answerCallbackQuery yuborilmadi (tarmoq xatosi, yutildi)")]
    internal static partial void NetworkError(ILogger logger, Exception exception);
}
