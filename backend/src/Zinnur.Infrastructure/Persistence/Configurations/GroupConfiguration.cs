using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Groups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).IsRequired().HasMaxLength(150);

        // DateOnly -> Postgres `date` (vaqt zonasi muammosi umuman yo'q).
        builder.Property(g => g.StartDate).HasColumnType("date");

        // Kurs o'chirilsa guruh tarixi saqlanib qoladi -> FK NULL bo'ladi.
        builder.HasOne(g => g.Course)
            .WithMany()
            .HasForeignKey(g => g.CourseId)
            .OnDelete(DeleteBehavior.SetNull);

        // USTOZ / KURATOR — navigatsiya property'si yo'q, shuning uchun
        // munosabat QO'LDA e'lon qilinadi (aks holda EF hech qanday FK yaratmaydi
        // va bazada "yo'q ustoz" ga ishora qoladi).
        //
        // Restrict: foydalanuvchi hech qachon O'CHIRILMAYDI, `IsActive=false`
        // qilinadi. Shu sabab User'ga ishora qiluvchi BARCHA FK'lar Restrict —
        // izchil va kutilmagan kaskad o'chirish bo'lmaydi.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.AssistantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => new { g.IsActive, g.CourseId })
            .HasDatabaseName("IX_Groups_IsActive_CourseId");

        builder.HasIndex(g => g.TeacherId).HasDatabaseName("IX_Groups_TeacherId");
        builder.HasIndex(g => g.AssistantId).HasDatabaseName("IX_Groups_AssistantId");
    }
}
