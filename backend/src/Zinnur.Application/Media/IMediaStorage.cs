namespace Zinnur.Application.Media;

/// <summary>
/// ========================================================================
/// KATTA MEDIA UCHUN OMBOR PORTI (dars videosi, imtihon rasmi, shart fayli)
/// ========================================================================
///
/// ★ NIMA UCHUN <c>ISubmissionStorage</c> DAN ALOHIDA PORT:
///
///  1) OQIM BILAN YOZISH. `ISubmissionStorage.SaveAsync` faylni
///     <c>ReadOnlyMemory&lt;byte&gt;</c> sifatida oladi — ya'ni BUTUN fayl
///     xotirada bo'lishi shart. 10 MB ovoz uchun bu to'g'ri qaror edi,
///     1 GB video uchun esa halokat: ikki bir vaqtdagi yuklash konteynerni
///     OOM ga olib boradi. Bu port <c>Stream</c> qabul qiladi.
///
///  2) `Range` (QISMAN O'QISH). Videoda oldinga o'tish (seek) uchun brauzer
///     `Range: bytes=…` yuboradi. Mavjud portda bu YO'Q va u ATAYLAB
///     shunday edi (izohi: "tarmoq oqimi izlanmaydi"). Bu yerda esa
///     `Range` OMBORGA UZATILADI — ya'ni izlash ombor tomonida bo'ladi va
///     API xotirasiga faqat so'ralgan bo'lak tushadi.
///
///  3) O'CHIRISH. Vazifa javobi hech qachon o'chirilmaydi (baho yo'qolmasin),
///     dars videosi esa o'chiriladi — ya'ni `DeleteAsync` mavjud portda
///     ATAYLAB yo'q edi.
///
/// ★ PRESIGNED URL ISHLATILMAYDI — loyihaning mavjud qarori
/// (`ISubmissionStorage` izohi va `PROGRESS.md`): presigned havola
/// CHIQARILGACH uni ushlagan har kim ochadi va bekor qilib bo'lmaydi
/// (eski tizimning X-6 kamchiligi). Bu yerda "havola" tushunchasi YO'Q:
/// HAR BAYT SO'ROVI gating va to'lov tekshiruvidan o'tadi
/// (`LessonAssetService.EnsureCanReadAsync`).
///
/// ⚠️ ANIQLIK KIRITISH (WAVE 2, R6): "har bayt so'rovi `Authorization`
/// dan o'tadi" degan avvalgi jumla endi TO'LIQ TO'G'RI EMAS EDI va
/// tuzatildi. Brauzerning `&lt;video src&gt;` elementi `Authorization`
/// sarlavhasini yubora olmagani uchun KIMLIKNI ANIQLASHNING ikkinchi
/// yo'li qo'shildi — qisqa muddatli, FAYLGA BOG'LANGAN chipta
/// (<see cref="IMediaAccessTicketService"/>).
///
/// 🔴 QAROR O'ZGARMADI: chipta faqat "KIM" degan savolga javob beradi,
/// "RUXSATMI" degan savol esa AVVALGIDEK har so'rovda, bazadan hal
/// qilinadi. Ya'ni qaytarib bo'ladigan ruxsat (revocability) — bu
/// portning butun ma'nosi — SAQLANDI: qarzi paydo bo'lgan yoki darsi
/// qulflangan o'quvchi videoni DAVOM ETTIRA OLMAYDI, holbuki presigned
/// havolada u faylni oxirigacha ko'rib bo'lardi.
///
/// ⚠️ TARMOQ NARXI HAQIDA OGOHLANTIRISH: `IRecordingStorage` izohida
/// AYNI savol uchun TESKARI qaror qabul qilingan (dars yozuvi uchun
/// presigned — sabab: 0.5 GB × 20 o'quvchi trafigi LiveKit SFU bilan
/// bitta kanalni bo'lishadi). Dars videosining trafik profili shunga
/// O'XSHASH. Bu port ONGLI ravishda qaytarib bo'ladigan ruxsatni
/// (revocability) tanlaydi; agar kelajakda kanal to'yinsa, yechim —
/// CDN yoki alohida domen, presigned emas.
/// </summary>
public interface IMediaStorage
{
    /// <summary>
    /// Ombor sozlanganmi (manzil, bucket, kalitlar). Sozlanmagan bo'lsa
    /// use-case 503 qaytaradi va LOKAL DISKKA YOZMAYDI (eski tizim shu
    /// yerda lokal diskka yozgani uchun fayllar deploy'da yo'qolardi).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Faylni OQIM bilan saqlaydi va OBYEKT KALITINI qaytaradi (URL emas).
    /// </summary>
    /// <exception cref="Zinnur.Application.Common.Exceptions.ServiceUnavailableException">
    /// Ombor sozlanmagan yoki javob bermayapti.
    /// </exception>
    Task<string> SaveAsync(MediaUpload upload, CancellationToken ct = default);

