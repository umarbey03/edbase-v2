namespace Zinnur.Application.Media;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// MEDIA CHIPTASI — `&lt;video src&gt;` UCHUN QISQA MUDDATLI KIRISH BELGISI
/// ════════════════════════════════════════════════════════════════════════
///
/// ── MUAMMO ─────────────────────────────────────────────────────────────
///
/// 🔴 Brauzerning `&lt;video src="..."&gt;` elementi `Authorization`
/// SARLAVHASINI YUBORMAYDI va uni yuborishga majburlashning yo'li YO'Q
/// (bu `fetch` emas, brauzerning ichki media yuklovchisi). Shu sababli
/// `GET /api/v1/lessons/assets/{id}` — sarlavha talab qiladigan endpoint —
/// FRONTENDDAN UMUMAN O'YNATIB BO'LMASDI. Dars videosi platformada bor
/// edi, yuklanardi, lekin hech kim ko'ra olmasdi.
///
/// ── UCH VARIANT VA TANLASH SABABI ──────────────────────────────────────
///
/// **A) PRESIGNED HAVOLA (R2/S3 imzosi).** RAD ETILDI.
///
///   <see cref="IMediaStorage"/> port izohi bu savolni ALLAQACHON ko'rib
///   chiqqan va presigned'ni ONGLI ravishda rad etgan: havola
///   chiqarilgach uni ushlagan har kim ochadi va ruxsat QAYTA
///   tekshirilmaydi. O'sha qaror BUGUN HAM TO'G'RI, chunki dars videosi
///   ortida IKKI dinamik darvoza turadi:
///     • to'lov bloki (`PaymentBlockScope.Video`) — qarz PAYDO BO'LISHI
///       bilan yopilishi kerak;
///     • gating — dars ochiqligi o'quvchining progressi bilan o'zgaradi.
///   Presigned havola ikkalasini ham FAQAT chiqarilgan ONDA tekshiradi.
///
///   ⚠️ `IRecordingStorage` da AYNI savolga TESKARI javob berilgan
///   (dars YOZUVI uchun presigned). U qaror ham o'z joyida to'g'ri va
///   o'zgartirilmadi: yozuv uchun keltirilgan ikki sabab bu yerda
///   AMAL QILMAYDI — (1) proxy'da `Range` YO'Q edi, bu yerda esa u
///   allaqachon TO'LIQ ishlaydi (`RangeHeader` + `IMediaStorage`), va
///   (2) yozuv jonli darsning SFU kanali bilan bir vaqtda oqadi, dars
///   videosi esa jonli darsdan MUSTAQIL vaqtda ko'riladi.
///
/// **B) SESSIYA TOKENINI QUERY'GA QO'YISH** (`?access_token=…`, SignalR
///   `/hubs` yo'lidagi kabi). 🔴 RAD ETILDI — ENG XAVFLI variant.
///   `<video src>` manzili brauzer tarixiga, `Referer` sarlavhasiga va
///   oraliq proksi loglariga tushadi. Ya'ni video havolasi TO'LIQ HISOB
///   huquqini 15 daqiqaga ulashadigan havolaga aylanardi. `/hubs` uchun
///   bu maqbul (WebSocket manzili sahifada qolmaydi), media uchun — yo'q.
///
/// **C) ALOHIDA, MAQSADGA BOG'LANGAN CHIPTA (TANLANDI).**
///
///   Bu port. Chipta:
///     • FAQAT bitta `assetId` uchun yaroqli (imzo ichida shu Id bor);
///     • qisqa muddatli (<see cref="DefaultTtl"/> — 15 daqiqa,
///       `IRecordingStorage.DefaultLinkTtl` bilan AYNI);
///     • JWT EMAS va autentifikatsiya quvuriga (`AddJwtBearer`) UMUMAN
///       KIRMAYDI. Bu ATAYLAB: chipta hech qanday boshqa endpointni
///       ocholmasligi TUZILISH bilan kafolatlanadi, dasturchining
///       ehtiyotkorligi bilan emas.
///
/// ── ★★ ENG MUHIM XOSSA: CHIPTA RUXSAT BERMAYDI ─────────────────────────
///
/// 🔴 Chipta FAQAT "SEN KIMSAN?" degan savolga javob beradi.
/// "SENGA RUXSATMI?" degan savol HAR BAYT SO'ROVIDA qaytadan, bazadan
/// hal qilinadi — `LessonAssetService.EnsureCanReadAsync` (to'lov bloki +
/// gating) va `LoadActorAsync` (profil faolmi). Ya'ni:
///
///   • qarzi paydo bo'lgan o'quvchi keyingi `Range` so'rovida TO'XTAYDI;
///   • darsi qulflangan o'quvchi videoni davom ettira olmaydi;
///   • o'chirilgan profil darhol kesiladi.
///
/// Brauzer video davomida O'NLAB `Range` so'rovi yuboradi, ya'ni bekor
/// qilish amalda BIR NECHA SONIYA ichida kuchga kiradi. Presigned
/// havolada esa bu MUMKIN EMAS edi.
///
/// ⚠️ QOLDIQ XAVF (ochiq yozilgan): chiptani ushlagan odam SHU BITTA
/// faylni chipta muddati tugagunicha (≤15 daqiqa) o'qiy oladi — go'yo
/// chipta egasi bo'lgandek. `TokenVersion` (hamma qurilmadan chiqish /
/// rol o'zgarishi) shu oynada tekshirilmaydi; PROFIL O'CHIRILISHI esa
/// tekshiriladi (`LoadActorAsync`). Bu qoldiq presigned havolanikidan
/// QAT'IY KICHIK: u yerda ham aynan shu 15 daqiqa bor, ustiga ustak
/// gating va to'lov darvozasi umuman qayta ko'rilmasdi.
/// </summary>
public interface IMediaAccessTicketService
{
    /// <summary>
    /// Chipta muddati.
    ///
    /// ★ 15 DAQIQA — `IRecordingStorage.DefaultLinkTtl` bilan ATAYLAB
    /// AYNI: platformada "qisqa muddatli media havolasi" tushunchasi
    /// BITTA bo'lsin, ikki xil raqam ikki xil pleyer xatti-harakatini
    /// keltirib chiqarmasin.
    ///
    /// ⚠️ SHARTNOMA: uzun videoda pleyer chiptani YANGILASHI kerak
    /// (`expiresAt` aynan shuning uchun qaytariladi) va yangilagach
    /// `currentTime` ni tiklashi shart — busiz 40 daqiqalik dars
    /// o'rtasida "sababsiz" to'xtardi. `RecordingPlayerModal` da AYNI
    /// naqsh allaqachon ishlaydi.
    /// </summary>
    static TimeSpan DefaultTtl => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Chipta yasaydi.
    ///
    /// 🔴 CHAQIRUVCHINING MAJBURIYATI: bu metod HECH QANDAY ruxsatni
    /// tekshirmaydi — u sof kriptografiya. Ruxsat chaqiruvchida
    /// (<c>LessonAssetService.CreateTicketAsync</c>) tekshirilishi SHART.
    /// </summary>
    /// <param name="assetId">Chipta AYNAN shu faylga bog'lanadi.</param>
    /// <param name="userId">Chipta kimning nomidan berilyapti.</param>
    MediaAccessTicket Issue(long assetId, long userId);

