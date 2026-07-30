using Zinnur.Domain.Enums;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// Obyekt omboriga (Cloudflare R2 / S3) yozish PORTI.
///
/// Application qatlami AWS SDK'ni ham, HTTP'ni ham ko'rmaydi — amalga
/// oshirilishi Infrastructure'da.
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
