using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SelfStudyNudgeConfiguration : IEntityTypeConfiguration<SelfStudyNudge>
{
    public void Configure(EntityTypeBuilder<SelfStudyNudge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SelfStudyNudges");
        builder.HasKey(n => n.Id);

        builder.HasOne(n => n.Student)
            .WithMany()
            .HasForeignKey(n => n.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.RecipientId)
            .OnDelete(DeleteBehavior.SetNull);

        // 🔴 IDEMPOTENTLIK: bir faolsizlik davrida bir bosqich — bir marta.
        //    Ikki parallel yurish (yoki qayta urinish) ikkinchi xabarni
        //    yubora olmaydi: `SaveChanges` shu yerda yiqiladi.
        builder.HasIndex(n => new { n.StudentId, n.Stage, n.AnchorAt })
            .IsUnique()
            .HasDatabaseName("UX_SelfStudyNudges_Student_Stage_Anchor");

        // Hisobot: «oxirgi N kun» bo'yicha.
        builder.HasIndex(n => n.SentAt)
            .HasDatabaseName("IX_SelfStudyNudges_SentAt");
    }
}
