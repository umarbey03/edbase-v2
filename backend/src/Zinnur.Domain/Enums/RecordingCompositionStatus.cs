namespace Zinnur.Domain.Enums;

/// <summary>
/// TUNGI YIG'ISH (kompozitsiya) qayerda turibdi.
///
/// ★ NIMA UCHUN <c>RecordingStatus</c> YETMAYDI: u "fayl bormi" degan
/// savolga javob beradi va uning yakuniy holatlari
/// (<c>Completed</c>/<c>Failed</c>) O'QUVCHIGA ko'rinadigan haqiqat.
/// Yangi yo'lda esa dars tugagandan keyin ham HECH NARSA yakunlanmaydi:
/// xom fayllar omborda yotadi, ffmpeg esa kechasi ishga tushadi. Bu ikki
/// savolni bitta ustunga sig'dirishga urinsak, "yozuv navbatda" holati
/// <c>Active</c> ("ketmoqda") yoki <c>Requested</c> ("boshlanmadi") deb
/// yolg'on aytishga majbur bo'lardik — ikkalasi ham xato, va ikkalasi ham
/// watchdog'ni chalg'itardi.
///
/// Ya'ni ikki ustun ikki qatlamni ifodalaydi:
///   <c>Status</c>            — foydalanuvchi uchun natija (fayl ochiladimi);
///   <c>CompositionStatus</c> — ishlab chiqarish jarayoni (qaysi bosqichda).
/// Yakunda ular ROSTLASHADI: <see cref="Completed"/> bo'lganda
/// <c>Status = Completed</c>, <see cref="Failed"/> bo'lganda
/// <c>Status = Failed</c> — bu ROSTLASH domendagi metodlarning ichida
/// bajariladi, servis qatlamida emas.
///
/// 🔴 <c>NULL</c> — eski yo'l (<c>RecordingPipeline.RoomComposite</c>)
/// uchun YAGONA to'g'ri qiymat: u yerda umuman yig'ish bosqichi yo'q.
/// <c>RoomComposite</c> qatorida bo'sh bo'lmagan qiymat — XATO, "hali
/// boshlanmagan" degani emas.
///
/// ⚠️ RAQAMLAR BAZAGA YOZILADI. Yangi holat FAQAT oxiriga qo'shiladi.
/// </summary>
public enum RecordingCompositionStatus
{
    /// <summary>
    /// Dars ketmoqda (yoki hozirgina tugadi) — xom bo'laklar hali
    /// yig'ilyapti: trek egress'lari ochiq, ovoz mikseri ishlayapti.
    ///
    /// Bu holat qator YARATILGANDA qo'yiladi, ya'ni "hali hech narsa
    /// boshlanmagan" degan bo'sh oraliq YO'Q — jarayon bazada birinchi
    /// lahzadanoq ko'rinadi.
    /// </summary>
    Collecting = 0,

    /// <summary>
    /// Barcha bo'laklar yakuniy holatga yetdi (tayyor yoki yiqilgan) —
    /// endi TUNGI OYNA kutilmoqda.
    ///
    /// ★ Bu holat NAVBAT: kompozitor ishchisi aynan shu qatorlarni
    /// eng eskisidan boshlab oladi. "Bir kechada ulgurmadi" degan hol
    /// ham shu yerga qaytadi — ish yo'qolmaydi, keyingi kecha davom etadi.
    /// </summary>
    Queued = 1,

    /// <summary>
    /// Kompozitor bu qatorni EGALLAB oldi (ijara muddati
    /// <c>CompositionLeaseUntil</c> da) va ffmpeg ishlayapti.
    ///
    /// ⚠️ MUDDATI O'TGAN ijara — bu "ishlayapti" emas, "ishchi qulagan"
    /// degani: keyingi ishchi shunday qatorni qayta egallaydi va ishni
    /// BOSHIDAN boshlaydi (yarim yozilgan mp4 davom ettirilmaydi).
    /// </summary>
    Running = 2,

    /// <summary>
    /// Yakuniy mp4 omborga yuklandi va tekshirildi; shu bilan birga
    /// <c>SessionRecording.Status</c> ham <c>Completed</c> bo'ldi — ya'ni
    /// yozuv endi ochiladigan holatda.
    /// </summary>
    Completed = 3,

    /// <summary>
    /// Voz kechildi. Sabab <c>SessionRecording.CompositionError</c> va
    /// <c>Error</c> da — XODIM uchun, o'zbekcha. <c>Status</c> ham
    /// <c>Failed</c> ga o'tadi, chunki fayl bo'lmaydi.
    /// </summary>
    Failed = 4,
}
