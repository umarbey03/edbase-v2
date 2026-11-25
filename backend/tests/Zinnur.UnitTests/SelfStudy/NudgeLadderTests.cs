using Zinnur.Application.SelfStudy.Nudges;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.SelfStudy;

/// <summary>
/// MUSTAQIL FAOLSIZLIK ESLATMALARI ZANJIRI (2026-11-25).
///
/// Qoida: 1/2-kun — o'quvchiga, 3-kun — kuratorga, 5-kun — savol,
/// 14-kun — oxirgi xabar; faqat oldinga, bir yurishda eng yuqorisi.
/// </summary>
public class NudgeLadderTests
{
    private static readonly NudgeThresholds T = NudgeThresholds.Default;
    private static readonly IReadOnlyCollection<NudgeStage> None = [];

    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.CreateCustomTimeZone(
        "UZ", TimeSpan.FromHours(5), "UZ", "UZ");

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, NudgeStage.Day1)]
    [InlineData(2, NudgeStage.Day2)]
    [InlineData(3, NudgeStage.CuratorAlert)]
    [InlineData(4, NudgeStage.CuratorAlert)]
    [InlineData(5, NudgeStage.Ask)]
    [InlineData(14, NudgeStage.Final)]
    [InlineData(21, NudgeStage.Final)]
    public void Fresh_period_picks_highest_ripe_stage(int idle, NudgeStage? expected) =>
        NudgeLadder.Next(idle, None, studentMessagesAllowed: true, T).Should().Be(expected);

    [Fact]
    public void Long_gone_student_without_any_nudge_is_not_started()
    {
        // Funksiya yoqilgan kuni bir oydan beri kirmagan odamga birdan xabar ketmasin.
        NudgeLadder.Next(30, None, true, T).Should().BeNull();
    }

    [Fact]
    public void Ladder_never_goes_backwards()
    {
        // Kuratorga xabar ketgan — ertasi «1-kun» xabari bema'ni bo'lardi.
        NudgeLadder.Next(4, [NudgeStage.CuratorAlert], true, T).Should().BeNull();
        NudgeLadder.Next(5, [NudgeStage.CuratorAlert], true, T).Should().Be(NudgeStage.Ask);
    }

    [Fact]
    public void Already_sent_stage_is_not_repeated()
    {
        NudgeLadder.Next(1, [NudgeStage.Day1], true, T).Should().BeNull();
        NudgeLadder.Next(2, [NudgeStage.Day1], true, T).Should().Be(NudgeStage.Day2);
        NudgeLadder.Next(40, [NudgeStage.Day1, NudgeStage.Final], true, T).Should().BeNull();
    }

    [Fact]
    public void Opted_out_student_still_triggers_curator_alert_only()
    {
        NudgeLadder.Next(2, None, studentMessagesAllowed: false, T).Should().BeNull();
        NudgeLadder.Next(3, None, studentMessagesAllowed: false, T).Should().Be(NudgeStage.CuratorAlert);
        NudgeLadder.Next(14, [NudgeStage.CuratorAlert], studentMessagesAllowed: false, T).Should().BeNull();
    }

    [Fact]
    public void Idle_days_are_calendar_days_in_center_time_zone()
    {
        // Kecha 21:00 (Toshkent) dars qildi, bugun 19:00 — 22 soat, lekin 1 kun.
        var anchor = new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero); // 21:00 UZ
        var now = new DateTimeOffset(2026, 11, 25, 14, 0, 0, TimeSpan.Zero);    // 19:00 UZ

        NudgeLadder.IdleDays(anchor, now, Tashkent).Should().Be(1);
        NudgeLadder.IdleDays(now, now, Tashkent).Should().Be(0);

        // UTC'da kun almashmagan, Toshkentda almashgan (23:30 UTC = 04:30 UZ).
        var lateUtc = new DateTimeOffset(2026, 11, 24, 18, 30, 0, TimeSpan.Zero); // 23:30 UZ, 24-noyabr
        var nextMorningUtc = new DateTimeOffset(2026, 11, 24, 19, 30, 0, TimeSpan.Zero); // 00:30 UZ, 25-noyabr
        NudgeLadder.IdleDays(lateUtc, nextMorningUtc, Tashkent).Should().Be(1);
    }

    [Fact]
    public void Variants_rotate()
    {
        NudgeLadder.PickVariant(0, 3).Should().Be(0);
        NudgeLadder.PickVariant(1, 3).Should().Be(1);
        NudgeLadder.PickVariant(3, 3).Should().Be(0);
        NudgeLadder.PickVariant(7, 1).Should().Be(0);
    }

    [Fact]
    public void Response_is_recorded_once()
    {
        var now = DateTimeOffset.UtcNow;
        var nudge = SelfStudyNudge.Create(1, 1, NudgeStage.Ask, now, 0, true, now);

        nudge.Respond(NudgeResponse.Hard, now).Should().BeTrue();
        nudge.Respond(NudgeResponse.Snooze, now).Should().BeFalse();
        nudge.Response.Should().Be(NudgeResponse.Hard);
    }
}

