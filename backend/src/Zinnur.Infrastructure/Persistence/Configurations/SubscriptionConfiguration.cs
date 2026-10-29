using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "SubscriptionPlans",
            table =>
            {
                table.HasCheckConstraint("CK_SubscriptionPlans_Months", "\"Months\" BETWEEN 1 AND 12");
                table.HasCheckConstraint("CK_SubscriptionPlans_Price", "\"Price\" >= 0");
            });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(SubscriptionPlan.MaxNameLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(SubscriptionPlan.MaxDescriptionLength);
        builder.Property(p => p.IncludesInteractiveBook).HasDefaultValue(false);
        builder.Property(p => p.Price).HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.HasOne(p => p.Course)
            .WithMany()
            .HasForeignKey(p => p.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionPeriodConfiguration : IEntityTypeConfiguration<SubscriptionPeriod>
{
    public void Configure(EntityTypeBuilder<SubscriptionPeriod> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "SubscriptionPeriods",
            table =>
            {
                table.HasCheckConstraint("CK_SubscriptionPeriods_Range", "\"EndsAt\" > \"StartsAt\"");
                table.HasCheckConstraint("CK_SubscriptionPeriods_Amount", "\"Amount\" >= 0");
            });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);
        builder.Property(p => p.Note).HasMaxLength(SubscriptionPeriod.MaxNoteLength);
        builder.Property(p => p.CancelReason).HasMaxLength(SubscriptionPeriod.MaxNoteLength);
        builder.Ignore(p => p.IsCancelled);

        // ★ 2026-10-29: tarifdagi darslik belgisi davrga MUZLATILADI (P2-3).
        //   Eski qatorlar migratsiyada o'z tarifidan to'ldiriladi.
        builder.Property(p => p.IncludesInteractiveBook).HasDefaultValue(false);

        builder.HasOne(p => p.Student)
            .WithMany()
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Plan)
            .WithMany()
            .HasForeignKey(p => p.PlanId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Course)
            .WithMany()
            .HasForeignKey(p => p.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.CancelledById)
            .OnDelete(DeleteBehavior.Restrict);

        // "Hozir faol obunasi bormi" — har so'rovda gating va reyting so'raydi.
        builder.HasIndex(p => new { p.StudentId, p.EndsAt })
            .HasDatabaseName("IX_SubscriptionPeriods_StudentId_EndsAt");
    }
}

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "PurchaseOrders",
            table => table.HasCheckConstraint("CK_PurchaseOrders_Amount", "\"Amount\" > 0"));

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Code).HasMaxLength(PurchaseOrder.CodeLength).IsRequired();
        builder.Property(o => o.Amount).HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);
        builder.Property(o => o.DecisionNote).HasMaxLength(PurchaseOrder.MaxNoteLength);
        builder.Ignore(o => o.IsPending);

        builder.HasIndex(o => o.Code).IsUnique().HasDatabaseName("IX_PurchaseOrders_Code");
        builder.HasIndex(o => new { o.Status, o.CreatedAt }).HasDatabaseName("IX_PurchaseOrders_Status_CreatedAt");
        builder.HasIndex(o => new { o.StudentId, o.CreatedAt }).HasDatabaseName("IX_PurchaseOrders_StudentId_CreatedAt");

        builder.HasOne(o => o.Student)
            .WithMany()
            .HasForeignKey(o => o.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Plan)
            .WithMany()
            .HasForeignKey(o => o.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Course>()
            .WithMany()
            .HasForeignKey(o => o.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.DecidedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubscriptionPeriod>()
            .WithMany()
            .HasForeignKey(o => o.SubscriptionPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PaymentTransaction>()
            .WithMany()
            .HasForeignKey(o => o.PaymentTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
