using Zinnur.Application.Recordings.Dtos;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// LIVEKIT EGRESS PORTI — xona yozuvini boshlash va to'xtatish
/// ════════════════════════════════════════════════════════════════════════
///
/// Application qatlami HTTP'ni ham, Twirp'ni ham, S3 kalitlarini ham
/// ko'rmaydi: amalga oshirilishi Infrastructure'da
/// (<c>LiveKitEgressClient</c>).
///
/// ── FAYL API ORQALI O'TMAYDI ────────────────────────────────────────────
///
/// Egress videoni TO'G'RIDAN-TO'G'RI obyekt omboriga yozadi: bizning
/// serverimiz orqali bitta bayt ham o'tmaydi. Shuning uchun port'da
/// "faylni oling" degan metod YO'Q va bo'lishi ham mumkin emas — biz faqat
/// KALITNI (yo'lni) beramiz va natijani webhook orqali bilamiz.
///
/// Bu ONGLI tanlov: 80 daqiqalik dars ~0.5 GB. Uni API konteyneri orqali
/// o'tkazish jonli darsning O'ZI foydalanadigan tarmoq kanalini yeb
/// qo'yardi (LiveKit ayni serverda turadi).
///
/// ── NIMA UCHUN ISTISNO TASHLAMAYDI ──────────────────────────────────────
///
/// 🔴 YOZUVNING BOSHLANMASLIGI DARSNI TO'XTATMASLIGI SHART. Egress —
/// alohida xizmat va u yiqilgan bo'lishi mumkin. Shuning uchun metodlar
/// <see cref="EgressStartResult"/> qaytaradi: chaqiruvchi xatoni bazaga
/// yozadi va watchdog'ga qoldiradi, ustoz esa darsni odatdagidek o'tadi.
/// </summary>
public interface ILiveKitEgress
{
    /// <summary>
    /// Yozuv umuman mumkinmi: LiveKit kaliti VA obyekt ombori sozlanganmi.
    ///
    /// ★ IKKALASI HAM SHART: Egress faylni bizning omborimizga o'zi yozadi,
    /// ya'ni unga S3 kalitlari uzatiladi. Ombor sozlanmagan bo'lsa yozuv
    /// "boshlangandek" ko'rinib, hech qayerga tushmasdi.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Xona yozuvini boshlaydi (Room Composite → MP4 → S3/R2).
    /// </summary>
    Task<EgressStartResult> StartRoomRecordingAsync(
        EgressStartRequest request, CancellationToken ct = default);

    /// <summary>
    /// Yozuvni to'xtatadi.
    ///
    /// ⚠️ To'xtatish DARHOL fayl degani emas: Egress videoni yakunlab,
    /// omborga yuklashi kerak. Yakuniy holat faqat <c>egress_ended</c>
    /// webhook'i bilan keladi.
    ///
    /// Natija <c>false</c> bo'lishi NORMAL: LiveKit allaqachon to'xtagan
    /// egress uchun xato qaytaradi (masalan xona yopilgan bo'lsa u o'zi
    /// to'xtaydi).
    /// </summary>
    Task<bool> StopRecordingAsync(string egressId, CancellationToken ct = default);
}
