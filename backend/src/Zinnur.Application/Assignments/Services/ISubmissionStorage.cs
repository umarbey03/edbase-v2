using Zinnur.Domain.Enums;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// Obyekt omboriga (Cloudflare R2 / S3) yozish va undan O'QISH PORTI.
///
/// Application qatlami AWS SDK'ni ham, HTTP'ni ham ko'rmaydi — amalga
/// oshirilishi Infrastructure'da.
///
/// ========================================================================
/// ★ NIMA UCHUN "PRESIGNED URL" EMAS, BALKI OQIM (PROXY) — ASOSLASH
/// ========================================================================
/// Faylni ustozga ko'rsatishning ikki yo'li bor edi:
///
///   A) PRESIGNED GET URL — API qisqa muddatli imzolangan havola qaytaradi,
///      brauzer faylni TO'G'RIDAN-TO'G'RI ombordan oladi. API trafikni
///      ko'tarmaydi.
///
///   B) OQIM (proxy) — API har so'rovda ruxsatni tekshiradi va fayl
///      baytlarini o'zi uzatadi.
///
/// TANLANDI: B. Uchta sabab, uchalasi ham amaliy:
///
///   1) RUXSAT HAR SO'ROVDA TEKSHIRILADI. Presigned havola — bu MUDDATLI
///      KALIT: u chiqarilgach uni ushlagan HAR KIM (Telegramga tashlangan
///      link, brauzer tarixi, proksi jurnali) faylni ocha oladi va biz buni
///      bekor qila olmaymiz. Eski tizimning X-6 kamchiligi aynan shu edi —
///      `/media` katalogi autentifikatsiyasiz ochiq bo'lib, havolani bilgan
///      istalgan odam o'quvchining ishini ko'rardi. Bu yerda esa fayl
///      "havolasi" YO'Q: `GET /api/v1/submissions/files/{id}` har safar
///      `Authorization` va ruxsat qoidasidan o'tadi.
///
///   2) OMBOR MANZILI BRAUZERGA OCHIQ BO'LISHI SHART EMAS. Presigned URL
///      ishlashi uchun ombor internetdan ko'rinishi va bizda IKKI manzil
///      (ichki + brauzerga beriladigan) bo'lishi kerak edi — xuddi LiveKit
///      `Url`/`PublicUrl` juftligi kabi. SigV4 imzosi HOST bilan
///      bog'langani uchun ular chalkashsa imzo jimgina buziladi. Dev'da
///      MinIO umuman docker tarmog'ida turadi. Proxy'da ombor MANZILI
///      hech qachon tashqariga chiqmaydi.
///
///   3) TRAFIK QO'RQINCHLI EMAS. Fayl chegarasi 5-10 MB, javobga ko'pi
///      bilan 5 ta ilova, ustoz ularni kuniga bir marta ochadi. Bu API
///      uchun sezilarsiz yuk; jonli dars video oqimi LiveKit'da,
///      API'dan o'tmaydi.
/// </summary>
public interface ISubmissionStorage
{
    /// <summary>
    /// Ombor sozlanganmi (bucket + kalitlar bor).
    ///
    /// NIMA UCHUN OSHKOR: sozlanmagan bo'lsa servis 503 qaytaradi va
    /// LOKAL DISKKA YOZMAYDI. Eski tizim aynan shu yerda lokal diskka
    /// yozardi ("keyinchalik R2 ga o'tamiz") va natijada fayllar bitta
    /// konteynerga bog'lanib qoldi: ikkinchi replika ko'tarilganda
    /// o'quvchining rasmi 404 bera boshlardi, deploy'da esa butunlay
    /// yo'qolardi.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Faylni saqlaydi va OBYEKT KALITINI qaytaradi (to'liq URL emas).
    /// </summary>
    Task<string> SaveAsync(SubmissionUpload upload, CancellationToken ct = default);

    /// <summary>
    /// Obyektni O'QISH uchun ochadi. Ombor javobi TO'LIQ KUTILMAYDI —
    /// sarlavhalar kelishi bilan oqim qaytariladi va baytlar klientga
    /// bevosita uzatiladi. Aks holda 10 MB ovoz avval butunlay API
    /// xotirasiga tushardi va bir necha bir vaqtdagi so'rov konteynerni
    /// OOM ga olib borardi (yuklashda buni <c>SubmissionAttachmentReader</c>
    /// hal qilgan — o'qishda ham AYNI qoida).
    ///
    /// <c>null</c> — obyekt omborda YO'Q. Bu bazadagi yozuv bilan ombor
    /// mazmuni ajralib qolganini bildiradi (masalan bucket qo'lda
    /// tozalangan); chaqiruvchi buni 404 ga aylantiradi.
    ///
    /// Qaytarilgan qiymat EGALIK QILADI: chaqiruvchi uni
    /// <c>await using</c> yoki javob tugagach o'chirish ro'yxati orqali
    /// yopishi SHART, aks holda HTTP ulanishi hovuzga qaytmaydi.
    /// </summary>
    /// <exception cref="Zinnur.Application.Common.Exceptions.ServiceUnavailableException">
    /// Ombor sozlanmagan yoki javob bermayapti.
    /// </exception>
    Task<StoredFile?> OpenReadAsync(string objectKey, CancellationToken ct = default);
}

/// <summary>
/// Ombordan O'QISHGA ochilgan obyekt.
///
/// Oqim bilan BIRGA uni tug'dirgan tashqi resurs (HTTP javobi) ham shu
/// yerda saqlanadi: faqat oqimni yopish YETARLI EMAS, javob obyekti
/// yopilmasa ulanish hovuzga qaytmaydi va sekin sizib boruvchi soket
/// oqishi paydo bo'lardi.
/// </summary>
/// <param name="content">Fayl baytlari (odatda tarmoq oqimi — QAYTA O'QILMAYDI).</param>
/// <param name="contentType">Ombor qaytargan MIME turi.</param>
/// <param name="sizeBytes">Ma'lum bo'lsa — hajm (<c>Content-Length</c>).</param>
/// <param name="owner">Oqim bilan birga yopiladigan tashqi resurs.</param>
public sealed class StoredFile(
    Stream content,
    string contentType,
    long? sizeBytes,
    IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public string ContentType { get; } = contentType;

    public long? SizeBytes { get; } = sizeBytes;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);

        owner?.Dispose();
    }
}

/// <summary>
/// Saqlashga tayyor fayl: hajmi allaqachon TEKSHIRILGAN, turi MAZMUNIDAN
/// aniqlangan (klient sarlavhasiga ishonilmaydi).
/// </summary>
/// <param name="StudentId">Kalit prefiksida ishlatiladi (kim yuklagani ko'rinib turadi).</param>
/// <param name="Extension">Nuqtasiz kengaytma (<c>jpg</c>, <c>m4a</c>).</param>
/// <param name="ContentType">MAZMUNDAN aniqlangan MIME turi.</param>
public sealed record SubmissionUpload(
    long StudentId,
    AttachmentKind Kind,
    string Extension,
    string ContentType,
    ReadOnlyMemory<byte> Content);
