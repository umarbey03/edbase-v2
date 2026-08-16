using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Bayram kalendari (2026-08-16) — o'quv bo'limi/admin boshqaradigan
/// UMUMIY sanalar ro'yxati.
/// </summary>
public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Holidays");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Label).IsRequired().HasMaxLength(Holiday.MaxLabelLength);

        // ★ SANA NOYOB — bitta kun ikki marta bayram sifatida yozilmasin
        // (`HolidayService.CreateAsync` shuni 409 bilan tutadi, indeks —
        // oxirgi himoya, `GroupCategories.Name` bilan AYNI naqsh).
        builder.HasIndex(h => h.Date)
            .IsUnique()
            .HasDatabaseName("UX_Holidays_Date");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(h => h.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
