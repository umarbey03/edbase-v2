using System.Text;
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
    /// <summary>Kulgan yuz emojisi — UTF-16 da IKKI kod birligi (surrogat juftlik).</summary>
    private const string Emoji = "\U0001F600";

    /// <summary>
    /// Postgres'ga yozishdagi kabi QAT'IY kodlash: yolg'iz surrogat uchrasa
    /// `U+FFFD` bilan yashirmaydi, `EncoderFallbackException` bilan yiqiladi.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Satrda juftsiz (yolg'iz) surrogat bormi.</summary>
    private static bool ContainsLoneSurrogate(string value)
    {
        var i = 0;
        while (i < value.Length)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return true;

                i += 2;      // to'liq juftlik — o'tib ketamiz
                continue;
            }

            if (char.IsLowSurrogate(value[i]))
                return true; // yuqori surrogatsiz past surrogat

            i++;
        }

        return false;
    }

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

    // ------------------------------------------------------------------ ★ surrogat juftliklar (regressiya)

    /// <summary>
    /// ★ REGRESSIYA. C# satri UTF-16 KOD BIRLIKLARIdan iborat va emoji ikkita
    /// kod birligi — surrogat juftlik. Ilgari kesish oddiy `text[..500]` edi va
    /// 500-chegara juftlikning O'RTASIGA tushsa YOLG'IZ surrogat qolardi.
    ///
    /// Bu men o'lchab tasdiqlagan holat: `len=500`, oxirgi belgi yolg'iz yuqori
    /// surrogat, UTF-8 ga aylantirilganda `U+FFFD` ga tushib qoladi, qat'iy
    /// kodlashda esa `EncoderFallbackException` bilan yiqiladi. Ya'ni 500-belgisi
    /// emojiga to'g'ri kelgan xabar chat yozuvini buzardi.
    ///
    /// Endi chegara yuqori surrogatga tushsa bitta belgi orqaga chekinadi —
    /// natija 499 belgi, emoji butunlay tushib qoladi.
    /// </summary>
    [Fact]
    public void NormalizeBody_WhenSurrogatePairStraddlesTheLimit_TruncatesOneCharacterEarlier()
    {
        var raw = new string('a', 499) + Emoji + "keyingi matn";

        var result = ChatMessage.NormalizeBody(raw);

        result.Should().HaveLength(ChatMessage.MaxBodyLength - 1);
    }

    /// <summary>★ Natijada juftsiz surrogat qolmasligi kerak.</summary>
    [Fact]
    public void NormalizeBody_WhenSurrogatePairStraddlesTheLimit_LeavesNoLoneSurrogate()
    {
        var raw = new string('a', 499) + Emoji + "keyingi matn";

        var result = ChatMessage.NormalizeBody(raw);

        ContainsLoneSurrogate(result).Should().BeFalse();
    }

    /// <summary>
    /// ★ Amaliy tekshiruv: natija qat'iy UTF-8 kodlashdan o'tishi kerak —
    /// Postgres'ga yozishda aynan shu yo'l bosib o'tiladi.
    /// </summary>
    [Fact]
    public void NormalizeBody_WhenSurrogatePairStraddlesTheLimit_EncodesWithStrictUtf8WithoutThrowing()
    {
        var raw = new string('a', 499) + Emoji + "keyingi matn";
        var result = ChatMessage.NormalizeBody(raw);

        var act = () => StrictUtf8.GetBytes(result);

        act.Should().NotThrow();
    }

    [Fact]
    public void NormalizeBody_WhenSurrogatePairStraddlesTheLimit_DropsThePartialEmoji()
    {
        var raw = new string('a', 499) + Emoji + "keyingi matn";

        var result = ChatMessage.NormalizeBody(raw);

        result.Should().Be(new string('a', 499));
    }

    /// <summary>
    /// ★ Past surrogat chegarada: emoji 498–499 indekslarni egallaydi, ya'ni
    /// TO'LIQ chegara ichida. Bunda orqaga chekinish SHART EMAS — emoji butun
    /// holida saqlanadi va 500 belgi to'liq ishlatiladi.
    /// </summary>
    [Fact]
    public void NormalizeBody_WhenSurrogatePairEndsExactlyAtTheLimit_KeepsTheWholeEmoji()
    {
        var raw = new string('a', 498) + Emoji + "keyingi matn";

        var result = ChatMessage.NormalizeBody(raw);

        result.Should().Be(new string('a', 498) + Emoji);
    }

    [Fact]
    public void NormalizeBody_WhenSurrogatePairEndsExactlyAtTheLimit_UsesTheFullLimit()
    {
        var raw = new string('a', 498) + Emoji + "keyingi matn";

        var result = ChatMessage.NormalizeBody(raw);

        result.Should().HaveLength(ChatMessage.MaxBodyLength);
    }

    /// <summary>Chegaradan uzoqdagi emoji umuman tegilmasligi kerak.</summary>
    [Fact]
    public void NormalizeBody_WithEmojiWellInsideTheLimit_ReturnsTheTextUntouched()
    {
        var raw = "Salom " + Emoji + " qanday yordam bera olaman?";

        var result = ChatMessage.NormalizeBody(raw);

        result.Should().Be(raw);
    }

    [Fact]
    public void NormalizeBody_WithEmojiAtTheVeryEndOfAShortMessage_KeepsIt()
    {
        var raw = "Rahmat" + Emoji;

        var result = ChatMessage.NormalizeBody(raw);

        result.Should().EndWith(Emoji);
    }

    /// <summary>
    /// Butunlay emojidan iborat uzun xabar: kesish qayerga tushishidan qat'i
    /// nazar natija haqiqiy UTF-16 bo'lib qolishi kerak.
    /// Bu yerda bitta ASCII belgi juftliklarni toq indekslarga suradi, ya'ni
    /// 499-indeks aynan YUQORI surrogatga to'g'ri keladi — eng xatarli holat.
    /// </summary>
    [Fact]
    public void NormalizeBody_ForEmojiHeavyText_ProducesValidUtf16()
    {
        var raw = "x" + string.Concat(Enumerable.Repeat(Emoji, 400));

        var result = ChatMessage.NormalizeBody(raw);

        ContainsLoneSurrogate(result).Should().BeFalse();
    }

    [Fact]
    public void NormalizeBody_ForEmojiHeavyText_EncodesWithStrictUtf8WithoutThrowing()
    {
        var raw = "x" + string.Concat(Enumerable.Repeat(Emoji, 400));
        var result = ChatMessage.NormalizeBody(raw);

        var act = () => StrictUtf8.GetBytes(result);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Kesish MAKSIMUM bitta belgi yo'qotadi — chegara hech qachon 499 dan
    /// pastga tushmasligi kerak (aks holda xabar keraksiz qisqaradi).
    /// </summary>
    [Fact]
    public void NormalizeBody_ForEmojiHeavyText_LosesAtMostOneCharacter()
    {
        var raw = "x" + string.Concat(Enumerable.Repeat(Emoji, 400));

        var result = ChatMessage.NormalizeBody(raw);

        result.Length.Should().BeInRange(ChatMessage.MaxBodyLength - 1, ChatMessage.MaxBodyLength);
    }
}
