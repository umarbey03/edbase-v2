using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PayrollRuleConfiguration : IEntityTypeConfiguration<PayrollRule>
{
    /// <summary>Summalar MANFIY bo'lmaydi — sabab <c>TariffConfiguration</c> dagi bilan bir xil.</summary>
    private const string AmountsNonNegativeCheck =
        """("Amount" >= 0 AND ("PlanAmount" IS NULL OR "PlanAmount" >= 0))""";

    /// <summary>Ko'paytiruvchi berilgan bo'lsa 1 dan kichik bo'lmaydi (`PayrollRule.Validate` bilan AYNI qoida).</summary>
    private const string MultiplierCheck =
        """("WeekendHolidayMultiplier" IS NULL OR "WeekendHolidayMultiplier" >= 1)""";

    /// <summary>Muddat teskari bo'lmaydi — bazada ham, domainda ham.</summary>
    private const string PeriodOrderCheck =
        """("ActiveTo" IS NULL OR "ActiveTo" >= "ActiveFrom")""";

    /// <summary>
    /// Reja IKKI ustundan iborat va ular BIRGA to'ldiriladi
    /// (sabab <c>PayrollRule.Validate</c> izohida).
    /// </summary>
    private const string PlanPairCheck =
        """(("PlanAmount" IS NULL) = ("PlanReachedPercent" IS NULL))""";

    public void Configure(EntityTypeBuilder<PayrollRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PayrollRules", table =>
        {
            table.HasCheckConstraint("CK_PayrollRules_Amounts_NonNegative", AmountsNonNegativeCheck);
            table.HasCheckConstraint("CK_PayrollRules_Multiplier", MultiplierCheck);
            table.HasCheckConstraint("CK_PayrollRules_PeriodOrder", PeriodOrderCheck);
            table.HasCheckConstraint("CK_PayrollRules_PlanPair", PlanPairCheck);
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(PayrollRule.MaxNameLength);

        builder.Property(r => r.Kind).HasConversion<int>();
        builder.Property(r => r.Role).HasConversion<int>();
        builder.Property(r => r.Basis).HasConversion<int>();
        builder.Property(r => r.GroupType).HasConversion<int>();

        builder.Property(r => r.Amount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(r => r.PlanAmount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(r => r.PlanReachedPercent)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(r => r.WeekendHolidayMultiplier)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // Mahalliy KALENDAR sanalari — `Tariff.ActiveFrom` bilan AYNI sabab.
        builder.Property(r => r.ActiveFrom).HasColumnType("date");
        builder.Property(r => r.ActiveTo).HasColumnType("date");

        // Hisoblanuvchi property — ustun EMAS.
        builder.Ignore(r => r.Specificity);

        // ★ ASOSIY O'QISH YO'LI: dvigatel har safar "shu sanada kuchdagi
        //   faol qoidalar" ni oladi (`PayrollService`, `LessonAccrualService`).
        //   Bu indekssiz har dars yakunlanishida butun jadval skanerlanardi.
        builder.HasIndex(r => new { r.IsActive, r.Role, r.ActiveFrom })
            .HasDatabaseName("IX_PayrollRules_IsActive_Role_ActiveFrom");

        // O'CHIRISH: Restrict — qoida HAQ TARIXINING sababi. Xodim/kurs/guruh
        // o'chirilganda uning qoidalari kaskad bilan ketsa, "nega o'tgan oy
        // shuncha chiqqan" savoliga javob yo'qolardi (`TeacherRate` dagi
        // AYNI mulohaza).
        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Course)
            .WithMany()
            .HasForeignKey(r => r.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Group)
            .WithMany()
            .HasForeignKey(r => r.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Category)
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Bosqichlar qoidaning BO'LAGI — qoida o'chsa ular ham ketadi
        // (mustaqil ma'nosi yo'q, `SessionPayoutLine` esa tarixni saqlaydi).
        builder.HasMany(r => r.Tiers)
            .WithOne(t => t.Rule!)
            .HasForeignKey(t => t.RuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
