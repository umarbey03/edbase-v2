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
///
/// ── IKKINCHI ISTE'MOLCHI: TUNGI YIG'UVCHI (SPEC-RECORDING-V2) ──────────
///
/// Yuqoridagi hamma narsa yozuvni KO'RSATISH haqida. 2026-09 dan bu portda
/// ikkinchi, butunlay boshqa iste'molchi ham bor — xom fayllarni o'qib,
/// tayyor mp4 ni qaytarib qo'yadigan tungi yig'uvchi. Uning uchun
/// PRESIGNED HAVOLA ISHLATILMAYDI va buning sababi
/// <see cref="OpenReadAsync"/> izohida: ular ikki xil talab, bitta port.
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

    // ════════════════════════════════════════════════════════════════════
    // TUNGI YIG'UVCHI YO'LI (xom fayllar) — SPEC-RECORDING-V2, M4
    // ════════════════════════════════════════════════════════════════════
    //
    // Yuqoridagi a'zolar YOZUVNI KO'RSATISH uchun. Quyidagi to'rttasi esa
    // butunlay boshqa iste'molchiga tegishli: TUNGI YIG'UVCHI (00:00–09:00,
    // `RecordingCompositionWorker`). Dars davomida `TrackEgress` xom
    // fayllarni omborga yozadi (trek videolari + xonaning aralashtirilgan
    // ovozi), kechasi esa ular BITTA mp4 ga yig'ilib, yozuvning ALLAQACHON
    // mavjud `ObjectKey` iga qo'yiladi.
    //
    // ⚠️ ESKI IZOH BEKOR BO'LDI: bu port ilgari ATAYLAB faqat O'QIRDI —
    //    "Egress faylni bizsiz, to'g'ridan-to'g'ri yozadi, ya'ni yuklash
    //    metodi bu yerda bo'lishi ham mumkin emas". `RoomComposite` yo'li
    //    uchun bu HAMON to'g'ri va u o'zgarmadi. `TrackComposition` yo'lida
    //    esa yakuniy faylni Egress emas, BIZ yozamiz — shuning uchun
    //    <see cref="PutAsync"/> paydo bo'ldi.
    //
    // ★ NIMA UCHUN ALOHIDA PORT EMAS: kalit sxemasi
    //   (<see cref="BuildObjectKey"/>, <see cref="BuildRawObjectKey"/>) va
    //   imzo AYNI omborga tegishli. Ikkinchi port yasalsa "yozuv fayli
    //   qayerda turadi?" degan savol ikki joyda javob olardi va ular bir
    //   kun ajralib ketardi.
    //
    // ★ NIMA UCHUN `IMediaStorage` QAYTA ISHLATILMADI: uning
    //   `SaveAsync` metodi kalitni O'ZI yasaydi (`KeyPrefix/lesson-assets/…`)
    //   va uni tashqaridan berib bo'lmaydi. Yig'uvchiga esa AYNAN teskarisi
    //   kerak: fayl bazadagi MAVJUD kalitga tushishi shart, aks holda
    //   o'quvchining havolasi bo'sh joyga qarab qolardi.

    /// <summary>
    /// XOM (raw) trek obyektining kaliti:
    /// <c>raw/{dars}/{yozuv}/{trek}.{kengaytma}</c>.
    ///
    /// ★ NIMA UCHUN ALOHIDA ILDIZ (<c>raw/</c>): xom fayllar hech qachon
    /// foydalanuvchiga berilmaydi va yig'ish tugagach O'CHIRILADI. Ular
    /// <c>recordings/</c> ichida tursa, mavjud vositalar (admin ro'yxati,
    /// bucket'ning umr sikli qoidalari, zaxira skriptlari) ularni "yozuv"
    /// deb ko'rardi va bittasi albatta yarim tayyor faylni o'quvchiga
    /// ko'rsatib qo'yardi.
    ///
    /// ★ NIMA UCHUN KALITDA HAM DARS, HAM YOZUV ID'si: soya rejimida
    /// (A/B) bitta darsda IKKI yozuv qatori bo'ladi, qayta urinishda esa
    /// yana bittasi. Faqat dars ID'si bilan ular BIR-BIRINING xom
    /// fayllarini ustidan yozardi va nosozlik "video yarim joyda uzilib
    /// qoladi" ko'rinishida — sababsiz — chiqardi.
    ///
    /// ⚠️ OYLIK PAPKA ATAYLAB YO'Q (<see cref="BuildObjectKey"/> dan farqli):
    /// xom fayl darsdan keyingi kechagacha (navbat uzun bo'lsa bir necha
    /// kecha) yashaydi va o'chiriladi, ya'ni "papkada o'n minglab obyekt
    /// yig'ilib qolmasin" degan sabab bu yerda ishlamaydi. Tekis sxema esa
    /// tozalashni sodda qiladi: <c>raw/{dars}/{yozuv}/</c> prefiksi butunlay
    /// o'chiriladi.
    ///
    /// ⚠️ TASODIFIY QISM HAM YO'Q: kalit Egress'ga <c>filepath</c> sifatida
    /// beriladi va bazaga yoziladi — ya'ni u OLDINDAN aniq bo'lishi kerak.
    /// Taxmin qilinishidan xavotir ham o'rinsiz: xom faylga presigned havola
    /// HECH QACHON chiqarilmaydi.
    /// </summary>
    /// <param name="trackSid">
    /// LiveKit trek identifikatori (<c>TR_…</c>) yoki xona ovozi uchun
    /// sentinel <c>ROOM</c> (<c>RecordingTrack.RoomAudioSid</c>).
    /// </param>
    /// <param name="extension">
    /// Nuqtasiz kengaytma (<c>webm</c>, <c>mp4</c>, <c>ogg</c>). Nuqta bilan
    /// berilsa ham qabul qilinadi.
    /// </param>
    string BuildRawObjectKey(long sessionId, long recordingId, string trackSid, string extension);

    /// <summary>
    /// Obyektni O'QISHGA ochadi (imzolangan <c>GET</c>, ICHKI manzil).
    ///
    /// 🔴 NIMA UCHUN PRESIGNED HAVOLA EMAS — ffmpeg'ga havola berib
    /// yubormang. Uch sabab, uchalasi ham hal qiluvchi:
    ///
    ///   1) MUDDAT. <see cref="DefaultLinkTtl"/> — 15 daqiqa, kodlash esa
    ///      SOATLAB davom etadi. Havola ish o'rtasida "tugab" qolardi va
    ///      ffmpeg buni faylning oxiri deb qabul qilib, YARIM videoni
    ///      muvaffaqiyat sifatida qaytarardi.
    ///   2) IZLASH. ffmpeg HTTP ustida orqaga-oldinga izlaydi; R2 ga
    ///      bunday so'rovlar sekin va uzilganda yomon qayta uriniladi.
    ///   3) NARX. Cloudflare R2 dan BIZNING serverimizga chiqish trafigi
    ///      BEPUL, ya'ni oqim uzatishning tejamkorlik dalili ham yo'q.
    ///
    /// Javob TANASI KUTILMAYDI — sarlavhalar kelishi bilan oqim qaytariladi
    /// (<c>ResponseHeadersRead</c>). Qaytarilgan qiymat EGALIK QILADI:
    /// chaqiruvchi uni yopishi SHART, aks holda HTTP ulanishi hovuzga
    /// qaytmaydi.
    ///
    /// ⚠️ <c>Range</c> ATAYLAB YO'Q (<c>IMediaStorage</c> dan farqli): xom
    /// fayl BUTUNLAY diskka tushiriladi. Qisman o'qish faqat ffmpeg'ni
    /// tarmoqqa qaytarib bog'lardi — 1-banddagi muammoning aynan o'zi.
    /// </summary>
    /// <returns><c>null</c> — obyekt omborda YO'Q (yig'uvchi bu trekni tashlab ketadi).</returns>
    Task<StoredRecordingObject?> OpenReadAsync(string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Tayyor faylni BERILGAN kalitga qo'yadi (bitta <c>PUT</c>).
    ///
    /// ★ NIMA UCHUN KALIT TASHQARIDAN: yig'uvchi faylni bazadagi MAVJUD
    /// <c>SessionRecording.ObjectKey</c> ga yozadi — o'sha kalit dars
    /// boshlanganda yaratilgan va o'quvchi havolasi ham o'shanga qarab
    /// beriladi. Yangi kalit yasalsa qator bir joyni, fayl boshqa joyni
    /// ko'rsatardi.
    ///
    /// ★ NIMA UCHUN KO'P BO'LAKLI (multipart) YUKLASH EMAS: R2 da bitta
    /// obyekt uchun chegara 5 GiB, tungi natija esa 1–2 GB. Bitta
    /// <c>PUT</c> da kalit YO YO'Q, YO TO'LIQ — ya'ni o'quvchi hech qachon
    /// yarim faylni ko'rmaydi. Ko'p bo'lakli yuklash esa uzilganda
    /// "tugallanmagan yuklash" qoldiqlarini yasardi va ular uchun pul
    /// to'lanardi.
    ///
    /// ⚠️ CHEGARA <c>StorageOptions.LargeUploadTimeoutSeconds</c> (1800 s)
    /// dan olinadi, <c>TimeoutSeconds</c> (60 s) dan EMAS: o'lchangan bitta
    /// dars 1.75 GB chiqqan va 60 soniya unga ~250 Mbit/s doimiy tezlik
    /// talab qilardi.
    /// </summary>
    /// <param name="content">
    /// ⚠️ IZLANADIGAN (seekable) oqim bo'lishi SHART: SigV4 tananing
    /// SHA-256 xeshini talab qiladi, ya'ni oqim ikki marta o'qiladi
    /// (bir marta xesh uchun, bir marta yuborish uchun). Yig'uvchida bu
    /// shart o'z-o'zidan bajariladi — u LOKAL fayl beradi.
    ///
    /// Oqim BOSHIDAN o'qiladi: pozitsiya majburan <c>0</c> ga qaytariladi,
    /// ya'ni "yarim o'qilgan oqim" xeshni tanadan ajratib yubora olmaydi.
    /// </param>
    /// <param name="length">
    /// Obyekt hajmi (<c>Content-Length</c>). Oqim hajmiga MOS bo'lishi
    /// shart — farq qilsa <see cref="InvalidOperationException"/>.
    /// </param>
    Task PutAsync(
        string objectKey,
        Stream content,
        long length,
        string contentType,
        CancellationToken ct = default);

    /// <summary>
    /// Obyektni o'chiradi (xom fayllarni tozalash uchun). Obyekt allaqachon
    /// yo'q bo'lsa XATO BERMAYDI — o'chirish takroriy chaqirilishi NORMAL
    /// holat: tozalash muvaffaqiyatli yig'ishdan KEYIN, alohida qadamda
    /// bajariladi va keyingi kecha uni qaytadan urinib ko'radi.
    /// </summary>
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}

