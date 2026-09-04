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
///
/// ── NIMA UCHUN UCHTA BOSHLASH METODI, BITTASI EMAS ──────────────────────
///
/// Uchala metod uchta BOSHQA yozuv turini ishga tushiradi va ular
/// bir-birining parametri emas:
///
///   • <see cref="StartRoomRecordingAsync"/> — ESKI quvur: brauzerda
///     chizilgan katakli ko'rinish, real vaqtda x264 (~1.5 yadro);
///   • <see cref="StartTrackRecordingAsync"/> — bitta trek, qayta
///     kodlashsiz o'tkazish (~0.05 yadro);
///   • <see cref="StartRoomAudioRecordingAsync"/> — butun xonaning
///     aralashtirilgan ovozi, brauzersiz (SDK manbasi).
///
/// 🔴 IKKINCHISI VA UCHINCHISI BAYROQ SIFATIDA BIRLASHTIRILMAYDI. Ikkinchi
/// va uchinchi metod tanasidagi farq — <c>layout</c> va
/// <c>custom_base_url</c> ning YO'QLIGI — Egress'ning brauzer ishga
/// tushirish/tushirmaslik qaroriga to'g'ridan-to'g'ri ta'sir qiladi.
/// "<c>audioOnly</c>" degan mantiqiy bayroqli yagona metod o'sha qarorni
/// bitta beparvo tahrirga qoldirardi va natijasi JIMGINA olti barobar
/// qimmat quvur bo'lardi.
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
    /// BITTA TREKNI yozib olishni boshlaydi (<c>TrackEgress</c> → xom
    /// fayl → S3/R2).
    ///
    /// ★ QAYTA KODLASH YO'Q: LiveKit treklarni "borligicha" (passthrough)
    /// yozadi, ya'ni brauzer ham, x264 ham ishga tushmaydi. Bitta trek
    /// ~0.05 yadro turadi — shu sababdan olti dars bir vaqtda sig'adi.
    ///
    /// ⚠️ BITTA DARSDA BIR NECHTA CHAQIRUV ODATIY HOL: ekran har yoqilib
    /// o'chirilganda va ustoz har uzilib-ulanganda YANGI trek va yangi
    /// fayl paydo bo'ladi.
    /// </summary>
    Task<EgressStartResult> StartTrackRecordingAsync(
        TrackEgressStartRequest request, CancellationToken ct = default);

    /// <summary>
    /// BUTUN XONANING ovozini yozib olishni boshlaydi — faqat-ovozli
    /// <c>RoomCompositeEgress</c> (→ Opus/OGG → S3/R2).
    ///
    /// Bitta darsga BITTA chaqiruv va bitta uzluksiz fayl: ustoz, ekran
    /// ovozi va gapirgan har bir o'quvchi. Ovoz o'chirilganda fayl
    /// to'xtamaydi — unga jimlik yoziladi, ya'ni vaqt o'qi butun dars
    /// davomida uzilmaydi va video bo'laklar AYNAN shu o'qqa
    /// joylashtiriladi.
    ///
    /// 🔴 BU METOD <see cref="StartRoomRecordingAsync"/> BILAN AYNI
    /// LIVEKIT METODIGA BORADI, LEKIN TANASI BOSHQACHA VA SHU FARQ
    /// QIMMATNI HAL QILADI. Batafsil sabab — sinf izohidagi "uchta
    /// boshlash metodi" bo'limi.
    /// </summary>
    Task<EgressStartResult> StartRoomAudioRecordingAsync(
        RoomAudioEgressStartRequest request, CancellationToken ct = default);

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
