using Zinnur.Application.Gating;
using Zinnur.Application.Gating.Dtos;

namespace Zinnur.UnitTests.Gating;

/// <summary>
/// Sur'at nazorati (gating) qoidasi.
///
/// Qoida: dars N OCHIQ ⟺ (N−1) dars TUGATILGAN **VA** N ustoz sur'atidan
/// oshmagan. Istisnolar: qo'lda ochilgan dars va BIRINCHI dars.
///
/// Bu fayl eski tizimning ikki nosozligini qo'riqlaydi:
///   1) tugatilganlik shartlari MAVJUD BO'LMAGAN element uchun ham talab
///      qilinmasin (videosi yo'q darsni "video ko'rilmagan" deb yopib
///      qo'yish butun kursni qulflardi);
///   2) ustoz sur'ati bilan "oldingi dars tugatilmagan" SABABLARI
///      ajratilsin — o'quvchi nima qilishi kerakligini bilishi uchun.
/// </summary>
public class LessonGateTests
{
    /// <summary>Hech qanday sharti yo'q, ya'ni TUGATILGAN hisoblanadigan dars.</summary>
    private static LessonFacts Empty(long id = 1) => new(
        LessonId: id,
        HasVideo: false, VideoWatched: false,
        HasAssignment: false, AssignmentSubmitted: false,
        HasTest: false, TestTaken: false,
        UnlockedOverride: false);

    // ================================================================== TUGATILGANLIK

    [Fact]
    public void IsComplete_WithNoRequirements_IsTrue()
    {
        LessonGate.IsComplete(Empty()).Should().BeTrue(
            "sharti yo'q dars avtomatik tugatilgan hisoblanadi");
    }

