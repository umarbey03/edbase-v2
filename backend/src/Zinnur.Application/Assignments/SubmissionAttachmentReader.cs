using System.Buffers;
using System.Globalization;
using System.Text;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Assignments;

/// <summary>
/// Klientdan kelgan bitta fayl — HTTP'dan MUSTAQIL ko'rinish.
/// (Application qatlami <c>IFormFile</c> ni ko'rmaydi: u ASP.NET turi.)
/// </summary>
/// <param name="ClientContentType">
/// Klient AYTGAN tur. QARORGA ASOS BO'LMAYDI — faqat xato xabarida ishlatiladi.
/// Uni istalgan foydalanuvchi xohlagan qiymatga o'zgartira oladi.
/// </param>
public sealed record IncomingFile(string? FileName, string? ClientContentType, Stream Content);

/// <summary>O'qilgan va tekshirilgan ilova.</summary>
public sealed record ReadAttachment(
    AttachmentKind Kind,
    string ContentType,
    string Extension,
    ReadOnlyMemory<byte> Content);

/// <summary>
/// ========================================================================
/// ★ FAYL YUKLASH — ESKI TIZIMNING Q-2 BUGI SHU YERDA TUZATILGAN
/// ========================================================================
///
/// ESKI KOD (`app/services/storage.py`):
///
///     data = await file.read()                       # ← BUTUN fayl xotiraga
///     if len(data) &gt; max_mb * 1024 * 1024:           # ← chegara KEYIN
///         raise HTTPException(400, err)
///
/// Ya'ni chegara tekshiruvi faylni XOTIRAGA OLGANDAN KEYIN turardi va
/// hech nimani himoya qilmasdi: 2 GB fayl yuborilsa server uni to'liq
/// o'qib, xotirasi tugab yiqilardi (bir necha bir vaqtdagi so'rov —
/// butun konteyner OOM). Chegara bor edi, lekin FOYDASIZ edi.
///
/// SHU YERDAGI QOIDALAR:
///
///  1) Chegara O'QISH DAVOMIDA tekshiriladi va oshgani ANIQLANGAN ZAHOTI
///     o'qish TO'XTATILADI — qolgan baytlar umuman o'qilmaydi. Xotirada
///     ko'pi bilan "chegara + bitta bufer" bo'ladi.
///
///  2) Tur MAZMUNDAN aniqlanadi (sehrli baytlar), klient sarlavhasidan EMAS.
///     Eski tizim `file.content_type` ga ishonardi — uni har qanday klient
///     yozib yubora oladi, ya'ni `.exe` ni "image/png" deb yuklash mumkin edi.
///
///  3) Chegara TURGA bog'liq (rasm 5 MB, ovoz 10 MB). Shuning uchun avval
///     sarlavha baytlari o'qiladi, tur aniqlanadi, KEYIN aynan o'sha
///     turning chegarasi bilan oqim davom etadi.
/// </summary>
public static class SubmissionAttachmentReader
{
    /// <summary>Rasm chegarasi (telefonda olingan surat odatda 2-4 MB).</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>Ovoz chegarasi (1 daqiqalik webm/opus ~1 MB).</summary>
    public const int MaxAudioBytes = 10 * 1024 * 1024;

    /// <summary>Eng katta ruxsat etilgan hajm — controller'dagi so'rov chegarasi shunga tayanadi.</summary>
    public const int MaxAnyBytes = MaxAudioBytes;

    /// <summary>Tur aniqlash uchun yetarli sarlavha (`ftyp` brendi 12-baytda).</summary>
    private const int HeaderSize = 32;

    private const int CopyBufferSize = 64 * 1024;

    /// <summary>
    /// Faylni o'qiydi, hajmini OQIM DAVOMIDA tekshiradi va turini mazmunidan
    /// aniqlaydi.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Fayl bo'sh, turi qo'llanmaydi yoki hajmi chegaradan oshgan.
    /// </exception>
    public static async Task<ReadAttachment> ReadAsync(IncomingFile file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var header = new byte[HeaderSize];

        // Sarlavha: EOF'da xato bermaydi (kichik fayl ham bo'lishi mumkin).
        var headerLength = await file.Content
            .ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, ct)
            .ConfigureAwait(false);

        if (headerLength == 0)
            throw Invalid("Fayl bo'sh.");

