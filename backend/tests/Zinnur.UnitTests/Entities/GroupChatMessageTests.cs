using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// Guruh chati xabarining DOMAIN qoidalari.
///
/// Bu yerda ruxsat yoki baza YO'Q — faqat "xabar qanday tug'iladi":
/// matn tozalanadimi, chegara qanday kesiladi va yorliqlar qotib
/// qoladimi.
/// </summary>
public class GroupChatMessageTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsWhitespace()
    {
        var message = Create("   savolim bor   ");

        message.Body.Should().Be("savolim bor");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Create_WithEmptyBody_Throws(string? body)
    {
        var act = () => Create(body);

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Chegara jonli dars chatidan (500) uzunroq: bu yerda o'quvchi
    /// batafsil savol yozadi.
    /// </summary>
    [Fact]
    public void Create_WithLongBody_TruncatesToMaxLength()
    {
        var message = Create(new string('a', GroupChatMessage.MaxBodyLength + 250));

        message.Body.Should().HaveLength(GroupChatMessage.MaxBodyLength);
    }

    /// <summary>
    /// ★ SURROGAT JUFTLIK. Chegaraning AYNAN ustiga emoji tushganda uni
    /// ikkiga bo'lish YOLG'IZ surrogat qoldiradi; bunday satr Postgres'da
    /// <c>U+FFFD</c> ga aylanadi.
    ///
    /// Test shuni qulflaydi: kesilgan matn ichida yolg'iz surrogat
    /// QOLMAYDI (ya'ni bitta belgi qurbon qilinadi).
    /// </summary>
    [Fact]
    public void Create_WhenLimitFallsInsideEmoji_DoesNotSplitSurrogatePair()
    {
        // 1999 ta oddiy belgi + emoji (2 kod birligi) => 2001 kod birligi.
        // Naif kesish 2000-chi o'rinda emojining BIRINCHI yarmini qoldirardi.
        var body = new string('a', GroupChatMessage.MaxBodyLength - 1) + "😀";

        var message = Create(body);

        message.Body.Should().HaveLength(GroupChatMessage.MaxBodyLength - 1);
        char.IsSurrogate(message.Body[^1]).Should().BeFalse(
            "yolg'iz surrogat Postgres'da U+FFFD ga aylanardi");
    }

    /// <summary>Chegaraga to'liq sig'gan emoji BUZILMAYDI.</summary>
    [Fact]
    public void Create_WhenEmojiFitsExactly_KeepsIt()
    {
        var body = new string('a', GroupChatMessage.MaxBodyLength - 2) + "😀";

        var message = Create(body);

        message.Body.Should().HaveLength(GroupChatMessage.MaxBodyLength);
        // `EndWith` ning ikkinchi argumenti — FluentAssertions'da `because`
        // izohi (satr), taqqoslash turi EMAS. Taqqoslash baribir ordinal.
        message.Body.Should().EndWith("😀");
    }

    [Fact]
    public void Create_WithLongSenderName_Truncates()
    {
        var message = GroupChatMessage.Create(
            groupId: 1,
            GroupChatChannel.Teacher,
            senderId: 7,
            new string('N', GroupChatMessage.MaxSenderNameLength + 40),
            UserRole.Student,
            "salom",
            Now);

        message.SenderName.Should().HaveLength(GroupChatMessage.MaxSenderNameLength);
    }

    [Fact]
    public void Create_WithEmptySenderName_UsesFallback()
    {
        var message = GroupChatMessage.Create(
            1, GroupChatChannel.Teacher, 7, "  ", UserRole.Student, "salom", Now);

        message.SenderName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_WithUnknownChannel_Throws()
    {
        var act = () => GroupChatMessage.Create(
            1, (GroupChatChannel)77, 7, "Ali", UserRole.Student, "salom", Now);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-3L)]
    public void Create_WithoutGroup_Throws(long groupId)
    {
        var act = () => GroupChatMessage.Create(
            groupId, GroupChatChannel.Teacher, 7, "Ali", UserRole.Student, "salom", Now);

        act.Should().Throw<DomainException>();
    }

    /// <summary>Yorliq QOTIB qoladi — keyin ustoz almashtirilsa ham o'zgarmaydi.</summary>
    [Fact]
    public void Create_StoresSenderRoleAndChannel()
    {
        var message = GroupChatMessage.Create(
            5, GroupChatChannel.Curator, 9, "Kurator", UserRole.Assistant, "javob", Now);

        message.Channel.Should().Be(GroupChatChannel.Curator);
        message.SenderRole.Should().Be(UserRole.Assistant);
        message.SentAt.Should().Be(Now);
        message.CreatedAt.Should().Be(Now);
    }

    private static GroupChatMessage Create(string? body) =>
        GroupChatMessage.Create(
            1, GroupChatChannel.Teacher, 7, "Ali", UserRole.Student, body, Now);
}

/// <summary>
/// "Qayergacha o'qidim" belgisi — o'qilmaganlar sanog'ining asosi.
/// Eng muhim qoida: belgi ORQAGA ketmaydi.
/// </summary>
public class GroupChatReadTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Advance_MovesForward()
    {
        var marker = new GroupChatRead();

        marker.Advance(10, Now).Should().BeTrue();
        marker.LastReadMessageId.Should().Be(10);
        marker.UpdatedAt.Should().Be(Now);
    }

    /// <summary>
    /// ★ TARTIBSIZ SO'ROVLAR. Klient bir vaqtda ikki so'rov yuborsa va
    /// eskisi KEYIN yetib borsa, chegara orqaga surilib, allaqachon
    /// o'qilgan xabarlar yana "o'qilmagan" bo'lib qolardi.
    /// </summary>
    [Fact]
    public void Advance_Backwards_IsIgnored()
    {
        var marker = new GroupChatRead();
        marker.Advance(10, Now);

        marker.Advance(4, Now.AddSeconds(1)).Should().BeFalse();
        marker.LastReadMessageId.Should().Be(10);
    }

    /// <summary>Takroriy so'rov — o'zgarish yo'q (idempotentlik).</summary>
    [Fact]
    public void Advance_ToSameId_IsIdempotent()
    {
        var marker = new GroupChatRead();
        marker.Advance(10, Now);

        marker.Advance(10, Now.AddSeconds(1)).Should().BeFalse();
        marker.LastReadMessageId.Should().Be(10);
    }

    [Fact]
    public void Advance_WithNegativeId_Throws()
    {
        var marker = new GroupChatRead();

        var act = () => marker.Advance(-1, Now);

        act.Should().Throw<DomainException>();
    }
}
