using System.Globalization;
using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Modul ichidagi o'quv darsi (kurs kontenti).
/// Diqqat: bu <see cref="LiveSession"/> EMAS — u jonli dars.
///
/// Dars ikki turda bo'ladi (<see cref="LessonKind"/>): odatiy (video
/// qismlari) va imtihon (rasmlar). Media <see cref="Assets"/> da.
/// </summary>
public class ModuleLesson : BaseEntity
{
    public long ModuleId { get; set; }

    public CourseModule? Module { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int Position { get; set; }

    public int? DurationMin { get; set; }

    /// <summary>
    /// Dars turi. Bazada <c>int</c>, standart qiymat <c>Normal = 0</c> —
    /// ya'ni migratsiyadan oldin yaratilgan barcha darslar ODATIY bo'lib
    /// qoladi (mavjud ma'lumot ma'nosi o'zgarmaydi).
    /// </summary>
    public LessonKind Kind { get; set; } = LessonKind.Normal;

    /// <summary>
    /// Dars mediasi: odatiy darsda video qismlari, imtihon darsida rasmlar.
    /// Tartib <see cref="LessonAsset.Position"/> bo'yicha.
    /// </summary>
    public ICollection<LessonAsset> Assets { get; set; } = new List<LessonAsset>();

    // ---------------------------------------------------------------- invariant

    /// <summary>
    /// Dars TURIGA mos keladigan YAGONA asset turi.
    ///
    /// Bu ta'rif BITTA joyda: yuklash, tur almashtirish va tekshiruv
    /// uchalasi shu xossani o'qiydi. Yangi dars turi qo'shilganda
    /// kompilyator shu <c>switch</c> ni to'ldirishga majbur qiladi.
    /// </summary>
    public LessonAssetKind AllowedAssetKind => Kind switch
    {
        LessonKind.Normal => LessonAssetKind.Video,
        LessonKind.Exam => LessonAssetKind.Image,
        _ => throw new DomainException("Dars turi noma'lum."),
    };

    /// <summary>
    /// Berilgan asset turi shu darsga mos keladimi.
    /// </summary>
    /// <exception cref="DomainException">
    /// Mos kelmasa — masalan imtihon darsiga video yuklashga urinilsa.
    /// </exception>
    public void EnsureAssetKindAllowed(LessonAssetKind kind)
    {
        if (kind == AllowedAssetKind) return;

        throw new DomainException(
            Kind == LessonKind.Exam
                ? "Imtihon darsiga video yuklanmaydi — faqat rasm."
                : "Odatiy darsga rasm yuklanmaydi — faqat video. "
                  + "Rasm kerak bo'lsa dars turini «Imtihon» qilib belgilang.");
    }

    /// <summary>
    /// ★★ TURNI ALMASHTIRISH — MOS KELMAYDIGAN MEDIA BO'LSA TO'XTATADI.
    ///
    /// 🔴 JIMGINA O'CHIRISH YO'Q. Odatiy darsni imtihonga aylantirish
    /// "avtomatik ravishda videolarni o'chirish" degani bo'lardi — bir
    /// soatlik video bitta tugma bilan, ogohlantirishsiz yo'qolib ketardi
    /// va uni qaytarib bo'lmasdi. Shuning uchun bu yerda XATO ko'tariladi
    /// va foydalanuvchiga NECHTA fayl qo'lda o'chirilishi kerakligi
    /// aytiladi.
    ///
    /// <paramref name="existingAssetCount"/> — MOS KELMAYDIGAN assetlar
    /// soni (chaqiruvchi bazadan sanaydi). Nol bo'lsa tur o'zgaradi.
    /// </summary>
    /// <exception cref="DomainException">
    /// Yangi turga mos kelmaydigan media bor (HTTP 409 ga aylanadi).
    /// </exception>
    public void ChangeKind(LessonKind kind, int existingAssetCount)
    {
        if (Kind == kind) return;

        if (existingAssetCount > 0)
        {
            // Hozirgi (ESKI) tur bo'yicha nima borligini aytamiz — foydalanuvchi
            // ekranda aynan o'sha ro'yxatni ko'rib turadi.
            var what = Kind == LessonKind.Normal ? "video" : "rasm";
            var count = existingAssetCount.ToString(CultureInfo.InvariantCulture);

            throw new DomainException(
                $"Dars turini o'zgartirib bo'lmaydi: darsda {count} ta {what} bor va "
                + $"yangi turda u qabul qilinmaydi. Avval shu {count} ta {what}ni o'chiring, "
                + "keyin turni almashtiring. (Ular avtomatik o'chirilmaydi — "
                + "yuklangan fayl jimgina yo'qolmasligi kerak.)");
        }

        Kind = kind;
    }
}
