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
            sessionId: 1, authorId: 2, default, null, null, "Kuzatuv boshlandi.", Now);

        review.Verdict.Should().Be(SessionReviewVerdict.NotReviewed);
        review.IsDecided.Should().BeFalse();
    }

    [Fact]
    public void Create_TrimsAllSections_AndKeepsTheAuthorAndSession()
    {
        var review = SessionReview.Create(
            sessionId: 5, authorId: 9, SessionReviewVerdict.HasIssue,
            "  Tushuntirish aniq.  ", "  Vaqt sust taqsimlangan.  ", "  Mikrofon tekshiriladi.  ", Now);

        review.SessionId.Should().Be(5);
        review.AuthorId.Should().Be(9);
        review.Plus.Should().Be("Tushuntirish aniq.");
        review.Minus.Should().Be("Vaqt sust taqsimlangan.");
        review.Conclusion.Should().Be("Mikrofon tekshiriladi.");
        review.Verdict.Should().Be(SessionReviewVerdict.HasIssue);
        review.IsDecided.Should().BeTrue();
        review.CreatedAt.Should().Be(Now);
    }

    /// <summary>
    /// <see cref="SessionReview.Plus"/>/<see cref="SessionReview.Minus"/>
    /// IXTIYORIY: ko'p tahlilda ijobiy yoki kamchilik yozadigan narsa
    /// bo'lavermaydi, faqat xulosa.
    /// </summary>
    [Fact]
    public void Create_WithoutPlusOrMinus_Succeeds()
    {
        var review = SessionReview.Create(
            1, 2, SessionReviewVerdict.Approved, null, null, "Yaxshi dars.", Now);

        review.Plus.Should().BeNull();
        review.Minus.Should().BeNull();
        review.Conclusion.Should().Be("Yaxshi dars.");
    }

    /// <summary>
    /// Bo'sh xulosa — ma'nosiz qator. Uni ruxsat etish ro'yxatda "tahlil
    /// bor" nishonini yoqib, ochilganda bo'sh oyna ko'rsatardi: ustoz
    /// uchun bu eng chalg'ituvchi holat.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithAnEmptyConclusion_Throws(string? conclusion)
    {
        var act = () => SessionReview.Create(1, 2, SessionReviewVerdict.Approved, null, null, conclusion, Now);

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Chegara <c>StudentNote</c> nikidan KICHIK EMAS (2000) — sabab
    /// <c>SessionReview.MaxSectionLength</c> izohida: yagona 4000 belgilik
    /// `Body` endi uchta aniq maqsadli bo'limga bo'lingan.
    /// </summary>
    [Fact]
    public void Create_WithAnOverlongConclusion_Throws()
    {
        var conclusion = new string('a', SessionReview.MaxSectionLength + 1);

        var act = () => SessionReview.Create(1, 2, SessionReviewVerdict.Approved, null, null, conclusion, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithAnOverlongPlus_Throws()
    {
        var plus = new string('a', SessionReview.MaxSectionLength + 1);

        var act = () => SessionReview.Create(1, 2, SessionReviewVerdict.Approved, plus, null, "Xulosa.", Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_AtExactlyTheLimit_IsAccepted()
    {
        var conclusion = new string('a', SessionReview.MaxSectionLength);

        var review = SessionReview.Create(1, 2, SessionReviewVerdict.Approved, null, null, conclusion, Now);

        review.Conclusion.Should().HaveLength(SessionReview.MaxSectionLength);
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
        var review = SessionReview.Create(1, 2, SessionReviewVerdict.NotReviewed, null, null, "Qoralama.", Now);

        review.Edit(SessionReviewVerdict.Approved, "Kuchli tomon.", null, "Yakuniy xulosa.", Now.AddDays(1));

        review.AuthorId.Should().Be(2);
        review.Verdict.Should().Be(SessionReviewVerdict.Approved);
        review.Plus.Should().Be("Kuchli tomon.");
        review.Conclusion.Should().Be("Yakuniy xulosa.");
        review.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Edit_WithAnEmptyConclusion_Throws_AndLeavesTheReviewIntact()
    {
        var review = SessionReview.Create(1, 2, SessionReviewVerdict.Approved, null, null, "Yaxshi dars.", Now);

        var act = () => review.Edit(SessionReviewVerdict.HasIssue, null, null, "  ", Now.AddDays(1));

        act.Should().Throw<DomainException>();
        review.Conclusion.Should().Be("Yaxshi dars.");
        review.Verdict.Should().Be(SessionReviewVerdict.Approved);
    }

    /// <summary>
    /// TO'LIQ ALMASHTIRISH: <see cref="SessionReview.Edit"/> chaqirilganda
    /// avvalgi <c>Plus</c> berilmasa (<c>null</c>) — O'CHADI, "saqlab
    /// qolinmaydi" (`LessonGrade.Apply` bilan AYNI qoida).
    /// </summary>
    [Fact]
    public void Edit_WithoutPlus_ClearsThePreviousValue()
    {
        var review = SessionReview.Create(
            1, 2, SessionReviewVerdict.Approved, "Eski ijobiy fikr.", null, "Xulosa.", Now);

        review.Edit(SessionReviewVerdict.Approved, null, null, "Xulosa.", Now.AddDays(1));

        review.Plus.Should().BeNull();
    }
}
