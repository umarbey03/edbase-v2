namespace Zinnur.Domain.Enums;

/// <summary>
/// Dars yozuvining hayot sikli.
///
/// ★ NIMA UCHUN BEShTA HOLAT, IKKITA EMAS ("bor / yo'q"): yozuv jarayoni
/// BIZDAN TASHQARIDA (LiveKit Egress) bajariladi va u yerda ish bir necha
/// daqiqa davom etadi. Oraliq holatsiz tizim "fayl hali yo'q" bilan
/// "yozuv umuman boshlanmadi" ni ajrata olmasdi — eski tizimda aynan
/// shunday edi: <c>recording_url IS NULL</c> ikkalasini ham bildirardi va
/// nosozlikni faqat qo'lda, log ichidan topish mumkin edi.
///
/// ⚠️ RAQAMLAR BAZAGA YOZILADI. Yangi holat FAQAT oxiriga qo'shiladi,
/// mavjud qiymatlar hech qachon o'zgartirilmaydi.
/// </summary>
public enum RecordingStatus
{
    /// <summary>
    /// Yozuv so'raldi, lekin Egress hali tasdiqlamadi (yoki so'rov yiqildi).
    ///
    /// Bu holat WATCHDOG uchun: qator Egress'ga murojaatdan OLDIN
    /// saqlanadi, ya'ni jarayon o'sha lahzada qulasa ham "boshlanmagan
    /// yozuv" izsiz yo'qolmaydi va fon vazifasi uni qayta uradi.
    /// </summary>
    Requested = 0,

    /// <summary>
    /// Egress so'rovni qabul qildi va <c>EgressId</c> berdi, lekin
    /// "boshlandi" hodisasi hali kelmadi.
    /// </summary>
    Starting = 1,

    /// <summary>Yozuv HAQIQATAN ketmoqda (<c>egress_started</c> keldi).</summary>
    Active = 2,

    /// <summary>Fayl omborga yozildi va ochish mumkin (YAGONA "ko'rsa bo'ladi" holati).</summary>
    Completed = 3,

    /// <summary>
    /// Yozuv chiqmadi. Sabab <c>Error</c> da — u XODIM uchun, ya'ni
    /// "nega bu darsning yozuvi yo'q?" degan savolga javob bazada turadi.
    /// </summary>
    Failed = 4,
}
