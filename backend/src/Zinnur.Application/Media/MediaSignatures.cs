using System.Text;

namespace Zinnur.Application.Media;

/// <summary>
/// Media turkumi. Bayroqlar — chaqiruvchi bir vaqtda bir nechtasini
/// ruxsat etishi mumkin (masalan vazifa sharti: rasm, audio va hujjat).
/// </summary>
[Flags]
public enum MediaCategories
{
    None = 0,
    Image = 1,
    Audio = 2,
    Video = 4,
    Document = 8,
}

/// <summary>Mazmundan ANIQLANGAN fayl turi.</summary>
/// <param name="Category">Turkum (rasm/audio/video/hujjat).</param>
/// <param name="ContentType">MIME turi — bazaga va javobga AYNAN shu yoziladi.</param>
/// <param name="Extension">Nuqtasiz kengaytma (<c>mp4</c>, <c>jpg</c>).</param>
public sealed record MediaSignature(MediaCategories Category, string ContentType, string Extension);

/// <summary>
/// ========================================================================
/// SEHRLI BAYTLAR — PLATFORMADAGI YAGONA TUR ANIQLASH JOYI
/// ========================================================================
///
/// ★ NIMA UCHUN MARKAZLASHTIRILDI: bu jadval ilgari
/// <see cref="Zinnur.Application.Assignments.SubmissionAttachmentReader"/>
/// ICHIDA edi. Dars videosi va vazifa sharti biriktirmasi qo'shilganda uni
/// nusxalash kerak bo'lardi — ya'ni bir nechta "ruxsat etilgan turlar"
/// ro'yxati paydo bo'lardi va bir kuni ulardan biri yangi formatni
/// (masalan iPhone'ning HEIC surati) tanimay qolardi. Endi jadval BITTA,
/// har chaqiruvchi esa faqat O'ZIGA kerakli TURKUMLARNI ruxsat etadi.
///
/// ★ QOIDALAR:
///
///  1) KENGAYTMAGA VA `Content-Type` SARLAVHASIGA ISHONILMAYDI. Ikkalasini
///     ham istalgan klient xohlagan qiymatga yozib yubora oladi: `.mp4` deb
///     nomlangan fayl ichida PDF (yoki bajariladigan kod) bo'lishi mumkin.
///     Faqat MAZMUN hisobga olinadi.
///
///  2) "NOMA'LUM BO'LSA RUXSAT BERISH" TAQIQ. Bu RUXSAT ro'yxati (allow
///     list), taqiq ro'yxati emas — noma'lum fayl rad etiladi.
///
///  3) IKKI MA'NOLI KONTEYNERLAR (ISO-BMFF va Matroska) chaqiruvchining
///     ruxsat to'plamiga qarab hal qiladi. Sabab quyida.
///
/// ── ★★ IKKI MA'NOLI KONTEYNERLAR — NOZIK JOY ──────────────────────────
///
/// `ftyp` (ISO-BMFF) va EBML (Matroska/WebM) konteynerlarida audio ham,
/// video ham AYNI sehrli baytlar bilan boshlanadi. Ularni faqat konteyner
/// ichini tahlil qilib ajratish mumkin — bu esa yuzlab qator kod va yangi
/// xato manbai.
///
/// Shuning uchun: turkum CHAQIRUVCHINING ruxsat to'plamidan aniqlanadi va
/// AUDIO USTUN turadi (agar ikkalasi ham ruxsat etilgan bo'lsa).
///
/// 🔴 AUDIO NEGA USTUN: iOS Safari'dagi `MediaRecorder` OVOZ yozuvini
/// `ftyp` konteynerida, ba'zan VIDEO brendi (`mp42`, `isom`) bilan beradi.
/// Agar video ustun bo'lsa, o'quvchining ovozli javobi "video" deb
/// topilib, javob topshirish yo'lida RAD ETILARDI — va sabab hech qayerda
/// ko'rinmasdi. Bu xatti-harakat ATAYLAB, mavjud (isbotlangan) yo'lni
/// saqlab qolish uchun shunday: javob topshirish faqat `Image|Audio` ni
/// ruxsat etadi, dars videosi esa faqat `Video` ni — ya'ni har ikki yo'lda
/// natija BIR MA'NOLI.
/// </summary>
public static class MediaSignatures
{
    /// <summary>Tur aniqlash uchun yetarli sarlavha (`ftyp` brendi 12-baytda).</summary>
    public const int HeaderSize = 32;

    /// <summary>
    /// Fayl turini SEHRLI BAYTLARDAN aniqlaydi.
    /// </summary>
    /// <param name="header">Faylning boshidagi baytlar (32 bayt yetarli).</param>
    /// <param name="allowed">
    /// Chaqiruvchi qabul qiladigan turkumlar. Ikki ma'noli konteynerlar
    /// AYNAN shu to'plam bo'yicha hal qilinadi (izoh: sinf sarlavhasida).
    /// </param>
    /// <param name="signature">Topilgan tur.</param>
    /// <returns>
    /// <c>false</c> — tur noma'lum YOKI ruxsat etilgan turkumga kirmaydi.
    /// </returns>
    public static bool TryDetect(
        ReadOnlySpan<byte> header, MediaCategories allowed, out MediaSignature signature)
    {
        signature = null!;

        if (!TryRead(header, allowed, out var found))
            return false;

        // Turkum ruxsat etilganmi — YAKUNIY darvoza. Bu tekshiruv bitta
        // joyda: har chaqiruvchi o'zi tekshirsa, biri unutardi.
        if ((found.Category & allowed) == MediaCategories.None)
            return false;

        signature = found;
        return true;
    }

