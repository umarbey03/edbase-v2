namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 LIVEKIT WEBHOOK IMZOSINI TEKSHIRISH PORTI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN BU LOYIHADAGI ENG MUHIM TEKSHIRUVLARDAN BIRI
///
/// Eski tizimda LiveKit webhook'i UMUMAN tekshirilmasdi (audit X-3):
/// endpoint manzilini bilgan istalgan odam soxta "dars tugadi" yoki
/// "yozuv tayyor" hodisasini yuborib, tizim holatini o'zgartira olardi —
/// davomat, yozuv kaliti, dars holati. Manzil esa sir emas: u LiveKit
/// konfiguratsiyasida, deploy skriptlarida va tarmoq jurnallarida turadi.
///
/// ★ LIVEKIT QANDAY IMZOLAYDI
///
/// Har so'rovda <c>Authorization</c> sarlavhasida HS256 JWT keladi:
///     { "iss": "&lt;ApiKey&gt;", "exp": …, "nbf": …,
///       "sha256": "&lt;base64(SHA-256(TANA))&gt;" }
/// Token API SIRI bilan imzolanadi.
///
/// 🔴 FAQAT TOKENNI TEKSHIRISH YETARLI EMAS. Yaroqli token bir marta
/// ushlansa (masalan xato sozlangan proksi jurnalidan), uni BOSHQA tana
/// bilan qayta yuborish mumkin bo'lardi — ya'ni imzo bor, lekin u ayni
/// shu MAZMUNGA taalluqli emas. Shuning uchun tananing SHA-256 xeshi
/// token ichidagi <c>sha256</c> da'vosi bilan solishtirilishi SHART.
///
/// ★ SOZLANMAGAN BO'LSA — TEKSHIRUV O'TKAZIB YUBORILMAYDI
///
/// Eski tizimda <c>if (settings.LIVEKIT_API_SECRET)</c> shartida sir
/// bo'sh bo'lsa BUTUN tekshiruv chetlab o'tilardi — ya'ni eng xavfli
/// holatda himoya o'zi o'chib qolardi. Bu yerda aksincha:
/// <see cref="IsConfigured"/> <c>false</c> bo'lsa endpoint UMUMAN
/// ishlamaydi (404 — Telegram webhook'i bilan bir xil qat'iylik).
/// </summary>
public interface ILiveKitWebhookVerifier
{
    /// <summary>
    /// Kalit va sir bormi. <c>false</c> bo'lsa chaqiruvchi endpointni
    /// 404 qiladi — "sirsiz qabul qilish" degan rejim MAVJUD EMAS.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Imzoni VA tana xeshini tekshiradi.
    /// </summary>
    /// <param name="authorizationHeader">
    /// <c>Authorization</c> sarlavhasining qiymati (<c>Bearer </c> prefiksi
    /// bilan ham, prefikssiz ham qabul qilinadi — LiveKit versiyalari ikki
    /// xil yuboradi).
    /// </param>
    /// <param name="body">So'rov tanasi — AYNAN kelgan baytlar.</param>
    WebhookVerification Verify(string? authorizationHeader, ReadOnlySpan<byte> body);
}

/// <summary>
/// Tekshiruv natijasi.
///
/// ★ <see cref="Reason"/> FAQAT LOG UCHUN va hech qachon HTTP javobiga
/// tushmaydi: "imzo yaroqsiz" bilan "xesh mos kelmadi" ni ajratib berish
/// hujumchiga qaysi bosqichda to'xtaganini aytardi.
/// </summary>
public readonly record struct WebhookVerification(bool IsValid, string? Reason)
{
    public static WebhookVerification Valid => new(true, null);

    public static WebhookVerification Invalid(string reason) => new(false, reason);
}
