using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Finance;

/// <summary>
/// <see cref="Payment"/> qoidalari — moliyaning eng nozik joyi.
///
/// Eski tizimda kelgan pul shunday hisoblanardi:
/// <c>months_covered = max(1, round(amount / monthly))</c> — ya'ni HAR QANDAY
/// summa kamida bitta to'liq oyni yopardi. 100 000 so'm 540 000 lik oyni
/// "to'langan" qilib qo'yardi va markaz jimgina pul yo'qotardi.
/// </summary>
public class PaymentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    private static Payment NewPayment(decimal amount = 540_000m, string period = "2026-07") => new()
    {
        Id = 1,
        StudentId = 42,
        GroupId = 7,
        Period = period,
        Amount = amount,
        BaseAmount = amount,
    };

    // ------------------------------------------------------- ★ asosiy eski xato

    /// <summary>
    /// ★ ENG MUHIM TEST: kam summa oyni YOPMAYDI.
    /// 540 000 lik oyga 100 000 tushsa — oy hamon qarz, qolgani 440 000.
    /// </summary>
    [Fact]
    public void ApplyPayment_WithLessThanFullAmount_LeavesMonthOpenAsPartial()
    {
        var payment = NewPayment();

        var applied = payment.ApplyPayment(100_000m, Now);

        applied.Should().Be(100_000m);
        payment.Status.Should().Be(PaymentStatus.Partial);
        payment.PaidAmount.Should().Be(100_000m);
        payment.Outstanding.Should().Be(440_000m, "qolgan qism hamon qarz");
        payment.IsOpen.Should().BeTrue();
        payment.PaidAt.Should().BeNull("oy to'liq to'lanmagan — to'lov sanasi qo'yilmaydi");
    }

    [Fact]
    public void ApplyPayment_WithExactAmount_ClosesMonth()
    {
        var payment = NewPayment();

        var applied = payment.ApplyPayment(540_000m, Now);

        applied.Should().Be(540_000m);
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.Outstanding.Should().Be(0m);
        payment.PaidAt.Should().Be(Now);
    }

    /// <summary>Ortiqcha pul shu oyga TUSHMAYDI — qaytariladi va keyingi oyga ketadi.</summary>
    [Fact]
    public void ApplyPayment_WithMoreThanOutstanding_TakesOnlyWhatIsOwed()
    {
        var payment = NewPayment();

        var applied = payment.ApplyPayment(700_000m, Now);

        applied.Should().Be(540_000m, "faqat qarz qismi olinadi");
        payment.PaidAmount.Should().Be(540_000m, "oy summasidan oshib ketmasin");
        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void ApplyPayment_TwiceInParts_ClosesMonthOnSecondPayment()
    {
        var payment = NewPayment();

        payment.ApplyPayment(200_000m, Now);
        payment.ApplyPayment(340_000m, Now.AddDays(3));

        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.PaidAmount.Should().Be(540_000m);
        payment.PaidAt.Should().Be(Now.AddDays(3), "yopilgan payt — oxirgi to'lov vaqti");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void ApplyPayment_WithNonPositiveAmount_Throws(decimal amount)
    {
        var payment = NewPayment();

        var act = () => payment.ApplyPayment(amount, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ApplyPayment_OnClosedMonth_Throws()
    {
        var payment = NewPayment();
        payment.ApplyPayment(540_000m, Now);

        var act = () => payment.ApplyPayment(10_000m, Now);

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ kechirim

    [Fact]
    public void Waive_LeavesNoDebtButRecordsNoMoney()
    {
        var payment = NewPayment();

        payment.Waive(Now, actorId: 5);

        payment.Status.Should().Be(PaymentStatus.Waived);
        payment.IsOpen.Should().BeFalse("kechirilgan oy qarz emas");
        payment.PaidAt.Should().BeNull("kassaga pul tushmagan — kunlik tushum soxta bo'lmasin");
        payment.MarkedById.Should().Be(5);
    }

    [Fact]
    public void Waive_OnPaidMonth_Throws()
    {
        var payment = NewPayment();
        payment.ApplyPayment(540_000m, Now);

        var act = () => payment.Waive(Now);

        act.Should().Throw<DomainException>();
    }

    // ----------------------------------------------------------------- qaytarish

    /// <summary>
    /// ★ Eski tizimda qaytarish faqat jurnalga yozilardi va oy "to'langan"
    /// bo'lib qolardi — pul qaytgan, lekin tizim o'quvchini qarzsiz deb bilardi.
    /// </summary>
    [Fact]
    public void Reverse_OnPaidMonth_ReopensDebt()
    {
        var payment = NewPayment();
        payment.ApplyPayment(540_000m, Now);

        var returned = payment.Reverse(540_000m, Now.AddDays(1));

        returned.Should().Be(540_000m);
        payment.Status.Should().Be(PaymentStatus.Due);
        payment.PaidAmount.Should().Be(0m);
        payment.Outstanding.Should().Be(540_000m);
        payment.PaidAt.Should().BeNull();
    }

    [Fact]
    public void Reverse_PartOfPayment_LeavesMonthPartial()
    {
        var payment = NewPayment();
        payment.ApplyPayment(540_000m, Now);

        payment.Reverse(200_000m, Now.AddDays(1));

        payment.Status.Should().Be(PaymentStatus.Partial);
        payment.PaidAmount.Should().Be(340_000m);
        payment.Outstanding.Should().Be(200_000m);
    }

    [Fact]
    public void Reverse_MoreThanPaid_ReturnsOnlyWhatWasPaid()
    {
        var payment = NewPayment();
        payment.ApplyPayment(100_000m, Now);

        var returned = payment.Reverse(500_000m, Now.AddDays(1));

        returned.Should().Be(100_000m, "tushmagan pulni qaytarib bo'lmaydi");
        payment.PaidAmount.Should().Be(0m);
    }

    [Fact]
    public void Reverse_OnWaivedMonth_Throws()
    {
        var payment = NewPayment();
        payment.Waive(Now);

        var act = () => payment.Reverse(100_000m, Now);

        act.Should().Throw<DomainException>();
    }

    // ---------------------------------------------------------------- invariant

    [Fact]
    public void Validate_WithPaidMoreThanAmount_Throws()
    {
        var payment = NewPayment();
        payment.PaidAmount = 600_000m;

        var act = payment.Validate;

        act.Should().Throw<DomainException>("bazadagi CHECK bilan bir xil qoida");
    }

    /// <summary>
    /// ★ Uch summa bir-biriga mos bo'lishi shart. Busiz
    /// <c>BaseAmount=600 000, DiscountAmount=60 000, Amount=999 999</c> qatori
    /// barcha boshqa tekshiruvlardan o'tib ketardi va moliya hisoboti
    /// ("kutilgan tushum" va "berilgan chegirma") uydirmaga aylanardi.
    /// </summary>
    [Fact]
    public void Validate_WhenAmountDoesNotMatchBaseMinusDiscount_Throws()
    {
        var payment = NewPayment(600_000m);
        payment.DiscountAmount = 60_000m;
        payment.Amount = 999_999m;

        var act = payment.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_WithConsistentDiscount_Passes()
    {
        var payment = NewPayment(600_000m);
        payment.DiscountAmount = 60_000m;
        payment.Amount = 540_000m;

        var act = payment.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithDiscountAboveBase_Throws()
    {
        var payment = NewPayment();
        payment.DiscountAmount = payment.BaseAmount + 1m;

        var act = payment.Validate;

        act.Should().Throw<DomainException>();
    }
}
