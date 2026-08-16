using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PayrollAdjustments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Amount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(a => a.Reason).HasMaxLength(PayrollAdjustment.MaxReasonLength);

        builder.Property(a => a.PeriodStart).HasColumnType("date");

        builder.HasIndex(a => new { a.UserId, a.PeriodStart })
            .HasDatabaseName("IX_PayrollAdjustments_UserId_PeriodStart");

        // O'CHIRISH: Restrict — moliyaviy TARIX, xodim o'chirilganda kaskad
        // bilan yo'qolmasin (`TeacherRateConfiguration` dagi bilan AYNI
        // mulohaza).
        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.CreatedBy)
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
