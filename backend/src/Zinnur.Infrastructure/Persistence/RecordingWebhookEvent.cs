namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// ══════════════════════════════════════════════════════════════════════════
/// ISHLANGAN LIVEKIT HODISASINING IZI (idempotentlik jurnali)
/// ══════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN JADVAL KERAK
///
/// LiveKit webhook'dan 200 kutadi. Javob kechiksa yoki tarmoq uzilsa AYNI
/// hodisani QAYTA yuboradi. Bu jadvalsiz bitta <c>egress_ended</c> ikki
/// marta ishlanardi: yozuv "tugallandi" deb ikki marta belgilanardi va
/// (kelajakda xabarnoma qo'shilsa) o'quvchilarga ikkita "yozuv tayyor"
/// bildirishnomasi ketardi.
///
/// ★ NIMA UCHUN XOTIRADA (yoki Redis'da) EMAS: API bir necha konteynerda
/// ishlaydi va takror hodisa BOSHQA instansiyaga tushishi mumkin. Bundan
/// tashqari bu yozuv holat o'zgarishi bilan AYNI tranzaksiyada saqlanishi
/// kerak — Redis'da esa "belgilandi, lekin qayta ishlanmadi" holati paydo
/// bo'lardi.
///
/// ★ NIMA UCHUN DOMAIN ENTITY EMAS (<see cref="TelegramUpdate"/> bilan AYNI
/// sabab): bu biznes tushunchasi emas, YETKAZIB BERISH mexanizmi. Shuning
/// uchun sinf Infrastructure ichida qoladi va <c>IApplicationDbContext</c>
/// da OCHILMAYDI.
///
/// ★ NIMA UCHUN KALIT SATR, <see cref="TelegramUpdate.UpdateId"/> kabi son
/// EMAS: LiveKit hodisa Id'si <c>EV_…</c> ko'rinishidagi matn. Bundan
/// tashqari u UMUMAN BO'LMASLIGI mumkin — o'shanda chaqiruvchi TANA XESHINI
/// (<c>sha256:…</c>) kalit qilib beradi, ya'ni bir xil tana ikki marta
/// kelsa baribir to'siladi. Sonli kalit bu ikkinchi rejimni ko'tara olmasdi.
/// </summary>
public sealed class RecordingWebhookEvent
{
    /// <summary>
    /// Kalitning eng katta uzunligi.
    ///
    /// LiveKit Id'si ~30 belgi, tana xeshi esa <c>sha256:</c> + 64 belgi.
    /// 200 — ikkalasidan ham ancha ko'p, lekin cheksiz emas: chegara
    /// bo'lmasa hujumchi ulkan qiymat yuborib indeksni shishirardi.
    /// </summary>
    public const int MaxEventIdLength = 200;

    /// <summary>LiveKit bergan hodisa Id'si yoki tana xeshi — BIRLAMCHI KALIT.</summary>
    public required string EventId { get; set; }

    /// <summary>
    /// Qachon qabul qilingan. Faqat TOZALASH uchun: jadval cheksiz o'smasin
    /// (eski qatorlarni davriy o'chirish — <c>TelegramUpdates</c> bilan
    /// AYNI reja).
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
