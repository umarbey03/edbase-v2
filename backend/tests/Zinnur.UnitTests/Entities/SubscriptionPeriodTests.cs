using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// OBUNA DAVRINING ISHLATILMAGAN QIYMATI (2026-10-29, Q-1/Q-2).
///
/// ★ PUL FORMULASI — aniq raqamlar bilan: <c>Amount × qolgan / jami</c>,
///   butun so'mgacha PASTGA. Integratsion testlar haqiqiy soat bilan
///   ishlaydi va faqat oraliqni tekshira oladi — aniq qiymat shu yerda.
/// </summary>
public class SubscriptionPeriodTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static SubscriptionPeriod Period(decimal amount = 300_000m, int days = 30) => new()
    {
        StudentId = 1,
        CourseId = 1,
        StartsAt = Start,
        EndsAt = Start.AddDays(days),
        Amount = amount,
        CreatedById = 1,
    };

    [Fact]
    public void Unused_value_is_pro_rata_by_remaining_time()
    {
        Period().UnusedValue(Start.AddDays(10)).Should().Be(200_000m);
        Period(199_000m).UnusedValue(Start.AddDays(10)).Should().Be(132_666m, "199 000 × 20/30 = 132 666.67 → pastga");
    }

    [Fact]
    public void Unused_value_is_rounded_down_to_whole_sum()
    {
        // 1 soniya o'tgan: 300 000 × (30 kun − 1 s) / 30 kun = 299 999.88 → 299 999.
        Period().UnusedValue(Start.AddSeconds(1)).Should().Be(299_999m);
    }

    [Fact]
    public void Future_period_is_worth_its_full_amount_and_ended_or_cancelled_is_worth_nothing()
    {
        Period().UnusedValue(Start.AddDays(-3)).Should().Be(300_000m);
        Period().UnusedValue(Start.AddDays(30)).Should().Be(0m);

        var cancelled = Period();
        cancelled.CancelledAt = Start;
        cancelled.UnusedValue(Start.AddDays(1)).Should().Be(0m);

        Period(0m).UnusedValue(Start.AddDays(1)).Should().Be(0m, "bepul berilgan davr pul qaytarmaydi");
    }

    [Fact]
    public void Close_ends_a_running_period_now_and_refuses_others()
    {
        var running = Period();
        running.CloseAt(Start.AddDays(10));
        running.EndsAt.Should().Be(Start.AddDays(10));
        running.IsCancelled.Should().BeFalse();

        var future = Period();
        future.Invoking(p => p.CloseAt(Start.AddDays(-1))).Should().Throw<DomainException>();

        var ended = Period();
        ended.Invoking(p => p.CloseAt(Start.AddDays(31))).Should().Throw<DomainException>();
    }
}