/// <summary>Ombordagi obyekt haqidagi qisqa ma'lumot (<c>HEAD</c> javobi).</summary>
/// <param name="SizeBytes">Hajmi; ombor aytmasa <c>null</c>.</param>
public sealed record StoredObjectInfo(long? SizeBytes);

/// <summary>
/// Ombordan O'QISHGA ochilgan XOM obyekt.
///
/// Oqim bilan BIRGA uni tug'dirgan tashqi resurs (HTTP javobi) ham shu
/// yerda saqlanadi: faqat oqimni yopish YETARLI EMAS — javob obyekti
/// yopilmasa ulanish hovuzga qaytmaydi va sekin soket oqishi paydo bo'ladi.
/// Tungi yig'uvchi bitta darsda 10+ xom faylni ketma-ket ochadi, ya'ni
/// oqish bir necha kechada ulanishlar hovuzini butunlay quritardi.
///
/// ★ NIMA UCHUN <c>StoredMedia</c> (dars videosi porti) QAYTA
/// ISHLATILMADI: unda <c>IsPartial</c> va <c>TotalLength</c> bor —
/// ular FAQAT <c>Range</c> so'rovlari uchun ma'noli. Bu yo'lda esa
/// <c>Range</c> ATAYLAB yo'q (sabab: <see cref="IRecordingStorage.OpenReadAsync"/>),
/// ya'ni o'sha ikki maydon abadiy <c>false</c>/takroriy qiymat bo'lib
/// turardi va birinchi o'qigan odam ularni "demak qisman o'qish bor" deb
/// tushunardi. Bu yerdagi shartnoma ataylab kambag'al: OQIM va HAJM.
/// </summary>
/// <param name="content">Fayl baytlari (tarmoq oqimi — QAYTA O'QILMAYDI).</param>
/// <param name="sizeBytes">Obyekt hajmi; ombor aytmasa <c>null</c>.</param>
/// <param name="owner">Oqim bilan birga yopiladigan tashqi resurs.</param>
public sealed class StoredRecordingObject(
    Stream content,
    long? sizeBytes,
    IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public long? SizeBytes { get; } = sizeBytes;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);

        owner?.Dispose();
    }
}
