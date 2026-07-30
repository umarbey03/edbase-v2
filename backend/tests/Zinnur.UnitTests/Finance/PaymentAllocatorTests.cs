using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Finance;

namespace Zinnur.UnitTests.Finance;

/// <summary>
/// Pulni oylarga taqsimlash va orqaga qaytarish.
///
/// Bu qoidalar eski tizimda SQL bilan aralashib ketgan servis ichida edi va
/// umuman test qilinmagan — eng qimmat moliyaviy xato aynan shu yerda yashagan.
/// </summary>
public class PaymentAllocatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    private static Payment Due(long id, string period, decimal amount = 540_000m) => new()
    {
        Id = id,
        StudentId = 42,
        GroupId = 7,
        Period = period,
        Amount = amount,
        BaseAmount = amount,
    };

    private static StudentAccount Account(decimal balance = 0m) => new()
    {
        StudentId = 42,
        Balance = balance,
    };

    // ------------------------------------------------------------ taqsimlash

    /// <summary>★ Eng eski qarz birinchi yopiladi.</summary>
    [Fact]
    public void Allocate_PaysOldestDebtFirst()
    {
        var may = Due(1, "2026-05");
        var june = Due(2, "2026-06");
        var july = Due(3, "2026-07");

        // Ro'yxat ATAYLAB aralash tartibda: tartib Domain ichida quriladi.
        var result = PaymentAllocator.Allocate([july, may, june], 540_000m, Now);

        may.Status.Should().Be(PaymentStatus.Paid, "eng eski oy birinchi yopiladi");
        june.Status.Should().Be(PaymentStatus.Due);
        july.Status.Should().Be(PaymentStatus.Due);
        result.MonthsClosed.Should().Be(1);
        result.Leftover.Should().Be(0m);
    }

    [Fact]
    public void Allocate_WithEnoughMoney_ClosesSeveralMonthsInOrder()
    {
        var may = Due(1, "2026-05");
        var june = Due(2, "2026-06");
        var july = Due(3, "2026-07");

        var result = PaymentAllocator.Allocate([may, june, july], 1_200_000m, Now);

        may.Status.Should().Be(PaymentStatus.Paid);
        june.Status.Should().Be(PaymentStatus.Paid);
        july.Status.Should().Be(PaymentStatus.Partial, "qolgan 120 000 uchinchi oyga qisman tushdi");
        july.PaidAmount.Should().Be(120_000m);

        result.MonthsClosed.Should().Be(2);
        result.MonthsPartial.Should().Be(1);
        result.Applied.Should().Be(1_200_000m);
        result.Leftover.Should().Be(0m);
    }

    /// <summary>
    /// ★ Ortiqcha pul YO'QOLMAYDI — `Leftover` da qaytadi va chaqiruvchi uni
    /// balansga qo'shadi. Eski tizimda u shunchaki e'tibordan chetda qolardi.
    /// </summary>
    [Fact]
    public void Allocate_WithMoreMoneyThanDebt_ReturnsLeftover()
    {
        var july = Due(1, "2026-07");

        var result = PaymentAllocator.Allocate([july], 800_000m, Now);

        july.Status.Should().Be(PaymentStatus.Paid);
        result.Applied.Should().Be(540_000m);
        result.Leftover.Should().Be(260_000m, "ortiqcha pul chaqiruvchiga qaytadi");
    }

    [Fact]
    public void Allocate_SkipsClosedAndWaivedMonths()
    {
        var paid = Due(1, "2026-05");
        paid.ApplyPayment(540_000m, Now);

        var waived = Due(2, "2026-06");
        waived.Waive(Now);

        var open = Due(3, "2026-07");

        var result = PaymentAllocator.Allocate([paid, waived, open], 540_000m, Now);

        open.Status.Should().Be(PaymentStatus.Paid);
        result.TouchedIds.Should().ContainSingle().And.Contain(3L);
    }

    [Fact]
    public void Allocate_WithNoOpenDebt_ReturnsEverythingAsLeftover()
    {
        var result = PaymentAllocator.Allocate([], 300_000m, Now);

        result.Applied.Should().Be(0m);
        result.Leftover.Should().Be(300_000m);
    }

    [Fact]
    public void Allocate_WithNonPositiveAmount_Throws()
    {
        var act = () => PaymentAllocator.Allocate([Due(1, "2026-07")], 0m, Now);

        act.Should().Throw<DomainException>();
    }

    // ---------------------------------------------------------------- balans

    /// <summary>
    /// Oldindan to'lagan o'quvchi keyingi oy qarzdor bo'lib chiqmasligi kerak:
    /// yangi oy yozuvi ochilgach balansdan avtomatik yopiladi.
    /// </summary>
    [Fact]
    public void ConsumeBalance_ClosesNewMonthFromPrepaidMoney()
    {
        var account = Account(600_000m);
        var july = Due(1, "2026-07");

        var result = PaymentAllocator.ConsumeBalance(account, [july], Now);

        july.Status.Should().Be(PaymentStatus.Paid);
        result.MonthsClosed.Should().Be(1);
        account.Balance.Should().Be(60_000m, "faqat kerakli qism ishlatiladi");
    }

    [Fact]
    public void ConsumeBalance_WithLessThanDebt_LeavesMonthPartialAndEmptiesBalance()
    {
        var account = Account(200_000m);
        var july = Due(1, "2026-07");

        PaymentAllocator.ConsumeBalance(account, [july], Now);

        july.Status.Should().Be(PaymentStatus.Partial);
        july.PaidAmount.Should().Be(200_000m);
        account.Balance.Should().Be(0m);
    }

    [Fact]
    public void ConsumeBalance_WithoutDebt_KeepsBalanceUntouched()
    {
        var account = Account(500_000m);

        var result = PaymentAllocator.ConsumeBalance(account, [], Now);

        result.Applied.Should().Be(0m);
        account.Balance.Should().Be(500_000m);
    }

    // -------------------------------------------------------------- qaytarish

    /// <summary>Avval balansdan yechiladi — u yerdagi pul hali oyga tegmagan.</summary>
    [Fact]
    public void Reverse_TakesFromBalanceFirst()
    {
        var account = Account(300_000m);
        var july = Due(1, "2026-07");
        july.ApplyPayment(540_000m, Now);

        var result = PaymentAllocator.Reverse(account, [july], 200_000m, Now);

        result.FromBalance.Should().Be(200_000m);
        result.FromPayments.Should().Be(0m);
        account.Balance.Should().Be(100_000m);
        july.Status.Should().Be(PaymentStatus.Paid, "oyga tegilmadi");
    }

    /// <summary>
    /// Balans yetmasa — ENG YANGI to'langan oydan qaytariladi. Eski oylar
    /// yopiq qoladi, aks holda o'quvchi bir necha oy oldin bloklangan holatga
    /// qaytib qolardi.
    /// </summary>
    [Fact]
    public void Reverse_WhenBalanceIsNotEnough_TakesFromNewestPaidMonth()
    {
        var june = Due(1, "2026-06");
        june.ApplyPayment(540_000m, Now);
        var july = Due(2, "2026-07");
        july.ApplyPayment(540_000m, Now);

        var result = PaymentAllocator.Reverse(Account(0m), [june, july], 540_000m, Now);

        july.Status.Should().Be(PaymentStatus.Due, "eng yangi oy ochiladi");
        june.Status.Should().Be(PaymentStatus.Paid, "eski oy tegilmaydi");
        result.FromPayments.Should().Be(540_000m);
        result.Unreturned.Should().Be(0m);
    }

    [Fact]
    public void Reverse_MoreThanEverPaid_ReportsUnreturnedRemainder()
    {
        var july = Due(1, "2026-07");
        july.ApplyPayment(100_000m, Now);

        var result = PaymentAllocator.Reverse(Account(0m), [july], 500_000m, Now);

        result.Returned.Should().Be(100_000m);
        result.Unreturned.Should().Be(400_000m, "tushmagan pul qaytarilgan deb yozilmaydi");
    }

    [Fact]
    public void Reverse_SpansBalanceAndPayments()
    {
        var account = Account(100_000m);
        var july = Due(1, "2026-07");
        july.ApplyPayment(540_000m, Now);

        var result = PaymentAllocator.Reverse(account, [july], 300_000m, Now);

        result.FromBalance.Should().Be(100_000m);
        result.FromPayments.Should().Be(200_000m);
        account.Balance.Should().Be(0m);
        july.PaidAmount.Should().Be(340_000m);
        july.Status.Should().Be(PaymentStatus.Partial);
    }
}
