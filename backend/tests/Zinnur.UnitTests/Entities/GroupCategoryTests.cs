using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// ========================================================================
/// GURUH KATEGORIYASI (R21b) — INVARIANT VA GURUH BILAN CHEGARA
/// ========================================================================
///
/// Bu yerdagi testlarning ikkitasi mazmunan eng muhim:
///
///  • <see cref="Validate_WithBlankName_Throws"/> — nomi bo'sh kategoriya
///    tanlagichda KO'RINMAS bo'lib qolardi: guruhda yorliq bor, lekin
///    ekranda hech nima yo'q. Xodim uni "yorliqsiz" deb o'ylab, ikkinchi
///    marta qo'shardi.
///
///  • <see cref="Group_WithoutCategory_IsValid"/> — talab kelganda bazada
///    33 ta guruh bor edi va ularning BIRORTASIDA kategoriya yo'q. Agar
///    kategoriya guruh invariantiga kirsa, o'sha 33 guruhning har qanday
///    tahriri 409 bilan rad etilardi.
/// </summary>
public class GroupCategoryTests
{
    private static GroupCategory NewCategory(string name = "IELTS") => new() { Name = name };

    // ------------------------------------------------------------------ invariant

    [Fact]
    public void Validate_WithProperName_DoesNotThrow()
    {
        var category = NewCategory();

        var act = () => category.Validate();

        act.Should().NotThrow();
    }

    /// <summary>Bo'sh nom — ko'rinmas yorliq (sinf izohidagi 1-sabab).</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Validate_WithBlankName_Throws(string name)
    {
        var category = NewCategory(name);

        var act = () => category.Validate();

        act.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Chegaradan uzun nom — bazada ustun <c>varchar(100)</c>, ya'ni
    /// tekshiruvsiz u SQL darajasidagi tushunarsiz xatoga aylanardi.
    /// </summary>
    [Fact]
    public void Validate_WithTooLongName_Throws()
    {
        var category = NewCategory(new string('a', GroupCategory.MaxNameLength + 1));

        var act = () => category.Validate();

        act.Should().Throw<DomainException>();
    }

    /// <summary>Aynan chegara qabul qilinadi (off-by-one qo'riqchisi).</summary>
    [Fact]
    public void Validate_AtExactlyMaxLength_DoesNotThrow()
    {
        var category = NewCategory(new string('a', GroupCategory.MaxNameLength));

        var act = () => category.Validate();

        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------ guruh bilan chegara

    /// <summary>
    /// 🔴 KATEGORIYA GURUH INVARIANTIGA KIRMAYDI.
    ///
    /// Mavjud 33 guruhning birortasida yorliq yo'q; kategoriya majburiy
    /// bo'lsa ularning HAR QANDAY tahriri (hatto nomni o'zgartirish ham)
    /// 409 bilan rad etilardi va o'quv bo'limi ularni umuman tahrirlay
    /// olmasdi.
    /// </summary>
    [Fact]
    public void Group_WithoutCategory_IsValid()
    {
        var group = NewGroup();

        group.CategoryId.Should().BeNull("standart qiymat — yorliqsiz guruh");

        var act = () => group.ValidateScheduleRule();

        act.Should().NotThrow();
    }

    /// <summary>
    /// ★ KATEGORIYA JADVALGA TA'SIR QILMAYDI.
    ///
    /// <c>ScheduleRuleDiffersFrom</c> "jadval qayta tuzilsinmi" degan
    /// savolga javob beradi va u faqat sana/kun/soat/davomiylik/oy/tur
    /// bo'yicha hisoblanadi. Kategoriya u yerga kirib qolsa, yorliqni
    /// almashtirish 70 ta darsni JIMGINA qayta yaratardi — dars Id'lari,
    /// LiveKit xona nomlari va tarqatilgan havolalar buzilardi.
    ///
    /// Bu test aynan shu regressiyani qo'riqlaydi: kategoriya o'zgargan
    /// bo'lsa ham taqqoslash `false` qaytarishi kerak.
    /// </summary>
    [Fact]
    public void ChangingCategory_DoesNotAffectTheScheduleRule()
    {
        var group = NewGroup();
        group.CategoryId = 7;

        var differs = group.ScheduleRuleDiffersFrom(
            group.StartDate,
            group.Weekdays,
            group.StartTime,
            group.DurationMinutes,
            group.CourseMonths,
            group.Type);

        differs.Should().BeFalse(
            "kategoriya — YORLIQ, u dars jadvaliga hech qanday aloqasi yo'q");
    }

    private static Group NewGroup() => new()
    {
        Name = "ATF-1",
        StartDate = new DateOnly(2026, 3, 2),
        Weekdays = [DayOfWeek.Monday, DayOfWeek.Wednesday],
    };
}
