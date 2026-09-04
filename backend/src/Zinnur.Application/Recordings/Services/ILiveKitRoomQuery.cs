using Zinnur.Application.Recordings.Dtos;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// LIVEKIT'NING JORIY HOLATINI O'QISH — FAQAT O'QISH
/// ════════════════════════════════════════════════════════════════════════
///
/// ── NIMA UCHUN BU BOR ───────────────────────────────────────────────────
///
/// Yangi yozuv quvuri (<c>RecordingPipeline.TrackComposition</c>) treklarni
/// WEBHOOK orqali topadi. Webhook esa yetkazilmasligi mumkin: API
/// konteyneri dars o'rtasida qayta ishga tushsa, o'sha oraliqda kelgan
/// <c>room_started</c> va <c>track_published</c> hodisalari BUTUNLAY
/// yo'qoladi va ularni hech kim qayta yubormaydi.
///
/// Shuning uchun tiklash job'iga "LiveKit hozir nima ko'rib turibdi"
/// degan savolni berish kerak: xonada qanday treklar bor va qaysi
/// egress'lar hali tirik. Bu — o'sha savol.
///
/// ── NIMA UCHUN <see cref="ILiveKitEgress"/> GA QO'SHILMADI ──────────────
///
/// U port BOSHQARADI (yozuvni boshlaydi va to'xtatadi), bu port esa
/// faqat O'QIYDI va hech narsani o'zgartirmaydi. Ikkalasini birlashtirsak,
/// "yozuvni boshqaradigan" port hech qachon yozuv boshlamaydigan
/// chaqiruvchilarga ham berilardi. Amalga oshirilishi ikkalasi uchun
/// BITTA sinf (<c>LiveKitEgressClient</c>) — token, Twirp va xato
/// ishlovi baribir aynan bir xil.
///
/// ── LIVEKIT NOSOZLIGI ISTISNO EMAS, NATIJA ──────────────────────────────
///
/// 🔴 "LiveKit javob bermadi" BILAN "xonada hech narsa yo'q" NI ARALASHTIRIB
/// BO'LMAYDI. Ikkalasini ham bo'sh ro'yxat bilan qaytarsak, tarmoq uzilgan
/// daqiqada tiklash job'i "mikser o'lgan" deb xulosa chiqarib, tirik
/// egress ustiga ikkinchisini ishga tushirardi — darsda ikkita ovoz fayli.
/// Shuning uchun natijalarda muvaffaqiyat bayrog'i alohida turadi
/// (<see cref="LiveKitTrackListResult"/>, <see cref="LiveKitEgressListResult"/>).
/// </summary>
public interface ILiveKitRoomQuery
{
    /// <summary>
    /// Xonadagi ishtirokchilarning E'LON QILINGAN treklari
    /// (<c>livekit.RoomService/ListParticipants</c>).
    ///
    /// ★ NIMA UCHUN NATIJA YASSI, ISHTIROKCHILAR DARAXTI EMAS: chaqiruvchi
    /// "shu xonada qanday treklar bor va ular kimniki" degan yagona
    /// savolga javob so'raydi — sabab <see cref="LiveKitPublishedTrackDto"/>
    /// izohida.
    ///
    /// ⚠️ BU CHAQIRUV BOSHQA GRANTLI TOKEN TALAB QILADI
    /// (<c>video: { roomAdmin: true, room: … }</c>) — egress tokenidagi
    /// <c>roomRecord</c> bu yerda ishlamaydi. Grant xona nomiga
    /// BOG'LANADI, ya'ni token faqat shu xonani ko'rsatadi.
    /// </summary>
    Task<LiveKitTrackListResult> ListParticipantsAsync(
        string roomName, CancellationToken ct = default);

    /// <summary>
    /// Xonadagi FAOL egress'lar (<c>livekit.Egress/ListEgress</c>).
    ///
    /// ★ "Faqat faol" — shartnomaning bir qismi, chaqiruvchining tanlovi
    /// emas: bu ro'yxatning yagona iste'molchisi "bizning qatorimiz
    /// <c>Active</c> deydi, LiveKit'da esa bunday egress bormi?" degan
    /// savolga javob izlaydi. Tugagan egress'larni ham qaytarish o'sha
    /// savolni har chaqiruv joyida qayta filtrlashga majbur qilardi.
    ///
    /// ⚠️ BO'SH RO'YXAT — MA'NOLI JAVOB: "xonada faol egress yo'q", ya'ni
    /// mikser o'lgan. Aynan shuning uchun xato holati bo'sh ro'yxat bilan
    /// ifodalanmaydi (port izohiga qarang).
    /// </summary>
    Task<LiveKitEgressListResult> ListEgressAsync(
        string roomName, CancellationToken ct = default);
}
