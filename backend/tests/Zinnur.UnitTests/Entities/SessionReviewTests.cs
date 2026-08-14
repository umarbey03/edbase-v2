using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// DARS SIFATI TAHLILI (R29 / R30) — domain qoidalari.
///
/// ★ NIMA UCHUN BU TESTLAR KERAK: tahlil matni chegarasi va "xulosasiz
/// yaratilgan tahlil QORALAMA bo'ladi" qoidasi servisda emas, entity'da
/// turadi. Servis qatlamida bo'lganda ular upsert'ning ikkala shoxobchasida
/// (yaratish va tahrirlash) alohida yozilardi va bittasida albatta
/// tushib qolardi.
/// </summary>
public sealed class SessionReviewTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    // ================================================================= yaratish

    /// <summary>
    /// 🔴 XULOSASIZ TAHLIL — QORALAMA, "Tasdiqlandi" EMAS.
    ///
    /// <c>SessionReviewVerdict.NotReviewed = 0</c> bo'lishi shart: u C#
    /// ning default qiymati. Agar 0 da <c>Approved</c> tursa, xulosani
    /// unutgan xodim darsni JIMGINA tasdiqlab qo'yardi.
    /// </summary>
    [Fact]
    public void Create_WithoutAVerdict_IsADraft_NotAnApproval()
    {
        var review = SessionReview.Create(
            sessionId: 1, authorId: 2, default, "Kuzatuv boshlandi.", Now);

        review.Verdict.Should().Be(SessionReviewVerdict.NotReviewed);
        review.IsDecided.Should().BeFalse();
    }

    [Fact]
    public void Create_TrimsTheBody_AndKeepsTheAuthorAndSession()
    {
        var review = SessionReview.Create(
            sessionId: 5, authorId: 9, SessionReviewVerdict.HasIssue, "  Vaqt sust taqsimlangan.  ", Now);

        review.SessionId.Should().Be(5);
        review.AuthorId.Should().Be(9);
        review.Body.Should().Be("Vaqt sust taqsimlangan.");
        review.Verdict.Should().Be(SessionReviewVerdict.HasIssue);
        review.IsDecided.Should().BeTrue();
        review.CreatedAt.Should().Be(Now);
    }

    /// <summary>
    /// Bo'sh tahlil — ma'nosiz qator. Uni ruxsat etish ro'yxatda "tahlil
    /// bor" nishonini yoqib, ochilganda bo'sh oyna ko'rsatardi: ustoz
    /// uchun bu eng chalg'ituvchi holat.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithAnEmptyBody_Throws(string? body)
    {
        var act = () => SessionReview.Create(1, 2, SessionReviewVerdict.Approved, body, Now);

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Chegara <c>StudentNote</c> nikidan KATTA (4000 va 2000), lekin
    /// mavjud — sabab <c>SessionReview.MaxBodyLength</c> izohida.
    /// </summary>
    [Fact]
    public void Create_WithAnOverlongBody_Throws()
    {
        var body = new string('a', SessionReview.MaxBodyLength + 1);

        var act = () => SessionReview.Create(1, 2, SessionReviewVerdict.Approved, body, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_AtExactlyTheLimit_IsAccepted()
    {
        var body = new string('a', SessionReview.MaxBodyLength);

        var review = SessionReview.Create(1, 2, SessionReviewVerdict.Approved, body, Now);

        review.Body.Should().HaveLength(SessionReview.MaxBodyLength);
    }

    // ================================================================= tahrirlash

    /// <summary>
    /// 🔴 MUALLIF TAHRIRLASHDA O'ZGARMAYDI.
    ///
    /// <c>StudentNote.Edit</c> dagi AYNI qoida: tahrirlash "boshqa odam
    /// yozgan" qilib ko'rsatish yo'li bo'lmasligi kerak. Ustoz uchun
    /// "kim aytdi" savoli barqaror javobga ega bo'lishi shart.
    /// </summary>
    [Fact]
    public void Edit_DoesNotChangeTheAuthor()
    {
        var review = SessionReview.Create(1, 2, SessionReviewVerdict.NotReviewed, "Qoralama.", Now);

        review.Edit(SessionReviewVerdict.Approved, "Yakuniy xulosa.", Now.AddDays(1));

        review.AuthorId.Should().Be(2);
        review.Verdict.Should().Be(SessionReviewVerdict.Approved);
        review.Body.Should().Be("Yakuniy xulosa.");
        review.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Edit_WithAnEmptyBody_Throws_AndLeavesTheReviewIntact()
    {
        var review = SessionReview.Create(1, 2, SessionReviewVerdict.Approved, "Yaxshi dars.", Now);

        var act = () => review.Edit(SessionReviewVerdict.HasIssue, "  ", Now.AddDays(1));

        act.Should().Throw<DomainException>();
        review.Body.Should().Be("Yaxshi dars.");
        review.Verdict.Should().Be(SessionReviewVerdict.Approved);
    }
}