public class NudgeTemplatesTests
{
    private static NudgeContext Ctx(bool needsWork = false, string name = "Ali <Vali>") =>
        new(name, "Harflar & tovushlar", CompletedLessons: 3, TotalLessons: 12, needsWork, "Kurator", IdleDays: 5);

    [Theory]
    [InlineData(NudgeStage.Day1, 0)]
    [InlineData(NudgeStage.Day1, 1)]
    [InlineData(NudgeStage.Day1, 2)]
    [InlineData(NudgeStage.Day2, 0)]
    [InlineData(NudgeStage.Day2, 1)]
    [InlineData(NudgeStage.Ask, 0)]
    [InlineData(NudgeStage.Final, 0)]
    [InlineData(NudgeStage.SnoozeReturn, 0)]
    public void User_values_are_escaped_in_telegram_and_plain_in_app(NudgeStage stage, int variant)
    {
        var message = NudgeTemplates.ForStudent(stage, variant, Ctx());

        // 🔴 O'quvchi ismidagi `<` Telegram so'rovini 400 bilan yiqitardi.
        message.TelegramHtml.Should().Contain("Ali &lt;Vali&gt;");
        message.TelegramHtml.Should().NotContain("<Vali>");

        // Qo'ng'iroqcha — sof matn: teg ham, entity ham yo'q, bir satr.
        message.PlainBody.Should().Contain("Ali <Vali>");
        message.PlainBody.Should().NotContain("<b>").And.NotContain("&lt;").And.NotContain("\n");
        message.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Needs_work_message_explains_the_unlock()
    {
        var message = NudgeTemplates.ForStudent(NudgeStage.Day1, 0, Ctx(needsWork: true));

        message.PlainBody.Should().Contain("keyingi dars ochiladi");
        message.Title.Should().Be("Kurator izohi sizni kutyapti");
    }

    [Fact]
    public void Progress_variant_shows_completed_and_percent()
    {
        var message = NudgeTemplates.ForStudent(NudgeStage.Day1, 1, Ctx());

        message.PlainBody.Should().Contain("3 ta darsni").And.Contain("25%");
    }

    [Fact]
    public void Ask_stage_has_answer_buttons_and_every_nudge_has_opt_out()
    {
        var ask = NudgeTemplates.StudentButtons(NudgeStage.Ask, 42);
        ask.Should().Contain("nd:42:t").And.Contain("nd:42:s").And.Contain("nd:42:h").And.Contain("nd:42:b");
        ask.Should().Contain("nd:42:off").And.Contain(TelegramTemplates.OpenAppButtonData);

        var day1 = NudgeTemplates.StudentButtons(NudgeStage.Day1, 7);
        day1.Should().Contain("nd:7:off").And.NotContain("nd:7:h");

        // Telegram `callback_data` chegarasi — 64 bayt.
        foreach (var data in new[] { "nd:9223372036854775807:off" })
            System.Text.Encoding.UTF8.GetByteCount(data).Should().BeLessThanOrEqualTo(64);
    }

    [Fact]
    public void Nudge_templates_use_dynamic_keyboards()
    {
        TelegramTemplates.MarkupFor(TelegramTemplates.SelfStudyNudge).Should().Be(TelegramMarkup.InlineButtons);
        TelegramTemplates.MarkupFor(TelegramTemplates.SelfStudyNudgeOptedOut).Should().Be(TelegramMarkup.InlineButtons);
        TelegramTemplates.MarkupFor(TelegramTemplates.SelfStudyNudgeReply).Should().Be(TelegramMarkup.None);
    }

    [Fact]
    public void Curator_alert_names_student_and_lesson()
    {
        var alert = NudgeTemplates.CuratorIdleAlert(Ctx(name: "Malika"));

        alert.TelegramHtml.Should().Contain("<b>Malika</b>").And.Contain("Harflar &amp; tovushlar").And.Contain("3/12");
        alert.PlainBody.Should().Contain("Harflar & tovushlar");
    }
}
