using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// ========================================================================
/// DARS TURI VA MEDIA INVARIANTI
/// ========================================================================
///
/// Qoida: `Normal` -> faqat `Video`, `Exam` -> faqat `Image`.
///
/// 🔴 ENG MUHIM TEKSHIRUV — TUR ALMASHTIRISHDA JIMGINA O'CHIRISH YO'Q.
/// "Qulay" yechim mos kelmaydigan faylni avtomatik o'chirish bo'lardi va
/// o'shanda bir soatlik video bitta tugma bilan, ogohlantirishsiz yo'qolib
/// ketardi — qaytarib bo'lmaydigan yo'qotish. Shuning uchun Domain XATO
/// ko'taradi va nechta fayl qo'lda o'chirilishi kerakligini AYTADI.
/// </summary>
public sealed class ModuleLessonKindTests
{
    // ================================================================= standart

    /// <summary>
    /// Yangi dars ODATIY. Bu migratsiya uchun ham muhim: mavjud barcha
    /// darslar `Kind = 0` bilan qoladi, ya'ni ma'nosi o'zgarmaydi.
    /// </summary>
    [Fact]
    public void NewLesson_IsNormalByDefault()
    {
        var lesson = new ModuleLesson { Name = "Dars" };

        lesson.Kind.Should().Be(LessonKind.Normal);
        lesson.AllowedAssetKind.Should().Be(LessonAssetKind.Video);
    }

    [Fact]
    public void ExamLesson_AllowsOnlyImages()
    {
        var lesson = new ModuleLesson { Name = "Imtihon", Kind = LessonKind.Exam };

        lesson.AllowedAssetKind.Should().Be(LessonAssetKind.Image);
    }

    // ================================================================= invariant

    [Fact]
    public void NormalLesson_RejectsImageAsset()
    {
        var lesson = new ModuleLesson { Name = "Dars" };

        var act = () => lesson.EnsureAssetKindAllowed(LessonAssetKind.Image);

        act.Should().Throw<DomainException>()
            .WithMessage("*faqat video*");
    }

    [Fact]
    public void ExamLesson_RejectsVideoAsset()
    {
        var lesson = new ModuleLesson { Name = "Imtihon", Kind = LessonKind.Exam };

        var act = () => lesson.EnsureAssetKindAllowed(LessonAssetKind.Video);

        act.Should().Throw<DomainException>()
            .WithMessage("*faqat rasm*");
    }

    [Fact]
    public void MatchingAssetKind_IsAccepted()
    {
        var normal = new ModuleLesson { Name = "Dars" };
        var exam = new ModuleLesson { Name = "Imtihon", Kind = LessonKind.Exam };

        normal.Invoking(l => l.EnsureAssetKindAllowed(LessonAssetKind.Video))
            .Should().NotThrow();

        exam.Invoking(l => l.EnsureAssetKindAllowed(LessonAssetKind.Image))
            .Should().NotThrow();
    }

    // ================================================================= TUR ALMASHTIRISH

    /// <summary>Media yo'q — tur erkin o'zgaradi.</summary>
    [Fact]
    public void ChangeKind_WithoutAssets_Succeeds()
    {
        var lesson = new ModuleLesson { Name = "Dars" };

        lesson.ChangeKind(LessonKind.Exam, existingAssetCount: 0);

        lesson.Kind.Should().Be(LessonKind.Exam);
        lesson.AllowedAssetKind.Should().Be(LessonAssetKind.Image);
    }

    /// <summary>
    /// ★★ MOS KELMAYDIGAN MEDIA BOR — XATO (409 ga aylanadi) va TUR
    /// O'ZGARMAYDI.
    /// </summary>
    [Fact]
    public void ChangeKind_WithExistingAssets_ThrowsAndKeepsKind()
    {
        var lesson = new ModuleLesson { Name = "Dars" };

        var act = () => lesson.ChangeKind(LessonKind.Exam, existingAssetCount: 3);

        act.Should().Throw<DomainException>();

        // 🔴 ENG MUHIMI: holat O'ZGARMAGAN. Agar `Kind` xatodan OLDIN
        //    o'zgartirilganda, chaqiruvchi istisnoni ushlab qolsa (yoki
        //    `SaveChanges` boshqa sababdan chaqirilsa) dars yarim holatda
        //    saqlanib qolardi: turi imtihon, ichida esa videolar.
        lesson.Kind.Should().Be(LessonKind.Normal);
    }

    /// <summary>
    /// Xato xabari NECHTA fayl borligini VA nima qilish kerakligini aytadi —
    /// foydalanuvchi "409" bilan yolg'iz qolmasin.
    /// </summary>
    [Fact]
    public void ChangeKind_ErrorMessage_NamesCountAndAction()
    {
        var lesson = new ModuleLesson { Name = "Dars" };

        var act = () => lesson.ChangeKind(LessonKind.Exam, existingAssetCount: 7);

        act.Should().Throw<DomainException>()
            .WithMessage("*7*video*")
            .And.Message.Should().Contain("o'chiring");
    }

    /// <summary>Imtihondan odatiyga o'tishda RASMLAR haqida aytiladi.</summary>
    [Fact]
    public void ChangeKind_FromExam_MentionsImages()
    {
        var lesson = new ModuleLesson { Name = "Imtihon", Kind = LessonKind.Exam };

        var act = () => lesson.ChangeKind(LessonKind.Normal, existingAssetCount: 2);

        act.Should().Throw<DomainException>().WithMessage("*2*rasm*");
    }

    /// <summary>
    /// AYNI turga "o'zgartirish" — hech nima qilmaydi, media bo'lsa ham
    /// XATO BERMAYDI. Bu muhim: `PUT` semantikasida forma joriy turni
    /// QAYTARIB yuboradi, ya'ni oddiy nom tahriri ham shu yo'ldan o'tadi.
    /// Xato bersa, videosi bor darsning nomini umuman o'zgartirib
    /// bo'lmasdi.
    /// </summary>
    [Fact]
    public void ChangeKind_ToSameKind_IsNoOpEvenWithAssets()
    {
        var lesson = new ModuleLesson { Name = "Dars" };

        lesson.Invoking(l => l.ChangeKind(LessonKind.Normal, existingAssetCount: 5))
            .Should().NotThrow();

        lesson.Kind.Should().Be(LessonKind.Normal);
    }
}
