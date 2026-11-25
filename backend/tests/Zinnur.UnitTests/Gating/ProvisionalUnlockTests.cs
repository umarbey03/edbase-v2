using Zinnur.Application.Gating;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.Gating;

/// <summary>
/// «BITTA DARS OLDINGA» (Mustaqil, 2026-11-25).
///
/// Kurator kechqurun ishlamaydi. Oldingi dars to'liq bajarilgan va testi
/// 100% bo'lsa, keyingi dars kurator qarorisiz VAQTINCHA ochiladi — lekin
/// faqat bitta: vaqtinchalik ochilgan darsdan keyingisi ochilmaydi.
/// </summary>
public class ProvisionalUnlockTests
{
    private const int SelfPace = int.MaxValue;

    /// <summary>Hammasi bajarilgan, test 100%, kurator hali qaror chiqarmagan.</summary>
    private static LessonFacts Perfect(long id, MasteryStatus? mastery = null) => new(
        LessonId: id,
        HasVideo: true, VideoWatched: true,
        HasAssignment: true, AssignmentSubmitted: true,
        HasTest: true, TestTaken: true,
        UnlockedOverride: false,
        RequiresMastery: true,
        Mastery: mastery,
        TestPerfect: true);

    private static LessonFacts Fresh(long id) =>
        Perfect(id) with { VideoWatched = false, AssignmentSubmitted = false, TestTaken = false, TestPerfect = false };

    [Fact]
    public void Perfect_test_opens_the_next_lesson_provisionally()
    {
        var result = LessonGate.EvaluateAll([Perfect(1), Fresh(2), Fresh(3)], SelfPace);

        result[1].Unlocked.Should().BeTrue();
        result[1].Provisional.Should().BeTrue();
        result[1].LockReason.Should().BeNull();
        result[2].Unlocked.Should().BeFalse();
    }

    [Fact]
    public void Only_one_lesson_can_be_on_credit()
    {
        // 2-dars vaqtincha ochiq va u ham 100% — 3-dars baribir kutadi.
        var result = LessonGate.EvaluateAll([Perfect(1), Perfect(2), Fresh(3)], SelfPace);

        result[1].Provisional.Should().BeTrue();
        result[2].Unlocked.Should().BeFalse();
        result[2].LockReason.Should().Be(LessonLockReason.AwaitingReview);
    }

    [Fact]
    public void Imperfect_test_waits_for_the_curator()
    {
        var result = LessonGate.EvaluateAll([Perfect(1) with { TestPerfect = false }, Fresh(2)], SelfPace);

        result[1].Unlocked.Should().BeFalse();
        result[1].LockReason.Should().Be(LessonLockReason.AwaitingReview);
    }

    [Fact]
    public void Lesson_without_a_test_waits_for_the_curator()
    {
        var noTest = Perfect(1) with { HasTest = false, TestTaken = false };

        var result = LessonGate.EvaluateAll([noTest, Fresh(2)], SelfPace);

        result[1].Unlocked.Should().BeFalse();
        result[1].LockReason.Should().Be(LessonLockReason.AwaitingReview);
    }

    [Fact]
    public void Unfinished_work_never_unlocks_even_with_a_perfect_test()
    {
        var result = LessonGate.EvaluateAll([Perfect(1) with { AssignmentSubmitted = false }, Fresh(2)], SelfPace);

        result[1].Unlocked.Should().BeFalse();
        result[1].LockReason.Should().Be(LessonLockReason.PreviousIncomplete);
    }

    [Fact]
    public void Needs_work_revokes_the_provisional_lesson()
    {
        var result = LessonGate.EvaluateAll([Perfect(1, MasteryStatus.NeedsWork), Fresh(2)], SelfPace);

        result[1].Unlocked.Should().BeFalse();
        result[1].Provisional.Should().BeFalse();
        result[1].LockReason.Should().Be(LessonLockReason.NeedsWork);
    }

    [Fact]
    public void Once_mastered_the_credit_moves_forward()
    {
        // 1-dars o'zlashtirildi: 2-dars qat'iy ochiq, endi 3-dars vaqtincha.
        var result = LessonGate.EvaluateAll(
            [Perfect(1, MasteryStatus.Mastered), Perfect(2), Fresh(3), Fresh(4)], SelfPace);

        result[1].Unlocked.Should().BeTrue();
        result[1].Provisional.Should().BeFalse();
        result[2].Unlocked.Should().BeTrue();
        result[2].Provisional.Should().BeTrue();
        result[3].Unlocked.Should().BeFalse();
    }

    [Fact]
    public void Group_students_never_get_provisional_lessons()
    {
        var group = Perfect(1) with { RequiresMastery = false };

        var result = LessonGate.EvaluateAll([group, group with { LessonId = 2 }, Fresh(3) with { RequiresMastery = false }], 5);

        result.Should().OnlyContain(l => !l.Provisional);
    }

    [Fact]
    public void Single_lesson_path_matches_the_tree()
    {
        LessonFacts[] lessons = [Perfect(1, MasteryStatus.Mastered), Perfect(2), Perfect(3), Fresh(4)];
        var tree = LessonGate.EvaluateAll(lessons, SelfPace);

        for (var i = 0; i < lessons.Length; i++)
        {
            var previous = i > 0 ? lessons[i - 1] : null;
            var beforePrevious = i > 1 ? lessons[i - 2] : null;
            var previousFirm = previous is not null
                && LessonGate.Decide(i - 1, previous, beforePrevious, SelfPace, 0, previousFirm: false).Unlocked;

            var single = LessonGate.Decide(i, lessons[i], previous, SelfPace, 0, previousFirm);

            single.Unlocked.Should().Be(tree[i].Unlocked, $"dars {i}");
            single.Provisional.Should().Be(tree[i].Provisional, $"dars {i}");
            single.Reason.Should().Be(tree[i].LockReason, $"dars {i}");
        }
    }
}
