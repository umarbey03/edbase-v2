using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// To'kilish sabablari katalogi (2026-08-18). Sabab va falsafa
/// <see cref="AttritionReason"/> izohida.
/// </summary>
public sealed class AttritionReasonConfiguration : IEntityTypeConfiguration<AttritionReason>
{
    public void Configure(EntityTypeBuilder<AttritionReason> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AttritionReasons");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Label).IsRequired().HasMaxLength(AttritionReason.MaxLabelLength);

        // Nom takrorlanmasin — ikkita "Moliyaviy" bo'lsa foiz hisoboti
        // bitta sababni ikki ulushga bo'lib yuborardi. Indeks AYNAN mos
        // kelishni tekshiradi; katta/kichik harf farqini servis rad etadi.
        builder.HasIndex(r => r.Label)
            .IsUnique()
            .HasDatabaseName("UX_AttritionReasons_Label");
    }
}
