using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Jadval nomi BIRLIKDA: "Progress" sanoqsiz ot, "LessonProgresses"
        // grammatik jihatdan ham, o'qishda ham noqulay.
        builder.ToTable("LessonProgress");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.OverrideReason).HasMaxLength(OverrideReasonMaxLength);

        builder.Ignore(p => p.IsVideoWatched);

        builder.HasOne(p => p.Student)
            .WithMany()
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ModuleLesson)
            .WithMany()
            .HasForeignKey(p => p.ModuleLessonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Istisnoni kim ochgani — navigatsiyasiz FK.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.OverrideById)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // ★ BIR O'QUVCHI + BIR DARS = BITTA PROGRESS QATORI.
        //
        // "Video ko'rildi" xabari klientdan bir necha marta kelishi mumkin
        // (pleyer har 10 sekundda yuboradi, sahifa qayta yuklanadi).
        // Indekssiz har xabar yangi qator yaratardi va gating "ko'rilganmi"
        // savoliga qaysi qatorga qarab javob berishini bilmasdi.
        // ============================================================
        builder.HasIndex(p => new { p.StudentId, p.ModuleLessonId })
            .IsUnique()
            .HasDatabaseName("UX_LessonProgress_StudentId_ModuleLessonId");
    }

    private const int OverrideReasonMaxLength = 500;
}
