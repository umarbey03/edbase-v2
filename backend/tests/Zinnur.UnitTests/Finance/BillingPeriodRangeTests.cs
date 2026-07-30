using Zinnur.Domain.Finance;

namespace Zinnur.UnitTests.Finance;

/// <summary>
/// ========================================================================
/// OY ORALIG'I — VAQT ZONASI TUZOG'I
/// ========================================================================
///
/// Oylik reyting "shu oydagi" darslar, vazifalar va testlarni sanaydi.
/// "Shu oy" esa MAHALLIY kalendar tushunchasi, UTC emas.
///
/// Toshkent — UTC+5. Ya'ni 1-avgust 00:00 (Toshkent) UTC'da 31-iyul
/// 19:00. Oraliq to'g'ridan-to'g'ri UTC'da olinsa, 31-iyul kechqurun
/// soat 19:00 dan keyin bo'lgan HAMMA narsa (aynan shu paytda dars
/// o'tiladi!) avgust oyiga tushib ketardi.
/// </summary>
public class BillingPeriodRangeTests
{
    private static readonly TimeZoneInfo Tashkent =
        TimeZoneInfo.CreateCustomTimeZone("Test/Tashkent", TimeSpan.FromHours(5), "Toshkent", "Toshkent");

    [Fact]
    public void UtcRange_StartsAtLocalMidnight_NotUtcMidnight()
    {
        var (startUtc, endUtc) = BillingPeriod.Create(2026, 8).UtcRange(Tashkent);

        // 2026-08-01 00:00 Toshkent  ->  2026-07-31 19:00 UTC
        startUtc.Should().Be(new DateTimeOffset(2026, 7, 31, 19, 0, 0, TimeSpan.Zero));

        // 2026-09-01 00:00 Toshkent  ->  2026-08-31 19:00 UTC
        endUtc.Should().Be(new DateTimeOffset(2026, 8, 31, 19, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// ★ ENG MUHIM HOLAT: oyning oxirgi kunidagi kechki dars.
    /// 31-iyul soat 19:00 (Toshkent) — bu HALI IYUL. UTC bo'yicha
    /// hisoblangan oraliq uni avgustga qo'shib yuborardi.
    /// </summary>
    [Fact]
    public void LastEveningOfMonth_BelongsToThatMonth()
    {
        // 2026-07-31 19:00 Toshkent = 2026-07-31 14:00 UTC
        var eveningLesson = new DateTimeOffset(2026, 7, 31, 14, 0, 0, TimeSpan.Zero);

        var july = BillingPeriod.Create(2026, 7).UtcRange(Tashkent);
        var august = BillingPeriod.Create(2026, 8).UtcRange(Tashkent);

        (eveningLesson >= july.StartUtc && eveningLesson < july.EndUtc)
            .Should().BeTrue("31-iyul kechqurungi dars IYUL oyiga tegishli");

        (eveningLesson >= august.StartUtc && eveningLesson < august.EndUtc)
            .Should().BeFalse("u avgustga o'tib ketmasligi kerak");
    }

    /// <summary>
    /// Ketma-ket oylar chegarasi TUTASHADI: biri tugagan onda ikkinchisi
    /// boshlanadi. Bo'shliq bo'lsa o'sha ondagi yozuv hech qaysi oyga
    /// tushmay yo'qolardi, ustma-ust tushsa — ikki marta sanalardi.
    /// </summary>
    [Fact]
    public void ConsecutiveMonths_AreContiguous_NoGapNoOverlap()
    {
        var july = BillingPeriod.Create(2026, 7).UtcRange(Tashkent);
        var august = BillingPeriod.Create(2026, 8).UtcRange(Tashkent);

        july.EndUtc.Should().Be(august.StartUtc);
    }

    [Fact]
    public void DecemberRolls_IntoNextYear()
    {
        var (startUtc, endUtc) = BillingPeriod.Create(2026, 12).UtcRange(Tashkent);

        startUtc.Should().Be(new DateTimeOffset(2026, 11, 30, 19, 0, 0, TimeSpan.Zero));
        endUtc.Should().Be(new DateTimeOffset(2026, 12, 31, 19, 0, 0, TimeSpan.Zero));
    }
}
