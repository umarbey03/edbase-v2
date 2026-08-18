using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// TO'KILISH SABABI KATALOGI (2026-08-18) — o'quv bo'limi so'ragan FOIZ
/// hisobotining asosi.
///
/// ★ TASNIF FAQAT KERAKLI HODISADA: "guruhga qo'shildi" da to'kilish
/// sababi bo'lishi mumkin emas. Bu qoida tekshirilmasa, jurnalda ma'nosiz
/// tasnif paydo bo'lib, foizlarni jimgina buzardi.
/// </summary>
public class AttritionReasonTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    private static AttritionReason Reason(string label = "Moliyaviy qiyinchilik")
    {
        var reason = new AttritionReason { Id = 7 };
        reason.Apply(label);

        return reason;
    }

    // ============================================================ katalog

    [Fact]
    public void Apply_TrimsLabel()
    {
        Reason("  Moliyaviy  ").Label.Should().Be("Moliyaviy");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_WithoutLabel_Throws(string label)
    {
        var act = () => Reason(label);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Apply_TruncatesTooLongLabel()
    {
        Reason(new string('a', 250)).Label.Should().HaveLength(AttritionReason.MaxLabelLength);
    }

    [Fact]
    public void IsActive_DefaultsToTrue()
    {
        new AttritionReason().IsActive.Should().BeTrue();
    }

    // ============================================================ hodisaga bog'lanishi

    private static GroupMembershipEvent Event(MembershipEventKind kind, long? reasonId) =>
        GroupMembershipEvent.Create(
            studentId: 3,
            groupId: 5,
            teacherId: 2,
            kind,
            // Sabab talab qiladigan turlarda matn ham majburiy.
            reason: GroupMembershipEvent.RequiresReason(kind) ? "Izoh" : null,
            movedToGroupId: kind == MembershipEventKind.Moved ? 9 : null,
            actorId: 4,
            lessonsCompleted: 3,
            Moment,
            reasonId);

    [Theory]
    [InlineData(MembershipEventKind.Stopped)]
    [InlineData(MembershipEventKind.Paused)]
    [InlineData(MembershipEventKind.Moved)]
    public void Create_WithLossKind_KeepsReasonId(MembershipEventKind kind)
    {
        Event(kind, reasonId: 7).ReasonId.Should().Be(7);
    }

    /// <summary>
    /// Qo'shilish/qaytishda tasnif TASHLANADI — u yerda "to'kilish
    /// sababi" tushunchasi yo'q.
    /// </summary>
    [Theory]
    [InlineData(MembershipEventKind.Joined)]
    [InlineData(MembershipEventKind.Resumed)]
    public void Create_WithNonLossKind_DropsReasonId(MembershipEventKind kind)
    {
        Event(kind, reasonId: 7).ReasonId.Should().BeNull();
    }

    [Fact]
    public void Create_WithoutReasonId_LeavesItNull()
    {
        // Katalogdan OLDIN yozilgan hodisalar shu ko'rinishda —
        // hisobotda ular "Belgilanmagan" ulushiga tushadi.
        Event(MembershipEventKind.Stopped, reasonId: null).ReasonId.Should().BeNull();
    }

    /// <summary>Tasnif berilgani erkin matnni ALMASHTIRMAYDI — ikkalasi ham saqlanadi.</summary>
    [Fact]
    public void Create_KeepsBothReasonTextAndClassification()
    {
        var e = Event(MembershipEventKind.Stopped, reasonId: 7);

        e.Reason.Should().Be("Izoh");
        e.ReasonId.Should().Be(7);
    }
}