    [Fact]
    public void IsComplete_WithVideoNotWatched_IsFalse()
    {
        var facts = Empty() with { HasVideo = true, VideoWatched = false };

        LessonGate.IsComplete(facts).Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithVideoWatched_IsTrue()
    {
        var facts = Empty() with { HasVideo = true, VideoWatched = true };

        LessonGate.IsComplete(facts).Should().BeTrue();
    }

    /// <summary>
    /// ★ Video KO'RILGAN deb belgilangan, lekin darsda video YO'Q — shart
    /// baribir talab qilinmaydi (mavjud bo'lmagan element bloklamaydi).
    /// </summary>
    [Fact]
    public void IsComplete_WhenLessonHasNoVideo_IgnoresWatchedFlag()
    {
        var facts = Empty() with { HasVideo = false, VideoWatched = false };

        LessonGate.IsComplete(facts).Should().BeTrue();
    }

    [Fact]
    public void IsComplete_WithAssignmentNotSubmitted_IsFalse()
    {
        var facts = Empty() with { HasAssignment = true, AssignmentSubmitted = false };

        LessonGate.IsComplete(facts).Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithTestNotTaken_IsFalse()
    {
        var facts = Empty() with { HasTest = true, TestTaken = true };
        var notTaken = facts with { TestTaken = false };

        LessonGate.IsComplete(facts).Should().BeTrue();
        LessonGate.IsComplete(notTaken).Should().BeFalse();
    }

    /// <summary>Uchta shart BIRGA: bittasi bajarilmasa dars tugatilmagan.</summary>
    [Fact]
    public void IsComplete_RequiresEveryPresentRequirement()
    {
        var all = Empty() with
        {
            HasVideo = true, VideoWatched = true,
            HasAssignment = true, AssignmentSubmitted = true,
            HasTest = true, TestTaken = true,
        };

        LessonGate.IsComplete(all).Should().BeTrue();
        LessonGate.IsComplete(all with { VideoWatched = false }).Should().BeFalse();
        LessonGate.IsComplete(all with { AssignmentSubmitted = false }).Should().BeFalse();
        LessonGate.IsComplete(all with { TestTaken = false }).Should().BeFalse();
    }

    // ================================================================== OCHIQLIK

    /// <summary>
    /// ★ BIRINCHI dars DOIM ochiq — ustoz hali hech qanday dars o'tmagan
    /// bo'lsa ham. Aks holda o'quvchi kursni umuman boshlay olmasdi.
    /// </summary>
    [Fact]
    public void Evaluate_FirstLesson_IsAlwaysUnlocked()
    {
        var (unlocked, reason) = LessonGate.Evaluate(
            index: 0, Empty(), previous: null, taughtLessonCount: 0);

        unlocked.Should().BeTrue();
        reason.Should().BeNull();
    }

    /// <summary>★ Qo'lda ochilgan dars — boshqa hech qanday shart tekshirilmaydi.</summary>
    [Fact]
    public void Evaluate_WithOverride_IgnoresPaceAndPreviousLesson()
    {
        var previous = Empty(1) with { HasAssignment = true, AssignmentSubmitted = false };
        var current = Empty(2) with { UnlockedOverride = true };

        var (unlocked, reason) = LessonGate.Evaluate(
            index: 9, current, previous, taughtLessonCount: 0);

        unlocked.Should().BeTrue("o'quv bo'limi istisno qo'ygan");
        reason.Should().BeNull();
    }

    /// <summary>★ Ustoz yetib kelmagan dars YOPIQ — sababi `TeacherPace`.</summary>
    [Fact]
    public void Evaluate_BeyondTeacherPace_IsLockedWithPaceReason()
    {
        var (unlocked, reason) = LessonGate.Evaluate(
            index: 3, Empty(4), Empty(3), taughtLessonCount: 2);

        unlocked.Should().BeFalse();
        reason.Should().Be(LessonLockReason.TeacherPace);
    }

    /// <summary>
    /// ★ Ustoz N ta dars o'tgan bo'lsa N-indeksli dars OCHIQ bo'ladi —
    /// ya'ni o'tilgan darsdan KEYINGI dars ochiladi (o'quvchi oldinga qarab
    /// tayyorlanishi uchun).
    /// </summary>
    [Fact]
    public void Evaluate_ExactlyAtTeacherPace_IsUnlocked()
    {
        var (unlocked, reason) = LessonGate.Evaluate(
            index: 2, Empty(3), Empty(2), taughtLessonCount: 2);

        unlocked.Should().BeTrue();
        reason.Should().BeNull();
    }

    /// <summary>★ Oldingi dars tugatilmagan — sababi `PreviousIncomplete`.</summary>
    [Fact]
    public void Evaluate_WithIncompletePreviousLesson_IsLockedWithPreviousReason()
    {
        var previous = Empty(1) with { HasTest = true, TestTaken = false };

        var (unlocked, reason) = LessonGate.Evaluate(
            index: 1, Empty(2), previous, taughtLessonCount: 5);

        unlocked.Should().BeFalse();
        reason.Should().Be(LessonLockReason.PreviousIncomplete);
    }

    /// <summary>
    /// Ikki shart ham buzilgan bo'lsa SUR'AT sababi ustun: o'quvchiga
    /// "vazifani topshir" deyish noto'g'ri bo'lardi — dars baribir
    /// ochilmaydi, chunki ustoz unga yetmagan.
    /// </summary>
    [Fact]
    public void Evaluate_WhenBothRulesFail_ReportsTeacherPaceFirst()
    {
        var previous = Empty(1) with { HasAssignment = true, AssignmentSubmitted = false };

        var (unlocked, reason) = LessonGate.Evaluate(
            index: 4, Empty(5), previous, taughtLessonCount: 1);

        unlocked.Should().BeFalse();
        reason.Should().Be(LessonLockReason.TeacherPace);
    }

    // ================================================================== BUTUN DARAXT

    /// <summary>
    /// Zanjir: 1-dars tugatilgan -> 2-dars ochiq; 2-dars tugatilmagan ->
    /// 3-dars yopiq. TRANZITIV EMAS: qoida faqat BEVOSITA oldingi darsga
    /// qaraydi (aynan shu sababli bitta dars tekshiruvi arzon).
    /// </summary>
    [Fact]
    public void EvaluateAll_LocksOnlyTheLessonAfterAnIncompleteOne()
    {
        var lessons = new List<LessonFacts>
        {
            Empty(1),                                                    // tugatilgan
            Empty(2) with { HasAssignment = true, AssignmentSubmitted = false },
            Empty(3),
            Empty(4),
        };

        var result = LessonGate.EvaluateAll(lessons, taughtLessonCount: 10);

        result[0].Unlocked.Should().BeTrue("birinchi dars doim ochiq");
        result[1].Unlocked.Should().BeTrue("1-dars tugatilgan");
        result[2].Unlocked.Should().BeFalse("2-dars tugatilmagan");
        result[2].LockReason.Should().Be(LessonLockReason.PreviousIncomplete);

        // 3-dars YOPIQ bo'lsa ham, 4-dars uchun faqat 3-darsning
        // TUGATILGANLIGI muhim (u shartsiz, ya'ni tugatilgan).
        result[3].Unlocked.Should().BeTrue();
    }

    [Fact]
    public void EvaluateAll_StopsAtTeacherPace()
    {
        var lessons = new List<LessonFacts> { Empty(1), Empty(2), Empty(3), Empty(4) };

        var result = LessonGate.EvaluateAll(lessons, taughtLessonCount: 1);

        result[0].Unlocked.Should().BeTrue();
        result[1].Unlocked.Should().BeTrue("ustoz 1 ta dars o'tgan -> 2-dars ham ochiq");
        result[2].Unlocked.Should().BeFalse();
        result[2].LockReason.Should().Be(LessonLockReason.TeacherPace);
        result[3].Unlocked.Should().BeFalse();
    }

    /// <summary>Ustoz hech narsa o'tmagan: faqat birinchi dars ochiq.</summary>
    [Fact]
    public void EvaluateAll_WithZeroPace_UnlocksOnlyTheFirstLesson()
    {
        var lessons = new List<LessonFacts> { Empty(1), Empty(2), Empty(3) };

        var result = LessonGate.EvaluateAll(lessons, taughtLessonCount: 0);

        result[0].Unlocked.Should().BeTrue();
        result[1].Unlocked.Should().BeFalse();
        result[2].Unlocked.Should().BeFalse();
    }

    [Fact]
    public void EvaluateAll_AssignsSequentialIndexes()
    {
        var lessons = new List<LessonFacts> { Empty(10), Empty(20), Empty(30) };

        var result = LessonGate.EvaluateAll(lessons, taughtLessonCount: 5);

        result.Select(l => l.Index).Should().Equal(0, 1, 2);
        result.Select(l => l.LessonId).Should().Equal(10L, 20L, 30L);
    }

    [Fact]
    public void EvaluateAll_WithEmptyCourse_ReturnsEmpty()
    {
        LessonGate.EvaluateAll([], taughtLessonCount: 3).Should().BeEmpty();
    }

    /// <summary>DTO faktlarni O'ZGARTIRMASDAN uzatadi (interfeys shularni ko'rsatadi).</summary>
    [Fact]
    public void Describe_CopiesEveryFactIntoTheDto()
    {
        var facts = Empty(7) with
        {
            HasVideo = true, VideoWatched = true,
            HasAssignment = true, AssignmentSubmitted = false,
            HasTest = true, TestTaken = true,
        };

        var dto = LessonGate.Describe(index: 4, facts, unlocked: true, reason: null);

        dto.LessonId.Should().Be(7);
        dto.Index.Should().Be(4);
        dto.Unlocked.Should().BeTrue();
        dto.Completed.Should().BeFalse("vazifa topshirilmagan");
        dto.HasVideo.Should().BeTrue();
        dto.VideoWatched.Should().BeTrue();
        dto.HasAssignment.Should().BeTrue();
        dto.AssignmentSubmitted.Should().BeFalse();
        dto.HasTest.Should().BeTrue();
        dto.TestTaken.Should().BeTrue();
        dto.UnlockedOverride.Should().BeFalse();
    }

    // ================================================================== himoya

    [Fact]
    public void IsComplete_WithNullFacts_Throws()
    {
        var act = () => LessonGate.IsComplete(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EvaluateAll_WithNullList_Throws()
    {
        var act = () => LessonGate.EvaluateAll(null!, 0);

        act.Should().Throw<ArgumentNullException>();
    }
}
