using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="ChatMessage.NormalizeBody"/> — chat matnini serverda tozalash.
/// Uzunlik cheklovi FAQAT serverda ishonchli: frontenddagi `maxlength`
/// atributini istalgan foydalanuvchi chetlab o'tishi mumkin.
/// </summary>
public class ChatMessageTests
{
    [Fact]
    public void NormalizeBody_WithSurroundingWhitespace_TrimsIt()
    {
        var result = ChatMessage.NormalizeBody("   Assalomu alaykum   ");

        result.Should().Be("Assalomu alaykum");
    }

    [Fact]
    public void NormalizeBody_WithNewLinesAroundText_TrimsThem()
    {
        var result = ChatMessage.NormalizeBody("\r\n\tsavol bor\n ");

        result.Should().Be("savol bor");
    }

    [Fact]
    public void NormalizeBody_WithInnerWhitespace_KeepsIt()
    {
        var result = ChatMessage.NormalizeBody(" ikki   so'z ");

        result.Should().Be("ikki   so'z");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(" \t\r\n ")]
    public void NormalizeBody_WithEmptyOrWhitespaceOnlyInput_ThrowsDomainException(string? raw)
    {
        var act = () => ChatMessage.NormalizeBody(raw);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void NormalizeBody_WithSingleCharacter_ReturnsIt()
    {
        var result = ChatMessage.NormalizeBody("a");

        result.Should().Be("a");
    }

    // ------------------------------------------------------------------ uzunlik chegarasi

    [Fact]
    public void NormalizeBody_AtExactlyMaxBodyLength_ReturnsTheStringUntouched()
    {
        var exactly = new string('a', ChatMessage.MaxBodyLength);

        var result = ChatMessage.NormalizeBody(exactly);

        result.Should().Be(exactly);
    }

    [Fact]
    public void NormalizeBody_OneCharacterAboveMaxBodyLength_TruncatesToMaxBodyLength()
    {
        var tooLong = new string('a', ChatMessage.MaxBodyLength + 1);

        var result = ChatMessage.NormalizeBody(tooLong);

        result.Should().HaveLength(ChatMessage.MaxBodyLength);
    }

    [Fact]
    public void NormalizeBody_AboveMaxBodyLength_KeepsTheLeadingCharacters()
    {
        var head = new string('x', ChatMessage.MaxBodyLength);
        var tooLong = head + "KESILISHI-KERAK";

        var result = ChatMessage.NormalizeBody(tooLong);

        result.Should().Be(head);
    }

    [Fact]
    public void NormalizeBody_FarAboveMaxBodyLength_TruncatesToMaxBodyLength()
    {
        var flood = new string('z', 10_000);

        var result = ChatMessage.NormalizeBody(flood);

        result.Should().HaveLength(ChatMessage.MaxBodyLength);
    }

    /// <summary>
    /// Uzunlik TRIM'dan KEYIN o'lchanadi: bo'sh joylar bilan 500 belgidan oshgan
    /// matn kesilmasligi kerak.
    /// </summary>
    [Fact]
    public void NormalizeBody_WithWhitespacePaddingAroundMaxLengthText_DoesNotTruncate()
    {
        var exactly = new string('a', ChatMessage.MaxBodyLength);

        var result = ChatMessage.NormalizeBody("    " + exactly + "    ");

        result.Should().Be(exactly);
    }

    [Fact]
    public void MaxBodyLength_IsFiveHundred()
    {
        ChatMessage.MaxBodyLength.Should().Be(500);
    }
}
