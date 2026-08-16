using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PayrollApprovalConfiguration : IEntityTypeConfiguration<PayrollApproval>
{
    public void Configure(EntityTypeBuilder<PayrollApproval> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PayrollApprovals");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status).HasConversion<int>();

        builder.Property(a => a.SnapshotTotalAmount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(a => a.PeriodStart).HasColumnType("date");

        // ★ BITTA XODIM — BITTA DAVR — BITTA holat yozuvi.
        builder.HasIndex(a => new { a.UserId, a.PeriodStart })
            .IsUnique()
            .HasDatabaseName("UX_PayrollApprovals_UserId_PeriodStart");

        // O'CHIRISH: Restrict — tasdiqlash/to'lov TARIXI, xodim o'chirilganda
        // kaskad bilan yo'qolmasin (`TeacherRateConfiguration` dagi bilan
        // AYNI mulohaza).
        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.ApprovedBy)
            .WithMany()
            .HasForeignKey(a => a.ApprovedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.PaidBy)
            .WithMany()
            .HasForeignKey(a => a.PaidById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
