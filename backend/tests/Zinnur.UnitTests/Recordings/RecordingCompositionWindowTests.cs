using Zinnur.Application.Recordings.Services;

namespace Zinnur.UnitTests.Recordings;

/// <summary>
/// TUNGI OYNA (00:00–09:00, Asia/Tashkent).
///
/// ★ NIMA UCHUN ALOHIDA TEST FAYLI: bu yerda uchta xatoga moyil narsa
/// birlashadi — mahalliy vaqt (konteyner UTC'da ishlaydi), yarim tunni
/// kesib o'tadigan oyna va "oxirigacha qancha qoldi" hisobi. Ularning
/// har biri jimgina buziladi: montaj dars vaqtida ishlab ketadi yoki
/// aksincha, kechasi umuman ishlamaydi va buni faqat ertalab, yozuvlar
/// yo'qligidan bilishadi.
/// </summary>
public sealed class RecordingCompositionWindowTests
{
    /// <summary>
    /// Asia/Tashkent — UTC+5, yoz vaqtiga o'tish YO'Q.
    ///
    /// ⚠️ IANA nomi ishlatiladi: konteyner Linux va u yerda Windows
    /// nomlari (<c>Central Asia Standard Time</c>) mavjud emas.
    /// </summary>
    private static readonly TimeZoneInfo Tashkent =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    private static readonly TimeSpan Cutoff = TimeSpan.FromMinutes(30);

    // ═══════════════════════════════════════════════════ standart oyna

    /// <summary>
    /// 🔴 KUNDUZI HECH NARSA YIG'ILMAYDI. Kodlash 3.5 yadroni yeydi va
    /// uni dars vaqtida yurgizish jonli darsning sifatini tushirardi —
    /// aynan shu quvur oldini olishi kerak bo'lgan narsa.
    /// </summary>
    [Theory]
    [InlineData(5, 0)]      // 10:00 Toshkent — darslarning o'rtasi
    [InlineData(9, 30)]     // 14:30 Toshkent
    [InlineData(14, 0)]     // 19:00 Toshkent — kechki darslar
    [InlineData(18, 59)]    // 23:59 Toshkent — oyna ochilishiga bir daqiqa
    public void OutsideTheWindow_NothingIsClaimed(int utcHour, int utcMinute)
    {
        var window = Evaluate(Utc(utcHour, utcMinute));

        window.IsOpen.Should().BeFalse();
        window.CanStart.Should().BeFalse();
    }

    /// <summary>Kechasi oyna ochiq va ish boshlash mumkin.</summary>
    [Theory]
    [InlineData(19, 0)]     // 00:00 Toshkent — oynaning boshi
    [InlineData(21, 30)]    // 02:30 Toshkent
    [InlineData(3, 0)]      // 08:00 Toshkent — oxirigacha bir soat
    public void InsideTheWindow_WorkCanStart(int utcHour, int utcMinute)
    {
        var window = Evaluate(Utc(utcHour, utcMinute));

        window.IsOpen.Should().BeTrue();
        window.CanStart.Should().BeTrue();
    }

    /// <summary>
    /// 🔴 OXIRIGA 30 DAQIQA QOLGANDA YANGI ISH BOSHLANMAYDI.
    ///
    /// Boshlangan kodlash 09:00 da UZILADI va uning natijasi TASHLANADI
    /// (yarim mp4 da <c>moov</c> atomi yo'q). Ya'ni 08:55 da boshlangan
    /// ish deyarli aniq behuda sarflangan protsessor vaqti — oynaning
    /// oxirgi yarim soati ATAYLAB bo'sh qoldiriladi.
    /// </summary>
    [Theory]
    [InlineData(3, 30)]     // 08:30 — chegaraning aynan o'zi
    [InlineData(3, 45)]     // 08:45
    [InlineData(3, 59)]     // 08:59
    public void InsideTheLastHalfHour_TheWindowIsOpenButNothingStarts(int utcHour, int utcMinute)
    {
        var window = Evaluate(Utc(utcHour, utcMinute));

        window.IsOpen.Should().BeTrue("09:00 gacha oyna hali ochiq");
        window.CanStart.Should().BeFalse("uzilishi aniq bo'lgan ish boshlanmaydi");
    }