        // TUR MAZMUNDAN aniqlanadi — klient sarlavhasi hisobga OLINMAYDI.
        if (!TrySniff(header.AsSpan(0, headerLength), out var kind, out var contentType, out var extension))
        {
            throw Invalid(
                "Faylning turi qo'llab-quvvatlanmaydi. Rasm (jpg, png, webp, gif, heic) "
                + "yoki ovoz (mp3, m4a, ogg, webm, wav) yuboring. "
                + $"Klient aytgan tur: {Describe(file.ClientContentType)}.");
        }

        var limit = kind == AttachmentKind.Audio ? MaxAudioBytes : MaxImageBytes;

        if (headerLength > limit)
            throw TooLarge(kind, limit);

        // Sig'imi oldindan berilgan bufer: chegaradan katta o'smaydi, shuning
        // uchun MemoryStream ichki massivini qayta-qayta ikkilantirmaydi.
        using var buffer = new MemoryStream(Math.Min(limit, InitialCapacity));

        buffer.Write(header, 0, headerLength);
        var total = (long)headerLength;

        var chunk = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            while (true)
            {
                var read = await file.Content.ReadAsync(chunk.AsMemory(0, CopyBufferSize), ct)
                    .ConfigureAwait(false);

                if (read == 0) break;

                total += read;

                // ★ CHEGARA O'QISH DAVOMIDA. Oshdi — DARHOL to'xtaymiz va
                //   qolgan baytlarni umuman o'qimaymiz (eski tizim esa avval
                //   butun faylni yutib, keyin tekshirardi).
                if (total > limit)
                    throw TooLarge(kind, limit);

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        // `GetBuffer()` NUSXA OLMAYDI (ToArray() esa butun faylni ikkinchi
        // marta xotiraga ko'chiradi — 10 MB fayl 20 MB bo'lib ketardi).
        return new ReadAttachment(
            kind, contentType, extension, buffer.GetBuffer().AsMemory(0, (int)total));
    }

    /// <summary>
    /// Bir nechta faylni ketma-ket o'qiydi va SONINI cheklaydi.
    ///
    /// Ketma-ket (parallel emas): parallel bo'lsa 5 fayl × 10 MB = 50 MB
    /// bir vaqtda xotirada bo'lardi.
    /// </summary>
    public static async Task<List<ReadAttachment>> ReadAllAsync(
        IReadOnlyList<IncomingFile> files, int maxCount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count > maxCount)
        {
            throw Invalid(
                "Bitta javobga ko'pi bilan "
                + maxCount.ToString(CultureInfo.InvariantCulture)
                + " ta fayl ilova qilinadi.");
        }

        var result = new List<ReadAttachment>(files.Count);

        foreach (var file in files)
            result.Add(await ReadAsync(file, ct).ConfigureAwait(false));