    private static bool TryRead(
        ReadOnlySpan<byte> header, MediaCategories allowed, out MediaSignature signature)
    {
        // ---------------------------------------------------------- rasmlar
        if (Starts(header, [0xFF, 0xD8, 0xFF]))
            return Found(MediaCategories.Image, "image/jpeg", "jpg", out signature);

        if (Starts(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return Found(MediaCategories.Image, "image/png", "png", out signature);

        if (AsciiAt(header, 0, "GIF8"))
            return Found(MediaCategories.Image, "image/gif", "gif", out signature);

        // RIFF konteyneri: 8-baytdan boshlab turi yozilgan (WEBP yoki WAVE).
        if (AsciiAt(header, 0, "RIFF") && header.Length >= 12)
        {
            if (AsciiAt(header, 8, "WEBP"))
                return Found(MediaCategories.Image, "image/webp", "webp", out signature);

            if (AsciiAt(header, 8, "WAVE"))
                return Found(MediaCategories.Audio, "audio/wav", "wav", out signature);
        }

        // ---------------------------------------------------------- ISO-BMFF (`....ftypXXXX`)
        if (header.Length >= 12 && AsciiAt(header, 4, "ftyp"))
            return IsoBaseMedia(header, allowed, out signature);

        // ---------------------------------------------------------- audio
        if (AsciiAt(header, 0, "ID3"))
            return Found(MediaCategories.Audio, "audio/mpeg", "mp3", out signature);

        // MPEG freym sarlavhasi: 11 bit sinxron (FF Ex/Fx).
        if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return Found(MediaCategories.Audio, "audio/mpeg", "mp3", out signature);

        if (AsciiAt(header, 0, "OggS"))
            return Found(MediaCategories.Audio, "audio/ogg", "ogg", out signature);

        // ---------------------------------------------------------- EBML (Matroska / WebM)
        if (Starts(header, [0x1A, 0x45, 0xDF, 0xA3]))
        {
            // Audio USTUN — sabab sinf sarlavhasidagi izohda.
            return allowed.HasFlag(MediaCategories.Audio)
                ? Found(MediaCategories.Audio, "audio/webm", "webm", out signature)
                : Found(MediaCategories.Video, "video/webm", "webm", out signature);
        }

        // ---------------------------------------------------------- hujjat
        if (AsciiAt(header, 0, "%PDF-"))
            return Found(MediaCategories.Document, "application/pdf", "pdf", out signature);

        signature = null!;
        return false;
    }

    /// <summary>
    /// ISO Base Media (`ftyp`) konteyneri: HEIC (iPhone surati), MP4/M4A
    /// va QuickTime bir xil sehrli baytlarga ega — ularni BREND ajratadi.
    /// </summary>
    private static bool IsoBaseMedia(
        ReadOnlySpan<byte> header, MediaCategories allowed, out MediaSignature signature)
    {
        var brand = Encoding.ASCII.GetString(header.Slice(8, 4)).ToUpperInvariant();

        // HEIC — BIR MA'NOLI rasm (iPhone standart surat formati).
        if (HeicBrands.Contains(brand, StringComparer.Ordinal))
            return Found(MediaCategories.Image, "image/heic", "heic", out signature);

        // QuickTime — brend `qt  ` (oxirida ikki probel). Video bo'lsa
        // `video/quicktime`, aks holda audio yo'liga tushadi (pastda).
        var isQuickTime = string.Equals(brand, "QT  ", StringComparison.Ordinal);

        // Audio USTUN — sabab sinf sarlavhasidagi izohda.
        if (allowed.HasFlag(MediaCategories.Audio))
            return Found(MediaCategories.Audio, "audio/mp4", "m4a", out signature);

        return isQuickTime
            ? Found(MediaCategories.Video, "video/quicktime", "mov", out signature)
            : Found(MediaCategories.Video, "video/mp4", "mp4", out signature);
    }

    private static bool Found(
        MediaCategories category, string contentType, string extension,
        out MediaSignature signature)
    {
        signature = new MediaSignature(category, contentType, extension);
        return true;
    }

    private static bool Starts(ReadOnlySpan<byte> header, ReadOnlySpan<byte> prefix) =>
        header.Length >= prefix.Length && header[..prefix.Length].SequenceEqual(prefix);

    private static bool AsciiAt(ReadOnlySpan<byte> header, int offset, string value)
    {
        if (header.Length < offset + value.Length) return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (header[offset + i] != (byte)value[i]) return false;
        }

        return true;
    }

    /// <summary>iPhone surati va HEIF hosilalari brendlari.</summary>
    private static readonly string[] HeicBrands =
        ["HEIC", "HEIX", "HEVC", "HEVX", "MIF1", "MSF1"];
}
