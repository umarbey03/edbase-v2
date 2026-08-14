using System.Globalization;
using System.IO.Compression;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Media;
using Zinnur.Domain.Enums;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// Namuna (demo) ma'lumotiga biriktiriladigan fayl haqidagi ma'lumot.
/// </summary>
/// <param name="ObjectKey">Ombordagi kalit — <c>LessonAsset.ObjectKey</c> va hokazo.</param>
/// <param name="ContentType">MIME turi.</param>
/// <param name="SizeBytes">Hajm (bayt).</param>
/// <param name="Uploaded">
/// Fayl HAQIQATAN omborga yozildimi.
///
/// 🔴 <c>false</c> bo'lsa — bazada qator bor, LEKIN ombor bo'sh: ekranda
/// element ko'rinadi, ochilganda esa <c>404</c> qaytadi. Bu ONGLI kelishuv
/// (sabab <see cref="DemoMediaSink"/> izohida) va hisobotda alohida
/// ko'rsatiladi, aks holda "nega video ochilmayapti?" degan savol
/// tekshiruvchini soatlab chalg'itardi.
/// </param>
internal sealed record DemoFile(string ObjectKey, string ContentType, long SizeBytes, bool Uploaded);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DEMO FAYLLARI — QAYSI BIRI HAQIQIY, QAYSI BIRI FAQAT QATOR
/// ════════════════════════════════════════════════════════════════════════
///
/// Namuna ma'lumotining bir qismi (dars rasmi, chat biriktirmasi, javob
/// fayli) faqat ombordagi HAQIQIY obyekt bilan ma'noga ega: bazadagi qator
/// o'zi hech nimani ko'rsatmaydi, endpoint faylni ombordan o'qiydi va
/// topmasa <c>404</c> qaytaradi.
///
/// Shuning uchun bu sinf ikki xil ish qiladi:
///
///  • RASM va HUJJAT — baytlar SHU YERDA yasaladi va omborga YOZILADI.
///    Rasm PNG kodlovchisi bilan generatsiya qilinadi (<see cref="DemoPng"/>),
///    ya'ni tashqi fayl, resurs yoki internet KERAK EMAS. Yangi serverda
///    seeder hech narsaga bog'lanmasdan ishlaydi.
///
///  • VIDEO — faqat METAMA'LUMOT. Yaroqli MP4 ni kodsiz yasab bo'lmaydi
///    (ffmpeg yo'q, katta ikkilik blobni manba kodiga tiqish esa repo'ni
///    isitardi). Ko'p qismli pleer, qismlar tartibi, sarlavhalari va
///    davomiyligi — hammasi metama'lumotdan ishlaydi va TEKSHIRILADI;
///    faqat "Play" bosilganda oqim kelmaydi.
///
/// ⚠️ Ombor sozlanmagan bo'lsa (Storage:* bo'sh) rasm ham yozilmaydi —
/// qatorlar baribir yaratiladi, chunki ularsiz butun ekran bo'sh qolardi
/// va tekshiruvchi "biriktirma umuman yo'q" degan xulosaga kelardi.
/// Farqi hisobotda ko'rinadi: <c>Uploaded</c> soni va <c>Synthetic</c> soni.
/// </summary>
internal sealed class DemoMediaSink(IMediaStorage? media, ISubmissionStorage? submissions)
{
    /// <summary>Omborga HAQIQATAN yozilgan fayllar soni.</summary>
    public int Uploaded { get; private set; }

    /// <summary>Faqat qator sifatida yaratilgan (ombor bo'sh) fayllar soni.</summary>
    public int Synthetic { get; private set; }

    /// <summary>Oxirgi ombor xatosi — hisobotda ko'rsatiladi.</summary>
    public string? LastError { get; private set; }

    /// <summary>Media ombori umuman ishlayaptimi (rasm/hujjat yozish mumkinmi).</summary>
    public bool MediaReady => media is { IsConfigured: true };

    /// <summary>O'quvchi javobi fayllari ombori ishlayaptimi.</summary>
    public bool SubmissionsReady => submissions is { IsConfigured: true };