        return result;
    }

    /// <summary>Ruxsat etilgan formatlarni javob mazmunidan yig'adi (Domain tekshiruvi uchun).</summary>
    public static AnswerFormats DescribeFormats(bool hasText, IReadOnlyList<ReadAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        var formats = hasText ? AnswerFormats.Text : AnswerFormats.None;

        foreach (var attachment in attachments)
        {
            formats |= attachment.Kind == AttachmentKind.Audio
                ? AnswerFormats.Audio
                : AnswerFormats.Image;
        }

        return formats;
    }

    // ================================================================= sehrli baytlar

    /// <summary>
    /// Fayl turini SEHRLI BAYTLARDAN aniqlaydi.
    ///
    /// Ro'yxat ataylab QISQA: faqat o'quvchi telefoni va brauzeri haqiqatan
    /// hosil qiladigan formatlar. "Nomaʼlum bo'lsa ruxsat berish" TAQIQ —
    /// noma'lum fayl rad etiladi (ruxsat ro'yxati, taqiq ro'yxati emas).
    /// </summary>
    private static bool TrySniff(
        ReadOnlySpan<byte> header,
        out AttachmentKind kind,
        out string contentType,
        out string extension)
    {
        kind = AttachmentKind.Image;
        contentType = string.Empty;
        extension = string.Empty;

        // ---- rasmlar ----
        if (Starts(header, [0xFF, 0xD8, 0xFF]))
            return Set(AttachmentKind.Image, "image/jpeg", "jpg", out kind, out contentType, out extension);

        if (Starts(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return Set(AttachmentKind.Image, "image/png", "png", out kind, out contentType, out extension);

        if (StartsAscii(header, "GIF8"))
            return Set(AttachmentKind.Image, "image/gif", "gif", out kind, out contentType, out extension);

        // RIFF konteyneri: 8-baytdan boshlab turi yozilgan (WEBP yoki WAVE).
        if (StartsAscii(header, "RIFF") && header.Length >= 12)
        {
            if (AsciiAt(header, 8, "WEBP"))
                return Set(AttachmentKind.Image, "image/webp", "webp", out kind, out contentType, out extension);

            if (AsciiAt(header, 8, "WAVE"))
                return Set(AttachmentKind.Audio, "audio/wav", "wav", out kind, out contentType, out extension);
        }

        // ISO-BMFF (`....ftypXXXX`): HEIC (iPhone surati) va MP4/M4A (iOS ovozi)
        // bir xil konteynerda — ularni BREND ajratadi.
        if (header.Length >= 12 && AsciiAt(header, 4, "ftyp"))
        {
            var brand = Encoding.ASCII.GetString(header.Slice(8, 4)).ToUpperInvariant();

            if (HeicBrands.Contains(brand, StringComparer.Ordinal))
                return Set(AttachmentKind.Image, "image/heic", "heic", out kind, out contentType, out extension);

            // Qolgan ISO-BMFF brendlari (M4A, MP42, ISOM...) — ovoz/video.
            // MediaRecorder iOS Safari'da aynan shu shaklni beradi.
            return Set(AttachmentKind.Audio, "audio/mp4", "m4a", out kind, out contentType, out extension);
        }

        // ---- ovoz ----
        if (StartsAscii(header, "ID3"))
            return Set(AttachmentKind.Audio, "audio/mpeg", "mp3", out kind, out contentType, out extension);

        // MPEG freym sarlavhasi: 11 bit sinxron (FF Ex/Fx).
        if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return Set(AttachmentKind.Audio, "audio/mpeg", "mp3", out kind, out contentType, out extension);

        if (StartsAscii(header, "OggS"))
            return Set(AttachmentKind.Audio, "audio/ogg", "ogg", out kind, out contentType, out extension);

        // EBML (Matroska/WebM). Brauzerning `MediaRecorder` ovoz yozuvi —
        // odatda `audio/webm;codecs=opus`.
        if (Starts(header, [0x1A, 0x45, 0xDF, 0xA3]))
            return Set(AttachmentKind.Audio, "audio/webm", "webm", out kind, out contentType, out extension);

        return false;
    }

    private static bool Set(
        AttachmentKind value, string mime, string ext,
        out AttachmentKind kind, out string contentType, out string extension)
    {
        kind = value;
        contentType = mime;
        extension = ext;
        return true;
    }

    private static bool Starts(ReadOnlySpan<byte> header, ReadOnlySpan<byte> prefix) =>
        header.Length >= prefix.Length && header[..prefix.Length].SequenceEqual(prefix);

    private static bool StartsAscii(ReadOnlySpan<byte> header, string prefix) =>
        AsciiAt(header, 0, prefix);

    private static bool AsciiAt(ReadOnlySpan<byte> header, int offset, string value)
    {
        if (header.Length < offset + value.Length) return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (header[offset + i] != (byte)value[i]) return false;
        }

        return true;
    }

    private static ValidationException TooLarge(AttachmentKind kind, int limit)
    {
        var megabytes = (limit / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);
        var what = kind == AttachmentKind.Audio ? "Ovoz" : "Rasm";

        return Invalid($"{what} hajmi {megabytes} MB dan oshmasligi kerak.");
    }

    private static string Describe(string? clientContentType) =>
        string.IsNullOrWhiteSpace(clientContentType) ? "ko'rsatilmagan" : clientContentType;

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["files"] = [message] });

    /// <summary>Ko'p fayl kichik bo'ladi — 64 KB dan boshlanadi va kerak bo'lsa o'sadi.</summary>
    private const int InitialCapacity = 64 * 1024;

    private static readonly string[] HeicBrands = ["HEIC", "HEIX", "HEVC", "HEVX", "MIF1", "MSF1"];
}
