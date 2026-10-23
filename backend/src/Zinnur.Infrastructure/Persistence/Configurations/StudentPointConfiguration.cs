using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class StudentPointConfiguration : IEntityTypeConfiguration<StudentPoint>
{
    public void Configure(EntityTypeBuilder<StudentPoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StudentPoints");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Ref).HasMaxLength(StudentPoint.MaxRefLength).IsRequired();

        builder.HasOne(p => p.Student)
            .WithMany()
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Bitta hodisa — bitta yozuv (sinxronlash ikki marta yursa ham).
        builder.HasIndex(p => new { p.StudentId, p.Ref })
            .IsUnique()
            .HasDatabaseName("UX_StudentPoints_StudentId_Ref");

        // Oylik jami va reyting: "shu o'quvchilar, shu oy" so'rovi.
        builder.HasIndex(p => new { p.StudentId, p.OccurredAt })
            .HasDatabaseName("IX_StudentPoints_StudentId_OccurredAt");

        // Sinxronlash oynasi: "oxirgi N kun yozuvlari".
        builder.HasIndex(p => p.OccurredAt).HasDatabaseName("IX_StudentPoints_OccurredAt");
    }
}
