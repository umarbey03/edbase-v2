namespace Zinnur.Domain.Enums;

/// <summary>
/// Dars yozuvi QAYSI MEXANIZM bilan olinadi.
///
/// ★ NIMA UCHUN ENUM KERAK BO'LDI: ikkita yozuv yo'li BIR VAQTDA
/// yashaydi. Eskisi (<see cref="RoomComposite"/>) — LiveKit ichida
/// headless Chrome + real-time x264, bitta darsga ~1.5 yadro; yangisi
/// (<see cref="TrackComposition"/>) — darsda faqat ARZON tutib olish
/// (treklar passthrough + bitta aralashtirilgan ovoz), og'ir kodlash esa
/// KECHASI. Ular bir xil <c>SessionRecordings</c> jadvalida turadi,
/// shuning uchun har bir qator O'ZINI qaysi yo'l yaratganini aytishi
/// SHART.
///
/// ★ QATORNI KIM O'QIYDI — ikki xil savol, ikki xil joy:
///
///   • <c>SessionRecording.Pipeline</c> — "bu qatorni QAYSI yo'l
///     yaratdi". Fon vazifalari aynan shu ustun bo'yicha o'z ishini
///     ajratadi: eski watchdog faqat <see cref="RoomComposite"/> ni
///     nazorat qiladi (yangi yo'lning yakuniy fayli darsdan keyin ERTALAB
///     paydo bo'ladi, ya'ni watchdog uni "yo'qolgan" deb hisoblab, har
///     safar <c>Failed</c> qilib qo'yardi), tungi kompozitsiya esa faqat
///     <see cref="TrackComposition"/> ni oladi.
///
///   • <c>Group.RecordingPipeline</c> — "bu guruhning darslari QAYSI yo'l
///     bilan yozilsin". Bu <c>Group.RecordEnabled</c> dan MUSTAQIL:
///     birinchisi "yozilsinmi", ikkinchisi "qanday yozilsin". Yozuv
///     o'chirilgan guruh qaysi yo'l tanlangan bo'lishidan qat'i nazar
///     yozilmaydi.
///
/// ⚠️ RAQAMLAR BAZAGA YOZILADI (<c>RecordingStatus</c> dagi AYNI qoida).
/// Yangi qiymat FAQAT oxiriga qo'shiladi, mavjudlari hech qachon
/// o'zgartirilmaydi. <c>0</c> ning aynan eski xatti-harakat ekani ATAYLAB:
/// shu sabab mavjud qatorlarga migratsiyada MA'LUMOT to'ldirish kerak
/// emas — ustun standarti <c>0</c> va u to'g'ri javob.
///
/// 🔴 "IKKALASI HAM" DEGAN QIYMAT YO'Q VA BO'LMAYDI. Bosqichma-bosqich
/// yoyish davrida bitta guruh ikkala yo'ldan ham o'tkaziladi (A/B
/// solishtirish), lekin bu GURUHNING XOSSASI EMAS — vaqtinchalik holat.
/// U alohida global sozlama (<c>recordings.track_pipeline_shadow_groups</c>)
/// bilan beriladi va yoyish tugagach izsiz yo'qoladi. Agar u enum qiymati
/// bo'lganda, bazada mangu ma'nosiz uchinchi qiymat qolib ketardi.
/// </summary>
public enum RecordingPipeline
{
    /// <summary>
    /// BUGUNGI yo'l: LiveKit <c>RoomCompositeEgress</c> — dars davomida
    /// jonli kodlash, natijada tayyor mp4.
    ///
    /// Yagona qadamli va shu sababli SODDA, lekin qimmat: bitta darsga
    /// ~1.5 yadro, ya'ni 4 yadroli serverda bir vaqtda BITTA dars sig'adi.
    /// Bu qiymat KOD ICHIDA QOLADI va tanlanadigan bo'lib turadi — u yangi
    /// yo'lning ORQAGA QAYTISH yo'li.
    /// </summary>
    RoomComposite = 0,

    /// <summary>
    /// YANGI yo'l: dars davomida <c>TrackEgress</c> (video treklar,
    /// passthrough) + bitta faqat-ovozli <c>RoomCompositeEgress</c>
    /// (butun xonaning aralashtirilgan ovozi), kechasi esa ffmpeg ularni
    /// bitta mp4 ga yig'adi.
    ///
    /// ★ Yakuniy fayl darsdan bir necha soat KEYIN paydo bo'ladi. Shu
    /// sabab bu qiymatdagi qator uchun "fayl hali yo'q" NORMAL holat, xato
    /// emas — jarayonning qayerdaligi <c>CompositionStatus</c> da turadi.
    /// </summary>
    TrackComposition = 1,
}