    /// <summary>
    /// Chiptani tekshiradi va EGASINING Id'sini qaytaradi.
    ///
    /// <paramref name="assetId"/> imzoga KIRADI: 5-fayl uchun berilgan
    /// chipta 6-faylda ISHLAMAYDI. Aks holda bitta ochiq darsning
    /// chiptasi butun kutubxonani ochib berardi.
    /// </summary>
    /// <returns>
    /// <c>null</c> — chipta yo'q, shakli buzuq, imzosi noto'g'ri, muddati
    /// o'tgan yoki BOSHQA faylga tegishli. Sabab chaqiruvchiga
    /// AYTILMAYDI: barchasi bir xil 401 beradi, aks holda xato xabari
    /// hujumchiga qaysi qadamda adashganini o'rgatardi.
    /// </returns>
    long? TryResolveUserId(string? token, long assetId);
}

/// <summary>
/// Berilgan chipta.
/// </summary>
/// <param name="Token">
/// URL query'siga qo'yishga tayyor qiymat (faqat `A–Z a–z 0–9 . _ -`
/// belgilari — foiz-kodlash TALAB QILINMAYDI).
/// </param>
/// <param name="ExpiresAt">
/// Muddat. ⚠️ KLIENT SHARTNOMASINING BIR QISMI — pleyer shu vaqtdan
/// oldin yangi chipta so'raydi.
/// </param>
public sealed record MediaAccessTicket(string Token, DateTimeOffset ExpiresAt);
