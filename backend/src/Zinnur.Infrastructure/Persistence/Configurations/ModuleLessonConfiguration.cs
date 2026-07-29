using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class ModuleLessonConfiguration : IEntityTypeConfiguration<ModuleLesson>
{
    public void Configure(EntityTypeBuilder<ModuleLesson> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ModuleLessons");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Description).HasMaxLength(2000);

        builder.HasOne(l => l.Module)
            .WithMany(m => m.Lessons)
            .HasForeignKey(l => l.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.ModuleId, l.Position })
            .HasDatabaseName("IX_ModuleLessons_ModuleId_Position");
    }
}
