using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Jarima tariflari katalogi (2026-08-18). Sabab va falsafa
/// <see cref="PenaltyCategory"/> izohida.
/// </summary>
public sealed class PenaltyCategoryConfiguration : IEntityTypeConfiguration<PenaltyCategory>
{
    public void Configure(EntityTypeBuilder<PenaltyCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PenaltyCategories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Label).IsRequired().HasMaxLength(PenaltyCategory.MaxLabelLength);
        builder.Property(c => c.UnitLabel).HasMaxLength(PenaltyCategory.MaxUnitLength);
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.SystemKey).HasMaxLength(50);

        // Hisoblanadigan xossa — ustun EMAS.
        builder.Ignore(c => c.IsSystem);

        // ★ NOM TAKRORLANMASIN: ikkita "Darsga kechikish" bo'lsa,
        //   operator qaysi birini tanlaganini bilmasdi va oylik
        //   hisobotida bitta qoidabuzarlik ikki qatorga bo'linib
        //   ketardi. Indeks AYNAN mos kelishni tekshiradi; katta/kichik
        //   harf farqini servis qatlami rad etadi.
        builder.HasIndex(c => c.Label)
            .IsUnique()
            .HasDatabaseName("UX_PenaltyCategories_Label");

        // Tizim kategoriyasi (avtomatik jarima tarifi) — har kalitdan
        // bittadan. Qisman: oddiy kategoriyalarda `SystemKey` bo'sh.
        builder.HasIndex(c => c.SystemKey)
            .IsUnique()
            .HasFilter(""" "SystemKey" IS NOT NULL """)
            .HasDatabaseName("UX_PenaltyCategories_SystemKey");
    }
}
