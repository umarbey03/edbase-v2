using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class CourseModuleConfiguration : IEntityTypeConfiguration<CourseModule>
{
    public void Configure(EntityTypeBuilder<CourseModule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // DbSet nomi `Modules` — jadval nomi ham shunday (izchillik uchun aniq yozildi).
        builder.ToTable("Modules");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);

        // Modul kurssiz mavjud emas -> Cascade (ataylab, konvensiyaga tayanmasdan).
        builder.HasOne(m => m.Course)
            .WithMany(c => c.Modules)
            .HasForeignKey(m => m.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.CourseId, m.Position })
            .HasDatabaseName("IX_Modules_CourseId_Position");
    }
}
