using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class LessonMasteryConfiguration : IEntityTypeConfiguration<LessonMastery>
{
    public void Configure(EntityTypeBuilder<LessonMastery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LessonMasteries");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Note).HasMaxLength(LessonMastery.MaxNoteLength);

        builder.HasOne(m => m.Student)
            .WithMany()
            .HasForeignKey(m => m.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.ModuleLesson)
            .WithMany()
            .HasForeignKey(m => m.ModuleLessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.DecidedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Bir o'quvchi×dars uchun BITTA joriy qaror. Gating shu indeksdan o'qiydi.
        builder.HasIndex(m => new { m.StudentId, m.ModuleLessonId })
            .IsUnique()
            .HasDatabaseName("UX_LessonMasteries_Student_Lesson");
    }
}

public sealed class LessonMasteryEventConfiguration : IEntityTypeConfiguration<LessonMasteryEvent>
{
    public void Configure(EntityTypeBuilder<LessonMasteryEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LessonMasteryEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Note).HasMaxLength(LessonMasteryEvent.MaxNoteLength);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ModuleLesson>()
            .WithMany()
            .HasForeignKey(e => e.ModuleLessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.StudentId, e.ModuleLessonId, e.At })
            .HasDatabaseName("IX_LessonMasteryEvents_Student_Lesson_At");
    }
}
