using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TelegramUpdateConfiguration : IEntityTypeConfiguration<TelegramUpdate>
{
    public void Configure(EntityTypeBuilder<TelegramUpdate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TelegramUpdates");

        builder.HasKey(u => u.UpdateId);

        // ★ `ValueGeneratedNever` MAJBURIY: kalit TELEGRAM'niki. Busiz EF
        //   ustunni `identity` deb e'lon qilib, bizning qiymatimizni
        //   tashlab yuborardi — natijada har yangilanish yangi qator
        //   bo'lib, takror himoyasi UMUMAN ishlamasdi (jimgina!).
        builder.Property(u => u.UpdateId).ValueGeneratedNever();

        builder.Property(u => u.ReceivedAt).IsRequired();

        // Eski izlarni davriy o'chirish uchun (jadval cheksiz o'smasin).
        builder.HasIndex(u => u.ReceivedAt)
            .HasDatabaseName("IX_TelegramUpdates_ReceivedAt");
    }
}
