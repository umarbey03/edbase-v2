using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Payments;

namespace Zinnur.IntegrationTests.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// OYLIK TO'LOV YOZUVLARINI AVTOMATIK OCHISH (FAZA 5.5)
/// ════════════════════════════════════════════════════════════════════════
///
/// Eski tizimda oy QO'LDA ochilardi va ikkala yo'nalishda ham zarar bor edi:
/// unutilsa qarz umuman hisoblanmasdi, ikki marta bosilsa qarz ikki barobar
/// ko'rinardi.
///
/// ★ VAQT MINTAQASI ALOHIDA SINALADI: server UTC'da ishlaydi, markaz esa
/// Toshkent vaqtida yashaydi. 1-avgust 00:30 Toshkentda UTC'da hali
/// 31-iyul — "joriy oy" UTC bo'yicha hisoblansa butun markazning hisobi
/// bir oyga surilib ketardi.
/// </summary>
public sealed class MonthlyBillingJobTests(BillingJobFactory factory)
    : IClassFixture<BillingJobFactory>
{
    /// <summary>Toshkent UTC+5: 19:30Z — mahalliy vaqtda ERTASI kun 00:30.</summary>
    private static readonly DateTimeOffset JustAfterLocalMidnight =
        new(2026, 7, 31, 19, 30, 0, TimeSpan.Zero);

    /// <summary>18:30Z — mahalliy vaqtda hali o'sha kun 23:30.</summary>
    private static readonly DateTimeOffset JustBeforeLocalMidnight =
        new(2026, 7, 31, 18, 30, 0, TimeSpan.Zero);

    /// <summary>Oy o'rtasi — chegaraviy holatlardan uzoq.</summary>
    private static readonly DateTimeOffset MidJuly =
        new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);

    // ================================================================= 1) IDEMPOTENTLIK

    /// <summary>
    /// ★ IKKI YURISH — BITTA YOZUV. Vazifa har 30 daqiqada yuradi va
    /// konteyner qayta ko'tarilishi ham mumkin; takror yurish yangi qator
    /// YARATMASLIGI shart.
    ///
    /// Qoida <c>IPaymentService.OpenPeriodAsync</c> da (u allaqachon
    /// idempotent va unikal indeks bilan qo'llab-quvvatlangan) — bu test
    /// fon vazifasi o'sha yo'ldan borishini qulflaydi.
    /// </summary>
    [Fact]
    public async Task TwoRuns_CreateTheMonthlyRowOnlyOnce()
    {
        await factory.EnsureTariffAsync();
        factory.Clock.Set(MidJuly);

        var (studentId, _) = await factory.CreateBillableStudentAsync("billing-idem");

        var first = await factory.RunBillingJobAsync();
        first.Note.Should().Be("2026-07");

        (await RowCountAsync(studentId, "2026-07")).Should().Be(1);

        var second = await factory.RunBillingJobAsync();

        second.Processed.Should().Be(0, "takror yurish yangi yozuv yaratmasligi kerak");
        (await RowCountAsync(studentId, "2026-07")).Should().Be(1);
    }

    // ================================================================= 2) VAQT MINTAQASI

    /// <summary>
    /// 🔴 CHEGARA: UTC'da hali 31-iyul, Toshkentda esa allaqachon 1-avgust.
    /// Yozuv AVGUST oyiga ochilishi kerak.
    /// </summary>
    [Fact]
    public async Task JustAfterLocalMidnight_OpensTheNewMonth()
    {
        await factory.EnsureTariffAsync();
        factory.Clock.Set(JustAfterLocalMidnight);

        var (studentId, _) = await factory.CreateBillableStudentAsync("billing-tz-next");

        var result = await factory.RunBillingJobAsync();

        result.Note.Should().Be("2026-08",
            "oy MAHALLIY vaqtda hisoblanadi (UTC'da hali 31-iyul)");

        (await RowCountAsync(studentId, "2026-08")).Should().Be(1);
        (await RowCountAsync(studentId, "2026-07")).Should().Be(0);
    }

    /// <summary>
    /// Chegaraning ikkinchi tomoni: mahalliy vaqtda hali 31-iyul 23:30 —
    /// yozuv IYUL oyiga ochilishi kerak (avgustga emas).
    /// </summary>
    [Fact]
    public async Task JustBeforeLocalMidnight_StillOpensTheCurrentMonth()
    {
        await factory.EnsureTariffAsync();
        factory.Clock.Set(JustBeforeLocalMidnight);

        var (studentId, _) = await factory.CreateBillableStudentAsync("billing-tz-same");

        var result = await factory.RunBillingJobAsync();

        result.Note.Should().Be("2026-07");
        (await RowCountAsync(studentId, "2026-07")).Should().Be(1);
    }

    // ================================================================= 3) ISTISNO

    /// <summary>
    /// TO'LOVDAN OZOD (<c>PaymentExempt</c>) o'quvchiga ham yozuv ochiladi —
    /// va bu ATAYLAB.
    ///
    /// Bayroq "hisob chiqarilmasin" degani emas: u faqat qarz uchun
    /// BLOKLASHDAN ozod qiladi (<c>PaymentBlockService</c>). Yozuv
    /// ochilmasa markazning haqiqiy hisoboti buzilardi — bepul o'qiyotgan
    /// o'quvchi hech qayerda ko'rinmasdi. Summani nolga tushirish uchun
    /// ALOHIDA mexanizm bor: 100% chegirma (<c>StudentDiscount</c>).
    /// </summary>
    [Fact]
    public async Task ExemptStudent_StillGetsAMonthlyRow()
    {
        await factory.EnsureTariffAsync();
        factory.Clock.Set(MidJuly);

        var (studentId, _) = await factory.CreateBillableStudentAsync("billing-exempt");
        await MarkExemptAsync(studentId);

        await factory.RunBillingJobAsync();

        (await RowCountAsync(studentId, "2026-07")).Should().Be(1,
            "istisno bloklashga taalluqli, hisob-kitobga emas");
    }

    // ------------------------------------------------------------------ yordamchi

    private Task<int> RowCountAsync(long studentId, string period) =>
        factory.WithDbAsync(db => db.Payments
            .AsNoTracking()
            .CountAsync(p => p.StudentId == studentId && p.Period == period));

    /// <summary>
    /// <c>PaymentExempt</c> — SOYA (shadow) ustun, shuning uchun u
    /// <c>Entry</c> orqali yoziladi (izoh: <see cref="PaymentFields"/>).
    /// </summary>
    private Task<int> MarkExemptAsync(long studentId) =>
        factory.WithDbAsync(async db =>
        {
            var student = await db.Users.FirstAsync(u => u.Id == studentId);
            db.Entry(student).Property<bool>(PaymentFields.Exempt).CurrentValue = true;
            return await db.SaveChangesAsync();
        });
}
