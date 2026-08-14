using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// GURUH CHATI XABARIGA BIRIKTIRILGAN FAYL (rasm / ovoz / hujjat) — R16b
/// ========================================================================
///
/// Talab: *"telegram chat kabi bir xil ishlasin, emoji, rasm, fayl yuklash
/// possible"*. Emoji 1-to'lqinda tayyor bo'ldi (u shunchaki matn), fayl esa
/// yangi jadval talab qiladi.
///
/// ★ NIMA UCHUN ALOHIDA JADVAL, <c>GroupChatMessage</c> GA USTUN EMAS:
/// bitta xabarga BIR NECHTA fayl biriktiriladi (Telegram'dagi "albom"),
/// ya'ni <c>ObjectKey</c> ustuni bittagina qiymat uchun yetmasdi — AYNAN
/// shu sabab <see cref="AssignmentAttachment"/> da ham yozilgan
/// (<c>Assignment.ImageKey</c> bitta rasm edi).
///
/// ★ NIMA UCHUN <see cref="SubmissionFile"/> YOKI
/// <see cref="AssignmentAttachment"/> QAYTA ISHLATILMADI: ular BOSHQA
/// ruxsat qoidasiga bo'ysunadi. Vazifa javobining faylini faqat EGASI va
/// uning ustozi ko'radi; chat biriktirmasini esa BUTUN <c>(guruh, kanal)</c>
/// oqimi ko'radi — ya'ni ruxsat "kim yubordi" dan EMAS, "qaysi oqimga
/// yozildi" dan kelib chiqadi. Bir jadvalga qo'shilsa, bitta <c>WHERE</c> ni
/// unutish ikki yo'nalishda ham xato berardi: yo o'quvchining shaxsiy ishi
/// butun guruhga ochilardi, yo chat rasmi hech kimga ko'rinmasdi.
///
/// ── 🔴 XABAR BILAN BIR TRANZAKSIYADA TUG'ILADI ────────────────────────
///
/// <c>MessageId</c> — MAJBURIY (nullable EMAS). Ya'ni "yuklandi, lekin
/// yuborilmadi" degan oraliq holat UMUMAN mavjud emas.
///
/// ★ NIMA UCHUN BU MUHIM: muqobil dizayn ("avval yukla -> id ol -> keyin
/// shu id bilan xabar yubor") HAR BEKOR QILINGAN yozishda ombordagi
/// obyektni YETIM qoldirardi — foydalanuvchi rasmni tanlab, keyin fikridan
/// qaytsa, R2'da to'lanadigan obyekt MANGU qolardi. Uni tozalash uchun
/// yana bir fon vazifasi ("egasiz biriktirmalarni supurish") kerak
/// bo'lardi. Bu yerda esa yuklash va xabar BITTA
/// <c>multipart</c> so'rovda, BITTA <c>SaveChanges</c> bilan yoziladi;
/// baza qabul qilmasa, use-case omborga yozilgan obyektlarni O'ZI
/// o'chiradi.
///
/// ⚠️ NARXI (ochiq yozib qo'yiladi): yuklash progressi FAYL BOSHIGA emas,
/// BUTUN so'rov bo'yicha ko'rinadi va "yozayotganda fonda yuklab turish"
/// mumkin emas. Telegram'dan farqi shu. Bu ONGLI almashuv: yetim obyekt
/// PUL va uni hech kim sezmaydi, progress esa — qulaylik.
///
/// ── 🔴 O'CHIRISH VA YETIM OBYEKTLAR ───────────────────────────────────
///
/// Qator UCH yo'l bilan yo'qoladi va uchalasi ham OMBORDAGI obyektni
/// o'chirmaydi (baza R2 haqida hech nima bilmaydi):
///
///   1) <c>ChatRetentionJob</c> — N oydan eski xabarlarni QATTIQ o'chiradi.
///      ★ TUZATILDI: vazifa endi paketni o'chirishdan OLDIN o'sha
///        xabarlarning <c>ObjectKey</c> larini o'qib, ularni ombordan
///        o'chiradi. Bu eng muhim yo'l, chunki u TAKRORLANIB turadi.
///   2) Guruh o'chirilishi — <c>GroupChatMessageConfiguration</c> dagi
///      kaskad. ⚠️ Ilovada guruhni O'CHIRADIGAN endpoint YO'Q (guruhlar
///      ARXIVLANADI), ya'ni bu yo'l faqat qo'lda `psql` bilan yuriladi.
///      Shu holat uchun yagona himoya — obyekt kalitidagi
///      <c>group-chat/</c> prefiksi: operator bucket'ni prefiks bo'yicha
///      solishtirib tozalay oladi.
///   3) Bitta xabarni o'chirish — ENDPOINT YO'Q (qaror kuchida,
///      <see cref="GroupChatMessage"/> izohi).
/// </summary>
public class GroupChatAttachment : BaseEntity
{
    /// <summary>
    /// Bitta xabarga ko'pi bilan shuncha fayl.
    ///
    /// Qiymat <see cref="Submission.MaxAttachments"/> bilan AYNI — ikkita
    /// bir xil ma'noli chegara ikki xil raqam bo'lib qolmasin. Telegram
    /// albomida 10 ta bo'ladi, lekin bizda har fayl API orqali proksilanadi
    /// (presigned havola YO'Q), ya'ni bitta so'rovning eng yomon hajmi
    /// 5 × chegara bo'lib qoladi.
    /// </summary>
    public const int MaxPerMessage = 5;

