using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Books");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title).IsRequired().HasMaxLength(Book.MaxTitleLength);

        // Ombor kaliti `LessonAssets.ObjectKey` bilan AYNI uzunlikda.
        builder.Property(b => b.ObjectKey).IsRequired().HasMaxLength(500);
        builder.Property(b => b.ContentType).IsRequired().HasMaxLength(100);

        // Kalit noyob — bitta fayl ikki yozuvga bog'lanmasin (o'chirishda
        // fayl ham ketadi, ikkinchi yozuv "yetim" qolardi).
        builder.HasIndex(b => b.ObjectKey)
            .IsUnique()
            .HasDatabaseName("UX_Books_ObjectKey");

        // Ro'yxat: faol kitoblar nom bo'yicha.
        builder.HasIndex(b => new { b.IsActive, b.Title })
            .HasDatabaseName("IX_Books_IsActive_Title");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
