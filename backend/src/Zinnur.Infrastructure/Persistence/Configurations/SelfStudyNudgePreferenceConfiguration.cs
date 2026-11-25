using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SelfStudyNudgePreferenceConfiguration : IEntityTypeConfiguration<SelfStudyNudgePreference>
{
    public void Configure(EntityTypeBuilder<SelfStudyNudgePreference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SelfStudyNudgePreferences");
        builder.HasKey(p => p.Id);
        builder.Ignore(p => p.IsOptedOut);

        builder.HasOne(p => p.Student)
            .WithMany()
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Har o'quvchiga ko'pi bilan bitta qator.
        builder.HasIndex(p => p.StudentId)
            .IsUnique()
            .HasDatabaseName("UX_SelfStudyNudgePreferences_StudentId");
    }
}
