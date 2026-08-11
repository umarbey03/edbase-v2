using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// DARS MEDIASI — VIDEO QISMI YOKI IMTIHON RASMI
/// ========================================================================
///
/// ★ NIMA UCHUN BITTA JADVAL, IKKITA EMAS (`LessonVideos` + `LessonImages`):
/// yuklash oqimi, ruxsat tekshiruvi, o'chirish va tartiblash video va rasm
/// uchun AYNAN bir xil. Ikki jadval — ikki controller, ikki servis, ikki
/// test to'plami degani; ular vaqt o'tib bir-biridan uzoqlashadi va bir
/// kuni rasm yo'lida ruxsat tekshiruvi tushib qoladi (eski tizimda aynan
/// shunday bo'lgan: `/media` katalogi tekshiruvsiz ochiq edi).
///
/// ★ ESKI TIZIMDAN KO'CHIRILMAYDI: eski `lesson_videos` jadvali
/// `MA_LUMOT_KOCHIRISH.md` dagi "ko'chirilmaydigan 18 jadval" ro'yxatida —
/// ya'ni bu funksiya v2 da NOLDAN quriladi.
///
/// ── INVARIANT ──────────────────────────────────────────────────────────
/// Dars turi asset turini QAT'IY belgilaydi:
///     <see cref="LessonKind.Normal"/> -> faqat <see cref="LessonAssetKind.Video"/>
///     <see cref="LessonKind.Exam"/>   -> faqat <see cref="LessonAssetKind.Image"/>
/// Tekshiruv <see cref="ModuleLesson.EnsureAssetKindAllowed"/> da (yagona joy).
/// </summary>
public class LessonAsset : BaseEntity
{
    /// <summary>Sarlavha uzunligi ("1-qism", "Nazariya" kabi qisqa nomlar).</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Video davomiyligining aql bovar qiladigan yuqori chegarasi — 12 soat.
    ///
    /// Bu VALIDATSIYA chegarasi, biznes chegarasi emas: qiymat brauzerdan
    /// keladi (`<video>.duration`), ya'ni klient ixtiyoriy son yubora oladi.
    /// Chegarasiz bo'lsa UI'da "1 193 046 soat" kabi qiymat chiqib qolardi.
    /// </summary>
    public const int MaxDurationSec = 12 * 60 * 60;

    /// <summary>Rasm/video o'lchovining yuqori chegarasi (8K dan ham keng).</summary>
    public const int MaxPixelSize = 16_384;

    public long LessonId { get; set; }

    public ModuleLesson? Lesson { get; set; }

    public LessonAssetKind Kind { get; set; }

    /// <summary>Dars ichidagi tartib (0 dan, ZICH). Faqat "reorder" o'zgartiradi.</summary>
    public int Position { get; set; }

    /// <summary>
    /// Ko'rinadigan nom ("1-qism", "Nazariya"). <c>null</c> — UI tartib
    /// raqamidan nom yasaydi.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 🔴 OMBOR KALITI — UI'GA HECH QACHON CHIQMAYDI.
    ///
    /// `DAVOM_ETTIRISH.md` 6-bo'lim 16-tuzoq: `objectKey` ichki joylashuv
    /// ma'lumoti. Uni javobga solish ombor tuzilishini oshkor qiladi va
    /// klient uni to'g'ridan-to'g'ri so'rovda ishlatishga urinadi — o'shanda
    /// ruxsat tekshiruvi ma'nosini yo'qotadi. Fayl DOIM `assetId` bo'yicha,
    /// bazadagi kalit orqali o'qiladi.
    ///
    /// TO'LIQ URL EMAS: presigned havola muddatli, bazadagi URL bir soatdan
    /// keyin o'lardi (`SubmissionFile.ObjectKey` bilan ayni mulohaza).
    /// </summary>
    public required string ObjectKey { get; set; }

    /// <summary>MAZMUNDAN aniqlangan MIME turi (klient sarlavhasidan EMAS).</summary>
    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Video/audio davomiyligi. Klient bergan ma'lumot — faqat KO'RSATISH uchun.</summary>
    public int? DurationSec { get; set; }

    /// <summary>Piksel kengligi (bo'lsa) — galereyada joy hisoblash uchun.</summary>
    public int? Width { get; set; }

    /// <summary>Piksel balandligi (bo'lsa).</summary>
    public int? Height { get; set; }

    /// <summary>Kim yuklagani. Foydalanuvchi o'chirilmaydi, shuning uchun FK `Restrict`.</summary>
    public long? CreatedById { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Yozuvning o'zi izchilmi (kalit, tur, hajm va ko'rsatish maydonlari).
    ///
    /// ⚠️ Dars turiga MOSLIK bu yerda TEKSHIRILMAYDI — u darsni biladigan
    /// joyda (<see cref="ModuleLesson.EnsureAssetKindAllowed"/>) tekshiriladi,
    /// aks holda qoida ikki joyda yashab, biri eskirib qolardi.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ObjectKey))
            throw new DomainException("Ombor kaliti bo'sh bo'lishi mumkin emas.");

        if (string.IsNullOrWhiteSpace(ContentType))
            throw new DomainException("Fayl turi (MIME) aniqlanmagan.");

        if (Title?.Length > MaxTitleLength)
            throw new DomainException($"Nom {MaxTitleLength} belgidan oshmasin.");

        if (SizeBytes <= 0)
            throw new DomainException("Fayl hajmi noldan katta bo'lishi kerak.");

        if (DurationSec is { } duration && duration is < 0 or > MaxDurationSec)
            throw new DomainException("Davomiylik qiymati haqiqatga to'g'ri kelmaydi.");

        EnsurePixelSize(Width, "Kenglik");
        EnsurePixelSize(Height, "Balandlik");
    }

    private static void EnsurePixelSize(int? value, string what)
    {
        if (value is { } pixels && pixels is < 0 or > MaxPixelSize)
            throw new DomainException($"{what} qiymati haqiqatga to'g'ri kelmaydi.");
    }
}
