using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SelfStudyCuratorAssignmentConfiguration
    : IEntityTypeConfiguration<SelfStudyCuratorAssignment>
{
    public void Configure(EntityTypeBuilder<SelfStudyCuratorAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "SelfStudyCuratorAssignments",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SelfStudyCuratorAssignments_NotSelf",
                    "\"StudentId\" <> \"CuratorId\"");
                table.HasCheckConstraint(
                    "CK_SelfStudyCuratorAssignments_BackupDiffers",
                    "\"BackupCuratorId\" IS NULL OR \"BackupCuratorId\" <> \"CuratorId\"");
            });

        builder.HasKey(a => a.Id);
        builder.Ignore(a => a.IsActive);

        builder.HasOne(a => a.Student)
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Curator)
            .WithMany()
            .HasForeignKey(a => a.CuratorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.BackupCurator)
            .WithMany()
            .HasForeignKey(a => a.BackupCuratorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.AssignedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.EndedById)
            .OnDelete(DeleteBehavior.Restrict);

        // 🔴 BIR O'QUVCHIDA BITTA FAOL KURATOR — bazaning o'zi kafolatlaydi.
        builder.HasIndex(a => a.StudentId)
            .IsUnique()
            .HasFilter(""" "EndedAt" IS NULL """)
            .HasDatabaseName("UX_SelfStudyCuratorAssignments_ActiveStudent");

        // "Mening o'quvchilarim" — har ochilishda kurator va zaxira bo'yicha.
        builder.HasIndex(a => new { a.CuratorId, a.EndedAt })
            .HasDatabaseName("IX_SelfStudyCuratorAssignments_CuratorId_EndedAt");
        builder.HasIndex(a => new { a.BackupCuratorId, a.EndedAt })
            .HasDatabaseName("IX_SelfStudyCuratorAssignments_BackupCuratorId_EndedAt");
    }
}
