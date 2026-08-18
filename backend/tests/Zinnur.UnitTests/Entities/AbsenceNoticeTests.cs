using Zinnur.Application.Absentees;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// KELMAGANLARGA XABAR (2026-08-18) — yozuv va o'rin egallovchilar.
///
/// ★ O'RIN EGALLOVCHILAR SINALADI, CHUNKI ULAR PULGA EMAS, ISHONCHGA
/// TA'SIR QILADI: `{ism}` almashtirilmay ketsa, o'quvchi "Assalomu
/// alaykum, {ism}!" degan xabar oladi va markaz beparvo ko'rinadi.
/// Bunday xato jimgina sodir bo'ladi — hech qayerda istisno chiqmaydi.
/// </summary>
public class AbsenceNoticeTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Dars mahalliy vaqtda 15-avgust, 20:06 (Toshkent = UTC+5).</summary>
    private static readonly DateTimeOffset SessionStart = new(2026, 8, 15, 15, 6, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(5), "test", "test");

    // ============================================================ yozuv

    private static AbsenceNotice Notice(string? body = "Xabar matni") =>
        AbsenceNotice.Create(
            studentId: 3,
            groupId: 5,
            sessionId: 7,
            SessionStart,
            body,
            sentById: 4,
            toTelegram: true,
            Moment);

    [Fact]
    public void Create_FillsFields()
    {
        var notice = Notice();

        notice.StudentId.Should().Be(3);
        notice.GroupId.Should().Be(5);
        notice.SessionId.Should().Be(7);
        notice.SessionStart.Should().Be(SessionStart);
        notice.SentById.Should().Be(4);
        notice.SentAt.Should().Be(Moment);
        notice.ToTelegram.Should().BeTrue();
    }

    [Fact]
    public void Create_TrimsBody()
    {
        Notice("  Xabar  ").Body.Should().Be("Xabar");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithoutBody_Throws(string? body)
    {
        var act = () => Notice(body);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_TruncatesTooLongBody()
    {
        Notice(new string('a', 5_000)).Body.Should().HaveLength(AbsenceNotice.MaxBodyLength);
    }

    /// <summary>
    /// Navbat kaliti yozuv `Id` siga tayanadi, ya'ni yaratilganda hali
    /// bo'sh bo'ladi — servis uni birinchi saqlashdan KEYIN to'ldiradi.
    /// </summary>
    [Fact]
    public void Create_LeavesOutboxKeyEmpty()
    {
        Notice().OutboxKey.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithoutStudent_Throws(long studentId)
    {
        var act = () => AbsenceNotice.Create(
            studentId, 5, 7, SessionStart, "Matn", 4, true, Moment);

        act.Should().Throw<DomainException>();
    }

    // ============================================================ o'rin egallovchilar

    [Fact]
    public void Apply_ReplacesEveryPlaceholder()
    {
        var result = AbsenceNoticePlaceholders.Apply(
            "{ism} · {guruh} · {sana} · {vaqt} · {ustoz}",
            "Doniyor Ergashev",
            "ATF-2 (kechki)",
            SessionStart,
            Zone,
            "Nodira Qosimova");

        result.Should().Be("Doniyor Ergashev · ATF-2 (kechki) · 15.08.2026 · 20:06 · Nodira Qosimova");
    }

    /// <summary>
    /// Sana va vaqt MAHALLIY zonada ko'rsatiladi: o'quvchi UTC bilan
    /// ishlamaydi va "15:06" degan xabar uni chalg'itardi.
    /// </summary>
    [Fact]
    public void Apply_ConvertsToLocalTime()
    {
        var result = AbsenceNoticePlaceholders.Apply(
            "{vaqt}", "A", "B", SessionStart, Zone, null);

        result.Should().Be("20:06");
    }

    [Fact]
    public void Apply_RepeatsPlaceholderEverywhere()
    {
        var result = AbsenceNoticePlaceholders.Apply(
            "{ism}, salom {ism}!", "Ali", "B", SessionStart, Zone, null);

        result.Should().Be("Ali, salom Ali!");
    }

    /// <summary>Ustoz tayinlanmagan guruhda kalit BO'SH qoladi, matnda emas.</summary>
    [Fact]
    public void Apply_WithoutTeacher_LeavesEmpty()
    {
        AbsenceNoticePlaceholders.Apply("[{ustoz}]", "A", "B", SessionStart, Zone, null)
            .Should().Be("[]");
    }

    /// <summary>
    /// Noma'lum kalit O'ZGARISHSIZ qoladi — bu ATAYLAB: xato yozilgan
    /// `{isim}` bo'sh joyga aylanib ketsa, operator xatoni sezmasdi.
    /// </summary>
    [Fact]
    public void Apply_LeavesUnknownPlaceholderIntact()
    {
        AbsenceNoticePlaceholders.Apply("{isim}", "Ali", "B", SessionStart, Zone, null)
            .Should().Be("{isim}");
    }

    [Fact]
    public void Keys_AreDocumentedForUi()
    {
        AbsenceNoticePlaceholders.Keys.Should()
            .BeEquivalentTo(["{ism}", "{guruh}", "{sana}", "{vaqt}", "{ustoz}"]);
    }
}