    /// <summary>Ko'rinadigan fayl nomi ustunining chegarasi.</summary>
    public const int MaxFileNameLength = 200;

    public long MessageId { get; set; }

    public GroupChatMessage? Message { get; set; }

    /// <summary>Fayl turi — MAZMUNDAN aniqlanadi, klient aytganidan emas.</summary>
    public AttachmentKind Kind { get; set; }

    /// <summary>Xabar ichidagi tartib (0 dan, ZICH) — albom shu tartibda chiziladi.</summary>
    public int Position { get; set; }

    /// <summary>
    /// 🔴 OMBOR KALITI — UI'GA CHIQMAYDI. Sabab
    /// <see cref="LessonAsset.ObjectKey"/> da batafsil (qisqasi: kalit
    /// chaqiruvchidan qabul qilinsa, u begona obyektning yo'lini yozib
    /// yuborardi va ruxsat tekshiruvi ma'nosini yo'qotardi).
    /// </summary>
    public required string ObjectKey { get; set; }

    /// <summary>MAZMUNDAN aniqlangan MIME turi.</summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// Foydalanuvchiga ko'rsatiladigan nom (hujjat uchun MUHIM: "shartnoma.pdf"
    /// deb ko'rinishi kerak, "hujjat-12.pdf" deb emas).
    ///
    /// 🔴 KLIENT BERGAN NOM TOZALANADI (<see cref="SanitizeFileName"/>):
    /// unda yo'l ajratgichlari, boshqaruv belgilari va qo'shtirnoq bo'lishi
    /// mumkin — ularning hammasi <c>Content-Disposition</c> sarlavhasiga
    /// tushib, javobni buzardi yoki (eng yomon holatda) yuklab olinadigan
    /// fayl yo'lini o'zgartirardi.
    ///
    /// ⚠️ NOM TURNI ANIQLAMAYDI: tur baribir sehrli baytlardan olinadi.
    /// </summary>
    public string? FileName { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Ovoz davomiyligi (bo'lsa) — pleyer uzunlikni oldindan ko'rsatsin.</summary>
    public int? DurationSec { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    public void Validate()
    {
        if (MessageId <= 0)
            throw new DomainException("Biriktirma xabarga bog'langan bo'lishi kerak.");

        if (string.IsNullOrWhiteSpace(ObjectKey))
            throw new DomainException("Ombor kaliti bo'sh bo'lishi mumkin emas.");

        if (string.IsNullOrWhiteSpace(ContentType))
            throw new DomainException("Fayl turi (MIME) aniqlanmagan.");

        if (SizeBytes <= 0)
            throw new DomainException("Fayl hajmi noldan katta bo'lishi kerak.");

        if (DurationSec is { } duration && duration is < 0 or > LessonAsset.MaxDurationSec)
            throw new DomainException("Davomiylik qiymati haqiqatga to'g'ri kelmaydi.");
    }

    /// <summary>
    /// Klient bergan fayl nomini XAVFSIZ ko'rinishga keltiradi.
    ///
    /// 🔴 QILINADIGAN ISH: (1) yo'lning faqat OXIRGI bo'lagi olinadi —
    /// <c>../../etc/passwd</c> kabi qiymat <c>passwd</c> ga aylanadi;
    /// (2) boshqaruv belgilari va qo'shtirnoq olib tashlanadi — ular
    /// <c>Content-Disposition</c> sarlavhasini buzardi; (3) uzun nom
    /// surrogat juftlikni BUZMASDAN qirqiladi (emojili fayl nomlari
    /// haqiqatan uchraydi).
    ///
    /// ★ NOM BO'SH BO'LIB QOLSA <c>null</c> qaytadi — chaqiruvchi o'shanda
    /// turdan kelib chiqqan nom yasaydi. "Noma'lum" degan soxta nom
    /// yozilmaydi: bo'sh qiymat "nom yo'q" degan MA'NONI aniq ifodalaydi.
    /// </summary>
    public static string? SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Yo'lning oxirgi bo'lagi — ikkala ajratgich bo'yicha ham
        // (Windows klienti `\` yuboradi, `Path.GetFileName` esa Linux'da
        // uni ajratgich deb BILMAYDI).
        var name = raw.AsSpan();

        var slash = name.LastIndexOfAny('/', '\\');
        if (slash >= 0) name = name[(slash + 1)..];

        Span<char> buffer = name.Length <= 512 ? stackalloc char[name.Length] : new char[name.Length];
        var length = 0;

        foreach (var symbol in name)
        {
            // Boshqaruv belgilari va qo'shtirnoq — sarlavhani buzadi.
            if (char.IsControl(symbol) || symbol is '"' or '\r' or '\n') continue;

            buffer[length++] = symbol;
        }

        var cleaned = new string(buffer[..length]).Trim();

        if (cleaned.Length == 0) return null;

        if (cleaned.Length <= MaxFileNameLength) return cleaned;

        var cut = MaxFileNameLength;
        if (char.IsHighSurrogate(cleaned[cut - 1])) cut--;

        return cleaned[..cut];
    }
}