    /// <summary>
    /// Oynaning tugash payti — kodlashning bekor qilish signali AYNAN shu
    /// paytga qo'yiladi.
    /// </summary>
    [Fact]
    public void EndsAt_IsTheNextLocalNineOClock()
    {
        // 02:00 Toshkent = 21:00 UTC (oldingi kun)
        var window = Evaluate(new DateTimeOffset(2026, 9, 4, 21, 0, 0, TimeSpan.Zero));

        // 09:00 Toshkent (5-sentabr) = 04:00 UTC
        window.EndsAtUtc.Should().Be(new DateTimeOffset(2026, 9, 5, 4, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Oyna ochilishidan oldin ham <c>EndsAtUtc</c> KEYINGI 09:00 ni
    /// ko'rsatadi — o'tmishdagi paytni qaytarish bekor qilish signalini
    /// darhol ishga tushirardi.
    /// </summary>
    [Fact]
    public void EndsAt_IsAlwaysInTheFuture()
    {
        var now = Utc(9, 0);        // 14:00 Toshkent — oyna yopiq

        Evaluate(now).EndsAtUtc.Should().BeAfter(now);
    }

    // ═══════════════════════════════════════════════════ sozlangan oyna

    /// <summary>
    /// Yarim tunni KESIB O'TADIGAN oyna (22:00–06:00). Sozlama admin
    /// panelidan o'zgartiriladi, ya'ni bu shakl haqiqiy.
    ///
    /// ⚠️ Shart bu yerda TESKARI bo'ladi ("boshidan keyin YOKI oxiridan
    /// oldin") va aynan shu joy jimgina buziladi.
    /// </summary>
    [Theory]
    [InlineData(17, 30, true)]      // 22:30 Toshkent — oyna ichida
    [InlineData(20, 0, true)]       // 01:00 Toshkent — yarim tundan keyin
    [InlineData(0, 30, true)]       // 05:30 Toshkent — oxiriga yaqin
    [InlineData(2, 0, false)]       // 07:00 Toshkent — oyna yopilgan
    [InlineData(12, 0, false)]      // 17:00 Toshkent
    public void WindowCrossingMidnight_IsUnderstood(int utcHour, int utcMinute, bool expected)
    {
        var window = RecordingCompositionWindow.Evaluate(
            Utc(utcHour, utcMinute), Tashkent, new TimeOnly(22, 0), new TimeOnly(6, 0), Cutoff);

        window.IsOpen.Should().Be(expected);
    }

    /// <summary>
    /// 🔴 BOSHI VA OXIRI TENG BO'LSA OYNA YOPIQ, "SUTKA BO'YI" EMAS.
    ///
    /// Ikkinchi talqin xato sozlamani ("00:00"–"00:00") jimgina "har doim
    /// kodla" ga aylantirardi va montaj tushlik paytida ishlab turardi.
    /// Yopiq oyna esa darhol ko'rinadi: hech narsa yig'ilmaydi.
    /// </summary>
    [Fact]
    public void EqualStartAndEnd_ClosesTheWindow()
    {
        var window = RecordingCompositionWindow.Evaluate(
            Utc(21, 0), Tashkent, new TimeOnly(0, 0), new TimeOnly(0, 0), Cutoff);

        window.IsOpen.Should().BeFalse();
        window.CanStart.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════ sozlamani o'qish

    /// <summary>
    /// Buzuq sozlama ISTISNO TASHLAMAYDI — standart oynaga tushadi.
    ///
    /// ★ Sozlamalar panelidan kelgan bitta xato belgi butun tungi
    /// montajni yiqitmasligi kerak: yiqilgan fon xizmati ko'rinmasdi,
    /// standart oyna esa ishlayveradi.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9:00")]        // nol bilan to'ldirilmagan
    [InlineData("09:00:00")]    // sekundlar bilan
    [InlineData("kechasi")]
    [InlineData("25:00")]
    public void BrokenSetting_FallsBackToTheDefault(string? value)
    {
        RecordingCompositionWindow
            .Parse(value, RecordingCompositionWindow.DefaultEnd)
            .Should().Be(new TimeOnly(9, 0));
    }

    [Theory]
    [InlineData("00:00", 0, 0)]
    [InlineData("09:00", 9, 0)]
    [InlineData("22:30", 22, 30)]
    [InlineData(" 06:15 ", 6, 15)]
    public void ValidSetting_IsParsed(string value, int hour, int minute)
    {
        RecordingCompositionWindow
            .Parse(value, RecordingCompositionWindow.DefaultStart)
            .Should().Be(new TimeOnly(hour, minute));
    }

    // ═══════════════════════════════════════════════════ yordamchilar

    private static CompositionWindow Evaluate(DateTimeOffset nowUtc) =>
        RecordingCompositionWindow.Evaluate(
            nowUtc,
            Tashkent,
            RecordingCompositionWindow.DefaultStart,
            RecordingCompositionWindow.DefaultEnd,
            Cutoff);

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 5, hour, minute, 0, TimeSpan.Zero);
}
