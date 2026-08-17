using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Telegram;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IMessageSender"/> ning TELEGRAM amalga oshirilishi
/// (<c>sendMessage</c>, <c>parse_mode=HTML</c>).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN KUTUBXONA (Telegram.Bot) QO'SHILMADI
///
/// Bizga BITTA metod kerak — <c>sendMessage</c>. Kutubxona esa butun Bot API
/// yuzasini, o'zining `long polling` sikli va yangilanish modellarini olib
/// keladi hamda Telegram har API versiyasida yangilanishni talab qiladi.
/// Bitta `POST` uchun bu juda katta narx. Loyihada AYNI tanlov allaqachon
/// qilingan: <c>R2SubmissionStorage</c> AWS SDK o'rniga SigV4 ni qo'lda
/// yozadi, <c>LiveKitTokenService</c> esa JWT ni qo'lda imzolaydi.
///
/// ★ JSON QO'LDA YOZILADI (<c>Utf8JsonWriter</c>). Sabab: Telegram maydon
/// nomlari `snake_case`, bizning global JSON sozlamamiz esa camelCase.
/// Reflektsiyaga tayanish global sozlama o'zgargan kuni butun botni
/// JIMGINA buzardi — payload yuborilaverardi, faqat Telegram uni tushunmay
/// qo'yardi. Qo'lda yozilgan JSON hech qanday sozlamaga bog'liq emas.
///
/// ★ MATN QAYTA ISHLANMAYDI. <c>Body</c> — yuborishga TAYYOR matn
/// (`IMessageSender` shartnomasi). Bu yerda escape QILINMAYDI: shablonning
/// o'z `<b>` teglari o'quvchi ekranida so'zma-so'z ko'rinib qolardi.
///
/// ★ SIR HECH QAYERGA CHIQMAYDI. Bot tokeni so'rov URL'ining ICHIDA
/// (`/bot<token>/sendMessage`), shuning uchun URL logga, xato matniga yoki
/// Sentry'ga HECH QACHON yozilmaydi; tashqi manbadan kelgan har matn
/// <see cref="Redact"/> dan o'tadi.
///
/// ★★ TOKEN HAR YUBORISHDA QAYTA O'QILADI (<see cref="IRuntimeOptions{TOptions}"/>).
/// Ilgari u konstruktorda olinib, SINGLETON yuboruvchiga qotib qolardi:
/// token o'g'irlanib, panelda almashtirilganda ham bot ESKI (endi bekor
/// qilingan) token bilan urinaverardi va har xabar 401 bilan qaytardi.
/// Qiymatlar <see cref="SendAsync"/> boshida BIR MARTA olinadi va pastga
/// uzatiladi — aks holda URL bir tokendan, `Redact` esa boshqasidan
/// yasalib, sir logga sizib chiqishi mumkin edi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TelegramMessageSender(
    IHttpClientFactory httpClientFactory,
    IRuntimeOptions<TelegramOptions> options,
    ILogger<TelegramMessageSender> logger,
    TimeProvider clock) : IMessageSender
{
    /// <summary>Nomlangan HTTP klient (timeout DI'da sozlanadi).</summary>
    public const string HttpClientName = "zinnur-telegram";

    /// <summary>
    /// «Flood wait» tugash vaqti (UTC tick). <c>0</c> — chegara yo'q.
    ///
    /// ★ NIMA UCHUN: Telegram 429 bilan birga <c>retry_after</c> beradi —
    /// "shuncha sekund umuman murojaat qilma". Uni e'tiborsiz qoldirsak
    /// worker keyingi xabarlarni yuboraverib, chegarani UZAYTIRADI va
    /// bot vaqtinchalik bloklanishi mumkin.
    ///
    /// Muddat <c>MessageSendResult</c> orqali uzatilmaydi (shartnomada
    /// bunday maydon yo'q va uni o'zgartirish taqiqlangan), shuning uchun
    /// TO'SIQ shu yerda: muddat tugagunicha yuboruvchi Telegram'ga UMUMAN
    /// so'rov yubormaydi va darhol <c>Retry</c> qaytaradi. Xabar navbatda
    /// qoladi va <c>OutboxRetryPolicy</c> jadvali bo'yicha qaytadi.
    ///
    /// Bu instansiya ichidagi (best-effort) himoya: instansiyalararo umumiy
    /// chegara <c>IMessageRateLimiter</c> (Redis) ning ishi.
    /// </summary>
    private long _floodWaitUntilTicks;

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Telegram;

    /// <inheritdoc />
    public async Task<MessageSendResult> SendAsync(
        OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        ct.ThrowIfCancellationRequested();

        // AMALNING BOSHIDA BIR MARTA — izoh sinf tepasida.
        var settings = options.Current;

        if (string.IsNullOrWhiteSpace(settings.BotToken))
        {
            // Bu holatga normal oqimda tushilmaydi (yuboruvchi faqat
            // sozlangan bo'lsa ro'yxatdan o'tadi), lekin qayta urinish
            // baribir hech narsani o'zgartirmasdi.
            return MessageSendResult.Permanent("Telegram bot tokeni sozlanmagan.");
        }

        // ── Manzil ───────────────────────────────────────────────────────
        if (!long.TryParse(
                message.RecipientAddress,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var chatId)
            || chatId == 0)
        {
            // Yaroqsiz `chat_id` — qayta urinish holatni o'zgartirmaydi.
            return MessageSendResult.Permanent("Telegram chat_id yaroqsiz.");
        }

        if (message.Body.Length > NotificationText.MaxBodyLength)
            return MessageSendResult.Permanent("Xabar matni Telegram chegarasidan uzun.");

        // ── Flood wait ───────────────────────────────────────────────────
        if (TryGetFloodWait(out var remaining))
        {
            TelegramSendLog.FloodWaitActive(logger, message.Id, remaining.TotalSeconds);

            return MessageSendResult.Retry(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Telegram flood-wait davom etmoqda ({remaining.TotalSeconds:F0}s)."));
        }

        var payload = BuildPayload(
            chatId, message.Body, TelegramTemplates.MarkupFor(message.TemplateKey),
            message.CallbackData, settings);

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;

        try
        {
            // ⚠️ URI tokenni O'Z ICHIGA OLADI — u hech qayerga yozilmaydi.
            response = await client.PostAsync(BuildUri(settings), content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Tarmoq nosozligi — VAQTINCHALIK.
            TelegramSendLog.NetworkError(logger, ex, message.Id);

            return MessageSendResult.Retry("Telegram'ga ulanib bo'lmadi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout (bekor qilish EMAS — uni chaqiruvchi hal qiladi).
            TelegramSendLog.Timeout(logger, ex, message.Id);

            return MessageSendResult.Retry("Telegram javob bermadi (timeout).");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var (ok, description, retryAfter) = ParseResponse(body);

            if (response.IsSuccessStatusCode && ok)
                return MessageSendResult.Ok;

            var status = (int)response.StatusCode;

            // 429 → keyingi so'rovlarni ham to'samiz (yuqoridagi izoh).
            if (response.StatusCode == HttpStatusCode.TooManyRequests && retryAfter is { } seconds)
                RegisterFloodWait(seconds);

            var safeDescription = Redact(description, settings);

            // 2xx bo'lsa-yu `ok: false` bo'lsa — Telegram shartnomasi buzilgan.
            // Xaritalash uni muvaffaqiyat deb qaytarardi, shuning uchun
            // alohida ushlaymiz.
            if (response.IsSuccessStatusCode)
            {
                TelegramSendLog.Rejected(logger, message.Id, status, safeDescription);

                return MessageSendResult.Permanent(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Telegram ok=false qaytardi. {safeDescription}"));
            }

            var result = TelegramErrorMap.FromStatus(status, safeDescription, retryAfter);

            if (result.Retryable)
                TelegramSendLog.TemporaryFailure(logger, message.Id, status, safeDescription);
            else
                TelegramSendLog.Rejected(logger, message.Id, status, safeDescription);

            return result;
        }
    }

    // ================================================================= payload

    /// <summary>
    /// <c>sendMessage</c> tanasini yozadi.
    ///
    /// ★ TUGMA <c>TemplateKey</c> BO'YICHA tanlanadi: navbat yozuvida
    /// tugma uchun maydon yo'q va <c>IMessageSender</c> shartnomasini
    /// o'zgartirish taqiqlangan. Bu ayni paytda to'g'ri taqsimot ham —
    /// "raqamni ulash tugmasi" TELEGRAM'ning ko'rinish tafsiloti, use-case
    /// esa faqat xabar TURINI biladi (izoh: <c>TelegramTemplates</c>).
    /// </summary>
    private static byte[] BuildPayload(
        long chatId, string text, TelegramMarkup markup, string? callbackData, TelegramOptions settings)
    {
        var buffer = new ArrayBufferWriter<byte>(text.Length + PayloadOverhead);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteNumber("chat_id", chatId);
            writer.WriteString("text", text);
            writer.WriteString("parse_mode", "HTML");

            // Havola oldindan ko'rinishi O'CHIRILADI: eslatma xabaridagi
            // havola ostiga chiqadigan katta karta xabarni ekranda
            // ikki barobar uzaytirardi. Maydon Bot API 7.0 da "eskirgan"
            // deb belgilangan, lekin ORQAGA MOSLIK uchun ishlashda davom
            // etadi va u lokal `telegram-bot-api` serverida ham bor —
            // yangi `link_preview_options` esa faqat yangi serverda.
            writer.WriteBoolean("disable_web_page_preview", true);

            WriteMarkup(writer, markup, callbackData, settings);

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteMarkup(
        Utf8JsonWriter writer, TelegramMarkup markup, string? callbackData, TelegramOptions settings)
    {
        switch (markup)
        {
            case TelegramMarkup.RequestContact:
                writer.WriteStartObject("reply_markup");
                writer.WriteStartArray("keyboard");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("text", ShareContactButton);

                // ★ BUTUN HIMOYANING KIRISH NUQTASI: telefon FAQAT shu
                //   tugma orqali keladi. Telegram raqamning AYNAN shu
                //   akkauntga tegishli ekanini kafolatlaydi va qo'lda
                //   yozish imkoni umuman bo'lmaydi.
                writer.WriteBoolean("request_contact", true);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndArray();
                writer.WriteBoolean("resize_keyboard", true);

                // Bosilgandan keyin klaviatura yashiriladi — ekran ostida
                // abadiy osilib turmasin.
                writer.WriteBoolean("one_time_keyboard", true);
                writer.WriteEndObject();
                break;

            case TelegramMarkup.OpenApp when !string.IsNullOrWhiteSpace(settings.MiniAppUrl):
                writer.WriteStartObject("reply_markup");
                writer.WriteStartArray("inline_keyboard");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("text", OpenAppButton);
                writer.WriteStartObject("web_app");
                writer.WriteString("url", settings.MiniAppUrl);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;

            case TelegramMarkup.RemoveKeyboard:
                writer.WriteStartObject("reply_markup");
                writer.WriteBoolean("remove_keyboard", true);
                writer.WriteEndObject();
                break;

            // ★ USTOZ KUNLIK TASDIQLASH (2026-08-17) — sabab
            // `TelegramTemplates.EncodeButtons` izohida: qatorlar/tugmalar
            // ayni `CallbackData` ichida kodlangan, chunki har checkin/offer
            // uchun `callback_data` BOSHQA-BOSHQA (dinamik). `CallbackData`
            // bo'sh bo'lsa — tugmasiz yuboriladi (matn baribir yetib borsin).
            case TelegramMarkup.InlineButtons when !string.IsNullOrWhiteSpace(callbackData):
                WriteInlineButtons(writer, callbackData);
                break;

            // `OpenApp` bo'lsa-yu manzil sozlanmagan bo'lsa — tugmasiz
            // yuboriladi. MATN baribir yetib borishi kerak: `web_app` uchun
            // `https` shart va `http` bo'lsa Telegram BUTUN xabarni 400
            // bilan rad etardi.
            case TelegramMarkup.None:
            case TelegramMarkup.OpenApp:
            case TelegramMarkup.InlineButtons:
            default:
                break;
        }
    }

    /// <summary>
    /// <see cref="TelegramTemplates.EncodeButtons"/> bilan kodlangan
    /// matnni <c>inline_keyboard</c> ga aylantiradi (kodlash formati o'sha
    /// metod izohida).
    /// </summary>
    private static void WriteInlineButtons(Utf8JsonWriter writer, string encoded)
    {
        writer.WriteStartObject("reply_markup");
        writer.WriteStartArray("inline_keyboard");

        foreach (var row in encoded.Split(RowSeparator))
        {
            if (row.Length == 0) continue;

            writer.WriteStartArray();

            foreach (var button in row.Split(ButtonSeparator))
            {
                var parts = button.Split(LabelDataSeparator, 2);
                if (parts.Length != 2) continue;

                writer.WriteStartObject();
                writer.WriteString("text", parts[0]);
                writer.WriteString("callback_data", parts[1]);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Qatorlar orasidagi ajratgich (<see cref="TelegramTemplates.EncodeButtons"/> bilan bir xil).</summary>
    private const char RowSeparator = '\n';

    /// <summary>Bitta qatordagi tugmalar orasidagi ajratgich.</summary>
    private const char ButtonSeparator = '\t';

    /// <summary>Tugma matni va <c>callback_data</c>si orasidagi ajratgich (ASCII Unit Separator, 0x1F).</summary>
    private const char LabelDataSeparator = '\u001F';

    private static Uri BuildUri(TelegramOptions settings) =>
        new($"{settings.ApiBaseUrl.TrimEnd('/')}/bot{settings.BotToken}/sendMessage");

    // ================================================================= javob

    /// <summary>
    /// Telegram javobidan <c>ok</c>, <c>description</c> va
    /// <c>parameters.retry_after</c> ni ajratadi.
    ///
    /// Buzuq JSON ISTISNO TASHLAMAYDI: javobni o'qib bo'lmasa ham holat
    /// kodi bor va qaror shunga qarab qabul qilinadi.
    /// </summary>
    private static (bool Ok, string? Description, int? RetryAfter) ParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (false, null, null);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return (false, null, null);

            var ok = root.TryGetProperty("ok", out var okElement)
                     && okElement.ValueKind == JsonValueKind.True;

            var description = root.TryGetProperty("description", out var descriptionElement)
                              && descriptionElement.ValueKind == JsonValueKind.String
                ? descriptionElement.GetString()
                : null;

            int? retryAfter = null;

            if (root.TryGetProperty("parameters", out var parameters)
                && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("retry_after", out var retry)
                && retry.ValueKind == JsonValueKind.Number
                && retry.TryGetInt32(out var seconds))
            {
                retryAfter = seconds;
            }

            return (ok, description, retryAfter);
        }
        catch (JsonException)
        {
            return (false, null, null);
        }
    }

    // ================================================================= flood wait

    private bool TryGetFloodWait(out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        var until = Interlocked.Read(ref _floodWaitUntilTicks);
        if (until == 0) return false;

        var now = clock.GetUtcNow().UtcTicks;
        if (now >= until) return false;

        remaining = TimeSpan.FromTicks(until - now);
        return true;
    }

    /// <summary>Muddatni FAQAT UZAYTIRADI (parallel yuborishlar bir-birini qisqartirmasin).</summary>
    private void RegisterFloodWait(int seconds)
    {
        var clamped = Math.Clamp(seconds, 1, MaxFloodWaitSeconds);
        var until = clock.GetUtcNow().AddSeconds(clamped).UtcTicks;

        long current;

        do
        {
            current = Interlocked.Read(ref _floodWaitUntilTicks);
            if (current >= until) return;
        }
        while (Interlocked.CompareExchange(ref _floodWaitUntilTicks, until, current) != current);

        TelegramSendLog.FloodWaitRegistered(logger, clamped);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// Tashqaridan kelgan matndan bot tokenini olib tashlaydi.
    ///
    /// Telegram tavsifida token bo'lishi kutilmaydi, lekin bu MATN log'ga,
    /// bazadagi <c>LastError</c> ga va Sentry'ga tushadi. Bir marta sizib
    /// chiqqan tokenni qaytarib bo'lmaydi, tekshiruv esa arzon.
    /// </summary>
    private static string Redact(string? value, TelegramOptions settings)
    {
        if (string.IsNullOrEmpty(value)) return "Tavsif yo'q.";

        return string.IsNullOrEmpty(settings.BotToken)
            ? value
            : value.Replace(settings.BotToken, "[Filtered]", StringComparison.Ordinal);
    }

    /// <summary>Klaviatura tugmasidagi matn.</summary>
    private const string ShareContactButton = "📱 Raqamni ulashish";

    /// <summary>Mini App'ni ochuvchi tugma matni.</summary>
    private const string OpenAppButton = "🚀 Ilovani ochish";

    /// <summary>JSON'ning matndan tashqari qismi uchun taxminiy zaxira.</summary>
    private const int PayloadOverhead = 512;

    /// <summary>
    /// Flood-wait ning eng katta qiymati (sekund). Telegram nazariy jihatdan
    /// juda katta son berishi mumkin; bir soatdan uzoq to'siq esa navbatni
    /// butunlay o'ldirardi — bunday holat operator aralashuvini talab qiladi.
    /// </summary>
    private const int MaxFloodWaitSeconds = 3600;
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848).
/// ★ URL, bot tokeni va webhook siri BU YERDA YO'Q va hech qachon bo'lmaydi.
/// </summary>
internal static partial class TelegramSendLog
{
    [LoggerMessage(
        EventId = 6300,
        Level = LogLevel.Warning,
        Message = "Telegram'ga ulanish xatosi: xabar={MessageId}")]
    internal static partial void NetworkError(ILogger logger, Exception exception, long messageId);

    [LoggerMessage(
        EventId = 6301,
        Level = LogLevel.Warning,
        Message = "Telegram javob bermadi (timeout): xabar={MessageId}")]
    internal static partial void Timeout(ILogger logger, Exception exception, long messageId);

    [LoggerMessage(
        EventId = 6302,
        Level = LogLevel.Error,
        Message = "Telegram xabarni RAD ETDI: xabar={MessageId} status={Status} tavsif={Description}")]
    internal static partial void Rejected(
        ILogger logger, long messageId, int status, string description);

    [LoggerMessage(
        EventId = 6303,
        Level = LogLevel.Warning,
        Message = "Telegram vaqtinchalik xato: xabar={MessageId} status={Status} tavsif={Description}")]
    internal static partial void TemporaryFailure(
        ILogger logger, long messageId, int status, string description);

    [LoggerMessage(
        EventId = 6304,
        Level = LogLevel.Warning,
        Message = "Telegram flood-wait o'rnatildi: {Seconds}s davomida so'rov yuborilmaydi")]
    internal static partial void FloodWaitRegistered(ILogger logger, int seconds);

    [LoggerMessage(
        EventId = 6305,
        Level = LogLevel.Debug,
        Message = "Flood-wait davom etmoqda, so'rov yuborilmadi: xabar={MessageId} qoldi={Seconds}s")]
    internal static partial void FloodWaitActive(ILogger logger, long messageId, double seconds);
}
