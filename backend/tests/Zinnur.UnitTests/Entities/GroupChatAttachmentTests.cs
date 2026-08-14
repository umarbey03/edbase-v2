using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// ========================================================================
/// R16b — CHAT BIRIKTIRMASINING DOMAIN QOIDALARI
/// ========================================================================
///
/// Bu yerda ruxsat ham, ombor ham, baza ham YO'Q. Qulflanadigan narsa
/// ikkita va ikkalasi ham NOZIK:
///
///  1) 🔴 BO'SH MATN INVARIANTINING O'ZGARISHI. Ilgari "xabar bo'sh
///     bo'lishi mumkin emas" qoidasi MATNGA tegishli edi; endi u
///     MAZMUNGA tegishli ("matn YOKI biriktirma"). Testlar shuni
///     qulflaydi: yumshatish FAQAT biriktirmali yo'lda amal qiladi va
///     oddiy matn yo'liga SIZIB O'TMAYDI.
///
///  2) FAYL NOMINI TOZALASH. Klient bergan nom `Content-Disposition`
///     sarlavhasiga tushadi — yo'l ajratgichi, boshqaruv belgisi yoki
///     qo'shtirnoq javobni buzardi.
/// </summary>
public class GroupChatAttachmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    // ================================================================= bo'sh matn

    /// <summary>
    /// ★ ASOSIY YANGI QOIDA: izohsiz surat — HAQIQIY xabar.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWithAttachments_AllowsEmptyBody(string? body)
    {
        var message = GroupChatMessage.CreateWithAttachments(
            groupId: 1,
            GroupChatChannel.Teacher,
            senderId: 7,
            "Ali",
            UserRole.Student,
            body,
            attachmentCount: 1,
            Now);

        message.Body.Should().BeEmpty();
    }

    /// <summary>
    /// 🔴 YUMSHATISH SIZIB O'TMAYDI: biriktirmasiz bo'sh xabar HAMON rad
    /// etiladi — endi `CreateWithAttachments` ning O'ZIDA ham.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateWithAttachments_WithoutAttachments_Throws(int count)
    {
        var act = () => GroupChatMessage.CreateWithAttachments(
            1, GroupChatChannel.Teacher, 7, "Ali", UserRole.Student, null, count, Now);

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Oddiy (matnli) fabrika O'ZGARMADI — bo'sh matnni avvalgidek rad etadi.
    /// Bu test `GroupChatMessageTests` dagi bilan ATAYLAB takrorlanadi: u
    /// yerda "hozirgi xatti-harakat", bu yerda esa "yumshatish tegmagani"
    /// qulflanadi.
    /// </summary>
    [Fact]
    public void Create_StillRejectsEmptyBody()
    {
        var act = () => GroupChatMessage.Create(
            1, GroupChatChannel.Teacher, 7, "Ali", UserRole.Student, "   ", Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CreateWithAttachments_AboveMaxCount_Throws()
    {
        var act = () => GroupChatMessage.CreateWithAttachments(
            1,
            GroupChatChannel.Teacher,
            7,
            "Ali",
            UserRole.Student,
            "izoh",
            GroupChatAttachment.MaxPerMessage + 1,
            Now);

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Matn berilsa u AVVALGIDEK tozalanadi va kesiladi — ikkinchi fabrika
    /// o'z normalizatsiyasini yozmaydi.
    /// </summary>
    [Fact]
    public void CreateWithAttachments_TrimsAndTruncatesBody()
    {
        var message = GroupChatMessage.CreateWithAttachments(
            1,
            GroupChatChannel.Teacher,
            7,
            "Ali",
            UserRole.Student,
            "  " + new string('a', GroupChatMessage.MaxBodyLength + 50) + "  ",
            attachmentCount: 1,
            Now);

        message.Body.Should().HaveLength(GroupChatMessage.MaxBodyLength);
    }

    /// <summary>
    /// Surrogat juftlik himoyasi biriktirmali yo'lda HAM ishlaydi (ikkalasi
    /// bitta `MessageText` ni chaqiradi — nusxa yo'q).
    /// </summary>
    [Fact]
    public void CreateWithAttachments_DoesNotSplitSurrogatePair()
    {
        var body = new string('a', GroupChatMessage.MaxBodyLength - 1) + "😀";

        var message = GroupChatMessage.CreateWithAttachments(
            1, GroupChatChannel.Teacher, 7, "Ali", UserRole.Student, body, 1, Now);

        char.IsSurrogate(message.Body[^1]).Should().BeFalse();
    }

    /// <summary>Kanal va guruh tekshiruvlari ikkala fabrikada ham BIR XIL.</summary>
    [Fact]
    public void CreateWithAttachments_WithUnknownChannel_Throws()
    {
        var act = () => GroupChatMessage.CreateWithAttachments(
            1, (GroupChatChannel)77, 7, "Ali", UserRole.Student, null, 1, Now);

        act.Should().Throw<DomainException>();
    }

    // ================================================================= fayl nomi

    /// <summary>
    /// 🔴 YO'L AJRATGICHLARI OLIB TASHLANADI. Ikkala ajratgich ham
    /// tekshiriladi: `Path.GetFileName` Linux'da `\` ni ajratgich DEB
    /// BILMAYDI, Windows klienti esa aynan shuni yuboradi.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\Ali\rasm.jpg", "rasm.jpg")]
    [InlineData("papka/ichida/hujjat.pdf", "hujjat.pdf")]
    public void SanitizeFileName_StripsPath(string raw, string expected)
    {
        GroupChatAttachment.SanitizeFileName(raw).Should().Be(expected);
    }

    /// <summary>
    /// Qo'shtirnoq va boshqaruv belgilari `Content-Disposition` ni buzardi.
    /// </summary>
    [Fact]
    public void SanitizeFileName_RemovesQuotesAndControlChars()
    {
        var cleaned = GroupChatAttachment.SanitizeFileName("a\"b\r\nc\td.pdf");

        cleaned.Should().NotBeNull();
        cleaned!.Should().NotContain("\"");
        cleaned.Should().NotContain("\r");
        cleaned.Should().NotContain("\n");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void SanitizeFileName_WhenNothingLeft_ReturnsNull(string? raw)
    {
        GroupChatAttachment.SanitizeFileName(raw).Should().BeNull();
    }

    /// <summary>Uzun nom kesiladi va emoji IKKIGA BO'LINMAYDI.</summary>
    [Fact]
    public void SanitizeFileName_TruncatesWithoutSplittingSurrogatePair()
    {
        var raw = new string('n', GroupChatAttachment.MaxFileNameLength - 1) + "😀";

        var cleaned = GroupChatAttachment.SanitizeFileName(raw);

        cleaned.Should().NotBeNull();
        cleaned!.Length.Should().BeLessThanOrEqualTo(GroupChatAttachment.MaxFileNameLength);
        char.IsSurrogate(cleaned[^1]).Should().BeFalse();
    }

    // ================================================================= Validate

    [Fact]
    public void Validate_WithoutMessage_Throws()
    {
        var attachment = new GroupChatAttachment
        {
            ObjectKey = "group-chat/a.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 10,
        };

        var act = attachment.Validate;

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Validate_WithNonPositiveSize_Throws(long size)
    {
        var attachment = new GroupChatAttachment
        {
            MessageId = 1,
            ObjectKey = "group-chat/a.jpg",
            ContentType = "image/jpeg",
            SizeBytes = size,
        };

        var act = attachment.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithValidRow_DoesNotThrow()
    {
        var attachment = new GroupChatAttachment
        {
            MessageId = 1,
            Kind = AttachmentKind.Document,
            ObjectKey = "group-chat/a.pdf",
            ContentType = "application/pdf",
            FileName = "shartnoma.pdf",
            SizeBytes = 4096,
        };

        var act = attachment.Validate;

        act.Should().NotThrow();
    }
}
