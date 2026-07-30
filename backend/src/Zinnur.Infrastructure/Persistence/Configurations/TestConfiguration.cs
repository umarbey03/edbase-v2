using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TestConfiguration : IEntityTypeConfiguration<Test>
{
    public void Configure(EntityTypeBuilder<Test> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Tests");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(Test.MaxTitleLength);
        builder.Property(t => t.Description).HasMaxLength(DescriptionMaxLength);

        builder.Property(t => t.Kind).HasConversion<int>();

        // Hisoblanuvchi property'lar — ustun EMAS.
        // `MaxScore` savollar balining yig'indisi: savol qo'shilsa u O'ZI
        // o'zgaradi. Ustun bo'lsa denormalizatsiya bo'lardi va savol
        // tahrirlanganda yangilash unutilib, natijalar noto'g'ri chiqardi.
        builder.Ignore(t => t.MaxScore);
        builder.Ignore(t => t.IsLessonTest);

        builder.HasOne(t => t.ModuleLesson)
            .WithMany()
            .HasForeignKey(t => t.ModuleLessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // "Shu darsning testi bormi" — gating har dars uchun so'raydi.
        builder.HasIndex(t => t.ModuleLessonId)
            .HasDatabaseName("IX_Tests_ModuleLessonId");

        // O'quvchining "mavjud testlar" ro'yxati: e'lon qilinganlar, tur bo'yicha.
        builder.HasIndex(t => new { t.IsPublished, t.Kind })
            .HasDatabaseName("IX_Tests_IsPublished_Kind");
    }

    private const int DescriptionMaxLength = 2000;
}