    /// <summary>
    /// Rasm yasaydi va media omboriga yozadi.
    /// </summary>
    /// <param name="folder">Ombor papkasi — servislardagi qiymat bilan BIR XIL.</param>
    /// <param name="width">Kenglik (piksel).</param>
    /// <param name="height">Balandlik (piksel).</param>
    /// <param name="tone">Rang ohangi (0..5) — rasmlar bir-biridan farq qilsin.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    public async Task<DemoFile> ImageAsync(
        string folder, int width, int height, int tone, CancellationToken ct)
    {
        var bytes = DemoPng.Create(width, height, tone);

        return await SaveMediaAsync(folder, "png", "image/png", bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Matnli hujjat yasaydi va media omboriga yozadi.</summary>
    public async Task<DemoFile> DocumentAsync(string folder, string text, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);

        return await SaveMediaAsync(folder, "txt", "text/plain", bytes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// O'quvchi javobiga biriktiriladigan rasm.
    ///
    /// ★ ALOHIDA OMBOR: javob fayllari <see cref="ISubmissionStorage"/> da
    /// saqlanadi (kalit o'quvchi ID'sidan boshlanadi), media fayllari esa
    /// <see cref="IMediaStorage"/> da. Ikkalasini aralashtirsak
    /// <c>GET /submissions/files/{id}</c> faylni topa olmasdi.
    /// </summary>
    public async Task<DemoFile> SubmissionImageAsync(long studentId, CancellationToken ct)
    {
        var bytes = DemoPng.Create(720, 480, tone: 3);

        if (submissions is { IsConfigured: true })
        {
            try
            {
                var key = await submissions
                    .SaveAsync(
                        new SubmissionUpload(studentId, AttachmentKind.Image, "png", "image/png", bytes),
                        ct)
                    .ConfigureAwait(false);

                Uploaded++;
                return new DemoFile(key, "image/png", bytes.Length, Uploaded: true);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        Synthetic++;
        return new DemoFile(FakeKey("submissions", "png"), "image/png", bytes.Length, Uploaded: false);
    }

    /// <summary>
    /// Video uchun FAQAT kalit — baytlar yozilmaydi (sabab sinf izohida).
    /// </summary>
    public DemoFile VideoMetadata(long sizeBytes)
    {
        Synthetic++;
        return new DemoFile(FakeKey("lesson-assets", "mp4"), "video/mp4", sizeBytes, Uploaded: false);
    }

    /// <summary>Dars yozuvi uchun kalit — u ham faqat metama'lumot.</summary>
    public DemoFile RecordingMetadata(long sizeBytes)
    {
        Synthetic++;
        return new DemoFile(FakeKey("recordings", "mp4"), "video/mp4", sizeBytes, Uploaded: false);
    }

    private async Task<DemoFile> SaveMediaAsync(
        string folder, string extension, string contentType, byte[] bytes, CancellationToken ct)
    {
        if (media is { IsConfigured: true })
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);

                var key = await media
                    .SaveAsync(
                        new MediaUpload(folder, extension, contentType, stream, bytes.Length),
                        ct)
                    .ConfigureAwait(false);

                Uploaded++;
                return new DemoFile(key, contentType, bytes.Length, Uploaded: true);
            }
            catch (Exception ex)
            {
                // ⚠️ OMBOR XATOSI SEEDING'NI TO'XTATMAYDI. Aks holda MinIO
                // ko'tarilmagan mashinada BUTUN namuna ma'lumoti yo'qolardi —
                // holbuki uning 95% i umuman faylsiz.
                LastError = ex.Message;
            }
        }

        Synthetic++;
        return new DemoFile(FakeKey(folder, extension), contentType, bytes.Length, Uploaded: false);
    }

    /// <summary>
    /// Ombordagi obyekt YO'Q ekanini KALITNING O'ZI aytib turadi.
    ///
    /// ★ <c>demo-seed/</c> prefiksi ataylab: operator ombordan kalitni
    /// izlab topa olmaganda "bu haqiqiy fayl edi, yo'qolib qolibdi" degan
    /// noto'g'ri xulosaga kelmasin.
    /// </summary>
    private static string FakeKey(string folder, string extension) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"demo-seed/{folder}/{Guid.NewGuid():N}.{extension}");
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ENG KICHIK PNG KODLOVCHI — TASHQI BOG'LIQLIKSIZ
/// ════════════════════════════════════════════════════════════════════════
///
/// NIMA UCHUN QO'LDA YOZILDI:
///
///  1) Namuna rasmi HAQIQIY bo'lishi kerak — brauzer uni ochsin, aks holda
///     "biriktirma" xususiyati tekshirilmagan bo'lib qolardi.
///  2) Tayyor rasm faylini repo'ga qo'shish — ikkilik artefakt (diff'sda
///     ko'rinmaydi, litsenziyasi noaniq, Docker qatlamiga tushadi).
///  3) <c>System.Drawing</c> Linux konteynerida YO'Q, <c>ImageSharp</c> esa
///     butun paket — faqat namuna ma'lumoti uchun bog'liqlik qo'shish
///     ishlab chiqarish image'ini kattalashtirardi.
///
/// PNG formati bu ish uchun juda arzon: sarlavha + zlib bilan siqilgan
/// qatorlar + CRC32. <c>ZLibStream</c> .NET ichida bor, CRC32 esa 12 qator.
/// </summary>
internal static class DemoPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Ohanglar — rasmlar bir xil ko'rinmasin (RGB, yuqori qism).</summary>
    private static readonly (byte R, byte G, byte B)[] Tones =
    [
        (0x1E, 0x88, 0x5E),   // yashil
        (0x1B, 0x5E, 0xA8),   // ko'k
        (0x8E, 0x44, 0xAD),   // siyoh
        (0xC0, 0x6C, 0x1E),   // to'q sariq
        (0x2C, 0x3E, 0x50),   // grafit
        (0xA8, 0x32, 0x4A),   // qizg'ish
    ];

    /// <summary>
    /// Vertikal gradiyentli to'g'ri PNG qaytaradi.
    /// </summary>
    /// <param name="width">Kenglik (piksel).</param>
    /// <param name="height">Balandlik (piksel).</param>
    /// <param name="tone">Ohang indeksi (chegaradan chiqsa aylanadi).</param>
    public static byte[] Create(int width, int height, int tone)
    {
        var (r, g, b) = Tones[Math.Abs(tone) % Tones.Length];

        // Har qator: 1 bayt filtr turi (0 = None) + width * 3 bayt RGB.
        // Filtrsiz kodlash siqilishni yomonlashtiradi, lekin bu yerda hajm
        // muhim emas — TO'G'RILIK muhim, va filtr 0 xato qilish imkonini
        // bermaydi.
        var stride = 1 + (width * 3);
        var raw = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            var offset = y * stride;
            raw[offset] = 0;

            // Pastga tushgan sari yorqinlik oshadi — tekis rangdan farqli
            // o'laroq, bu rasm HAQIQATAN dekodlanganini ko'z bilan tasdiqlaydi.
            var lift = (byte)(40 * y / Math.Max(1, height - 1));

            for (var x = 0; x < width; x++)
            {
                var p = offset + 1 + (x * 3);
                raw[p] = Add(r, lift);
                raw[p + 1] = Add(g, lift);
                raw[p + 2] = Add(b, lift);
            }
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        // IHDR: kenglik, balandlik, bit chuqurligi 8, rang turi 2 (RGB),
        // siqish 0, filtr 0, interlace 0.
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(output, "IHDR"u8, ihdr);

        WriteChunk(output, "IDAT"u8, Deflate(raw));
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
    }

    private static byte Add(byte value, byte lift) => (byte)Math.Min(255, value + lift);

    private static byte[] Deflate(byte[] raw)
    {
        using var buffer = new MemoryStream();

        // ★ `ZLibStream`, `DeflateStream` EMAS: PNG'ning IDAT bo'limi RFC 1950
        //   (zlib sarlavhasi + Adler-32) talab qiladi. `DeflateStream` faqat
        //   RFC 1951 beradi va natijani hech bir dekoder ocha olmasdi.
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return buffer.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);

        output.Write(type);
        output.Write(data);

        // CRC TUR bayti bilan BIRGA hisoblanadi (faqat ma'lumot bo'yicha emas).
        var crc = Crc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, 0, unchecked((int)crc));
        output.Write(crcBytes);
    }

    private static void WriteBigEndian(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            var value = i;

            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;

            table[i] = value;
        }

        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in first)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

        foreach (var value in second)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }
}
