using Zinnur.Application.Gating;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.Gating;

/// <summary>
/// «YAXSHI O'ZLASHTIRDI» QOIDASI (Mustaqil, 2026-11-18).
///
/// N-dars faqat kurator (N−1)-darsni «o'zlashtirdi» deganda ochiladi —
/// o'quvchi video, vazifa va testni bajargan bo'lsa ham. Guruh o'quvchisi
/// (<c>RequiresMastery = false</c>) qoidasi bit-to-bit o'zgarmaydi.
/// </summary>
public class LessonMasteryGateTests
{
    private const int SelfPace = int.MaxValue;

    /// <summary>Mustaqil o'quvchining videoli, vazifali darsi — hammasi bajarilgan.</summary>
    private static LessonFacts Done(long id, MasteryStatus? mastery = null) => new(
        LessonId: id,
        HasVideo: true, VideoWatched: true,
        HasAssignment: true, AssignmentSubmitted: true,
        HasTest: false, TestTaken: false,
        UnlockedOverride: false,
        RequiresMastery: true,
        Mastery: mastery);

    private static LessonFacts NotDone(long id) =>
        Done(id) with { VideoWatched = false };

    [Fact]
    public void Work_done_without_curator_decision_is_not_complete()
    {
        LessonGate.IsWorkDone(Done(1)).Should().BeTrue();
        LessonGate.IsComplete(Done(1)).Should().BeFalse();
        LessonGate.IsComplete(Done(1, MasteryStatus.Mastered)).Should().BeTrue();
        LessonGate.IsComplete(Done(1, MasteryStatus.NeedsWork)).Should().BeFalse();
    }

    [Fact]
    public void Mastered_without_the_work_is_still_not_complete()
    {
        // Kurator xato bossa ham ish bajarilmagan dars tugallanmaydi.
        LessonGate.IsComplete(NotDone(1) with { Mastery = MasteryStatus.Mastered }).Should().BeFalse();
    }

    [Fact]
    public void Next_lesson_waits_for_the_curator()
    {
        var result = LessonGate.EvaluateAll([Done(1), Done(2), Done(3)], SelfPace);

        result[0].Unlocked.Should().BeTrue("birinchi dars doim ochiq");
        result[1].Unlocked.Should().BeFalse();
        result[1].LockReason.Should().Be(LessonLockReason.AwaitingReview);
        result[2].LockReason.Should().Be(LessonLockReason.AwaitingReview);
    }

    [Fact]
    public void Mastered_opens_exactly_the_next_lesson()
    {
        var result = LessonGate.EvaluateAll(
            [Done(1, MasteryStatus.Mastered), Done(2), NotDone(3)], SelfPace);

        result[1].Unlocked.Should().BeTrue();
        result[2].Unlocked.Should().BeFalse();
        result[2].LockReason.Should().Be(LessonLockReason.AwaitingReview);
    }

    [Fact]
    public void Needs_work_keeps_the_next_lesson_locked_with_its_own_reason()
    {
        var result = LessonGate.EvaluateAll([Done(1, MasteryStatus.NeedsWork), Done(2)], SelfPace);

        result[1].Unlocked.Should().BeFalse();
        result[1].LockReason.Should().Be(LessonLockReason.NeedsWork);
    }

    [Fact]
    public void Unfinished_work_still_says_previous_incomplete()
    {
        var result = LessonGate.EvaluateAll([NotDone(1), Done(2)], SelfPace);

        result[1].LockReason.Should().Be(LessonLockReason.PreviousIncomplete);
    }

    [Fact]
    public void Contentless_lesson_also_needs_the_curator()
    {
        var empty = new LessonFacts(1, false, false, false, false, false, false, false, RequiresMastery: true);

        var waiting = LessonGate.EvaluateAll([empty, Done(2)], SelfPace);
        waiting[1].LockReason.Should().Be(LessonLockReason.AwaitingReview);

        var mastered = LessonGate.EvaluateAll([empty with { Mastery = MasteryStatus.Mastered }, Done(2)], SelfPace);
        mastered[1].Unlocked.Should().BeTrue();
    }

    [Fact]
    public void Mastered_lesson_stays_open_even_if_an_earlier_one_is_reopened()
    {
        // 1-darsga keyin «qayta ishlasin» deyildi — o'quvchi o'zlashtirgan
        // 2-darsni yo'qotmaydi (o'zi tugatgan dars unga ochiq qoladi).
        var result = LessonGate.EvaluateAll(
            [Done(1, MasteryStatus.NeedsWork), Done(2, MasteryStatus.Mastered), Done(3)], SelfPace);

        result[1].Unlocked.Should().BeTrue();
        // Qoida faqat BEVOSITA oldingi darsga qaraydi: 2-dars o'zlashtirilgan.
        result[2].Unlocked.Should().BeTrue();
    }

    [Fact]
    public void Admin_override_still_wins()
    {
        var result = LessonGate.EvaluateAll([Done(1), Done(2) with { UnlockedOverride = true }], SelfPace);

        result[1].Unlocked.Should().BeTrue();
    }

    [Fact]
    public void Group_students_are_untouched()
    {
        var groupLesson = Done(1) with { RequiresMastery = false };

        LessonGate.IsComplete(groupLesson).Should().BeTrue();
        var result = LessonGate.EvaluateAll([groupLesson, groupLesson with { LessonId = 2 }], taughtLessonCount: 5);
        result[1].Unlocked.Should().BeTrue();
    }

    [Fact]
    public void Describe_carries_the_mastery_fields()
    {
        var dto = LessonGate.Describe(0, Done(1, MasteryStatus.Mastered), unlocked: true, reason: null);

        dto.RequiresMastery.Should().BeTrue();
        dto.Mastery.Should().Be(MasteryStatus.Mastered);
        dto.Completed.Should().BeTrue();
    }
}