    /// <summary>
    /// Obyektni O'QISHGA ochadi. <paramref name="range"/> berilsa ombordan
    /// FAQAT o'sha bo'lak so'raladi (`206 Partial Content`).
    ///
    /// Javob TANASI KUTILMAYDI — sarlavhalar kelishi bilan oqim qaytariladi.
    /// Qaytarilgan qiymat EGALIK QILADI: chaqiruvchi uni yopishi SHART,
    /// aks holda HTTP ulanishi hovuzga qaytmaydi.
    /// </summary>
    /// <returns><c>null</c> — obyekt omborda YO'Q (chaqiruvchi 404 qiladi).</returns>
    Task<StoredMedia?> OpenReadAsync(
        string objectKey, MediaByteRange? range = null, CancellationToken ct = default);

    /// <summary>
    /// Obyektni o'chiradi. Obyekt allaqachon yo'q bo'lsa XATO BERMAYDI
    /// (idempotent): o'chirish takroriy chaqirilishi normal holat.
    /// </summary>
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}

/// <summary>
/// Saqlashga tayyor fayl.
/// </summary>
/// <param name="Folder">
/// Ombordagi mantiqiy papka (<c>lesson-assets</c>, <c>assignment-attachments</c>).
/// Kalitning to'liq shakli OMBORNING ishi — use-case uni yasamaydi.
/// </param>
/// <param name="Extension">Nuqtasiz kengaytma, MAZMUNDAN aniqlangan.</param>
/// <param name="ContentType">MAZMUNDAN aniqlangan MIME turi.</param>
/// <param name="Content">
/// ⚠️ IZLANADIGAN (seekable) oqim bo'lishi SHART va pozitsiyasi 0 da
/// turishi kerak: SigV4 imzosi tananing SHA-256 xeshini talab qiladi,
/// ya'ni oqim BIR MARTA xesh uchun, ikkinchi marta yuborish uchun
/// o'qiladi. <c>IFormFile.OpenReadStream()</c> bu shartni bajaradi
/// (ASP.NET katta faylni vaqtinchalik DISKKA buferlaydi).
/// </param>
/// <param name="Length">Fayl hajmi (bayt) — <c>Content-Length</c> uchun.</param>
public sealed record MediaUpload(
    string Folder,
    string Extension,
    string ContentType,
    Stream Content,
    long Length);

/// <summary>
/// So'ralgan bayt oralig'i — IKKI CHEGARASI HAM ANIQ (inclusive).
///
/// ★ NIMA UCHUN "ochiq" oraliq (`bytes=500-`) bu yerda YO'Q: uni oxirgi
/// baytga aylantirish uchun faylning TO'LIQ hajmi kerak, u esa bazada
/// (`SizeBytes`) turadi. Normalizatsiya use-case'da bir marta bajariladi,
/// ombor esa faqat aniq oraliqni biladi — shu tufayli "oxiri qayerda?"
/// degan savol ikki joyda boshqa-boshqa javob olmaydi.
/// </summary>
public sealed record MediaByteRange(long From, long To)
{
    /// <summary>Oraliq uzunligi (bayt).</summary>
    public long Length => (To - From) + 1;
}

/// <summary>
/// Ombordan O'QISHGA ochilgan obyekt.
///
/// Oqim bilan BIRGA uni tug'dirgan tashqi resurs (HTTP javobi) ham shu
/// yerda saqlanadi: faqat oqimni yopish YETARLI EMAS — javob obyekti
/// yopilmasa ulanish hovuzga qaytmaydi va sekin soket oqishi paydo bo'ladi.
/// </summary>
/// <param name="content">Fayl baytlari (tarmoq oqimi — QAYTA O'QILMAYDI).</param>
/// <param name="contentType">Ombor qaytargan MIME turi.</param>
/// <param name="contentLength">SHU javobdagi baytlar soni (qisman bo'lsa — bo'lak uzunligi).</param>
/// <param name="totalLength">Obyektning TO'LIQ hajmi (`Content-Range` dan). Noma'lum bo'lsa <c>null</c>.</param>
/// <param name="isPartial">Ombor <c>206</c> qaytardimi.</param>
/// <param name="owner">Oqim bilan birga yopiladigan tashqi resurs.</param>
public sealed class StoredMedia(
    Stream content,
    string contentType,
    long? contentLength,
    long? totalLength,
    bool isPartial,
    IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public string ContentType { get; } = contentType;

    public long? ContentLength { get; } = contentLength;

    public long? TotalLength { get; } = totalLength;

    /// <summary>
    /// Ombor QISMAN javob qaytardimi. Chaqiruvchi `206` yoki `200`
    /// tanlashda AYNAN shu qiymatga tayanadi — o'zi taxmin qilmaydi.
    /// </summary>
    public bool IsPartial { get; } = isPartial;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);

        owner?.Dispose();
    }
}
