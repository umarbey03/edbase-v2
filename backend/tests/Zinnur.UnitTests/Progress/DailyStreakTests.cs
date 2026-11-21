using Zinnur.Domain.Progress;

namespace Zinnur.UnitTests.Progress;

/// <summary>
/// <see cref="DailyStreak"/> — Mustaqil o'quvchining kunlik seriyasi (2026-11-21):
/// haftasiga bitta muzlatish, 3/7/14/30 kunlik bonuslar.
/// </summary>
public class DailyStreakTests
{
    // 2026-11-16 — dushanba.
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private static List<DateOnly> Days(params int[] offsets) => [.. offsets.Select(o => Monday.AddDays(o))];

    [Theory]
    [InlineData(1, null)]
    [InlineData(3, 5)]
    [InlineData(7, 15)]
    [InlineData(14, 25)]
    [InlineData(30, 50)]
    [InlineData(45, null)]
    [InlineData(60, 50)]
    public void Bonus_milestones(int streak, int? bonus) =>
        DailyStreak.BonusFor(streak).Should().Be(bonus);

    [Theory]
    [InlineData(0, 3, 5)]
    [InlineData(3, 7, 15)]
    [InlineData(20, 30, 50)]
    [InlineData(30, 60, 50)]
    public void Next_bonus(int streak, int day, int bonus) =>
        DailyStreak.NextBonus(streak).Should().Be((day, bonus));

    [Fact]
    public void Consecutive_days_count_and_today_is_done()
    {
        var r = DailyStreak.Compute(Days(0, 1, 2), Monday.AddDays(2));

        r.Current.Should().Be(3);
        r.DoneToday.Should().BeTrue();
        r.Bonuses.Should().ContainSingle().Which.Bonus.Should().Be(5);
    }

    [Fact]
    public void Streak_is_alive_until_today_ends()
    {
        var r = DailyStreak.Compute(Days(0, 1), Monday.AddDays(2));

        r.Current.Should().Be(2);
        r.DoneToday.Should().BeFalse();
    }

    [Fact]
    public void One_missed_day_per_week_is_frozen_and_not_counted()
    {
        // Du, Se, (Chor o'tkazildi), Pa, Ju
        var r = DailyStreak.Compute(Days(0, 1, 3, 4), Monday.AddDays(4));

        r.Current.Should().Be(4);
        r.FrozenDays.Should().Equal(Monday.AddDays(2));
        r.FreezeAvailable.Should().BeFalse();
    }

    [Fact]
    public void Second_missed_day_in_the_same_week_breaks_the_streak()
    {
        // Du, (Se), Chor, (Pa), Ju
        var r = DailyStreak.Compute(Days(0, 2, 4), Monday.AddDays(4));

        r.Current.Should().Be(1);
        r.Best.Should().Be(2);
    }

    [Fact]
    public void Two_missed_days_in_a_row_break_the_streak()
    {
        var r = DailyStreak.Compute(Days(0, 1, 2, 5), Monday.AddDays(5));

        r.Current.Should().Be(1);
        r.Best.Should().Be(3);
    }

    [Fact]
    public void Yesterday_missed_keeps_streak_alive_with_freeze()
    {
        // Du, Se o'qidi; Chor o'tkazdi; bugun Pa — hali o'qimagan.
        var r = DailyStreak.Compute(Days(0, 1), Monday.AddDays(3));

        r.Current.Should().Be(2);
        r.DoneToday.Should().BeFalse();
        r.FreezeAvailable.Should().BeFalse();
    }

    [Fact]
    public void Three_days_gap_resets_to_zero()
    {
        var r = DailyStreak.Compute(Days(0, 1), Monday.AddDays(4));

        r.Current.Should().Be(0);
        r.Best.Should().Be(2);
    }

    [Fact]
    public void Freeze_resets_every_monday()
    {
        // 1-hafta Chor o'tkazildi (muzlatish), 2-hafta Chor ham — yangi hafta, yana muzlatish.
        var r = DailyStreak.Compute(Days(0, 1, 3, 4, 5, 6, 7, 8, 10), Monday.AddDays(10));

        r.Current.Should().Be(9);
        r.FrozenDays.Should().HaveCount(2);
    }

    [Fact]
    public void Sunday_missed_does_not_use_this_weeks_freeze()
    {
        // Ju, Sha o'qidi, Yak o'tkazdi, bugun Dush (yangi hafta) — hali o'qimagan.
        var r = DailyStreak.Compute(Days(4, 5), Monday.AddDays(7));

        r.Current.Should().Be(2);
        r.FreezeAvailable.Should().BeTrue();
    }

    [Fact]
    public void No_activity_is_zero()
    {
        var r = DailyStreak.Compute([], Monday);

        r.Current.Should().Be(0);
        r.Best.Should().Be(0);
        r.FreezeAvailable.Should().BeTrue();
    }

    [Fact]
    public void Weekly_goal_is_reached_on_the_fourth_active_day()
    {
        // 1-hafta: Du, Se, Pa, Sha (4 kun) — maqsad Shanbada; 2-hafta: 3 kun — yo'q.
        var weeks = DailyStreak.GoalWeeks(Days(0, 1, 3, 5, 7, 8, 9));

        weeks.Should().ContainSingle();
        weeks[0].Week.Should().Be(Monday);
        weeks[0].ReachedOn.Should().Be(Monday.AddDays(5));
    }

    [Fact]
    public void Week_starts_on_monday() =>
        DailyStreak.WeekOf(Monday.AddDays(6)).Should().Be(Monday);
}
