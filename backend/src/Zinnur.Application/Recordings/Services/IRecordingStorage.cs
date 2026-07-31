namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// YOZUVNI KO'RSATISH PORTI
/// ════════════════════════════════════════════════════════════════════════
///
/// ┌───────────────────────────────────────────────────────────────────┐
/// │ ★★ ONGLI ZIDDIYAT: PRESIGNED HAVOLA vs API ORQALI OQIM (PROXY)   │
/// └───────────────────────────────────────────────────────────────────┘
///
/// Loyihada uy vazifasi fayllari uchun ATAYLAB PROXY tanlangan
/// (<c>ISubmissionStorage</c> izohi: ruxsat har so'rovda tekshiriladi,
/// ombor manzili tashqariga chiqmaydi, trafik kichik). Dars yozuvi uchun
/// esa AKSINCHA — PRESIGNED havola tanlandi. Quyida har ikki tomon.
///
/// ── PROXY NIMA BERARDI (va nega bu yerda yetarli emas) ──────────────────
///
///   + Ruxsat HAR bayt so'ralganda tekshiriladi; "havola" degan tushuncha
///     umuman bo'lmaydi, ya'ni uni ulashib bo'lmaydi.
///   + Ombor manzili brauzerga hech qachon ko'rinmaydi.
///
///   − 🔴 TARMOQ KANALI. Bir dars yozuvi ~0.5 GB. Bir guruhda 20 o'quvchi
///     ko'rsa — 10 GB, oyiga 20 guruh × 8 dars — bir necha TERABAYT. Bu
///     trafik AYNI serverdan o'tadi, u yerda esa LiveKit SFU turadi:
///     jonli darsning media kanali platformaning ENG tanqis resursi
///     (200 obunachi × 1–2 Mbit/s). Yozuvni ko'rish jonli darsni
///     sekinlashtirsa — bu funksiya foydadan ko'ra zarar keltiradi.
///     Cloudflare R2 da esa brauzerga to'g'ridan-to'g'ri berilgan trafik
///     BEPUL va bizning kanalimizdan umuman o'tmaydi.
///   − 🔴 <c>Range</c> SO'ROVLARI. Videoda oldinga o'tish (seek) uchun
///     brauzer <c>Range: bytes=…</c> yuboradi. Mavjud proxy yo'li buni
///     qo'llab-quvvatlamaydi (<c>ISubmissionStorage</c> da
///     <c>enableRangeProcessing</c> ATAYLAB o'chirilgan: tarmoq oqimi
///     izlanmaydi). Ya'ni proxy uchun butunlay yangi, Range'ni tarjima
///     qiladigan yo'l yozish kerak bo'lardi — 206, <c>Content-Range</c>,
///     qisman javob keshi bilan birga.
///
/// ── PRESIGNED NIMA BILAN XAVFLI (va bu qanday cheklangan) ───────────────
///
///   − Havola CHIQARILGACH uni ushlagan HAR KIM ochadi (eski tizimning
///     X-6 turkumidagi kamchiligi). Eski tizimda muddat 4 SOAT edi va
///     havola darhol ulashishga yaroqli bo'lardi.
///
///   Shuning uchun bu yerda:
///     • muddat QISQA — <see cref="DefaultLinkTtl"/> (15 daqiqa);
///     • havola BAZAGA YOZILMAYDI va keshlanmaydi;
///     • har so'rovda ruxsat VA to'lov darvozasi qaytadan tekshiriladi
///       (ya'ni bloklangan o'quvchi keyingi havolani UMUMAN ololmaydi);
///     • klientga <c>expiresAt</c> beriladi — pleyer muddati tugashidan
///       oldin yangisini so'raydi.
///
/// XULOSA: yozuv uchun presigned, vazifa fayllari uchun proxy. Ikkalasi
/// ham ONGLI va sababi bu yerda yozilgan — kelajakda "nega ikki xil?"
/// degan savol javobsiz qolmasin.
/// </summary>
public interface IRecordingStorage
{
    /// <summary>
    /// Ko'rish havolasining amal qilish muddati.
    ///
    /// ★ 15 DAQIQA — ATAYLAB QISQA. Uzun muddat (eski tizimda 4 soat)
    /// havolani chat orqali ulashishga to'liq yaroqli qilardi. 15 daqiqa
    /// videoni BOSHLASH uchun yetarli, ulashish uchun esa deyarli foydasiz.
    ///
    /// ⚠️ SHARTNOMA: uzoq ko'rishda pleyer havolani YANGILASHI kerak
    /// (<c>RecordingLinkDto.ExpiresAt</c> aynan shuning uchun qaytariladi).
    /// </summary>
    static TimeSpan DefaultLinkTtl => TimeSpan.FromMinutes(15);

    /// <summary>Ombor sozlanganmi (bucket + kalitlar).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Yangi yozuv uchun obyekt kalitini yasaydi.
    ///
    /// ★ NIMA UCHUN KALIT USE-CASE'DA EMAS, OMBORDA YASALADI: ombordagi
    /// joylashuv sxemasi (papka, oy, kengaytma) — OMBORNING ishi. Use-case
    /// uni bilsa, prefiks o'zgarganda ikki joyni bir vaqtda tuzatish kerak
    /// bo'lardi va bittasi albatta unutilardi.
    ///
    /// ⚠️ Kalit AYNAN shu ko'rinishda Egress'ga <c>filepath</c> sifatida
    /// beriladi va bazaga yoziladi — shablonsiz, ya'ni fayl nomini
    /// OLDINDAN bilamiz. Shablon (<c>{room_name}</c>) ishlatilsa, haqiqiy
    /// nom faqat webhook bilan ma'lum bo'lardi va webhook yo'qolsa faylni
    /// ombordan topib bo'lmasdi.
    /// </summary>
    string BuildObjectKey(long sessionId);

    /// <summary>
    /// Imzolangan, MUDDATLI ko'rish havolasi (SigV4, query-string imzo).
    ///
    /// ⚠️ Manzil BRAUZER uchun mo'ljallangan qiymatdan quriladi
    /// (<c>Storage:PublicUrl</c>, bo'lmasa <c>Storage:ServiceUrl</c>) —
    /// LiveKit'dagi <c>Url</c>/<c>PublicUrl</c> juftligi bilan AYNI sabab:
    /// dev'da ombor Docker tarmog'i ichida (<c>http://minio:9000</c>) va
    /// brauzer u manzilga umuman kira olmaydi. Imzo HOSTGA bog'langani
    /// uchun manzil noto'g'ri bo'lsa ombor 403 qaytaradi.
    /// </summary>
    Uri CreateViewLink(string objectKey, TimeSpan ttl);

    /// <summary>
    /// Obyekt omborda BORMI va hajmi qancha (<c>HEAD</c>).
    ///
    /// ★ KIMGA KERAK: watchdog'ga. Webhook yo'qolsa (tarmoq, deploy,
    /// qayta ishga tushish) yozuv abadiy "Active" bo'lib qolardi. Watchdog
    /// esa ombordan SO'RAB, fayl haqiqatan borligini ko'rib, yozuvni
    /// yakunlay oladi — ya'ni haqiqat manbai LiveKit hodisasi emas,
    /// OMBORNING O'ZI.
    /// </summary>
    /// <returns><c>null</c> — obyekt yo'q.</returns>
    Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken ct = default);
}

/// <summary>Ombordagi obyekt haqidagi qisqa ma'lumot (<c>HEAD</c> javobi).</summary>
/// <param name="SizeBytes">Hajmi; ombor aytmasa <c>null</c>.</param>
public sealed record StoredObjectInfo(long? SizeBytes);
