using System.Globalization;
using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Telegram;

/// <summary>
/// Telegram Bot API javobini navbat qaroriga aylantiradi (sof funksiya).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ XARITALASH — <c>IMessageSender</c> SHARTNOMASIDA YOZILGANIDEK:
///
///   429 (Too Many Requests)        -> Retry   (biz juda tez yubordik)
///   5xx (Telegram tomonidagi xato) -> Retry   (o'tib ketadi)
///   400 / 403 / 401 / 404          -> Permanent
///   qolgan 4xx                     -> Permanent
///
/// ★ NIMA UCHUN 400 "DOIMIY": Telegram uni ikki holatda beradi —
///   (a) chat topilmadi / foydalanuvchi botni bloklagan,
///   (b) matn Telegram HTML qoidasiga mos emas.
///   Ikkalasida ham QAYTA URINISH HOLATNI O'ZGARTIRMAYDI: xabar
///   navbatni bekorga band qilib, 1.3 soat davomida to'rt marta qayta
///   urinilardi va oxirida baribir yiqilardi. Sabab esa `LastError` da
///   yozilib qoladi — operator "nega yetmadi" savoliga javob topadi.
///
/// ★ NIMA UCHUN 401 HAM "DOIMIY": bu bot tokeni noto'g'ri degani, ya'ni
///   KONFIGURATSIYA xatosi. Qayta urinish uni tuzatmaydi; xabar darhol
///   yiqilib, log'da aniq sabab qoladi.
///
/// ★ NIMA UCHUN ALOHIDA SINF: bu YAGONA qism, uni jonli Telegram serverisiz
///   sinash mumkin. Yuboruvchining ichida bo'lganda xaritalash faqat
///   haqiqiy tarmoq bilan tekshirilardi — ya'ni umuman tekshirilmasdi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public static class TelegramErrorMap
{
    /// <summary>HTTP holat kodini va Telegram tavsifini natijaga aylantiradi.</summary>
    /// <param name="statusCode">Telegram qaytargan HTTP holati.</param>
    /// <param name="description">
    /// Javob tanasidagi <c>description</c> (bo'lsa) — bazadagi
    /// <c>LastError</c> ga tushadi va nosozlikni tekshirishda YAGONA maslahat.
    /// </param>
    /// <param name="retryAfter">
    /// 429 dagi <c>parameters.retry_after</c> (sekund). Faqat sababga
    /// yoziladi: kutish muddatini <c>OutboxRetryPolicy</c> belgilaydi
    /// (izoh: <see cref="Zinnur.Application.Notifications.OutboxRetryPolicy"/>).
    /// </param>
    public static MessageSendResult FromStatus(int statusCode, string? description, int? retryAfter = null)
    {
        var detail = Describe(description);

        return statusCode switch
        {
            // Muvaffaqiyat. `ok: false` bo'lgan 200 javobini chaqiruvchi
            // ALOHIDA tekshiradi — bu yerga faqat holat kodi keladi.
            >= 200 and < 300 => MessageSendResult.Ok,

            429 => MessageSendResult.Retry(
                retryAfter is { } seconds
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Telegram tezlik chegarasi (429), retry_after={seconds}s. {detail}")
                    : string.Create(CultureInfo.InvariantCulture, $"Telegram tezlik chegarasi (429). {detail}")),

            >= 500 => MessageSendResult.Retry(
                string.Create(CultureInfo.InvariantCulture, $"Telegram xatosi ({statusCode}). {detail}")),

            _ => MessageSendResult.Permanent(
                string.Create(CultureInfo.InvariantCulture, $"Telegram rad etdi ({statusCode}). {detail}")),
        };
    }

    /// <summary>Tavsifni qisqartiradi — <c>LastError</c> ustuni 500 belgi.</summary>
    private static string Describe(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "Tavsif yo'q.";

        var trimmed = description.Trim();

        return trimmed.Length <= MaxDescriptionLength
            ? trimmed
            : trimmed[..MaxDescriptionLength];
    }

    /// <summary>
    /// Tavsifning eng katta uzunligi. <c>MessageOutbox.LastError</c> 500
    /// belgi; qolgan joy bizning prefiksimizga ketadi.
    /// </summary>
    private const int MaxDescriptionLength = 300;
}
