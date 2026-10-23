using Zinnur.Application.Progress.Services;
using Zinnur.Domain.Progress;

namespace Zinnur.UnitTests.Progress;

/// <summary>
/// <see cref="PointRules"/>, <see cref="PointsRanking"/> va jim soatlar —
/// loyiha egasi tasdiqlagan ball qoidalari (2026-10-23).
/// </summary>
public class PointRulesTests
{
    [Theory]
    [InlineData(1, null)]
    [InlineData(3, null)]
    [InlineData(4, 10)]
    [InlineData(5, null)]
    [InlineData(8, 25)]
    [InlineData(12, 10)]
    [InlineData(16, 25)]
    public void Streak_bonus_is_10_every_4th_and_25_every_8th_lesson(int streak, int? bonus) =>
        PointRules.StreakBonus(streak).Should().Be(bonus);

    [Theory]
    [InlineData(0, 4)]
    [InlineData(3, 1)]
    [InlineData(4, 4)]
    [InlineData(6, 2)]
    public void Lessons_to_next_bonus(int streak, int expected) =>
        PointRules.LessonsToNextBonus(streak).Should().Be(expected);

    [Theory]
    [InlineData(5, 5, 10)]
    [InlineData(4, 5, 8)]
    [InlineData(10, 10, 10)]
    [InlineData(7, 10, 8)]
    [InlineData(0, 5, 0)]
    public void Lesson_grade_is_five_point_scale_times_two(decimal score, decimal max, int expected) =>
        PointRules.LessonGrade(score, max).Should().Be(expected);

    [Theory]
    [InlineData(5, 5, 15)]
    [InlineData(3, 5, 9)]
    [InlineData(80, 100, 12)]
    public void Assignment_is_five_point_scale_times_three(decimal score, decimal max, int expected) =>
        PointRules.Assignment(score, max).Should().Be(expected);

    [Theory]
    [InlineData(10, 10, 10)]
    [InlineData(7, 10, 7)]
    [InlineData(1, 3, 3)]
    [InlineData(0, 10, 0)]
    [InlineData(5, 0, 0)]
    public void Test_points_scale_to_ten(decimal score, decimal max, int expected) =>
        PointRules.Test(score, max).Should().Be(expected);

    [Fact]
    public void Month_total_never_goes_below_zero()
    {
        PointRules.MonthTotal([PointRules.Absent, PointRules.Absent, PointRules.Present]).Should().Be(0);
        PointRules.MonthTotal([PointRules.Present, PointRules.Absent]).Should().Be(5);
    }

    [Fact]
    public void Equal_points_share_a_rank_and_zero_points_get_no_rank()
    {
        var ranked = PointsRanking.Rank(
            [
                new PointsStanding(1, "Anvar", 30),
                new PointsStanding(2, "Bobur", 50),
                new PointsStanding(3, "Aziz", 30),
                new PointsStanding(4, "Dilnoza", 0),
            ],
            new DateOnly(2026, 10, 23));

        ranked.Select(r => r.Rank).Should().Equal(1, 2, 2, 0);
        ranked[0].Standing.StudentId.Should().Be(2);
        ranked[3].Standing.StudentId.Should().Be(4);
    }

    [Fact]
    public void Tie_order_is_not_alphabetical_and_changes_between_days_but_not_within_a_day()
    {
        var standings = Enumerable.Range(1, 20)
            .Select(i => new PointsStanding(i, $"A{i:D2}", 40))
            .ToList();

        var day = new DateOnly(2026, 10, 23);
        var first = PointsRanking.Rank(standings, day).Select(r => r.Standing.StudentId).ToList();
        var again = PointsRanking.Rank(standings, day).Select(r => r.Standing.StudentId).ToList();
        var nextDay = PointsRanking.Rank(standings, day.AddDays(1)).Select(r => r.Standing.StudentId).ToList();

        again.Should().Equal(first);
        first.Should().NotEqual(standings.Select(s => s.StudentId));
        nextDay.Should().NotEqual(first);
    }

    [Fact]
    public void Quiet_hours_push_messages_to_eight_in_the_morning()
    {
        var tz = TimeZoneInfo.CreateCustomTimeZone("UZ", TimeSpan.FromHours(5), "UZ", "UZ");

        // 23:30 mahalliy -> ertasi 08:00 mahalliy = 03:00 UTC.
        var late = new DateTimeOffset(2026, 10, 23, 18, 30, 0, TimeSpan.Zero);
        PointsSyncService.QuietHoursEnd(late, tz)
            .Should().Be(new DateTimeOffset(2026, 10, 24, 3, 0, 0, TimeSpan.Zero));

        // 06:00 mahalliy -> shu kun 08:00 mahalliy.
        var early = new DateTimeOffset(2026, 10, 23, 1, 0, 0, TimeSpan.Zero);
        PointsSyncService.QuietHoursEnd(early, tz)
            .Should().Be(new DateTimeOffset(2026, 10, 23, 3, 0, 0, TimeSpan.Zero));

        // 15:00 mahalliy -> darhol.
        var day = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        PointsSyncService.QuietHoursEnd(day, tz).Should().BeNull();
    }
}
